using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.CKEditor;
using Dignite.Abp.FlexFields.Text;
using Dignite.Vault.Extract.Abstractions.Documents;
using Dignite.Vault.Extract.Documents;
using Dignite.Vault.Extract.Documents.DocumentTypes;
using Dignite.Vault.Extract.Documents.Fields;
using Dignite.Vault.Extract.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;
using Xunit;

namespace Dignite.Vault.Extract.EntityFrameworkCore.Documents.Fields;

[DependsOn(typeof(VaultExtractEntityFrameworkCoreTestModule))]
public class FieldTypeCatalogAndSearchabilityTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // DocumentAppService's wider constructor graph needs a blob container even though nothing here
        // reads or writes blob content — same substitute CabinetDeleteTestModule uses for the same reason.
        context.Services.AddSingleton(Substitute.For<IBlobContainer<VaultExtractDocumentContainer>>());
    }
}

/// <summary>
/// #562: <see cref="IFieldDefinitionAppService.GetFieldTypesAsync"/>, the fail-closed
/// <c>IsSearchable</c>-on-a-non-indexable-type guard, and the index rebuild an <c>IsSearchable</c> flip
/// owes the documents that already exist. All three exercise the real EF-backed stack: asserting the
/// index manager was *called* would not catch a rebuild that ran and derived nothing.
/// </summary>
public class FieldTypeCatalogAndSearchability_Tests
    : VaultExtractTestBase<FieldTypeCatalogAndSearchabilityTestModule>
{
    private readonly IFieldDefinitionAppService _fieldAppService;
    private readonly IDocumentTypeAppService _typeAppService;
    private readonly IDocumentTypeRepository _documentTypeRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly IFlexFieldIndexManager<Document> _indexManager;
    private readonly IDbContextProvider<VaultExtractDbContext> _dbContextProvider;

    public FieldTypeCatalogAndSearchability_Tests()
    {
        _fieldAppService = GetRequiredService<IFieldDefinitionAppService>();
        _typeAppService = GetRequiredService<IDocumentTypeAppService>();
        _documentTypeRepository = GetRequiredService<IDocumentTypeRepository>();
        _documentRepository = GetRequiredService<IDocumentRepository>();
        _indexManager = GetRequiredService<IFlexFieldIndexManager<Document>>();
        _dbContextProvider = GetRequiredService<IDbContextProvider<VaultExtractDbContext>>();
    }

    [Fact]
    public async Task GetFieldTypesAsync_Reports_Indexability_From_The_Kernel_Not_A_Hand_Copied_List()
    {
        var fieldTypes = await _fieldAppService.GetFieldTypesAsync();

        var byName = fieldTypes.ToDictionary(t => t.Name);

        // Exactly the #559 decision list, not everything the kernel resolver knows about: it also
        // registers Tree unconditionally as one of its own built-ins, and Vault Extract never wired
        // support for it (no branch in FlexFieldValueReader / FlexFieldValueSchemaBuilder /
        // FlexFieldValueJsonWriter). If this assertion ever gains "Tree", the allow-list filter in
        // GetFieldTypesAsync silently stopped running.
        byName.Keys.ShouldBe(
            new[] { "Text", "Number", "Boolean", "DateTime", "Select", "CKEditor", "Tags" },
            ignoreOrder: true);

        // CKEditor is the one type with no query-index slot; every other built-in plus Vault Extract's own
        // Tags decomposes into index rows.
        byName["CKEditor"].Indexable.ShouldBeFalse();
        foreach (var name in new[] { "Text", "Number", "Boolean", "DateTime", "Select", "Tags" })
        {
            byName[name].Indexable.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task CreateAsync_Rejects_Searchable_On_A_Type_With_No_Index_Slot()
    {
        var type = await CreateTypeAsync();

        var ex = await Should.ThrowAsync<BusinessException>(() => _fieldAppService.CreateAsync(
            new CreateFieldDefinitionDto
            {
                DocumentTypeId = type.Id,
                Name = "body",
                DisplayName = "Body",
                FieldTypeName = CKEditorFieldType.ControlName,
                IsSearchable = true,
            }));

        ex.Code.ShouldBe(VaultExtractErrorCodes.FieldDefinition.FieldTypeNotSearchable);
    }

    [Fact]
    public async Task UpdateAsync_Rejects_Turning_Searchable_On_For_A_Type_With_No_Index_Slot()
    {
        var type = await CreateTypeAsync();
        var field = await _fieldAppService.CreateAsync(new CreateFieldDefinitionDto
        {
            DocumentTypeId = type.Id,
            Name = "body",
            DisplayName = "Body",
            FieldTypeName = CKEditorFieldType.ControlName,
            IsSearchable = false,
        });

        var ex = await Should.ThrowAsync<BusinessException>(() => _fieldAppService.UpdateAsync(
            field.Id,
            new UpdateFieldDefinitionDto
            {
                Name = field.Name,
                DisplayName = field.DisplayName,
                FieldTypeName = field.FieldTypeName,
                IsSearchable = true,
            }));

        ex.Code.ShouldBe(VaultExtractErrorCodes.FieldDefinition.FieldTypeNotSearchable);
    }

    /// <summary>
    /// The bug this pins: <c>FieldDefinitionAppService.UpdateAsync</c> used to flip
    /// <see cref="Field.IsSearchable"/> with no reindex, so a field turned searchable stayed invisible to
    /// every filter until something else happened to trigger a rebuild. The seed document is synchronized
    /// to the index exactly once, before the flip — while the field is still not searchable, so that sync
    /// deliberately produces no row. Only the flip's own rebuild may put one there.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_Reindexes_Existing_Values_When_Searchable_Turns_On()
    {
        var type = await CreateTypeAsync();
        var field = await _fieldAppService.CreateAsync(new CreateFieldDefinitionDto
        {
            DocumentTypeId = type.Id,
            Name = "amount",
            DisplayName = "Amount",
            FieldTypeName = TextFieldType.ControlName,
            IsSearchable = false,
        });

        var documentId = await SeedDocumentWithValueAsync(type.TypeCode, field.Name!, "1000");

        // Not searchable yet, and already synchronized once: the value is in the bag, but the sync had
        // nothing to index while the field was non-searchable.
        (await IndexRowCountAsync(documentId, field.Id)).ShouldBe(0);

        await _fieldAppService.UpdateAsync(field.Id, new UpdateFieldDefinitionDto
        {
            Name = field.Name,
            DisplayName = field.DisplayName,
            FieldTypeName = field.FieldTypeName,
            IsSearchable = true,
        });

        // The document's bag was never touched again — only the field definition changed. A row here can
        // only have come from UpdateAsync's own rebuild.
        (await IndexRowCountAsync(documentId, field.Id)).ShouldBe(1);
    }

    /// <summary>The other direction: a field turned off must not leave a query-index row nothing will ever clean up again.</summary>
    [Fact]
    public async Task UpdateAsync_Drops_Stale_Index_Rows_When_Searchable_Turns_Off()
    {
        var type = await CreateTypeAsync();
        var field = await _fieldAppService.CreateAsync(new CreateFieldDefinitionDto
        {
            DocumentTypeId = type.Id,
            Name = "amount",
            DisplayName = "Amount",
            FieldTypeName = TextFieldType.ControlName,
            IsSearchable = true,
        });

        var documentId = await SeedDocumentWithValueAsync(type.TypeCode, field.Name!, "1000");
        (await IndexRowCountAsync(documentId, field.Id)).ShouldBe(1);

        await _fieldAppService.UpdateAsync(field.Id, new UpdateFieldDefinitionDto
        {
            Name = field.Name,
            DisplayName = field.DisplayName,
            FieldTypeName = field.FieldTypeName,
            IsSearchable = false,
        });

        (await IndexRowCountAsync(documentId, field.Id)).ShouldBe(0);
    }

    private async Task<DocumentTypeDto> CreateTypeAsync()
        => await _typeAppService.CreateAsync(new CreateDocumentTypeDto
        {
            TypeCode = $"host.searchability-{Guid.NewGuid():N}",
            DisplayName = "Searchability test",
        });

    /// <summary>Seeds one document holding <paramref name="value"/> under <paramref name="fieldName"/>, synchronized to the index exactly once under the field's current searchability. Returns the document id.</summary>
    private async Task<Guid> SeedDocumentWithValueAsync(string typeCode, string fieldName, string value)
    {
        var documentId = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            var type = await _documentTypeRepository.FindByTypeCodeAsync(typeCode);
            var doc = new Document(documentId, tenantId: null, DocumentTestData.NewFileOrigin(documentId));
            DocumentTestData.MarkClassified(doc, type!.Id);
            doc.SetFlexFields(new Dictionary<string, object?> { [fieldName] = value });
            await _documentRepository.InsertAsync(doc, autoSave: true);
            await _indexManager.SynchronizeAsync(doc);
        });
        return documentId;
    }

    private async Task<int> IndexRowCountAsync(Guid documentId, Guid fieldId)
    {
        var count = 0;
        await WithUnitOfWorkAsync(async () =>
        {
            var context = await _dbContextProvider.GetDbContextAsync();
            count = await context.Set<DocumentFlexFieldIndex>()
                .Where(r => r.DocumentId == documentId && r.FieldId == fieldId)
                .CountAsync();
        });
        return count;
    }
}
