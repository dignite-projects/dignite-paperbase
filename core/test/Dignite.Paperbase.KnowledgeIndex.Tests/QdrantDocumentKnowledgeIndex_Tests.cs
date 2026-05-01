using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.KnowledgeIndex;
using Dignite.Paperbase.KnowledgeIndex.Qdrant;
using Microsoft.Extensions.Options;
using Qdrant.Client.Grpc;
using Shouldly;
using Xunit;

namespace Dignite.Paperbase.KnowledgeIndex.Qdrant;

public class QdrantDocumentKnowledgeIndex_Tests
{
    private const string CollectionName = "paperbase_test";

    private readonly FakeQdrantClientGateway _gateway = new();
    private readonly QdrantDocumentKnowledgeIndex _index;

    public QdrantDocumentKnowledgeIndex_Tests()
    {
        _index = new QdrantDocumentKnowledgeIndex(
            _gateway,
            new QdrantFilterBuilder(),
            new QdrantPointIdGenerator(),
            Options.Create(new QdrantKnowledgeIndexOptions { CollectionName = CollectionName }));
    }

    [Fact]
    public async Task SearchAsync_Adds_Tenant_Filter_And_Does_Not_Leak_Other_Tenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await _index.SearchAsync(new VectorSearchRequest
        {
            TenantId = tenantA,
            QueryVector = new float[] { 0.1f, 0.2f }
        });

        _gateway.LastQueryFilter.ShouldNotBeNull();
        _gateway.LastQueryFilter!.Must.Count(c => c.Field.Key == QdrantPayloadFields.TenantId).ShouldBe(1);
        FilterHasKeyword(
            _gateway.LastQueryFilter,
            QdrantPayloadFields.TenantId,
            EncodeTenantId(tenantA)).ShouldBeTrue();
        FilterHasKeyword(
            _gateway.LastQueryFilter,
            QdrantPayloadFields.TenantId,
            EncodeTenantId(tenantB)).ShouldBeFalse();
    }

    [Fact]
    public async Task SearchAsync_Adds_DocumentId_Filter()
    {
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        await _index.SearchAsync(new VectorSearchRequest
        {
            TenantId = tenantId,
            DocumentId = documentId,
            QueryVector = new float[] { 0.1f, 0.2f }
        });

        FilterHasKeyword(
            _gateway.LastQueryFilter!,
            QdrantPayloadFields.DocumentId,
            EncodeDocumentId(documentId)).ShouldBeTrue();
    }

    [Fact]
    public async Task SearchAsync_Adds_DocumentTypeCode_Filter()
    {
        await _index.SearchAsync(new VectorSearchRequest
        {
            TenantId = Guid.NewGuid(),
            DocumentTypeCode = "contract.general",
            QueryVector = new float[] { 0.1f, 0.2f }
        });

        FilterHasKeyword(
            _gateway.LastQueryFilter!,
            QdrantPayloadFields.DocumentTypeCode,
            "contract.general").ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteByDocumentIdAsync_Deletes_By_Tenant_And_Document()
    {
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        await _index.DeleteByDocumentIdAsync(documentId, tenantId);

        _gateway.LastDeleteFilter.ShouldNotBeNull();
        FilterHasKeyword(
            _gateway.LastDeleteFilter!,
            QdrantPayloadFields.TenantId,
            EncodeTenantId(tenantId)).ShouldBeTrue();
        FilterHasKeyword(
            _gateway.LastDeleteFilter!,
            QdrantPayloadFields.DocumentId,
            EncodeDocumentId(documentId)).ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteByDocumentIdAsync_Uses_Host_Tenant_Payload_For_Null_Tenant()
    {
        var documentId = Guid.NewGuid();

        await _index.DeleteByDocumentIdAsync(documentId, tenantId: null);

        FilterHasKeyword(
            _gateway.LastDeleteFilter!,
            QdrantPayloadFields.TenantId,
            HostTenantId).ShouldBeTrue();
    }

    [Fact]
    public async Task UpsertDocumentAsync_Uses_Stable_PointId_For_Retry()
    {
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        await _index.UpsertDocumentAsync(CreateUpdate(tenantId, documentId));
        var firstPointId = _gateway.UpsertedPoints.Single().Id.Uuid;

        await _index.UpsertDocumentAsync(CreateUpdate(tenantId, documentId));
        var secondPointId = _gateway.UpsertedPoints.Single().Id.Uuid;

        secondPointId.ShouldBe(firstPointId);
    }

    [Fact]
    public async Task UpsertDocumentAsync_Deletes_Stale_Chunks_For_Same_Document()
    {
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        await _index.UpsertDocumentAsync(CreateUpdate(tenantId, documentId));

        _gateway.LastDeleteFilter.ShouldNotBeNull();
        FilterHasKeyword(
            _gateway.LastDeleteFilter!,
            QdrantPayloadFields.TenantId,
            EncodeTenantId(tenantId)).ShouldBeTrue();
        FilterHasKeyword(
            _gateway.LastDeleteFilter!,
            QdrantPayloadFields.DocumentId,
            EncodeDocumentId(documentId)).ShouldBeTrue();
        _gateway.LastDeleteFilter!.Must.Any(c => c.Field.Key == QdrantPayloadFields.ChunkIndex).ShouldBeTrue();
    }

    private static DocumentVectorIndexUpdate CreateUpdate(Guid? tenantId, Guid documentId)
    {
        return new DocumentVectorIndexUpdate
        {
            TenantId = tenantId,
            DocumentId = documentId,
            DocumentTypeCode = "contract.general",
            Chunks =
            [
                new DocumentVectorRecord
                {
                    ChunkIndex = 0,
                    Text = "chunk",
                    Vector = new float[] { 0.1f, 0.2f }
                }
            ]
        };
    }

    [Fact]
    public async Task SearchAsync_Routes_To_Hybrid_When_EnableHybridSearch_And_QueryText_Set()
    {
        var index = CreateIndex(enableHybrid: true);

        await index.SearchAsync(new VectorSearchRequest
        {
            TenantId = Guid.NewGuid(),
            QueryVector = new float[] { 0.1f, 0.2f },
            QueryText = "contract duration"
        });

        _gateway.HybridWasCalled.ShouldBeTrue();
        _gateway.QueryAsyncCalled.ShouldBeFalse();
        _gateway.LastHybridSparseVector.Indices.ShouldNotBeEmpty();
        _gateway.LastHybridQueryFilter.ShouldNotBeNull();
    }

    [Fact]
    public async Task SearchAsync_Routes_To_Dense_When_EnableHybridSearch_False()
    {
        // EnableHybridSearch defaults to false — shared _index is sufficient.
        await _index.SearchAsync(new VectorSearchRequest
        {
            TenantId = Guid.NewGuid(),
            QueryVector = new float[] { 0.1f, 0.2f },
            QueryText = "contract duration"
        });

        _gateway.QueryAsyncCalled.ShouldBeTrue();
        _gateway.HybridWasCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task SearchAsync_Hybrid_Branch_Does_Not_Forward_Normalized_MinScore()
    {
        // Caller provides a normalized cosine threshold (0.65). On the hybrid/RRF
        // branch the provider must drop it — RRF scores are on a different scale
        // (typically 0.01–0.065) and would be entirely filtered out.
        var index = CreateIndex(enableHybrid: true);

        await index.SearchAsync(new VectorSearchRequest
        {
            TenantId = Guid.NewGuid(),
            QueryVector = new float[] { 0.1f, 0.2f },
            QueryText = "contract duration",
            MinScore = 0.65
        });

        _gateway.LastHybridScoreThreshold.ShouldBeNull();
    }

    [Fact]
    public async Task SearchAsync_Hybrid_Result_Score_Is_Null()
    {
        // Provider signals "no normalized score available" by emitting Score=null
        // on the hybrid branch. Application-layer MinScore filters use this as a
        // pass-through signal.
        var documentId = Guid.NewGuid();
        _gateway.NextHybridResults =
        [
            new ScoredPoint
            {
                Id = new PointId { Uuid = Guid.NewGuid().ToString("D") },
                Score = 0.03f,
                Payload =
                {
                    [QdrantPayloadFields.DocumentId] = documentId.ToString("D"),
                    [QdrantPayloadFields.ChunkIndex] = 0L,
                    [QdrantPayloadFields.Text] = "chunk text"
                }
            }
        ];
        var index = CreateIndex(enableHybrid: true);

        var results = await index.SearchAsync(new VectorSearchRequest
        {
            TenantId = Guid.NewGuid(),
            QueryVector = new float[] { 0.1f, 0.2f },
            QueryText = "contract duration"
        });

        results.Count.ShouldBe(1);
        results[0].Score.ShouldBeNull();
    }

    [Fact]
    public async Task SearchAsync_Dense_Branch_Still_Forwards_MinScore()
    {
        // Dense path keeps the normalized-cosine threshold semantics.
        await _index.SearchAsync(new VectorSearchRequest
        {
            TenantId = Guid.NewGuid(),
            QueryVector = new float[] { 0.1f, 0.2f },
            MinScore = 0.65
        });

        _gateway.LastDenseScoreThreshold.ShouldBe(0.65f);
    }

    private QdrantDocumentKnowledgeIndex CreateIndex(bool enableHybrid) =>
        new QdrantDocumentKnowledgeIndex(
            _gateway,
            new QdrantFilterBuilder(),
            new QdrantPointIdGenerator(),
            Options.Create(new QdrantKnowledgeIndexOptions
            {
                CollectionName = CollectionName,
                EnableHybridSearch = enableHybrid
            }));

    private static bool FilterHasKeyword(Filter filter, string key, string value)
    {
        return filter.Must.Any(c =>
            c.Field.Key == key &&
            c.Field.Match.Keyword == value);
    }

    private const string HostTenantId = "__host__";

    private static string EncodeTenantId(Guid? tenantId)
        => tenantId?.ToString("D") ?? HostTenantId;

    private static string EncodeDocumentId(Guid documentId)
        => documentId.ToString("D");

    private sealed class FakeQdrantClientGateway : IQdrantClientGateway
    {
        public Filter? LastQueryFilter { get; private set; }
        public Filter? LastDeleteFilter { get; private set; }
        public IReadOnlyList<PointStruct> UpsertedPoints { get; private set; } = [];

        public bool QueryAsyncCalled { get; private set; }
        public bool HybridWasCalled { get; private set; }
        public (float[] Values, uint[] Indices) LastHybridSparseVector { get; private set; }
        public Filter? LastHybridQueryFilter { get; private set; }
        public float? LastDenseScoreThreshold { get; private set; }
        public float? LastHybridScoreThreshold { get; private set; }
        public IReadOnlyList<ScoredPoint> NextHybridResults { get; set; } = [];

        public Task EnsureCollectionAsync(QdrantKnowledgeIndexOptions options, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UpsertAsync(
            string collectionName,
            IReadOnlyList<PointStruct> points,
            CancellationToken cancellationToken = default)
        {
            collectionName.ShouldBe(CollectionName);
            UpsertedPoints = points;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ScoredPoint>> QueryAsync(
            string collectionName,
            float[] vector,
            Filter filter,
            ulong limit,
            float? scoreThreshold,
            CancellationToken cancellationToken = default)
        {
            collectionName.ShouldBe(CollectionName);
            QueryAsyncCalled = true;
            LastQueryFilter = filter;
            LastDenseScoreThreshold = scoreThreshold;
            return Task.FromResult<IReadOnlyList<ScoredPoint>>([]);
        }

        public Task<IReadOnlyList<ScoredPoint>> QueryHybridAsync(
            string collectionName,
            float[] denseVector,
            (float[] Values, uint[] Indices) sparseVector,
            Filter filter,
            ulong limit,
            float? scoreThreshold,
            CancellationToken cancellationToken = default)
        {
            collectionName.ShouldBe(CollectionName);
            HybridWasCalled = true;
            LastHybridSparseVector = sparseVector;
            LastHybridQueryFilter = filter;
            LastHybridScoreThreshold = scoreThreshold;
            return Task.FromResult(NextHybridResults);
        }

        public Task DeleteAsync(
            string collectionName,
            Filter filter,
            CancellationToken cancellationToken = default)
        {
            collectionName.ShouldBe(CollectionName);
            LastDeleteFilter = filter;
            return Task.CompletedTask;
        }
    }
}
