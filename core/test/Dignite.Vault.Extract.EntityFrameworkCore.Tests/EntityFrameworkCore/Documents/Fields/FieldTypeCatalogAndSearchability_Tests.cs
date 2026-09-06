using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.CKEditor;
using Dignite.Abp.FlexFields.Number;
using Dignite.Abp.FlexFields.Table;
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
/// index manager was *called* would not catch a rebuild that ran and derived nothing. #626 adds the
/// analogous <c>IsUniqueKey</c>-on-a-non-indexable-type guard, gated by the same predicate.
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

        // Exactly the #559/#625 decision list, not everything the kernel resolver knows about: it also
        // registers Tree and Matrix unconditionally as its own built-ins, and Vault Extract never wired
        // support for either (no IVaultExtractFieldTypeExtension for them). If this assertion ever gains
        // "Tree" or "Matrix", the allow-list filter in GetFieldTypesAsync silently stopped running.
        byName.Keys.ShouldBe(
            new[] { "Text", "Number", "Boolean", "DateTime", "Select", "CKEditor", "Tags", "Table" },
            ignoreOrder: true);

        // CKEditor and Table (#625: a list of composite row objects, not a scalar or list of scalars) are
        // the two types with no query-index slot; every other built-in plus Vault Extract's own Tags
        // decomposes into index rows.
        byName["CKEditor"].Indexable.ShouldBeFalse();
        byName["Table"].Indexable.ShouldBeFalse();
        foreach (var name in new[] { "Text", "Number", "Boolean", "DateTime", "Select", "Tags" })
        {
            byName[name].Indexable.ShouldBeTrue();
        }
    }

    /// <summary>
    /// #625: a Table field's own FieldTypeName is registered, but one of its columns names a
    /// FieldTypeName Vault Extract has never wired Vault-Extract-side support for. Rejected here, at
    /// create time - not merely at the kernel's own generic shape-validation level, and not deferred to
    /// extraction time, where FlexFieldValueSchemaBuilder's own last-resort failure would otherwise be the
    /// first thing to notice, after the field definition already exists.
    /// </summary>
    [Fact]
    public async Task CreateAsync_Rejects_A_Table_Column_With_An_Unregistered_FieldTypeName()
    {
        var type = await CreateTypeAsync();

        var configuration = new TableConfiguration
        {
            Columns = new List<InlineFieldDefinition>
            {
                new() { Name = "item", DisplayName = "Item", FieldTypeName = TextFieldType.ControlName },
                new() { Name = "qty", DisplayName = "Quantity", FieldTypeName = "SomeFutureType" }
            }
        }.ConfigurationDictionary;

        var ex = await Should.ThrowAsync<BusinessException>(() => _fieldAppService.CreateAsync(
            new CreateFieldDefinitionDto
            {
                DocumentTypeId = type.Id,
                Name = "line_items",
                DisplayName = "Line items",
                FieldTypeName = TableFieldType.ControlName,
                Configuration = configuration,
                IsSearchable = false,
            }));

        ex.Code.ShouldBe(VaultExtractErrorCodes.FieldDefinition.UnknownColumnFieldType);
    }

    /// <summary>
    /// #625 follow-up: a Table column's <c>Name</c> is concatenated raw into the LLM's JSON schema message
    /// (<c>TableFieldTypeExtension.BuildExtractionSchema</c> uses it verbatim as a property key) exactly
    /// like a top-level <c>Field.Name</c> is, so it needs the same prompt-injection allow-list
    /// (<c>FieldDefinitionConsts.NamePattern</c>) — rejected here, at create time, not merely accepted and
    /// later concatenated raw into a prompt.
    /// </summary>
    [Fact]
    public async Task CreateAsync_Rejects_A_Table_Column_With_An_Invalid_Name()
    {
        var type = await CreateTypeAsync();

        var configuration = new TableConfiguration
        {
            Columns = new List<InlineFieldDefinition>
            {
                new() { Name = "item", DisplayName = "Item", FieldTypeName = TextFieldType.ControlName },
                new() { Name = "bad name!\n", DisplayName = "Bad", FieldTypeName = TextFieldType.ControlName }
            }
        }.ConfigurationDictionary;

        var ex = await Should.ThrowAsync<BusinessException>(() => _fieldAppService.CreateAsync(
            new CreateFieldDefinitionDto
            {
                DocumentTypeId = type.Id,
                Name = "line_items",
                DisplayName = "Line items",
                FieldTypeName = TableFieldType.ControlName,
                Configuration = configuration,
                IsSearchable = false,
            }));

        ex.Code.ShouldBe(VaultExtractErrorCodes.FieldDefinition.InvalidColumnName);
    }

    /// <summary>A Table field whose every column IS registered saves normally - the positive counterpart above.</summary>
    [Fact]
    public async Task CreateAsync_Accepts_A_Table_Field_With_Registered_Columns()
    {
        var type = await CreateTypeAsync();

        var configuration = new TableConfiguration
        {
            Columns = new List<InlineFieldDefinition>
            {
                new() { Name = "item", DisplayName = "Item", FieldTypeName = TextFieldType.ControlName, Required = true },
                new() { Name = "qty", DisplayName = "Quantity", FieldTypeName = "Number" }
            }
        }.ConfigurationDictionary;

        var field = await _fieldAppService.CreateAsync(new CreateFieldDefinitionDto
        {
            DocumentTypeId = type.Id,
            Name = "line_items",
            DisplayName = "Line items",
            FieldTypeName = TableFieldType.ControlName,
            Configuration = configuration,
            IsSearchable = false,
        });

        field.FieldTypeName.ShouldBe(TableFieldType.ControlName);
    }

    /// <summary>
    /// #625 follow-up: before the recursive nesting-depth gate existed, only the IMMEDIATE columns'
    /// FieldTypeName were checked against the registry - a column that was itself composite (Table) was
    /// never recursed into, so an unregistered type nested two levels deep (Table -&gt; Table column -&gt; bad
    /// grandchild column) passed this gate and would only fail later, uncaught, inside
    /// TableFieldTypeExtension.BuildExtractionSchema's own defensive NotSupportedException. Rejected here,
    /// at create time, is the regression test for that gap.
    /// </summary>
    [Fact]
    public async Task CreateAsync_Rejects_An_Unregistered_Column_Type_Nested_Two_Levels_Deep()
    {
        var type = await CreateTypeAsync();

        var ex = await Should.ThrowAsync<BusinessException>(() => _fieldAppService.CreateAsync(
            new CreateFieldDefinitionDto
            {
                DocumentTypeId = type.Id,
                Name = "line_items",
                DisplayName = "Line items",
                FieldTypeName = TableFieldType.ControlName,
                Configuration = NestedTableConfiguration(2, "SomeFutureType"),
                IsSearchable = false,
            }));

        ex.Code.ShouldBe(VaultExtractErrorCodes.FieldDefinition.UnknownColumnFieldType);
    }

    /// <summary>A configuration nesting composite types exactly to CompositeFieldNesting.MaxDepth (Table &gt; Table &gt; Text, depth 3) saves normally.</summary>
    [Fact]
    public async Task CreateAsync_Accepts_A_Nested_Table_At_The_Max_Depth()
    {
        var type = await CreateTypeAsync();

        var field = await _fieldAppService.CreateAsync(new CreateFieldDefinitionDto
        {
            DocumentTypeId = type.Id,
            Name = "line_items",
            DisplayName = "Line items",
            FieldTypeName = TableFieldType.ControlName,
            Configuration = NestedTableConfiguration(2),
            IsSearchable = false,
        });

        field.FieldTypeName.ShouldBe(TableFieldType.ControlName);
    }

    /// <summary>
    /// One level past CompositeFieldNesting.MaxDepth (Table &gt; Table &gt; Table &gt; Text, depth 4) is
    /// refused before anything recurses into the configuration itself.
    /// </summary>
    [Fact]
    public async Task CreateAsync_Rejects_A_Nested_Table_Exceeding_The_Max_Depth()
    {
        var type = await CreateTypeAsync();

        var ex = await Should.ThrowAsync<BusinessException>(() => _fieldAppService.CreateAsync(
            new CreateFieldDefinitionDto
            {
                DocumentTypeId = type.Id,
                Name = "line_items",
                DisplayName = "Line items",
                FieldTypeName = TableFieldType.ControlName,
                Configuration = NestedTableConfiguration(3),
                IsSearchable = false,
            }));

        ex.Code.ShouldBe(VaultExtractErrorCodes.FieldDefinition.CompositeNestingTooDeep);
    }

    /// <summary>
    /// #625 follow-up (Option A: block, not migrate). A Table field's own FieldTypeName can stay "Table"
    /// while its COLUMNS still change shape underneath it - renaming, removing, adding, or reordering a
    /// column. Any of those orphans the cell data an already-extracted document holds under the old shape,
    /// exactly like a top-level type change would - so it is blocked the same way, with the same error
    /// code (<c>DataTypeChangeNotAllowed</c>): "this field's shape changed under stored values" is one
    /// rule, not two.
    /// </summary>
    [Theory]
    [InlineData("rename")]
    [InlineData("remove")]
    [InlineData("add")]
    [InlineData("reorder")]
    public async Task UpdateAsync_Blocks_A_Table_Column_Change_When_The_Field_Has_Values(string change)
    {
        var type = await CreateTypeAsync();
        var field = await _fieldAppService.CreateAsync(new CreateFieldDefinitionDto
        {
            DocumentTypeId = type.Id,
            Name = "line_items",
            DisplayName = "Line items",
            FieldTypeName = TableFieldType.ControlName,
            Configuration = new TableConfiguration
            {
                Columns = new List<InlineFieldDefinition>
                {
                    new() { Name = "item", DisplayName = "Item", FieldTypeName = TextFieldType.ControlName },
                    new() { Name = "qty", DisplayName = "Quantity", FieldTypeName = NumberFieldType.ControlName }
                }
            }.ConfigurationDictionary,
            IsSearchable = false,
        });

        await SeedDocumentWithValueAsync(type.TypeCode, field.Name!, "placeholder");

        var changedColumns = change switch
        {
            "rename" => new List<InlineFieldDefinition>
            {
                new() { Name = "item_name", DisplayName = "Item", FieldTypeName = TextFieldType.ControlName },
                new() { Name = "qty", DisplayName = "Quantity", FieldTypeName = NumberFieldType.ControlName }
            },
            "remove" => new List<InlineFieldDefinition>
            {
                new() { Name = "item", DisplayName = "Item", FieldTypeName = TextFieldType.ControlName }
            },
            "add" => new List<InlineFieldDefinition>
            {
                new() { Name = "item", DisplayName = "Item", FieldTypeName = TextFieldType.ControlName },
                new() { Name = "qty", DisplayName = "Quantity", FieldTypeName = NumberFieldType.ControlName },
                new() { Name = "note", DisplayName = "Note", FieldTypeName = TextFieldType.ControlName }
            },
            "reorder" => new List<InlineFieldDefinition>
            {
                new() { Name = "qty", DisplayName = "Quantity", FieldTypeName = NumberFieldType.ControlName },
                new() { Name = "item", DisplayName = "Item", FieldTypeName = TextFieldType.ControlName }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };

        var ex = await Should.ThrowAsync<BusinessException>(() => _fieldAppService.UpdateAsync(
            field.Id,
            new UpdateFieldDefinitionDto
            {
                Name = field.Name,
                DisplayName = field.DisplayName,
                FieldTypeName = field.FieldTypeName,
                Configuration = new TableConfiguration { Columns = changedColumns }.ConfigurationDictionary,
                IsSearchable = false,
            }));

        ex.Code.ShouldBe(VaultExtractErrorCodes.FieldDefinition.DataTypeChangeNotAllowed);
    }

    /// <summary>A genuinely fresh Table field (no values yet) stays freely editable - the guard only fires once some document actually holds a value.</summary>
    [Fact]
    public async Task UpdateAsync_Allows_A_Table_Column_Change_When_The_Field_Has_No_Values()
    {
        var type = await CreateTypeAsync();
        var field = await _fieldAppService.CreateAsync(new CreateFieldDefinitionDto
        {
            DocumentTypeId = type.Id,
            Name = "line_items",
            DisplayName = "Line items",
            FieldTypeName = TableFieldType.ControlName,
            Configuration = new TableConfiguration
            {
                Columns = new List<InlineFieldDefinition>
                {
                    new() { Name = "item", DisplayName = "Item", FieldTypeName = TextFieldType.ControlName }
                }
            }.ConfigurationDictionary,
            IsSearchable = false,
        });

        var updated = await _fieldAppService.UpdateAsync(field.Id, new UpdateFieldDefinitionDto
        {
            Name = field.Name,
            DisplayName = field.DisplayName,
            FieldTypeName = field.FieldTypeName,
            Configuration = new TableConfiguration
            {
                Columns = new List<InlineFieldDefinition>
                {
                    new() { Name = "item_name", DisplayName = "Item", FieldTypeName = TextFieldType.ControlName }
                }
            }.ConfigurationDictionary,
            IsSearchable = false,
        });

        updated.FieldTypeName.ShouldBe(TableFieldType.ControlName);
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
    /// #626: the same fail-closed guard as <c>FieldTypeNotSearchable</c>, gating <c>IsUniqueKey</c> instead
    /// of <c>IsSearchable</c> against the same indexability predicate. A Table/CKEditor value has no query
    /// index, so duplicate-detection fingerprinting could never usefully identify a document by it.
    /// </summary>
    [Fact]
    public async Task CreateAsync_Rejects_UniqueKey_On_A_Type_With_No_Index_Slot()
    {
        var type = await CreateTypeAsync();

        var ex = await Should.ThrowAsync<BusinessException>(() => _fieldAppService.CreateAsync(
            new CreateFieldDefinitionDto
            {
                DocumentTypeId = type.Id,
                Name = "body",
                DisplayName = "Body",
                FieldTypeName = CKEditorFieldType.ControlName,
                IsSearchable = false,
                IsUniqueKey = true,
            }));

        ex.Code.ShouldBe(VaultExtractErrorCodes.FieldDefinition.FieldTypeNotUniqueKeyable);
    }

    [Fact]
    public async Task UpdateAsync_Rejects_Turning_UniqueKey_On_For_A_Type_With_No_Index_Slot()
    {
        var type = await CreateTypeAsync();
        var field = await _fieldAppService.CreateAsync(new CreateFieldDefinitionDto
        {
            DocumentTypeId = type.Id,
            Name = "body",
            DisplayName = "Body",
            FieldTypeName = CKEditorFieldType.ControlName,
            IsSearchable = false,
            IsUniqueKey = false,
        });

        var ex = await Should.ThrowAsync<BusinessException>(() => _fieldAppService.UpdateAsync(
            field.Id,
            new UpdateFieldDefinitionDto
            {
                Name = field.Name,
                DisplayName = field.DisplayName,
                FieldTypeName = field.FieldTypeName,
                IsSearchable = false,
                IsUniqueKey = true,
            }));

        ex.Code.ShouldBe(VaultExtractErrorCodes.FieldDefinition.FieldTypeNotUniqueKeyable);
    }

    /// <summary>A Table field cannot be a unique key either: composite rows have no index slot the same way CKEditor's long text has none.</summary>
    [Fact]
    public async Task CreateAsync_Rejects_UniqueKey_On_A_Table_Field()
    {
        var type = await CreateTypeAsync();

        var ex = await Should.ThrowAsync<BusinessException>(() => _fieldAppService.CreateAsync(
            new CreateFieldDefinitionDto
            {
                DocumentTypeId = type.Id,
                Name = "line_items",
                DisplayName = "Line items",
                FieldTypeName = TableFieldType.ControlName,
                Configuration = new TableConfiguration
                {
                    Columns = new List<InlineFieldDefinition>
                    {
                        new() { Name = "item", DisplayName = "Item", FieldTypeName = TextFieldType.ControlName }
                    }
                }.ConfigurationDictionary,
                IsSearchable = false,
                IsUniqueKey = true,
            }));

        ex.Code.ShouldBe(VaultExtractErrorCodes.FieldDefinition.FieldTypeNotUniqueKeyable);
    }

    /// <summary>No regression: an indexable type still saves normally with IsUniqueKey = true.</summary>
    [Fact]
    public async Task CreateAsync_Accepts_UniqueKey_On_An_Indexable_Type()
    {
        var type = await CreateTypeAsync();

        var field = await _fieldAppService.CreateAsync(new CreateFieldDefinitionDto
        {
            DocumentTypeId = type.Id,
            Name = "code",
            DisplayName = "Code",
            FieldTypeName = TextFieldType.ControlName,
            IsUniqueKey = true,
        });

        field.IsUniqueKey.ShouldBeTrue();
    }

    /// <summary>No regression: turning IsUniqueKey on for an already-indexable field still succeeds.</summary>
    [Fact]
    public async Task UpdateAsync_Accepts_Turning_UniqueKey_On_For_An_Indexable_Type()
    {
        var type = await CreateTypeAsync();
        var field = await _fieldAppService.CreateAsync(new CreateFieldDefinitionDto
        {
            DocumentTypeId = type.Id,
            Name = "code",
            DisplayName = "Code",
            FieldTypeName = TextFieldType.ControlName,
            IsUniqueKey = false,
        });

        var updated = await _fieldAppService.UpdateAsync(field.Id, new UpdateFieldDefinitionDto
        {
            Name = field.Name,
            DisplayName = field.DisplayName,
            FieldTypeName = field.FieldTypeName,
            IsUniqueKey = true,
        });

        updated.IsUniqueKey.ShouldBeTrue();
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

    /// <summary>
    /// A Table whose single column is a Table whose single column is... <paramref name="levels"/> deep,
    /// bottoming out in a column of <paramref name="leafFieldTypeName"/>. Mirrors the kernel's own
    /// <c>CompositeFieldNesting_Tests.NestedTables</c> helper, which is internal to the flex-fields test
    /// assembly and so cannot be reused directly. <c>levels=1</c> is a Table with one leaf column (depth 2
    /// as a field of this type); <c>levels=2</c> reaches depth 3 (at <c>CompositeFieldNesting.MaxDepth</c>);
    /// <c>levels=3</c> reaches depth 4 (one past it).
    /// </summary>
    private static FieldConfigurationDictionary NestedTableConfiguration(int levels, string leafFieldTypeName = TextFieldType.ControlName)
    {
        if (levels <= 1)
        {
            return new TableConfiguration
            {
                Columns = new List<InlineFieldDefinition>
                {
                    new() { Name = "label", DisplayName = "Label", FieldTypeName = leafFieldTypeName }
                }
            }.ConfigurationDictionary;
        }

        return new TableConfiguration
        {
            Columns = new List<InlineFieldDefinition>
            {
                new()
                {
                    Name = "nested",
                    DisplayName = "Nested",
                    FieldTypeName = TableFieldType.ControlName,
                    Configuration = NestedTableConfiguration(levels - 1, leafFieldTypeName)
                }
            }
        }.ConfigurationDictionary;
    }

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
