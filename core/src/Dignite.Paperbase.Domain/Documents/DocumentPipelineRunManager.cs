using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Documents.Events;
using Volo.Abp.Data;
using Volo.Abp.Domain.Services;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// 流水线执行记录的统一入口。
/// 负责创建 Run、驱动状态流转、在每次状态变化后重新派生 Document.LifecycleStatus。
/// 所有向 Document 写入流水线结果的代码都必须通过此服务。
/// </summary>
public class DocumentPipelineRunManager : DomainService
{
    public virtual Task<DocumentPipelineRun> StartAsync(Document document, string pipelineCode)
    {
        var attemptNumber = document.PipelineRuns
            .Where(r => r.PipelineCode == pipelineCode)
            .Select(r => r.AttemptNumber)
            .DefaultIfEmpty(0)
            .Max() + 1;

        var run = new DocumentPipelineRun(
            GuidGenerator.Create(),
            document.Id,
            document.TenantId,
            pipelineCode,
            attemptNumber);

        run.MarkRunning(Clock.Now);
        document.AddPipelineRun(run);

        DeriveLifecycle(document);

        return Task.FromResult(run);
    }

    public virtual Task CompleteAsync(
        Document document,
        DocumentPipelineRun run)
    {
        run.MarkSucceeded(Clock.Now);

        document.PublishPipelineRunCompletedEvent(new DocumentPipelineRunCompletedEvent(
            document.Id,
            run.PipelineCode,
            run.Status));

        DeriveLifecycle(document);

        return Task.CompletedTask;
    }

    public virtual Task FailAsync(
        Document document,
        DocumentPipelineRun run,
        string errorMessage)
    {
        run.MarkFailed(Clock.Now, errorMessage);

        document.PublishPipelineRunCompletedEvent(new DocumentPipelineRunCompletedEvent(
            document.Id,
            run.PipelineCode,
            run.Status));

        DeriveLifecycle(document);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 记录文本提取结果、回写实际 SourceType 并完成 Run。
    /// </summary>
    public virtual Task CompleteTextExtractionAsync(
        Document document,
        DocumentPipelineRun run,
        string extractedText,
        SourceType sourceType = SourceType.Physical)
    {
        document.SetSourceType(sourceType);
        document.SetExtractedText(extractedText);
        return CompleteAsync(document, run);
    }

    /// <summary>
    /// 记录分类结果并完成 Run。
    /// </summary>
    public virtual Task CompleteClassificationAsync(
        Document document,
        DocumentPipelineRun run,
        string typeCode,
        double confidenceScore,
        string? reason = null)
    {
        document.SetClassificationResult(typeCode, confidenceScore, reason);
        return CompleteAsync(document, run);
    }

    /// <summary>
    /// 分类置信度不足：完成 Run 并将文档标记为待人工审核。
    /// 置信度信号由 <see cref="Document.ReviewStatus"/> = PendingReview 表达，不再记录在 Run 上。
    /// </summary>
    public virtual Task CompleteClassificationWithLowConfidenceAsync(
        Document document,
        DocumentPipelineRun run,
        string? reason = null,
        IReadOnlyList<PipelineRunCandidate>? candidates = null)
    {
        document.MarkPendingReview(reason);

        if (candidates is { Count: > 0 })
        {
            run.SetProperty(
                PipelineRunExtraPropertyNames.ClassificationCandidates,
                candidates);
        }

        return CompleteAsync(document, run);
    }

    /// <summary>
    /// 人工确认文档类型：写入分类结果、标记已审核、完成 Run。置信度固定为 1.0。
    /// 人工覆盖信号由 <see cref="Document.ReviewStatus"/> = Reviewed 表达。
    /// 该字面量与 Abstractions 层 <c>ClassificationDefaults.ManualClassificationConfidence</c>
    /// 同步维护（Domain 不依赖 Abstractions，故此处硬编码）。
    /// </summary>
    public virtual Task CompleteManualClassificationAsync(
        Document document,
        DocumentPipelineRun run,
        string typeCode)
    {
        document.SetClassificationResult(typeCode, 1.0, reason: null);
        document.MarkReviewed();
        return CompleteAsync(document, run);
    }

    public virtual Task SkipAsync(
        Document document,
        DocumentPipelineRun run,
        string reason)
    {
        run.MarkSkipped(Clock.Now, reason);

        document.PublishPipelineRunCompletedEvent(new DocumentPipelineRunCompletedEvent(
            document.Id,
            run.PipelineCode,
            run.Status));

        DeriveLifecycle(document);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 根据所有关键流水线的最新 Run 派生 Document.LifecycleStatus。
    /// </summary>
    protected virtual void DeriveLifecycle(Document document)
    {
        var derivedStatus = DocumentLifecycleStatus.Processing;

        var allSucceeded = true;

        foreach (var pipelineCode in PaperbasePipelines.KeyPipelines)
        {
            var latestRun = document.GetLatestRun(pipelineCode);

            if (latestRun == null)
            {
                allSucceeded = false;
                continue;
            }

            if (latestRun.Status == PipelineRunStatus.Failed)
            {
                derivedStatus = DocumentLifecycleStatus.Failed;
                allSucceeded = false;
                break;
            }

            if (latestRun.Status != PipelineRunStatus.Succeeded)
            {
                allSucceeded = false;
            }
        }

        if (derivedStatus != DocumentLifecycleStatus.Failed && allSucceeded)
        {
            derivedStatus = DocumentLifecycleStatus.Ready;
        }

        document.TransitionLifecycle(derivedStatus);
    }
}
