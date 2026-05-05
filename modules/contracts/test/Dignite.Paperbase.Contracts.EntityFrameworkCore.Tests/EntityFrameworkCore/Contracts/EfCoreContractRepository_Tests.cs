using System;
using System.Threading.Tasks;
using Dignite.Paperbase.Contracts.Contracts;
using Shouldly;
using Xunit;

namespace Dignite.Paperbase.Contracts.EntityFrameworkCore.Contracts;

public class EfCoreContractRepository_Tests : ContractsEntityFrameworkCoreTestBase
{
    private readonly ContractManager _contractManager;
    private readonly IContractRepository _contractRepository;

    public EfCoreContractRepository_Tests()
    {
        _contractManager = GetRequiredService<ContractManager>();
        _contractRepository = GetRequiredService<IContractRepository>();
    }

    [Fact]
    public async Task Should_Find_Contract_By_DocumentId()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var contract = await _contractManager.CreateAsync(
            documentId,
            ContractsDocumentTypes.General,
            CreateFields());

        await WithUnitOfWorkAsync(async () =>
        {
            await _contractRepository.InsertAsync(contract, autoSave: true);
        });

        // Act
        var found = await WithUnitOfWorkAsync(() =>
            _contractRepository.FindByDocumentIdAsync(documentId));

        // Assert
        found.ShouldNotBeNull();
        found.Id.ShouldBe(contract.Id);
        found.DocumentId.ShouldBe(documentId);
        found.CounterpartyName.ShouldBe("株式会社サンプル");
    }

    [Fact]
    public async Task Should_Return_Null_When_DocumentId_Does_Not_Exist()
    {
        // Act
        var found = await WithUnitOfWorkAsync(() =>
            _contractRepository.FindByDocumentIdAsync(Guid.NewGuid()));

        // Assert
        found.ShouldBeNull();
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
            ReviewStatus = ContractReviewStatus.Confirmed
        };
    }
}
