using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Dignite.Abp.FlexFields;
using Dignite.Vault.Extract.Documents;
using Dignite.Vault.Extract.Documents.Fields;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Guids;
using Xunit;

namespace Dignite.Vault.Extract.EntityFrameworkCore.Documents;

/// <summary>
/// Renaming a field definition rewrites its values' bag key. The assertions that matter are about
/// <i>scope</i>: Vault Extract's fields are unique per <c>(TenantId, DocumentTypeId, Name)</c>, so two
/// document types may each define the same field name, and a rename that is not scoped to one type silently
/// moves the other type's values to a key no definition backs — still indexed, no longer readable, with no
/// record of where they went.
/// </summary>
public class DocumentFieldValueMigrator_Tests : VaultExtractEntityFrameworkCoreTestBase
{
    private const string ContractType = "host.contract";
    private const string ReceiptType = "host.receipt";
    private const string SharedFieldName = "invoice_no";

    private readonly DocumentFieldValueMigrator _migrator;
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentTypeRepository _documentTypeRepository;
    private readonly IDataFilter _dataFilter;
    private readonly IGuidGenerator _guidGenerator;

    public DocumentFieldValueMigrator_Tests()
    {
        _migrator = GetRequiredService<DocumentFieldValueMigrator>();
        _documentRepository = GetRequiredService<IDocumentRepository>();
        _documentTypeRepository = GetRequiredService<IDocumentTypeRepository>();
        _dataFilter = GetRequiredService<IDataFilter>();
        _guidGenerator = GetRequiredService<IGuidGenerator>();
    }

    /// <summary>
    /// The regression this class exists for. Both types legitimately hold a value under
    /// <c>invoice_no</c>; renaming the contract type's field must move exactly one of them.
    /// </summary>
    [Fact]
    public async Task Renaming_moves_only_the_target_document_types_values()
    {
        var contract = _guidGenerator.Create();
        var receipt = _guidGenerator.Create();

        await WithUnitOfWorkAsync(async () =>
        {
            await SeedTypesAsync();
            await SeedDocumentAsync(contract, ContractType, SharedFieldName, "C-001");
            await SeedDocumentAsync(receipt, ReceiptType, SharedFieldName, "R-999");
        });

        var changed = await WithMigrationAsync(ContractType, SharedFieldName, "contract_no");

        changed.ShouldBe(1);

        await WithUnitOfWorkAsync(async () =>
        {
            var contractDoc = (await _documentRepository.FindAsync(contract))!;
            contractDoc.GetField("contract_no").ShouldBe("C-001");
            contractDoc.HasField(SharedFieldName).ShouldBeFalse();

            // The whole point: the receipt is untouched, still readable under its own type's field name.
            var receiptDoc = (await _documentRepository.FindAsync(receipt))!;
            receiptDoc.GetField(SharedFieldName).ShouldBe("R-999");
            receiptDoc.HasField("contract_no").ShouldBeFalse();
        });
    }

    /// <summary>
    /// A recycle-bin document must be renamed too: restoring it later would otherwise bring its values back
    /// under a key the current definition no longer uses, unreachable and with nothing left to name it.
    /// </summary>
    [Fact]
    public async Task Renaming_reaches_recycle_bin_documents()
    {
        var deleted = _guidGenerator.Create();

        await WithUnitOfWorkAsync(async () =>
        {
            await SeedTypesAsync();
            await SeedDocumentAsync(deleted, ContractType, SharedFieldName, "C-002");
        });

        await WithUnitOfWorkAsync(async () => await _documentRepository.DeleteAsync(deleted));

        var changed = await WithMigrationAsync(ContractType, SharedFieldName, "contract_no");

        changed.ShouldBe(1);

        await WithUnitOfWorkAsync(async () =>
        {
            using (_dataFilter.Disable<ISoftDelete>())
            {
                var document = (await _documentRepository.FindAsync(deleted))!;
                document.IsDeleted.ShouldBeTrue();
                document.GetField("contract_no").ShouldBe("C-002");
            }
        });
    }

    /// <summary>
    /// Re-running must converge rather than double-apply: the migrator flushes per page, so a failure part
    /// way through leaves earlier pages migrated and the fix is to run it again.
    /// </summary>
    [Fact]
    public async Task Renaming_is_idempotent()
    {
        var contract = _guidGenerator.Create();

        await WithUnitOfWorkAsync(async () =>
        {
            await SeedTypesAsync();
            await SeedDocumentAsync(contract, ContractType, SharedFieldName, "C-003");
        });

        (await WithMigrationAsync(ContractType, SharedFieldName, "contract_no")).ShouldBe(1);
        // Second pass finds nothing under the old key and reports no change, rather than throwing on the
        // value already sitting under the new one.
        (await WithMigrationAsync(ContractType, SharedFieldName, "contract_no")).ShouldBe(0);

        await WithUnitOfWorkAsync(async () =>
            (await _documentRepository.FindAsync(contract))!.GetField("contract_no").ShouldBe("C-003"));
    }

    private Task<int> WithMigrationAsync(string typeCode, string oldName, string newName)
    {
        var result = 0;
        return WithUnitOfWorkAsync(async () =>
        {
            result = await _migrator.RenameFieldAsync(TypeId(typeCode), oldName, newName);
            return result;
        });
    }

    private async Task SeedTypesAsync()
    {
        await _documentTypeRepository.InsertAsync(
            new DocumentType(TypeId(ContractType), null, ContractType, ContractType), autoSave: true);
        await _documentTypeRepository.InsertAsync(
            new DocumentType(TypeId(ReceiptType), null, ReceiptType, ReceiptType), autoSave: true);
    }

    private async Task SeedDocumentAsync(Guid id, string typeCode, string fieldName, string value)
    {
        var document = new Document(
            id,
            tenantId: null,
            fileOrigin: new FileOrigin($"blobs/{id:N}.pdf", "test-user", "application/pdf",
                $"{Guid.NewGuid():N}{Guid.NewGuid():N}", 1024, "c.pdf"));
        typeof(Document).GetProperty(nameof(Document.DocumentTypeId))!.SetValue(document, TypeId(typeCode));

        document.SetFlexFields(new System.Collections.Generic.Dictionary<string, object?> { [fieldName] = value });

        await _documentRepository.InsertAsync(document, autoSave: true);
    }

    private static Guid TypeId(string typeCode) => new(MD5.HashData(Encoding.UTF8.GetBytes("rename:type:" + typeCode)));
}
