using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dignite.Paperbase.Contracts.Contracts;
using Dignite.Paperbase.Contracts.Dtos;
using Dignite.Paperbase.Contracts.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Content;

namespace Dignite.Paperbase.Contracts;

public class ContractAppService : ContractsAppService, IContractAppService
{
    private readonly IContractRepository _contractRepository;
    private readonly ContractToContractDtoMapper _mapper;
    private readonly IContractExtractionCorrectionRecorder _correctionRecorder;

    public ContractAppService(
        IContractRepository contractRepository,
        ContractToContractDtoMapper mapper,
        IContractExtractionCorrectionRecorder correctionRecorder)
    {
        _contractRepository = contractRepository;
        _mapper = mapper;
        _correctionRecorder = correctionRecorder;
    }

    public virtual async Task<ContractDto> GetAsync(Guid id)
    {
        var contract = await _contractRepository.GetAsync(id);
        return _mapper.Map(contract);
    }

    public virtual async Task<PagedResultDto<ContractDto>> GetListAsync(GetContractListInput input)
    {
        var query = await _contractRepository.GetQueryableAsync();
        query = ApplyFilter(query, input);

        var totalCount = await AsyncExecuter.CountAsync(query);

        query = ApplySorting(query, input.Sorting);
        query = query.Skip(input.SkipCount).Take(input.MaxResultCount);

        var contracts = await AsyncExecuter.ToListAsync(query);

        return new PagedResultDto<ContractDto>(
            totalCount,
            contracts.Select(_mapper.Map).ToList());
    }

    [Authorize(ContractsPermissions.Contracts.Update)]
    public virtual async Task<ContractDto> UpdateAsync(Guid id, UpdateContractDto input)
    {
        var contract = await _contractRepository.GetAsync(id);
        var previousFields = CreateFieldsSnapshot(contract);
        var correctedFields = new ExtractedContractFields
        {
            Title = input.Title,
            ContractNumber = input.ContractNumber,
            PartyAName = input.PartyAName,
            PartyBName = input.PartyBName,
            CounterpartyName = input.CounterpartyName,
            SignedDate = input.SignedDate,
            EffectiveDate = input.EffectiveDate,
            ExpirationDate = input.ExpirationDate,
            TotalAmount = input.TotalAmount,
            Currency = input.Currency,
            ExtractionConfidence = 1.0,
            ReviewStatus = ContractReviewStatus.Corrected
        };

        contract.CorrectExtractedFields(correctedFields);
        await _correctionRecorder.RecordAsync(new ContractExtractionCorrectionContext
        {
            ContractId = contract.Id,
            DocumentId = contract.DocumentId,
            DocumentTypeCode = contract.DocumentTypeCode,
            PreviousFields = previousFields,
            CorrectedFields = correctedFields
        });

        await _contractRepository.UpdateAsync(contract, autoSave: true);
        return _mapper.Map(contract);
    }

    [Authorize(ContractsPermissions.Contracts.Confirm)]
    public virtual async Task ConfirmAsync(Guid id)
    {
        var contract = await _contractRepository.GetAsync(id);
        contract.Confirm();
        await _contractRepository.UpdateAsync(contract, autoSave: true);
    }

    protected virtual IQueryable<Contract> ApplyFilter(
        IQueryable<Contract> query,
        GetContractListInput input)
    {
        if (input.DocumentId.HasValue)
        {
            query = query.Where(x => x.DocumentId == input.DocumentId.Value);
        }

        if (!input.CounterpartyKeyword.IsNullOrWhiteSpace())
        {
            query = query.Where(x =>
                x.CounterpartyName != null &&
                x.CounterpartyName.Contains(input.CounterpartyKeyword!));
        }

        if (input.ExpirationDateFrom.HasValue)
        {
            query = query.Where(x =>
                x.ExpirationDate.HasValue &&
                x.ExpirationDate.Value >= input.ExpirationDateFrom.Value);
        }

        if (input.ExpirationDateTo.HasValue)
        {
            query = query.Where(x =>
                x.ExpirationDate.HasValue &&
                x.ExpirationDate.Value <= input.ExpirationDateTo.Value);
        }

        if (input.NeedsReview.HasValue)
        {
            query = query.Where(x => x.NeedsReview == input.NeedsReview.Value);
        }

        if (input.ReviewStatus.HasValue)
        {
            query = query.Where(x => x.ReviewStatus == input.ReviewStatus.Value);
        }

        if (input.TotalAmountMin.HasValue)
        {
            query = query.Where(x =>
                x.TotalAmount.HasValue &&
                x.TotalAmount.Value >= input.TotalAmountMin.Value);
        }

        if (input.TotalAmountMax.HasValue)
        {
            query = query.Where(x =>
                x.TotalAmount.HasValue &&
                x.TotalAmount.Value <= input.TotalAmountMax.Value);
        }

        return query;
    }

    protected virtual ExtractedContractFields CreateFieldsSnapshot(Contract contract)
    {
        return new ExtractedContractFields
        {
            Title = contract.Title,
            ContractNumber = contract.ContractNumber,
            PartyAName = contract.PartyAName,
            PartyBName = contract.PartyBName,
            CounterpartyName = contract.CounterpartyName,
            SignedDate = contract.SignedDate,
            EffectiveDate = contract.EffectiveDate,
            ExpirationDate = contract.ExpirationDate,
            TotalAmount = contract.TotalAmount,
            Currency = contract.Currency,
            AutoRenewal = contract.AutoRenewal,
            TerminationNoticeDays = contract.TerminationNoticeDays,
            GoverningLaw = contract.GoverningLaw,
            Summary = contract.Summary,
            ExtractionConfidence = contract.ExtractionConfidence,
            ReviewStatus = contract.ReviewStatus
        };
    }

    [Authorize(ContractsPermissions.Contracts.Export)]
    public virtual async Task<IRemoteStreamContent> ExportAsync(GetContractListInput input)
    {
        var query = await _contractRepository.GetQueryableAsync();
        query = ApplyFilter(query, input);
        query = ApplySorting(query, input.Sorting);

        var contracts = await AsyncExecuter.ToListAsync(query);
        var csv = BuildContractCsv(contracts);
        var bytes = Encoding.UTF8.GetBytes(csv);

        return new RemoteStreamContent(new MemoryStream(bytes), "contracts.csv", "text/csv");
    }

    protected virtual IQueryable<Contract> ApplySorting(
        IQueryable<Contract> query,
        string? sorting)
    {
        return sorting switch
        {
            "expirationDate" => query.OrderBy(x => x.ExpirationDate),
            "expirationDate desc" => query.OrderByDescending(x => x.ExpirationDate),
            "counterpartyName" => query.OrderBy(x => x.CounterpartyName),
            "counterpartyName desc" => query.OrderByDescending(x => x.CounterpartyName),
            "signedDate" => query.OrderBy(x => x.SignedDate),
            "signedDate desc" => query.OrderByDescending(x => x.SignedDate),
            _ => query.OrderByDescending(x => x.CreationTime)
        };
    }

    private static string BuildContractCsv(List<Contract> contracts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,DocumentId,DocumentTypeCode,Title,ContractNumber,PartyAName,PartyBName,CounterpartyName,SignedDate,EffectiveDate,ExpirationDate,TotalAmount,Currency,Status");

        foreach (var c in contracts)
        {
            sb.AppendLine(string.Join(",",
                c.Id,
                c.DocumentId,
                EscapeCsv(c.DocumentTypeCode),
                EscapeCsv(c.Title),
                EscapeCsv(c.ContractNumber),
                EscapeCsv(c.PartyAName),
                EscapeCsv(c.PartyBName),
                EscapeCsv(c.CounterpartyName),
                c.SignedDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                c.EffectiveDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                c.ExpirationDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                c.TotalAmount?.ToString("F2") ?? string.Empty,
                EscapeCsv(c.Currency),
                c.Status.ToString()));
        }

        return sb.ToString();
    }

    private static string EscapeCsv(string? value)
    {
        if (value.IsNullOrEmpty()) return string.Empty;
        if (value!.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
