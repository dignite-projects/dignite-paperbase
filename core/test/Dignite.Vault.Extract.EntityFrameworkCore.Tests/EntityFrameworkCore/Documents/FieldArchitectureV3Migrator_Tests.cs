using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Dignite.Abp.FlexFields;
using Dignite.Vault.Extract.Documents;
using Dignite.Vault.Extract.Documents.Fields;
using Dignite.Vault.Extract.Documents.Fields.Migration;
using Dignite.Vault.Extract.FlexFields.Tags;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Guids;
using Xunit;

namespace Dignite.Vault.Extract.EntityFrameworkCore.Documents;

/// <summary>
/// The v2 -> v3 data migration against the real database (#561): definitions, value bags, and the
/// rebuilt query index.
/// <para>
/// The assertions that matter here are the ones about <i>equivalence</i>. A migration that runs cleanly
/// but drops a value, reorders a multi-value set, or leaves a document unfilterable does not fail — it
/// succeeds and quietly changes what the product knows.
/// </para>
/// </summary>
public class FieldArchitectureV3Migrator_Tests : VaultExtractEntityFrameworkCoreTestBase
{
    private const string TypeCode = "host.contract";

    private readonly FieldArchitectureV3Migrator _migrator;
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentTypeRepository _documentTypeRepository;
    private readonly IFieldDefinitionRepository _fieldDefinitionRepository;
    private readonly IFieldRepository _fieldRepository;
    private readonly IFlexFieldQueryExecutor<Document> _queryExecutor;
    private readonly IDbContextProvider<VaultExtractDbContext> _dbContextProvider;
    private readonly IGuidGenerator _guidGenerator;

    public FieldArchitectureV3Migrator_Tests()
    {
        _migrator = GetRequiredService<FieldArchitectureV3Migrator>();
        _documentRepository = GetRequiredService<IDocumentRepository>();
        _documentTypeRepository = GetRequiredService<IDocumentTypeRepository>();
        _fieldDefinitionRepository = GetRequiredService<IFieldDefinitionRepository>();
        _fieldRepository = GetRequiredService<IFieldRepository>();
        _queryExecutor = GetRequiredService<IFlexFieldQueryExecutor<Document>>();
        _dbContextProvider = GetRequiredService<IDbContextProvider<VaultExtractDbContext>>();
        _guidGenerator = GetRequiredService<IGuidGenerator>();
    }

    [Fact]
    public async Task Migrates_definitions_preserving_identity()
    {
        await WithUnitOfWorkAsync(() => SeedSchemaAsync());

        var result = await WithMigrationAsync();

        result.DefinitionsMigrated.ShouldBe(4);
        result.TenantId.ShouldBeNull();

        await WithUnitOfWorkAsync(async () =>
        {
            var amount = await _fieldRepository.FindByNameAsync(TypeId(TypeCode), "amount");
            amount.ShouldNotBeNull();
            // Same id as the v2 definition - what keeps warning rows and index rows valid.
            amount!.Id.ShouldBe(FieldId("amount"));
            amount.FieldTypeName.ShouldBe("Number");
            amount.Description.ShouldBe("extract amount");
        });
    }

    [Fact]
    public async Task Migrates_values_into_the_bag_with_their_types_intact()
    {
        var id = _guidGenerator.Create();
        await WithUnitOfWorkAsync(async () =>
        {
            await SeedSchemaAsync();
            await SeedDocumentAsync(id);
        });

        await WithMigrationAsync();

        await WithUnitOfWorkAsync(async () =>
        {
            var doc = await _documentRepository.FindAsync(id);
            doc.ShouldNotBeNull();

            doc!.GetField("title").ShouldBe("Service Agreement");
            // A number stays a number: stringifying it would break every range filter on the field.
            Convert.ToDecimal(doc.GetField("amount")).ShouldBe(1500.50m);
            // A pure date lands at midnight, which is what keeps an equality filter an equality filter
            // once Date and DateTime share one field type.
            Convert.ToDateTime(doc.GetField("signed_on")).ShouldBe(new DateTime(2026, 3, 14, 0, 0, 0));
        });
    }

    /// <summary>
    /// Multi-value order is load-bearing twice: it is what the operator sees, and it is the order
    /// FieldFingerprintCalculator hashes in, so a bag built in row-enumeration order would produce a
    /// different fingerprint for identical data.
    /// </summary>
    [Fact]
    public async Task Preserves_multi_value_order()
    {
        var id = _guidGenerator.Create();
        await WithUnitOfWorkAsync(async () =>
        {
            await SeedSchemaAsync();
            await SeedDocumentAsync(id);
        });

        await WithMigrationAsync();

        await WithUnitOfWorkAsync(async () =>
        {
            var doc = await _documentRepository.FindAsync(id);
            var parties = doc!.GetField("parties");

            ReadList(parties).ShouldBe(new[] { "Acme Corp", "Globex", "Initech" });
        });
    }

    /// <summary>
    /// The whole point of migrating: a document that was filterable under v2 must still be findable
    /// afterwards, through the rebuilt index rather than the retired child rows.
    /// </summary>
    [Fact]
    public async Task Migrated_documents_are_findable_through_the_rebuilt_index()
    {
        var id = _guidGenerator.Create();
        await WithUnitOfWorkAsync(async () =>
        {
            await SeedSchemaAsync();
            await SeedDocumentAsync(id);
        });

        await WithMigrationAsync();

        await WithUnitOfWorkAsync(async () =>
        {
            var title = await _fieldRepository.FindByNameAsync(TypeId(TypeCode), "title");
            var query = await _documentRepository.GetQueryableAsync();

            var filtered = await _queryExecutor.ApplyFilterAsync(query, new[]
            {
                new FlexFieldQueryCondition(
                    title!.Id, "title", FlexFieldQueryOperator.Equals, "Service Agreement",
                    FlexFieldValueType.String)
            });

            (await filtered.Select(d => d.Id).ToListAsync()).ShouldContain(id);
        });

        // Multi-value fans out, so any one party matches - the behaviour v2's multi-row storage gave.
        await WithUnitOfWorkAsync(async () =>
        {
            var parties = await _fieldRepository.FindByNameAsync(TypeId(TypeCode), "parties");
            var query = await _documentRepository.GetQueryableAsync();

            var filtered = await _queryExecutor.ApplyFilterAsync(query, new[]
            {
                new FlexFieldQueryCondition(
                    parties!.Id, "parties", FlexFieldQueryOperator.Equals, "Globex",
                    FlexFieldValueType.String)
            });

            (await filtered.Select(d => d.Id).ToListAsync()).ShouldContain(id);
        });
    }

    /// <summary>
    /// Nothing is deleted: the v2 rows stay authoritative until the cutover retires them, which is what
    /// makes the rollback before that point a code revert rather than a restore.
    /// </summary>
    [Fact]
    public async Task Leaves_the_v2_rows_untouched()
    {
        var id = _guidGenerator.Create();
        await WithUnitOfWorkAsync(async () =>
        {
            await SeedSchemaAsync();
            await SeedDocumentAsync(id);
        });

        await WithMigrationAsync();

        await WithUnitOfWorkAsync(async () =>
        {
            var doc = await _documentRepository.FindWithFieldValuesAsync(id);
            doc!.ExtractedFieldValues.Count.ShouldBe(6);

            (await _fieldDefinitionRepository.GetListAsync(TypeId(TypeCode))).Count.ShouldBe(4);
        });
    }

    /// <summary>
    /// An interrupted run has to be resumable, so a second pass must add nothing and must not overwrite a
    /// bag an operator may have edited in between.
    /// </summary>
    [Fact]
    public async Task Is_idempotent()
    {
        var id = _guidGenerator.Create();
        await WithUnitOfWorkAsync(async () =>
        {
            await SeedSchemaAsync();
            await SeedDocumentAsync(id);
        });

        var first = await WithMigrationAsync();
        first.DefinitionsMigrated.ShouldBe(4);
        first.DocumentsMigrated.ShouldBe(1);

        var second = await WithMigrationAsync();
        second.DefinitionsMigrated.ShouldBe(0);
        second.DocumentsMigrated.ShouldBe(0);

        await WithUnitOfWorkAsync(async () =>
        {
            (await _fieldRepository.GetListAsync(TypeId(TypeCode))).Count.ShouldBe(4);

            var doc = await _documentRepository.FindAsync(id);
            doc!.GetField("title").ShouldBe("Service Agreement");
        });
    }

    /// <summary>
    /// Fingerprint recomputation is a separate call on purpose, so this asserts what
    /// <see cref="FieldArchitectureV3Migrator.MigrateAsync"/> does <i>not</i> do. Running it during the
    /// additive phase would leave a corpus split between v2-shaped and v3-shaped hashes as soon as the
    /// still-live v2 pipeline re-extracted anything — and duplicate detection compares those hashes by
    /// string equality, so the split would show up only as duplicates quietly going unnoticed.
    /// </summary>
    [Fact]
    public async Task Migrating_does_not_touch_fingerprints()
    {
        var id = _guidGenerator.Create();
        await WithUnitOfWorkAsync(async () =>
        {
            await SeedSchemaAsync();
            await SeedDocumentAsync(id);
        });

        string? before = null;
        await WithUnitOfWorkAsync(async () => before = (await _documentRepository.FindAsync(id))!.FieldFingerprint);

        await WithMigrationAsync();

        await WithUnitOfWorkAsync(async () =>
            (await _documentRepository.FindAsync(id))!.FieldFingerprint.ShouldBe(before));
    }

    /// <summary>
    /// The cutover step: once run, a document's fingerprint is derived from its bag, and two documents
    /// with the same unique-key values still agree - which is the only property duplicate detection
    /// actually needs.
    /// </summary>
    [Fact]
    public async Task Recompute_derives_fingerprints_from_the_bag()
    {
        var first = _guidGenerator.Create();
        var second = _guidGenerator.Create();

        await WithUnitOfWorkAsync(async () =>
        {
            await SeedSchemaAsync(uniqueKey: true);
            await SeedDocumentAsync(first);
            await SeedDocumentAsync(second, seedSchema: false);
        });

        await WithMigrationAsync();

        var recomputed = 0;
        await WithUnitOfWorkAsync(async () => recomputed = await _migrator.RecomputeFingerprintsAsync());

        recomputed.ShouldBe(2);

        await WithUnitOfWorkAsync(async () =>
        {
            var a = await _documentRepository.FindAsync(first);
            var b = await _documentRepository.FindAsync(second);

            a!.FieldFingerprint.ShouldNotBeNullOrWhiteSpace();
            // Same unique-key values -> same fingerprint, which is what makes them detectable duplicates.
            b!.FieldFingerprint.ShouldBe(a.FieldFingerprint);
        });
    }

    // --- helpers ---

    private async Task<FieldArchitectureV3MigrationResult> WithMigrationAsync()
    {
        FieldArchitectureV3MigrationResult result = null!;
        await WithUnitOfWorkAsync(async () => result = await _migrator.MigrateAsync());
        return result;
    }

    private static List<string> ReadList(object? value)
    {
        return value switch
        {
            List<string> list => list,
            IEnumerable<object> items => items.Select(i => i?.ToString() ?? string.Empty).ToList(),
            System.Text.Json.JsonElement element => element.EnumerateArray().Select(e => e.GetString()!).ToList(),
            _ => new List<string>()
        };
    }

    private async Task SeedSchemaAsync(bool uniqueKey = false)
    {
        await _documentTypeRepository.InsertAsync(
            new DocumentType(TypeId(TypeCode), null, TypeCode, TypeCode), autoSave: true);

        await InsertDefinitionAsync("title", FieldDataType.Text, "extract title", isUniqueKey: uniqueKey);
        await InsertDefinitionAsync("amount", FieldDataType.Number, "extract amount", isUniqueKey: uniqueKey);
        await InsertDefinitionAsync("signed_on", FieldDataType.Date, "extract date");
        await InsertDefinitionAsync("parties", FieldDataType.Text, "extract parties", allowMultiple: true);
    }

    private async Task InsertDefinitionAsync(
        string name, FieldDataType dataType, string prompt, bool allowMultiple = false, bool isUniqueKey = false)
    {
        await _fieldDefinitionRepository.InsertAsync(
            new FieldDefinition(
                FieldId(name), null, TypeId(TypeCode),
                name: name, displayName: name, prompt: prompt, dataType: dataType,
                allowMultiple: allowMultiple, isUniqueKey: isUniqueKey),
            autoSave: true);
    }

    private async Task SeedDocumentAsync(Guid id, bool seedSchema = true)
    {
        var doc = new Document(
            id,
            tenantId: null,
            fileOrigin: new FileOrigin($"blobs/{id:N}.pdf", "test-user", "application/pdf",
                $"{Guid.NewGuid():N}{Guid.NewGuid():N}", 1024, "c.pdf"));
        typeof(Document).GetProperty(nameof(Document.DocumentTypeId))!.SetValue(doc, TypeId(TypeCode));

        doc.SetFields(new[]
        {
            Value("title", FieldDataType.Text, "\"Service Agreement\""),
            Value("amount", FieldDataType.Number, "1500.50"),
            Value("signed_on", FieldDataType.Date, "\"2026-03-14\""),
            // Deliberately seeded out of order, so the ordering assertion tests the builder rather than
            // whatever order the rows happen to come back in.
            Value("parties", FieldDataType.Text, "\"Initech\"", order: 2),
            Value("parties", FieldDataType.Text, "\"Acme Corp\"", order: 0),
            Value("parties", FieldDataType.Text, "\"Globex\"", order: 1)
        });

        await _documentRepository.InsertAsync(doc, autoSave: true);
    }

    private static DocumentFieldValue Value(string name, FieldDataType dataType, string json, int order = 0)
        => new(FieldId(name), dataType, System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json), order);

    private static Guid FieldId(string name) => new(MD5.HashData(Encoding.UTF8.GetBytes("mig:field:" + name)));
    private static Guid TypeId(string typeCode) => new(MD5.HashData(Encoding.UTF8.GetBytes("mig:type:" + typeCode)));
}
