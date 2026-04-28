using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Rag;
using Dignite.Paperbase.Rag.Pgvector;
using Dignite.Paperbase.Rag.Pgvector.Documents;
using Dignite.Paperbase.Rag.Pgvector.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using Shouldly;
using Testcontainers.PostgreSql;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.MultiTenancy;
using Xunit;
using Xunit.Abstractions;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// Slice H — verifies that the **same-DBMS cross-database** deployment topology works end-to-end.
/// Spins up <b>two physically separate</b> PostgreSQL containers (both with pgvector),
/// representing the production split where the main DB and the vector DB run on independent
/// instances connected by two distinct connection strings.
///
/// <para>
/// <b>Why manual.</b> CI runners frequently lack Docker, and pulling two
/// <c>pgvector/pgvector:pg17</c> images + booting two clusters takes 60–120s — too expensive
/// for the regular test loop. Marked <c>[Trait("Category","Manual")]</c> so default
/// <c>dotnet test</c> filters skip it; run locally with:
/// <code>dotnet test --filter "Category=Manual" core/Dignite.Paperbase.slnx</code>
/// </para>
///
/// <para>
/// <b>Scope.</b> What this validates:
/// <list type="bullet">
///   <item>The main-DB connection (<c>"Paperbase"</c>) and the vector-DB connection
///         (<c>"PaperbaseRag"</c>) bind to different physical clusters without code change.</item>
///   <item><c>PgvectorRagDbContext.Database.MigrateAsync()</c> applies the SliceD/G migrations
///         only to the vector DB; the main DB remains untouched.</item>
///   <item>The migration history is recorded in <c>__EFMigrationsHistory_PgvectorRag</c>
///         (NOT the default <c>__EFMigrationsHistory</c>) on the vector cluster only — confirming
///         the two contexts can never collide on a shared history table.</item>
///   <item><c>PgvectorDocumentKnowledgeIndex.UpsertDocumentAsync</c>,
///         <see cref="IDocumentKnowledgeIndex.SearchAsync"/>,
///         <see cref="IDocumentKnowledgeIndex.SearchSimilarDocumentsAsync"/> all execute end-to-end
///         against the vector cluster, reading and writing only there.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Out of scope (covered elsewhere).</b>
/// <list type="bullet">
///   <item>Document deletion fan-out via <c>DocumentDeletingEventHandler.OnCompleted</c> — covered
///         by <see cref="DocumentDeletingEventHandler_Tests"/> (unit). Cross-DB version requires
///         a full ABP UoW and is documented in <c>docs/deployment-mixed-db.md</c>.</item>
///   <item><c>PgvectorReconciliationBackgroundJob</c> orphan cleanup — covered by component-level
///         tests; cross-DB scan logic is identical to single-DB because it operates on the vector
///         DbContext only and queries the main DocumentRepository through ABP's standard
///         <c>IDbContextProvider&lt;PaperbaseDbContext&gt;</c>.</item>
/// </list>
/// </para>
/// </summary>
[Trait("Category", "Manual")]
public class HybridDbDeployment_Tests : IAsyncLifetime
{
    private const string PgvectorImage = "pgvector/pgvector:pg17";
    private const int EmbeddingDim = PgvectorRagDbProperties.EmbeddingVectorDimension;

    private readonly ITestOutputHelper _output;

    private PostgreSqlContainer _mainDbContainer = default!;
    private PostgreSqlContainer _vectorDbContainer = default!;

    public HybridDbDeployment_Tests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        // Two independent containers — each with its own host port, database, credentials.
        // Topology proxy: same as production "main DB on cluster A, vector DB on cluster B".
        _mainDbContainer = new PostgreSqlBuilder()
            .WithImage(PgvectorImage)
            .WithDatabase("paperbase_main")
            .WithUsername("paperbase")
            .WithPassword("paperbase_test_pw")
            .Build();

        _vectorDbContainer = new PostgreSqlBuilder()
            .WithImage(PgvectorImage)
            .WithDatabase("paperbase_rag")
            .WithUsername("paperbase_rag")
            .WithPassword("paperbase_rag_test_pw")
            .Build();

        await Task.WhenAll(
            _mainDbContainer.StartAsync(),
            _vectorDbContainer.StartAsync());

        _output.WriteLine($"Main DB: {_mainDbContainer.GetConnectionString()}");
        _output.WriteLine($"Vector DB: {_vectorDbContainer.GetConnectionString()}");
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(
            _mainDbContainer.DisposeAsync().AsTask(),
            _vectorDbContainer.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task Vector_Migration_Lands_On_Vector_DB_Only_With_Independent_History_Table()
    {
        await using var vectorCtx = CreatePgvectorRagDbContext(_vectorDbContainer.GetConnectionString());
        await vectorCtx.Database.MigrateAsync();

        // Vector DB must have the dedicated history table — never the default name.
        var historyTablesOnVectorDb = await ListHistoryTablesAsync(_vectorDbContainer.GetConnectionString());
        historyTablesOnVectorDb.ShouldContain(PgvectorRagDbProperties.MigrationsHistoryTableName);
        historyTablesOnVectorDb.ShouldNotContain("__EFMigrationsHistory");

        // Main DB must NOT have any vector-related history — it's an isolated cluster
        // and we never connected the PgvectorRagDbContext to it.
        var historyTablesOnMainDb = await ListHistoryTablesAsync(_mainDbContainer.GetConnectionString());
        historyTablesOnMainDb.ShouldNotContain(PgvectorRagDbProperties.MigrationsHistoryTableName);

        // Vector DB has the chunk + document-vector tables.
        var vectorTables = await ListPaperbaseTablesAsync(_vectorDbContainer.GetConnectionString());
        vectorTables.ShouldContain("PaperbaseDocumentChunks");
        vectorTables.ShouldContain("PaperbaseDocumentVectors");

        // Main DB does NOT have them — they live exclusively on the vector cluster.
        var mainTables = await ListPaperbaseTablesAsync(_mainDbContainer.GetConnectionString());
        mainTables.ShouldNotContain("PaperbaseDocumentChunks");
        mainTables.ShouldNotContain("PaperbaseDocumentVectors");
    }

    [Fact]
    public async Task UpsertDocumentAsync_Writes_To_Vector_DB_Only()
    {
        await using var vectorCtx = CreatePgvectorRagDbContext(_vectorDbContainer.GetConnectionString());
        await vectorCtx.Database.MigrateAsync();

        var index = CreateIndex(vectorCtx);
        var documentId = Guid.NewGuid();

        await index.UpsertDocumentAsync(new DocumentVectorIndexUpdate
        {
            DocumentId = documentId,
            TenantId = null,
            DocumentTypeCode = "contract.general",
            Chunks =
            [
                new DocumentVectorRecord
                {
                    Id = Guid.NewGuid(),
                    DocumentId = documentId,
                    TenantId = null,
                    DocumentTypeCode = "contract.general",
                    ChunkIndex = 0,
                    Text = "業務委託契約書 第1条",
                    Vector = MakeVector(seed: 0.1f)
                },
                new DocumentVectorRecord
                {
                    Id = Guid.NewGuid(),
                    DocumentId = documentId,
                    TenantId = null,
                    DocumentTypeCode = "contract.general",
                    ChunkIndex = 1,
                    Text = "業務委託契約書 第2条",
                    Vector = MakeVector(seed: 0.2f)
                }
            ]
        });

        await using var verifyCtx = CreatePgvectorRagDbContext(_vectorDbContainer.GetConnectionString());
        var chunks = await verifyCtx.DocumentChunks.Where(c => c.DocumentId == documentId).ToListAsync();
        chunks.Count.ShouldBe(2);
        var docVector = await verifyCtx.DocumentVectors.FirstOrDefaultAsync(dv => dv.Id == documentId);
        docVector.ShouldNotBeNull();
        docVector!.ChunkCount.ShouldBe(2);
    }

    [Fact]
    public async Task SearchAsync_And_SearchSimilarDocumentsAsync_Work_Cross_DB()
    {
        await using var vectorCtx = CreatePgvectorRagDbContext(_vectorDbContainer.GetConnectionString());
        await vectorCtx.Database.MigrateAsync();
        var index = CreateIndex(vectorCtx);

        var docA = Guid.NewGuid();
        var docB = Guid.NewGuid();

        // Two documents with similar but distinct embeddings — vector search should rank them
        // by cosine distance to the query.
        await index.UpsertDocumentAsync(SingleChunkUpdate(docA, "契約金額の支払い条件", seed: 0.10f));
        await index.UpsertDocumentAsync(SingleChunkUpdate(docB, "契約解除の通知期間",     seed: 0.50f));

        // Vector mode — query embedding closer to docA's seed.
        var vectorHits = await index.SearchAsync(new VectorSearchRequest
        {
            QueryVector = MakeVector(seed: 0.11f),
            TopK = 5,
            Mode = VectorSearchMode.Vector
        });
        vectorHits.Count.ShouldBe(2);
        vectorHits[0].DocumentId.ShouldBe(docA);

        // Document-level similarity — closest to docA across the corpus excluding docA itself.
        var similar = await index.SearchSimilarDocumentsAsync(docA, tenantId: null, topK: 5);
        similar.Count.ShouldBe(1);
        similar[0].DocumentId.ShouldBe(docB);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static PgvectorRagDbContext CreatePgvectorRagDbContext(string connStr)
    {
        var options = new DbContextOptionsBuilder<PgvectorRagDbContext>()
            .UseNpgsql(connStr, o =>
            {
                o.UseVector();
                // Mirror the production registration in PgvectorRagEntityFrameworkCoreModule:
                // the dedicated history table name is the discriminator that lets two contexts
                // share a single physical DB without colliding (single-DB topology) and lets
                // the vector cluster stay clean of unrelated migration metadata (cross-DB topology).
                o.MigrationsHistoryTable(PgvectorRagDbProperties.MigrationsHistoryTableName);
            })
            .Options;

        return new PgvectorRagDbContext(options);
    }

    private static PgvectorDocumentKnowledgeIndex CreateIndex(PgvectorRagDbContext dbContext)
    {
        return new PgvectorDocumentKnowledgeIndex(
            new StaticDbContextProvider(dbContext),
            new HostlessCurrentTenant(),
            Substitute.For<IDataFilter>());
    }

    private static DocumentVectorIndexUpdate SingleChunkUpdate(Guid documentId, string text, float seed)
        => new()
        {
            DocumentId = documentId,
            TenantId = null,
            DocumentTypeCode = "contract.general",
            Chunks =
            [
                new DocumentVectorRecord
                {
                    Id = Guid.NewGuid(),
                    DocumentId = documentId,
                    TenantId = null,
                    DocumentTypeCode = "contract.general",
                    ChunkIndex = 0,
                    Text = text,
                    Vector = MakeVector(seed)
                }
            ]
        };

    private static float[] MakeVector(float seed)
    {
        var v = new float[EmbeddingDim];
        for (var i = 0; i < EmbeddingDim; i++)
            v[i] = seed + (i % 32) * 0.001f;
        return v;
    }

    private static async Task<HashSet<string>> ListHistoryTablesAsync(string connStr)
    {
        return await QueryTableNamesAsync(
            connStr,
            @"SELECT tablename FROM pg_tables
              WHERE schemaname = 'public'
                AND tablename LIKE '\_\_EFMigrationsHistory%' ESCAPE '\'");
    }

    private static async Task<HashSet<string>> ListPaperbaseTablesAsync(string connStr)
    {
        return await QueryTableNamesAsync(
            connStr,
            @"SELECT tablename FROM pg_tables
              WHERE schemaname = 'public'
                AND tablename LIKE 'Paperbase%'");
    }

    /// <summary>Open an Npgsql connection directly and read a single string column —
    /// avoids dragging an EF Core DbContext into a use case that has no entity model.</summary>
    private static async Task<HashSet<string>> QueryTableNamesAsync(string connStr, string sql)
    {
        await using var conn = new Npgsql.NpgsqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        return names;
    }

    private sealed class StaticDbContextProvider : IDbContextProvider<PgvectorRagDbContext>
    {
        private readonly PgvectorRagDbContext _dbContext;

        public StaticDbContextProvider(PgvectorRagDbContext dbContext) => _dbContext = dbContext;

        public PgvectorRagDbContext GetDbContext() => _dbContext;
        public Task<PgvectorRagDbContext> GetDbContextAsync() => Task.FromResult(_dbContext);
    }

    private sealed class HostlessCurrentTenant : ICurrentTenant
    {
        public bool IsAvailable => Id.HasValue;
        public Guid? Id { get; private set; }
        public string? Name { get; private set; }

        public IDisposable Change(Guid? id, string? name = null)
        {
            Id = id;
            Name = name;
            return new NoopDisposable();
        }

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
