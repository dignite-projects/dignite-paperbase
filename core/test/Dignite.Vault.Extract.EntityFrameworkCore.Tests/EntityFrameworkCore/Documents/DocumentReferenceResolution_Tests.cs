using System.Collections.Generic;
using Dignite.Abp.FlexFields.CKEditor;
using Dignite.Abp.FlexFields.Date;
using Dignite.Abp.FlexFields.Number;
using Dignite.Abp.FlexFields.Text;
using Dignite.Vault.Extract.FlexFields.Tags;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dignite.Vault.Extract.Documents;
using Dignite.Vault.Extract.Documents.DocumentTypes;
using Dignite.Vault.Extract.Documents.Fields;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Modularity;
using Xunit;

namespace Dignite.Vault.Extract.EntityFrameworkCore.Documents;

[DependsOn(typeof(VaultExtractEntityFrameworkCoreTestModule))]
public class DocumentReferenceResolutionTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Use real EF repositories + real AppService DTO assembly. DocumentAppService constructor depends on
        // IBlobContainer; this test does not trigger upload / download, so the substitute only satisfies
        // construction and avoids resolution failure from an unconfigured blob provider.
        context.Services.AddSingleton(Substitute.For<IBlobContainer<VaultExtractDocumentContainer>>());
    }
}

/// <summary>
/// #207 acceptance: TypeCode / Name rename unlock, soft-delete-penetrating join, and field-type change guard,
/// end to end through real EF (SQLite) + AppService DTO assembly. Core proof: internal relations use immutable
/// Ids, so renames do not cascade data rows while exit DTOs transparently reflect current code / name. Archived
/// (soft-deleted) fields can still be resolved by historical document read paths, and fields with extracted
/// values cannot change their field type.
/// </summary>
public class DocumentReferenceResolution_Tests : VaultExtractTestBase<DocumentReferenceResolutionTestModule>
{
    private readonly IDocumentAppService _documentAppService;
    private readonly IDocumentTypeAppService _documentTypeAppService;
    private readonly IFieldDefinitionAppService _fieldDefinitionAppService;
    private readonly IDocumentRepository _documentRepository;
    private readonly Dignite.Abp.FlexFields.IFlexFieldIndexManager<Document> _indexManager;

    public DocumentReferenceResolution_Tests()
    {
        _documentAppService = GetRequiredService<IDocumentAppService>();
        _documentTypeAppService = GetRequiredService<IDocumentTypeAppService>();
        _fieldDefinitionAppService = GetRequiredService<IFieldDefinitionAppService>();
        _documentRepository = GetRequiredService<IDocumentRepository>();
        _indexManager = GetRequiredService<Dignite.Abp.FlexFields.IFlexFieldIndexManager<Document>>();
    }

    [Fact]
    public async Task Rename_TypeCode_And_FieldName_Is_Reflected_In_Dto_Without_Cascade()
    {
        Guid typeId = default;
        Guid fieldId = default;
        var docId = Guid.NewGuid();

        await WithUnitOfWorkAsync(async () =>
        {
            (typeId, fieldId) = await SeedTypeFieldAndDocAsync(
                "contract.general", "amount", NumberFieldType.ControlName, docId, 100m);
        });

        // Before rename: DTO outputs original code / name.
        await WithUnitOfWorkAsync(async () =>
        {
            var dto = await GetDtoAsync("contract.general", docId);
            dto.DocumentTypeCode.ShouldBe("contract.general");
            dto.ExtractedFields.ShouldNotBeNull();
            dto.ExtractedFields!.ShouldContainKey("amount");
        });

        // Rename TypeCode + Name while the field type stays unchanged, so the guard is not triggered. The
        // internal ids stay put and nothing cascades — but in v3 the rename does rewrite the value bags,
        // because the field name is the key each document stores the value under.
        await WithUnitOfWorkAsync(async () =>
        {
            await _documentTypeAppService.UpdateAsync(typeId, new UpdateDocumentTypeDto
            {
                TypeCode = "contract.renamed",
                DisplayName = "Contract",
                ConfidenceThreshold = 0.7,
                Priority = 0
            });
            await _fieldDefinitionAppService.UpdateAsync(fieldId, new UpdateFieldDefinitionDto
            {
                Name = "total_amount",
                DisplayName = "Amount",
                Description = "Extract the amount.",
                FieldTypeName = NumberFieldType.ControlName,
                DisplayOrder = 0,
                IsRequired = false
            });
        });

        // After rename: DTO for the same historical document transparently reflects new code / name by joining the
        // current DocumentType / FieldDefinition.
        await WithUnitOfWorkAsync(async () =>
        {
            var dto = await GetDtoAsync("contract.renamed", docId);
            dto.DocumentTypeCode.ShouldBe("contract.renamed");
            dto.ExtractedFields.ShouldNotBeNull();
            dto.ExtractedFields!.ShouldContainKey("total_amount");
            dto.ExtractedFields.ShouldNotContainKey("amount");
        });
    }

    [Fact]
    public async Task Soft_Deleted_FieldDefinition_Name_Still_Resolves_For_Historical_Document()
    {
        Guid fieldId = default;
        var docId = Guid.NewGuid();

        await WithUnitOfWorkAsync(async () =>
        {
            (_, fieldId) = await SeedTypeFieldAndDocAsync(
                "host.invoice", "partner", TextFieldType.ControlName, docId, "Acme");
        });

        // Soft-delete the field. FieldDefinition deletion uses soft delete and has no in-use guard; field-value
        // rows remain.
        await WithUnitOfWorkAsync(() => _fieldDefinitionAppService.DeleteAsync(fieldId));

        // Historical document read path penetrates the soft-delete join, still resolves the archived field name,
        // and does not drop the key.
        await WithUnitOfWorkAsync(async () =>
        {
            var dto = await GetDtoAsync("host.invoice", docId);
            dto.ExtractedFields.ShouldNotBeNull();
            dto.ExtractedFields!.ShouldContainKey("partner");
        });
    }

    [Fact]
    public async Task Changing_FieldType_With_Existing_Field_Values_Is_Rejected()
    {
        Guid fieldId = default;
        var docId = Guid.NewGuid();

        await WithUnitOfWorkAsync(async () =>
        {
            (_, fieldId) = await SeedTypeFieldAndDocAsync(
                "contract.general", "amount", NumberFieldType.ControlName, docId, 100m);
        });

        // A field with extracted values cannot change its field type. The values stay in the bag either way
        // in v3 — but nothing would render or index them under the new type, which is the same silent
        // disappearance the v2 typed-column guard existed to prevent.
        await WithUnitOfWorkAsync(async () =>
        {
            var ex = await Should.ThrowAsync<BusinessException>(() =>
                _fieldDefinitionAppService.UpdateAsync(fieldId, new UpdateFieldDefinitionDto
                {
                    Name = "amount",
                    DisplayName = "Amount",
                    Description = "Extract the amount.",
                    FieldTypeName = TextFieldType.ControlName,   // Changed from Number to Text.
                    DisplayOrder = 0,
                    IsRequired = false
                }));

            ex.Code.ShouldBe(VaultExtractErrorCodes.FieldDefinition.DataTypeChangeNotAllowed);
        });
    }

    private async Task<(Guid TypeId, Guid FieldId)> SeedTypeFieldAndDocAsync(
        string typeCode, string fieldName, string fieldTypeName, Guid docId, object value)
    {
        var type = await _documentTypeAppService.CreateAsync(new CreateDocumentTypeDto
        {
            TypeCode = typeCode,
            DisplayName = "Type",
            ConfidenceThreshold = 0.7,
            Priority = 0
        });
        var field = await _fieldDefinitionAppService.CreateAsync(new CreateFieldDefinitionDto
        {
            DocumentTypeId = type.Id,
            Name = fieldName,
            DisplayName = "Field",
            Description = "Extract.",
            FieldTypeName = fieldTypeName
        });

        var doc = new Document(
            docId,
            tenantId: null,
            fileOrigin: new FileOrigin($"blobs/{docId:N}.pdf", "test-user", "application/pdf", $"{Guid.NewGuid():N}{Guid.NewGuid():N}", 1024, "f.pdf"));
        typeof(Document).GetProperty(nameof(Document.DocumentTypeId))!.SetValue(doc, type.Id);
        doc.SetFlexFields(new Dictionary<string, object?> { [fieldName] = value });
        await _documentRepository.InsertAsync(doc, autoSave: true);

        // The field-type guard answers "does this field have values" from the derived index, so a seed that
        // skipped this would let a type change through that production would refuse.
        await _indexManager.SynchronizeAsync(doc);

        return (type.Id, field.Id);
    }

    private async Task<DocumentListItemDto> GetDtoAsync(string documentTypeCode, Guid docId)
    {
        var result = await _documentAppService.GetListAsync(
            new GetDocumentListInput { DocumentTypeCode = documentTypeCode });
        return result.Items.Single(d => d.Id == docId);
    }
}
