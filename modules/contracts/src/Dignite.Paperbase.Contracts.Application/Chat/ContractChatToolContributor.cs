using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Chat;
using Dignite.Paperbase.Contracts.Contracts;
using Microsoft.Extensions.AI;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Linq;

namespace Dignite.Paperbase.Contracts.Chat;

/// <summary>
/// Contributes a <c>search_contracts</c> AI tool to document chat conversations scoped to
/// <see cref="ContractsDocumentTypes.General"/>. The tool queries the Contracts relational
/// store by structured criteria and returns matched document IDs so the model can optionally
/// chain to the built-in vector search for deeper semantic content retrieval.
/// </summary>
public class ContractChatToolContributor : IDocumentChatToolContributor, ITransientDependency
{
    private readonly IContractRepository _contractRepository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public ContractChatToolContributor(
        IContractRepository contractRepository,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _contractRepository = contractRepository;
        _asyncExecuter = asyncExecuter;
    }

    public virtual string DocumentTypeCode => ContractsDocumentTypes.General;

    public virtual IEnumerable<AIFunction> ContributeTools(DocumentChatToolContext ctx)
    {
        var binding = new ContractSearchBinding(_contractRepository, _asyncExecuter, ctx.TenantId);
        yield return AIFunctionFactory.Create(
            binding.SearchAsync,
            name: "search_contracts",
            description:
                "Search contracts by structured criteria: contract number, party name, " +
                "date range, or amount range. " +
                "Returns matched document IDs and contract metadata summaries. " +
                "Use the returned document IDs to restrict further document content search to the relevant contracts.");
    }

    // ── nested binding ───────────────────────────────────────────────────────

    /// <summary>
    /// Holds the bound context for the <c>search_contracts</c> AIFunction.
    /// Factored into a class so parameter-level <see cref="DescriptionAttribute"/>s are
    /// accessible via reflection (lambda parameters cannot carry attributes in C#).
    /// </summary>
    private sealed class ContractSearchBinding
    {
        private readonly IContractRepository _repo;
        private readonly IAsyncQueryableExecuter _executer;
        private readonly Guid? _tenantId;

        public ContractSearchBinding(
            IContractRepository repo,
            IAsyncQueryableExecuter executer,
            Guid? tenantId)
        {
            _repo = repo;
            _executer = executer;
            _tenantId = tenantId;
        }

        public async Task<string> SearchAsync(
            [Description("Contract number or partial number to search for")]
            string? contractNumber = null,
            [Description("Party name — matches Party A, Party B, or counterparty (partial match)")]
            string? partyName = null,
            [Description("Earliest signed date in ISO 8601 format, e.g. 2024-01-01")]
            DateTime? signedDateFrom = null,
            [Description("Latest signed date in ISO 8601 format")]
            DateTime? signedDateTo = null,
            [Description("Earliest expiration date in ISO 8601 format")]
            DateTime? expirationDateFrom = null,
            [Description("Latest expiration date in ISO 8601 format")]
            DateTime? expirationDateTo = null,
            [Description("Minimum total contract amount")]
            decimal? amountMin = null,
            [Description("Maximum total contract amount")]
            decimal? amountMax = null,
            CancellationToken cancellationToken = default)
        {
            var queryable = await _repo.GetQueryableAsync();

            // Explicit tenant filter — do not rely solely on ABP's ambient data filter,
            // which may be absent on background threads or in non-HTTP contexts.
            queryable = _tenantId.HasValue
                ? queryable.Where(c => c.TenantId == _tenantId)
                : queryable.Where(c => c.TenantId == null);

            if (!string.IsNullOrWhiteSpace(contractNumber))
                queryable = queryable.Where(c =>
                    c.ContractNumber != null && c.ContractNumber.Contains(contractNumber));

            if (!string.IsNullOrWhiteSpace(partyName))
                queryable = queryable.Where(c =>
                    (c.PartyAName != null && c.PartyAName.Contains(partyName)) ||
                    (c.PartyBName != null && c.PartyBName.Contains(partyName)) ||
                    (c.CounterpartyName != null && c.CounterpartyName.Contains(partyName)));

            if (signedDateFrom.HasValue)
                queryable = queryable.Where(c => c.SignedDate >= signedDateFrom);
            if (signedDateTo.HasValue)
                queryable = queryable.Where(c => c.SignedDate <= signedDateTo);

            if (expirationDateFrom.HasValue)
                queryable = queryable.Where(c => c.ExpirationDate >= expirationDateFrom);
            if (expirationDateTo.HasValue)
                queryable = queryable.Where(c => c.ExpirationDate <= expirationDateTo);

            if (amountMin.HasValue)
                queryable = queryable.Where(c => c.TotalAmount >= amountMin);
            if (amountMax.HasValue)
                queryable = queryable.Where(c => c.TotalAmount <= amountMax);

            var contracts = await _executer.ToListAsync(
                queryable.OrderByDescending(c => c.CreationTime).Take(20),
                cancellationToken);

            var result = new
            {
                documentIds = contracts.Select(c => c.DocumentId).ToList(),
                contracts = contracts.Select(c => new
                {
                    documentId = c.DocumentId,
                    contractNumber = c.ContractNumber,
                    title = c.Title,
                    partyAName = c.PartyAName,
                    partyBName = c.PartyBName,
                    counterpartyName = c.CounterpartyName,
                    totalAmount = c.TotalAmount,
                    currency = c.Currency,
                    signedDate = c.SignedDate?.ToString("yyyy-MM-dd"),
                    expirationDate = c.ExpirationDate?.ToString("yyyy-MM-dd"),
                    summary = c.Summary
                }).ToList()
            };

            return JsonSerializer.Serialize(result);
        }
    }
}
