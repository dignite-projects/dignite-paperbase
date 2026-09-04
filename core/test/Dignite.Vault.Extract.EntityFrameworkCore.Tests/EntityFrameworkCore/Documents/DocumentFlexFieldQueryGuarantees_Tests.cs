using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.Number;
using Dignite.Abp.FlexFields.Text;
using Dignite.Vault.Extract.Documents;
using Dignite.Vault.Extract.Documents.Fields;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Guids;
using Xunit;

namespace Dignite.Vault.Extract.EntityFrameworkCore.Documents;

/// <summary>
/// Query-behaviour guarantees the v3 field-filter pipeline (<see cref="DocumentFieldQueryResolver"/> +
/// <c>IFlexFieldQueryExecutor&lt;Document&gt;</c>) must still hold, ported from v2's
/// <c>EfCoreDocumentRepositorySearch_Tests</c> (#606 test-coverage gap): a resolved match is scoped to the
/// field it was resolved against, a soft-deleted document drops out of the default query the same as any
/// other <see cref="Document"/> query, and stacking filters narrows rather than widens.
/// <para>
/// Kept as a sibling to <see cref="DocumentFlexFieldPipeline_Tests"/> rather than folded into its shared
/// three-field fixture: these three cases each need a second document type (anchoring) or a second field
/// (AND) that fixture does not carry, and growing it would mean six unrelated tests inherit a schema they
/// do not need.
/// </para>
/// </summary>
public class DocumentFlexFieldQueryGuarantees_Tests : VaultExtractEntityFrameworkCoreTestBase
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentTypeRepository _documentTypeRepository;
    private readonly IFieldRepository _fieldRepository;
    private readonly IFieldTypeResolver _fieldTypeResolver;
    private readonly IFlexFieldIndexManager<Document> _indexManager;
    private readonly IFlexFieldQueryExecutor<Document> _queryExecutor;
    private readonly IDataFilter _dataFilter;
    private readonly IGuidGenerator _guidGenerator;

    public DocumentFlexFieldQueryGuarantees_Tests()
    {
        _documentRepository = GetRequiredService<IDocumentRepository>();
        _documentTypeRepository = GetRequiredService<IDocumentTypeRepository>();
        _fieldRepository = GetRequiredService<IFieldRepository>();
        _fieldTypeResolver = GetRequiredService<IFieldTypeResolver>();
        _indexManager = GetRequiredService<IFlexFieldIndexManager<Document>>();
        _queryExecutor = GetRequiredService<IFlexFieldQueryExecutor<Document>>();
        _dataFilter = GetRequiredService<IDataFilter>();
        _guidGenerator = GetRequiredService<IGuidGenerator>();
    }

    /// <summary>
    /// A field is bound to exactly one document type (field-architecture.md), so two types that each
    /// define a same-named field own two distinct <see cref="Field"/> rows with distinct ids. Resolving a
    /// filter against one type's field yields a condition keyed on that type's field id; a same-named
    /// field under a different type indexes its values under its own, different id, so it can never
    /// satisfy that condition. This is what v2 needed an explicit <c>documentTypeId</c> query parameter
    /// for; in v3 it falls out of resolving the field per document type in the first place, so this test
    /// deliberately runs <see cref="DocumentFieldQueryResolver.ResolveAsync"/> +
    /// <see cref="IFlexFieldQueryExecutor{TEntity}.ApplyFilterAsync"/> with no extra
    /// <c>Document.DocumentTypeId</c> predicate layered on top, to prove the anchoring holds without one.
    /// </summary>
    [Fact]
    public async Task Field_match_anchors_to_document_type()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var contractTypeId = await CreateDocumentTypeAsync("contract.general");
            var invoiceTypeId = await CreateDocumentTypeAsync("invoice.general");
            var contractParty = await CreateFieldAsync(contractTypeId, "party");
            var invoiceParty = await CreateFieldAsync(invoiceTypeId, "party");

            var contractDocId = await CreateAndIndexDocumentAsync(
                contractTypeId, (contractParty.Name, "Acme"));
            // Same field name, same value, but a document of a different type carrying a distinct Field row.
            await CreateAndIndexDocumentAsync(invoiceTypeId, (invoiceParty.Name, "Acme"));

            var conditions = await DocumentFieldQueryResolver.ResolveAsync(
                _fieldRepository, _fieldTypeResolver,
                new List<DocumentFieldFilter> { new() { Name = "party", Value = "Acme" } },
                contractTypeId, "contract.general");

            var query = await _documentRepository.GetQueryableAsync();
            var filtered = await _queryExecutor.ApplyFilterAsync(query, conditions);
            var ids = await filtered.Select(d => d.Id).ToListAsync();

            ids.ShouldHaveSingleItem().ShouldBe(contractDocId);
        });
    }

    /// <summary>
    /// The executor composes its subquery onto whatever base query the caller hands it — in production,
    /// <c>_documentRepository.GetQueryableAsync()</c>, which already carries ABP's <c>ISoftDelete</c>
    /// global filter. A deleted document must therefore drop out of a field-filtered result the same way
    /// it drops out of any other document query, and come back only inside an explicit
    /// <c>IDataFilter.Disable&lt;ISoftDelete&gt;()</c> scope (the recycle-bin path).
    /// </summary>
    [Fact]
    public async Task Soft_deleted_documents_are_excluded_by_default()
    {
        Guid id = default;
        List<FlexFieldQueryCondition> conditions = null!;

        await WithUnitOfWorkAsync(async () =>
        {
            var typeId = await CreateDocumentTypeAsync("host.soft-delete-guard");
            var party = await CreateFieldAsync(typeId, "party");
            id = await CreateAndIndexDocumentAsync(typeId, (party.Name, "Acme"));
            conditions = new List<FlexFieldQueryCondition>
            {
                new(party.Id, party.Name, FlexFieldQueryOperator.Equals, "Acme", FlexFieldValueType.String)
            };
        });

        (await MatchAsync(conditions)).ShouldHaveSingleItem().ShouldBe(id);

        await WithUnitOfWorkAsync(() => _documentRepository.DeleteAsync(id, autoSave: true));

        (await MatchAsync(conditions)).ShouldBeEmpty();

        await WithUnitOfWorkAsync(async () =>
        {
            using (_dataFilter.Disable<ISoftDelete>())
            {
                var matched = await MatchAsync(conditions);
                matched.ShouldHaveSingleItem().ShouldBe(id);
            }
        });
    }

    /// <summary>
    /// Different fields narrow each other, the same way v2's per-field <c>EXISTS</c> subqueries did when
    /// composed together: a document must satisfy every condition, not merely one of them.
    /// </summary>
    [Fact]
    public async Task Multiple_field_filters_are_ANDed()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var typeId = await CreateDocumentTypeAsync("host.and-guard");
            var party = await CreateFieldAsync(typeId, "party");
            var amount = await CreateFieldAsync(typeId, "amount", NumberFieldType.ControlName);

            var both = await CreateAndIndexDocumentAsync(
                typeId, (party.Name, "Acme"), (amount.Name, 300m));
            // Satisfies only the party condition -> AND across fields must exclude it.
            await CreateAndIndexDocumentAsync(typeId, (party.Name, "Acme"), (amount.Name, 999m));
            // Satisfies only the amount condition.
            await CreateAndIndexDocumentAsync(typeId, (party.Name, "Globex"), (amount.Name, 300m));

            var conditions = new List<FlexFieldQueryCondition>
            {
                new(party.Id, party.Name, FlexFieldQueryOperator.Equals, "Acme", FlexFieldValueType.String),
                new(amount.Id, amount.Name, FlexFieldQueryOperator.Equals, "300", FlexFieldValueType.Number)
            };

            (await MatchAsync(conditions)).ShouldHaveSingleItem().ShouldBe(both);
        });
    }

    // --- helpers ---

    private async Task<List<Guid>> MatchAsync(List<FlexFieldQueryCondition> conditions)
    {
        List<Guid> ids = null!;
        await WithUnitOfWorkAsync(async () =>
        {
            var query = await _documentRepository.GetQueryableAsync();
            var filtered = await _queryExecutor.ApplyFilterAsync(query, conditions);
            ids = await filtered.Select(d => d.Id).ToListAsync();
        });
        return ids;
    }

    private async Task<Guid> CreateDocumentTypeAsync(string typeCode)
    {
        var id = _guidGenerator.Create();
        await _documentTypeRepository.InsertAsync(
            new DocumentType(id, null, typeCode, typeCode), autoSave: true);
        return id;
    }

    private async Task<Field> CreateFieldAsync(
        Guid documentTypeId, string name, string fieldTypeName = TextFieldType.ControlName)
    {
        var field = new Field(
            _guidGenerator.Create(), null, documentTypeId,
            name: name, displayName: name, fieldTypeName: fieldTypeName);
        await _fieldRepository.InsertAsync(field, autoSave: true);
        return field;
    }

    /// <summary>
    /// Inserts a classified document carrying the given bag values and synchronizes the index in the same
    /// unit of work — the same order <c>FieldExtractionService</c> writes in production (#558: every bag
    /// write owes the index a <c>SynchronizeAsync</c> before the unit of work ends).
    /// </summary>
    private async Task<Guid> CreateAndIndexDocumentAsync(
        Guid documentTypeId, params (string Name, object Value)[] fields)
    {
        var id = _guidGenerator.Create();
        var doc = new Document(id, tenantId: null, DocumentTestData.NewFileOrigin(id));
        DocumentTestData.MarkClassified(doc, documentTypeId);
        foreach (var (name, value) in fields)
        {
            doc.SetField(name, value);
        }

        await _documentRepository.InsertAsync(doc, autoSave: true);
        await _indexManager.SynchronizeAsync(doc);
        return id;
    }
}
