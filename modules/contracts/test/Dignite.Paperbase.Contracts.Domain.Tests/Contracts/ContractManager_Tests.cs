using System;
using System.Threading.Tasks;
using Dignite.Paperbase.Contracts.Contracts;
using Shouldly;
using Xunit;

namespace Dignite.Paperbase.Contracts.Contracts;

public class ContractManager_Tests : ContractsDomainTestBase<ContractsDomainTestModule>
{
    private readonly ContractManager _contractManager;

    public ContractManager_Tests()
    {
        _contractManager = GetRequiredService<ContractManager>();
    }

    [Fact]
    public async Task Should_Create_Contract_From_Extracted_Fields()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var fields = CreateFields();

        // Act
        var contract = await _contractManager.CreateAsync(
            documentId,
            ContractsDocumentTypes.General,
            fields);

        // Assert
        contract.Id.ShouldNotBe(Guid.Empty);
        contract.TenantId.ShouldBeNull();
        contract.DocumentId.ShouldBe(documentId);
        contract.DocumentTypeCode.ShouldBe(ContractsDocumentTypes.General);
        contract.Status.ShouldBe(ContractStatus.Draft);
        contract.Title.ShouldBe(fields.Title);
        contract.CounterpartyName.ShouldBe(fields.CounterpartyName);
        contract.TotalAmount.ShouldBe(fields.TotalAmount);
        contract.NeedsReview.ShouldBeFalse();
    }

    [Fact]
    public async Task Should_Update_Extracted_Fields_And_Confirm()
    {
        // Arrange
        var contract = await _contractManager.CreateAsync(
            Guid.NewGuid(),
            ContractsDocumentTypes.General,
            CreateFields());

        var updatedFields = CreateFields();
        updatedFields.Title = "秘密保持契約書";
        updatedFields.CounterpartyName = "株式会社アップデート";
        updatedFields.TotalAmount = 250000m;
        updatedFields.NeedsReview = true;

        // Act
        contract.UpdateExtractedFields(updatedFields);

        // Assert
        contract.Title.ShouldBe("秘密保持契約書");
        contract.CounterpartyName.ShouldBe("株式会社アップデート");
        contract.TotalAmount.ShouldBe(250000m);
        contract.NeedsReview.ShouldBeTrue();

        // Act
        contract.Confirm();

        // Assert
        contract.NeedsReview.ShouldBeFalse();
        contract.Status.ShouldBe(ContractStatus.Active);
    }

    [Fact]
    public async Task Should_Archive_And_Restore_As_Draft_For_Document_Recycle_Bin()
    {
        var contract = await _contractManager.CreateAsync(
            Guid.NewGuid(),
            ContractsDocumentTypes.General,
            CreateFields());
        contract.Confirm();

        contract.ArchiveBecauseDocumentDeleted();
        contract.Status.ShouldBe(ContractStatus.Archived);

        contract.RestoreBecauseDocumentRestored();
        contract.Status.ShouldBe(ContractStatus.Draft);
        contract.NeedsReview.ShouldBeTrue();
    }

    private static ExtractedContractFields CreateFields()
    {
        return new ExtractedContractFields
        {
            Title = "業務委託契約書",
            ContractNumber = "CNT-2026-001",
            PartyAName = "株式会社ディグナイト",
            PartyBName = "株式会社サンプル",
            CounterpartyName = "株式会社サンプル",
            SignedDate = new DateTime(2026, 4, 1),
            EffectiveDate = new DateTime(2026, 4, 1),
            ExpirationDate = new DateTime(2027, 3, 31),
            TotalAmount = 1200000m,
            Currency = "JPY",
            ExtractionConfidence = 0.9,
            NeedsReview = false
        };
    }
}
