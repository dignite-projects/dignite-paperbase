using System;
using System.IO;
using System.Threading.Tasks;
using Dignite.Paperbase.Contracts.Contracts;
using Dignite.Paperbase.Contracts.Dtos;
using Shouldly;
using Xunit;

namespace Dignite.Paperbase.Contracts.EntityFrameworkCore.Contracts;

public class ContractAppService_Tests : ContractsEntityFrameworkCoreTestBase
{
    private readonly IContractAppService _appService;
    private readonly ContractManager _contractManager;
    private readonly IContractRepository _contractRepository;

    public ContractAppService_Tests()
    {
        _appService = GetRequiredService<IContractAppService>();
        _contractManager = GetRequiredService<ContractManager>();
        _contractRepository = GetRequiredService<IContractRepository>();
    }

    // ────────────────────────────────────────────────────────────────────────────
    // GetAsync
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Should_Get_Contract_By_Id()
    {
        var contract = await CreateAndSaveAsync("甲公司", 1_000_000m, new DateTime(2027, 3, 31), false);

        var dto = await _appService.GetAsync(contract.Id);

        dto.ShouldNotBeNull();
        dto.Id.ShouldBe(contract.Id);
        dto.CounterpartyName.ShouldBe("甲公司");
        dto.TotalAmount.ShouldBe(1_000_000m);
        dto.Status.ShouldBe(ContractStatus.Draft);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // GetListAsync — filters
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetList_Returns_All_When_No_Filter()
    {
        await CreateAndSaveAsync("甲公司", 1_000_000m, new DateTime(2027, 3, 31), false);
        await CreateAndSaveAsync("乙公司", 2_000_000m, new DateTime(2027, 6, 30), false);

        var result = await _appService.GetListAsync(new GetContractListInput { MaxResultCount = 100 });

        result.TotalCount.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetList_Filters_By_CounterpartyKeyword()
    {
        await CreateAndSaveAsync("テスト株式会社", 1_000_000m, new DateTime(2027, 3, 31), false);
        await CreateAndSaveAsync("另一公司", 2_000_000m, new DateTime(2027, 6, 30), false);

        var result = await _appService.GetListAsync(new GetContractListInput
        {
            CounterpartyKeyword = "テスト",
            MaxResultCount = 100
        });

        result.Items.ShouldAllBe(c => c.CounterpartyName!.Contains("テスト"));
        result.Items.ShouldNotContain(c => c.CounterpartyName == "另一公司");
    }

    [Fact]
    public async Task GetList_Filters_By_ExpirationDate_Range()
    {
        await CreateAndSaveAsync("早到期公司", 1_000_000m, new DateTime(2027, 1, 1), false);
        await CreateAndSaveAsync("中到期公司", 2_000_000m, new DateTime(2028, 1, 1), false);
        await CreateAndSaveAsync("晚到期公司", 3_000_000m, new DateTime(2029, 1, 1), false);

        var result = await _appService.GetListAsync(new GetContractListInput
        {
            ExpirationDateFrom = new DateTime(2027, 6, 1),
            ExpirationDateTo = new DateTime(2028, 6, 1),
            MaxResultCount = 100
        });

        result.Items.ShouldContain(c => c.CounterpartyName == "中到期公司");
        result.Items.ShouldNotContain(c => c.CounterpartyName == "早到期公司");
        result.Items.ShouldNotContain(c => c.CounterpartyName == "晚到期公司");
    }

    [Fact]
    public async Task GetList_Filters_By_NeedsReview_True()
    {
        await CreateAndSaveAsync("需审核公司", 1_000_000m, new DateTime(2027, 3, 31), needsReview: true);
        await CreateAndSaveAsync("普通公司", 2_000_000m, new DateTime(2027, 6, 30), needsReview: false);

        var result = await _appService.GetListAsync(new GetContractListInput
        {
            NeedsReview = true,
            MaxResultCount = 100
        });

        result.Items.ShouldAllBe(c => c.NeedsReview);
        result.Items.ShouldContain(c => c.CounterpartyName == "需审核公司");
        result.Items.ShouldNotContain(c => c.CounterpartyName == "普通公司");
    }

    [Fact]
    public async Task GetList_Filters_By_TotalAmount_Range()
    {
        await CreateAndSaveAsync("小额公司", 100_000m, new DateTime(2027, 3, 31), false);
        await CreateAndSaveAsync("中额公司", 500_000m, new DateTime(2027, 3, 31), false);
        await CreateAndSaveAsync("大额公司", 5_000_000m, new DateTime(2027, 3, 31), false);

        var result = await _appService.GetListAsync(new GetContractListInput
        {
            TotalAmountMin = 200_000m,
            TotalAmountMax = 1_000_000m,
            MaxResultCount = 100
        });

        result.Items.ShouldContain(c => c.CounterpartyName == "中额公司");
        result.Items.ShouldNotContain(c => c.CounterpartyName == "小额公司");
        result.Items.ShouldNotContain(c => c.CounterpartyName == "大额公司");
    }

    [Fact]
    public async Task GetList_Filters_By_DocumentId()
    {
        var targetDocId = Guid.NewGuid();
        await CreateAndSaveAsync("目标合同公司", 1_000_000m, new DateTime(2027, 3, 31), false, documentId: targetDocId);
        await CreateAndSaveAsync("其他合同公司", 2_000_000m, new DateTime(2027, 3, 31), false);

        var result = await _appService.GetListAsync(new GetContractListInput
        {
            DocumentId = targetDocId,
            MaxResultCount = 100
        });

        result.TotalCount.ShouldBe(1);
        result.Items[0].CounterpartyName.ShouldBe("目标合同公司");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // UpdateAsync
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Should_Update_Contract_Fields()
    {
        var contract = await CreateAndSaveAsync("旧公司", 1_000_000m, new DateTime(2027, 3, 31), false);

        var dto = await _appService.UpdateAsync(contract.Id, new UpdateContractDto
        {
            Title = "更新后标题",
            CounterpartyName = "新公司",
            TotalAmount = 2_000_000m,
            ExpirationDate = new DateTime(2028, 12, 31)
        });

        dto.Title.ShouldBe("更新后标题");
        dto.CounterpartyName.ShouldBe("新公司");
        dto.TotalAmount.ShouldBe(2_000_000m);
        dto.ExpirationDate.ShouldBe(new DateTime(2028, 12, 31));
    }

    // ────────────────────────────────────────────────────────────────────────────
    // ConfirmAsync
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Should_Confirm_Contract_Changes_Status_To_Active()
    {
        var contract = await CreateAndSaveAsync("待确认公司", 1_000_000m, new DateTime(2027, 3, 31), false);

        var before = await _appService.GetAsync(contract.Id);
        before.Status.ShouldBe(ContractStatus.Draft);

        await _appService.ConfirmAsync(contract.Id);

        var after = await _appService.GetAsync(contract.Id);
        after.Status.ShouldBe(ContractStatus.Active);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // ExportAsync
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Should_Export_Csv_With_Header_And_Row()
    {
        await CreateAndSaveAsync("CSV导出公司", 999_999m, new DateTime(2027, 12, 31), false);

        var remote = await _appService.ExportAsync(new GetContractListInput { MaxResultCount = 100 });

        remote.ShouldNotBeNull();
        remote.ContentType.ShouldBe("text/csv");

        using var reader = new StreamReader(remote.GetStream()!);
        var content = await reader.ReadToEndAsync();

        content.ShouldContain("Id,DocumentId");
        content.ShouldContain("CSV导出公司");
        content.ShouldContain("999999.00");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Helper
    // ────────────────────────────────────────────────────────────────────────────

    private async Task<Contract> CreateAndSaveAsync(
        string counterpartyName,
        decimal totalAmount,
        DateTime expirationDate,
        bool needsReview,
        Guid? documentId = null)
    {
        var contract = await _contractManager.CreateAsync(
            documentId ?? Guid.NewGuid(),
            ContractsDocumentTypes.General,
            new ExtractedContractFields
            {
                Title = $"{counterpartyName}的合同",
                ContractNumber = $"CNT-{Guid.NewGuid():N}".Substring(0, 16),
                PartyAName = "我方公司",
                PartyBName = counterpartyName,
                CounterpartyName = counterpartyName,
                SignedDate = new DateTime(2026, 1, 1),
                EffectiveDate = new DateTime(2026, 1, 1),
                ExpirationDate = expirationDate,
                TotalAmount = totalAmount,
                Currency = "CNY",
                ExtractionConfidence = 0.95,
                NeedsReview = needsReview
            });

        await WithUnitOfWorkAsync(async () =>
        {
            await _contractRepository.InsertAsync(contract, autoSave: true);
        });

        return contract;
    }
}
