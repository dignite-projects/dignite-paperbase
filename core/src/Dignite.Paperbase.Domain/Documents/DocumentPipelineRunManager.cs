using System;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Domain.Documents.Events;
using Volo.Abp.Domain.Services;

namespace Dignite.Paperbase.Domain.Documents;

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
        DocumentPipelineRun run,
        string resultCode = "Ok",
        string? metadata = null)
    {
        run.MarkSucceeded(Clock.Now, resultCode, metadata);

        document.PublishPipelineRunCompletedEvent(new DocumentPipelineRunCompletedEvent(
            document.Id,
            run.PipelineCode,
            run.Status,
            run.ResultCode));

        DeriveLifecycle(document);

        return Task.CompletedTask;
    }

    public virtual Task FailAsync(
        Document document,
        DocumentPipelineRun run,
        string errorMessage,
        string resultCode = "Error",
        string? metadata = null)
    {
        run.MarkFailed(Clock.Now, errorMessage, resultCode, metadata);

        document.PublishPipelineRunCompletedEvent(new DocumentPipelineRunCompletedEvent(
            document.Id,
            run.PipelineCode,
            run.Status,
            run.ResultCode));

        DeriveLifecycle(document);

        return Task.CompletedTask;
    }

    public virtual Task SkipAsync(Document document, DocumentPipelineRun run, string reason)
    {
        run.MarkSkipped(Clock.Now, reason);

        document.PublishPipelineRunCompletedEvent(new DocumentPipelineRunCompletedEvent(
            document.Id,
            run.PipelineCode,
            run.Status,
            run.ResultCode));

        DeriveLifecycle(document);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 根据所有关键流水线的最新 Run 派生 Document.LifecycleStatus。
    /// 派生规则：
    ///   任一关键流水线最新 Run = Failed → Failed
    ///   所有关键流水线最新 Run = Succeeded → Ready
    ///   其他（含尚未启动、Running、Skipped）→ Processing
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
