using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Dignite.Abp.FlexFields;
using Dignite.Vault.Extract.Documents;
using Dignite.Vault.Extract.Documents.Fields;
using Dignite.Vault.Extract.FlexFields.Tags;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Guids;
using Xunit;

namespace Dignite.Vault.Extract.EntityFrameworkCore.Documents;

/// <summary>
/// The field architecture v3 read/write chain end to end (#558), against the real database: the provider
/// resolves a document's fields, the index manager projects the bag into typed rows, and the query
/// executor pushes a filter down onto them.
/// <para>
/// These three are what make the value bag usable at all — the bag alone is authoritative but unqueryable
/// — so this is the first test that proves v3 can actually replace v2 rather than merely compile
/// alongside it.
/// </para>
/// </summary>
public class DocumentFlexFieldPipeline_Tests : VaultExtractEntityFrameworkCoreTestBase
{
    private const string TypeCode = "host.invoice";

    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentTypeRepository _documentTypeRepository;
    private readonly IFieldRepository _fieldRepository;
    private readonly IFlexFieldProvider<Document> _provider;
    private readonly IFlexFieldIndexManager<Document> _indexManager;
    private readonly IFlexFieldQueryExecutor<Document> _queryExecutor;
    private readonly IDbContextProvider<VaultExtractDbContext> _dbContextProvider;
    private readonly IGuidGenerator _guidGenerator;

    public DocumentFlexFieldPipeline_Tests()
    {
        _documentRepository = GetRequiredService<IDocumentRepository>();
        _documentTypeRepository = GetRequiredService<IDocumentTypeRepository>();
        _fieldRepository = GetRequiredService<IFieldRepository>();
        _provider = GetRequiredService<IFlexFieldProvider<Document>>();
        _indexManager = GetRequiredService<IFlexFieldIndexManager<Document>>();
        _queryExecutor = GetRequiredService<IFlexFieldQueryExecutor<Document>>();
        _dbContextProvider = GetRequiredService<IDbContextProvider<VaultExtractDbContext>>();
        _guidGenerator = GetRequiredService<IGuidGenerator>();
    }

    [Fact]
    public async Task Provider_merges_definition_usage_flags_and_bag_value()
    {
        var id = _guidGenerator.Create();
        await WithUnitOfWorkAsync(() => SeedAsync(id, bag => bag
            .SetField("invoice_no", "INV-001")
            .SetField("parties", new List<string> { "Acme", "Globex" })));

        await WithUnitOfWorkAsync(async () =>
        {
            var doc = await LoadAsync(id);
            var fields = await _provider.GetFlexFieldsAsync(doc);

            var byName = fields.ToDictionary(f => f.Name);
            byName.Count.ShouldBe(3);

            byName["invoice_no"].Value.ShouldBe("INV-001");
            byName["invoice_no"].Required.ShouldBeTrue();
            byName["invoice_no"].Searchable.ShouldBeTrue();
            byName["invoice_no"].FieldTypeName.ShouldBe("Text");

            byName["parties"].FieldTypeName.ShouldBe(TagsFieldType.ControlName);

            // Configured but never extracted: present as a field, with no value. The distinction matters -
            // the kernel needs the field to exist in order to report it as a missing required value.
            byName["notes"].Value.ShouldBeNull();
            byName["notes"].Searchable.ShouldBeFalse();
        });
    }

    /// <summary>
    /// An unclassified document genuinely has no fields, because type-bound fields are the only kind
    /// there are. Distinct from the tenant-mismatch case below, which must never present as "no fields".
    /// </summary>
    [Fact]
    public async Task Provider_returns_nothing_for_an_unclassified_document()
    {
        var id = _guidGenerator.Create();
        await WithUnitOfWorkAsync(() => SeedAsync(id, bag => bag, classify: false));

        await WithUnitOfWorkAsync(async () =>
        {
            var doc = await LoadAsync(id);
            (await _provider.GetFlexFieldsAsync(doc)).ShouldBeEmpty();
        });
    }

    /// <summary>
    /// Resolving a document from another tenant layer must fail loudly. The field query runs under the
    /// ambient tenant filter, so the alternative is resolving zero fields — which the index manager acts
    /// on by deleting every index row the document had.
    /// </summary>
    [Fact]
    public async Task Provider_fails_closed_on_a_tenant_mismatch()
    {
        var id = _guidGenerator.Create();
        await WithUnitOfWorkAsync(() => SeedAsync(id, bag => bag.SetField("invoice_no", "INV-001")));

        await WithUnitOfWorkAsync(async () =>
        {
            var doc = await LoadAsync(id);
            // The document is a host document; pretend the ambient layer is a tenant.
            using (GetRequiredService<Volo.Abp.MultiTenancy.ICurrentTenant>().Change(Guid.NewGuid()))
            {
                await Should.ThrowAsync<AbpException>(() => _provider.GetFlexFieldsAsync(doc));
            }
        });
    }

    [Fact]
    public async Task Synchronize_projects_the_bag_into_typed_index_rows()
    {
        var id = _guidGenerator.Create();
        await WithUnitOfWorkAsync(() => SeedAsync(id, bag => bag
            .SetField("invoice_no", "INV-001")
            .SetField("parties", new List<string> { "Acme", "Globex" })));

        await WithUnitOfWorkAsync(async () =>
        {
            var doc = await LoadAsync(id);
            await _indexManager.SynchronizeAsync(doc);
        });

        var rows = await IndexRowsAsync(id);

        // invoice_no yields one row; parties is multi-valued and fans out into one row per value. The
        // non-searchable notes field yields none even though the type declares it.
        rows.Count.ShouldBe(3);
        rows.Select(r => r.StringValue).OrderBy(v => v)
            .ShouldBe(new[] { "Acme", "Globex", "INV-001" });
        rows.ShouldAllBe(r => r.ValueType == FlexFieldValueType.String);
        rows.ShouldAllBe(r => r.TenantId == null);
    }

    /// <summary>
    /// Re-synchronizing must replace, not accumulate. A rebuild runs over documents that already have
    /// rows, so an append-only projection would multiply them on every pass.
    /// </summary>
    [Fact]
    public async Task Synchronize_replaces_the_previous_projection()
    {
        var id = _guidGenerator.Create();
        await WithUnitOfWorkAsync(() => SeedAsync(id, bag => bag.SetField("invoice_no", "INV-001")));

        await WithUnitOfWorkAsync(async () => await _indexManager.SynchronizeAsync(await LoadAsync(id)));
        (await IndexRowsAsync(id)).Count.ShouldBe(1);

        await WithUnitOfWorkAsync(async () =>
        {
            var doc = await LoadAsync(id);
            doc.SetField("invoice_no", "INV-002");
            await _documentRepository.UpdateAsync(doc, autoSave: true);
            await _indexManager.SynchronizeAsync(doc);
        });

        var rows = await IndexRowsAsync(id);
        rows.Count.ShouldBe(1);
        rows.Single().StringValue.ShouldBe("INV-002");
    }

    [Fact]
    public async Task Query_executor_pushes_a_field_filter_into_sql()
    {
        var matching = _guidGenerator.Create();
        var other = _guidGenerator.Create();

        await WithUnitOfWorkAsync(async () =>
        {
            await SeedAsync(matching, bag => bag.SetField("invoice_no", "INV-001"));
            await SeedAsync(other, bag => bag.SetField("invoice_no", "INV-999"), seedSchema: false);
        });

        await WithUnitOfWorkAsync(async () =>
        {
            await _indexManager.SynchronizeAsync(await LoadAsync(matching));
            await _indexManager.SynchronizeAsync(await LoadAsync(other));
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var invoiceNo = await _fieldRepository.FindByNameAsync(TypeId(TypeCode), "invoice_no");
            var query = await _documentRepository.GetQueryableAsync();

            var filtered = await _queryExecutor.ApplyFilterAsync(query, new[]
            {
                new FlexFieldQueryCondition(
                    invoiceNo!.Id, "invoice_no", FlexFieldQueryOperator.Equals, "INV-001",
                    FlexFieldValueType.String)
            });

            var ids = await filtered.Select(d => d.Id).ToListAsync();

            ids.ShouldHaveSingleItem();
            ids.Single().ShouldBe(matching);
        });
    }

    /// <summary>
    /// A multi-valued field matches when any one of its values matches - the behaviour v2's multi-row
    /// storage gave for free and that the fan-out in the index has to reproduce.
    /// </summary>
    [Fact]
    public async Task Query_executor_matches_any_value_of_a_multi_valued_field()
    {
        var id = _guidGenerator.Create();
        await WithUnitOfWorkAsync(() => SeedAsync(id, bag => bag
            .SetField("parties", new List<string> { "Acme", "Globex" })));

        await WithUnitOfWorkAsync(async () => await _indexManager.SynchronizeAsync(await LoadAsync(id)));

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

    // --- helpers ---

    private async Task<Document> LoadAsync(Guid id)
    {
        var doc = await _documentRepository.FindAsync(id);
        doc.ShouldNotBeNull();
        return doc!;
    }

    private async Task<List<DocumentFlexFieldIndex>> IndexRowsAsync(Guid documentId)
    {
        var rows = new List<DocumentFlexFieldIndex>();
        await WithUnitOfWorkAsync(async () =>
        {
            var context = await _dbContextProvider.GetDbContextAsync();
            rows = await context.Set<DocumentFlexFieldIndex>()
                .Where(r => r.DocumentId == documentId)
                .ToListAsync();
        });
        return rows;
    }

    /// <summary>
    /// Seeds the document type and its three fields (a plain text field, an open-vocabulary Tags field,
    /// and a deliberately non-searchable one), then a document carrying the given bag.
    /// </summary>
    private async Task SeedAsync(
        Guid id,
        Func<Document, Document> fillBag,
        bool classify = true,
        bool seedSchema = true)
    {
        if (seedSchema)
        {
            await _documentTypeRepository.InsertAsync(
                new DocumentType(TypeId(TypeCode), null, TypeCode, TypeCode), autoSave: true);

            await _fieldRepository.InsertAsync(new Field(
                FieldId("invoice_no"), null, TypeId(TypeCode),
                name: "invoice_no", displayName: "Invoice No", fieldTypeName: "Text",
                isRequired: true), autoSave: true);

            await _fieldRepository.InsertAsync(new Field(
                FieldId("parties"), null, TypeId(TypeCode),
                name: "parties", displayName: "Parties", fieldTypeName: TagsFieldType.ControlName),
                autoSave: true);

            // Not searchable: proves the flag actually gates projection rather than being decorative.
            await _fieldRepository.InsertAsync(new Field(
                FieldId("notes"), null, TypeId(TypeCode),
                name: "notes", displayName: "Notes", fieldTypeName: "Text",
                isSearchable: false), autoSave: true);
        }

        var doc = new Document(
            id,
            tenantId: null,
            fileOrigin: new FileOrigin($"blobs/{id:N}.pdf", "test-user", "application/pdf",
                $"{Guid.NewGuid():N}{Guid.NewGuid():N}", 1024, "f.pdf"));

        if (classify)
        {
            typeof(Document).GetProperty(nameof(Document.DocumentTypeId))!.SetValue(doc, TypeId(TypeCode));
        }

        fillBag(doc);

        await _documentRepository.InsertAsync(doc, autoSave: true);
    }

    private static Guid FieldId(string name) => new(MD5.HashData(Encoding.UTF8.GetBytes("v3field:" + name)));
    private static Guid TypeId(string typeCode) => new(MD5.HashData(Encoding.UTF8.GetBytes("v3type:" + typeCode)));
}
