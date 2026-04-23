using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.AI;
using Dignite.Paperbase.Abstractions.Documents;
using Dignite.Paperbase.Application.Documents.Classification;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Domain.Documents;
using Dignite.Paperbase.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Features;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Application.Documents.BackgroundJobs;

[BackgroundJobName("Paperbase.DocumentClassification")]
public class DocumentClassificationBackgroundJob
    : AsyncBackgroundJob<DocumentClassificationJobArgs>, ITransientDependency
{
    private readonly IDocumentRepository _documentRepository;
    private readonly DocumentPipelineRunManager _pipelineRunManager;
    private readonly IDocumentClassifier _documentClassifier;
    private readonly KeywordDocumentClassifier _keywordClassifier;
    private readonly IDistributedEventBus _distributedEventBus;
    private readonly DocumentTypeOptions _documentTypeOptions;
    private readonly IAiCostLedger _costLedger;
    private readonly IFeatureChecker _featureChecker;
    private readonly ICurrentTenant _currentTenant;

    public DocumentClassificationBackgroundJob(
        IDocumentRepository documentRepository,
        DocumentPipelineRunManager pipelineRunManager,
        IDocumentClassifier documentClassifier,
        KeywordDocumentClassifier keywordClassifier,
        IDistributedEventBus distributedEventBus,
        IOptions<DocumentTypeOptions> documentTypeOptions,
        IAiCostLedger costLedger,
        IFeatureChecker featureChecker,
        ICurrentTenant currentTenant)
    {
        _documentRepository = documentRepository;
        _pipelineRunManager = pipelineRunManager;
        _documentClassifier = documentClassifier;
        _keywordClassifier = keywordClassifier;
        _distributedEventBus = distributedEventBus;
        _documentTypeOptions = documentTypeOptions.Value;
        _costLedger = costLedger;
        _featureChecker = featureChecker;
        _currentTenant = currentTenant;
    }

    public override async Task ExecuteAsync(DocumentClassificationJobArgs args)
    {
        var document = await _documentRepository.GetAsync(args.DocumentId);
        var run = await _pipelineRunManager.StartAsync(document, PaperbasePipelines.Classification);
        await _documentRepository.UpdateAsync(document);

        try
        {
            // 预算检查（在调用 AI 之前，由核心模块负责）
            var budgetStr = await _featureChecker.GetOrNullAsync(PaperbaseAIFeatures.MonthlyBudgetUsd);
            var budget = decimal.TryParse(budgetStr, out var b) ? b : decimal.MaxValue;
            var used = await _costLedger.GetCurrentMonthUsageAsync(_currentTenant.Id);

            if (used >= budget)
            {
                await _pipelineRunManager.SkipAsync(
                    document, run,
                    reason: $"Monthly AI budget exceeded. Budget: ${budget:F2}, Used: ${used:F2}",
                    resultCode: "BudgetExceeded");
                await _documentRepository.UpdateAsync(document);
                return;
            }

            var request = BuildRequest(document);
            ClassificationResult result;

            try
            {
                result = await _documentClassifier.ClassifyAsync(request);
            }
            catch (Exception ex) when (IsAiProviderError(ex))
            {
                // AI 超时/Provider 错误：回退到关键字分类器（最后防线）
                Logger.LogWarning(ex,
                    "AI classifier failed for document {DocumentId}, falling back to keyword classifier.",
                    document.Id);
                result = await _keywordClassifier.ClassifyAsync(request);
            }

            await ApplyClassificationResultAsync(document, run, result);
            await _documentRepository.UpdateAsync(document);
        }
        catch (Exception ex)
        {
            await _pipelineRunManager.FailAsync(document, run, ex.Message);
            await _documentRepository.UpdateAsync(document);
        }
    }

    private ClassificationRequest BuildRequest(Document document)
    {
        return new ClassificationRequest
        {
            ExtractedText = document.ExtractedText ?? string.Empty,
            CandidateTypes = _documentTypeOptions.Types
                .OrderByDescending(t => t.Priority)
                .Select(t => new DocumentTypeHint
                {
                    TypeCode = t.TypeCode,
                    DisplayName = t.DisplayName,
                    Keywords = t.MatchKeywords
                }).ToList()
        };
    }

    private async Task ApplyClassificationResultAsync(
        Document document,
        DocumentPipelineRun run,
        ClassificationResult result)
    {
        var metadataJson = result.Metadata.Count > 0
            ? JsonSerializer.Serialize(result.Metadata)
            : null;

        if (!string.IsNullOrEmpty(result.TypeCode))
        {
            var typeDef = _documentTypeOptions.Types
                .FirstOrDefault(t => t.TypeCode == result.TypeCode);
            var threshold = typeDef?.ConfidenceThreshold ?? 0.7;

            if (result.ConfidenceScore >= threshold)
            {
                await _pipelineRunManager.CompleteClassificationAsync(
                    document, run, result.TypeCode, result.ConfidenceScore, metadataJson);

                await _distributedEventBus.PublishAsync(new DocumentClassifiedEto
                {
                    DocumentId = document.Id,
                    TenantId = document.TenantId,
                    DocumentTypeCode = result.TypeCode,
                    ConfidenceScore = result.ConfidenceScore,
                    ExtractedText = document.ExtractedText
                });
                return;
            }
        }

        // LowConfidence：置信度不足或无结果
        await _pipelineRunManager.CompleteAsync(document, run, "LowConfidence", metadataJson);
    }

    private static bool IsAiProviderError(Exception ex)
    {
        // 超时、网络错误、Provider 异常——这些场景回退到关键字分类器
        return ex is TimeoutException
            || ex is OperationCanceledException
            || ex.GetType().Name.Contains("HttpRequestException", StringComparison.OrdinalIgnoreCase)
            || (ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase));
    }
}

public class DocumentClassificationJobArgs
{
    public Guid DocumentId { get; set; }
}
