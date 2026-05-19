using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace Dignite.Paperbase.Documents;

public interface IDocumentAppService : IApplicationService
{
    Task<DocumentDto> GetAsync(Guid id);

    Task<PagedResultDto<DocumentListItemDto>> GetListAsync(GetDocumentListInput input);

    Task<DocumentDto> UploadAsync(UploadDocumentInput input);

    Task<IRemoteStreamContent> GetBlobAsync(Guid id);

    Task DeleteAsync(Guid id);

    Task PermanentDeleteAsync(Guid id);

    Task RestoreAsync(Guid id);

    Task<IRemoteStreamContent> GetExportAsync(GetDocumentListInput input);

    Task<DocumentDto> ConfirmClassificationAsync(Guid id, ConfirmClassificationInput input);

    /// <summary>
    /// 操作员主动修正分类——任意状态下都允许覆写到新类型。
    /// 行为：写入 DocumentTypeCode/ReviewStatus=Reviewed/Confidence=1.0，发布
    /// <see cref="Abstractions.Documents.DocumentClassifiedEto"/>（经 ABP transactional outbox 投递）。
    /// 下游业务消费方可订阅 DocumentClassifiedEto 来重跑各自的字段抽取——按
    /// <c>(DocumentId, EventType, EventTime)</c> 自行幂等以处理 at-least-once 重投。
    /// </summary>
    Task<DocumentDto> ReclassifyAsync(Guid id, ReclassifyDocumentInput input);

    /// <summary>
    /// 操作员通过待审核文档（OCR confidence 不达标或分类无法确认导致进队）。
    /// <para>
    /// 通过后自动推进流水线，兑现 CLAUDE.md "操作员手动确认通过 → 触发 <c>DocumentReadyEto</c>" 承诺：
    /// <list type="bullet">
    ///   <item>若 classification 尚未跑（OCR review 场景）→ schedule classification pipeline，完成后自然到 Ready</item>
    ///   <item>若 classification 已跑且 <c>DocumentTypeCode</c> 非空 → 即时 RecomputeLifecycle 到 Ready 发 <c>DocumentReadyEto</c></item>
    ///   <item>若 classification 已跑但 <c>DocumentTypeCode</c> 仍空 → 应走 <see cref="ReclassifyAsync"/> 而非本方法</item>
    /// </list>
    /// </para>
    /// </summary>
    Task<DocumentDto> ApproveReviewAsync(Guid id);

    /// <summary>
    /// 操作员拒绝待审核文档——文档落到 Failed 生命周期。
    /// </summary>
    Task<DocumentDto> RejectReviewAsync(Guid id, RejectReviewInput input);

    Task RetryPipelineAsync(Guid id, RetryPipelineInput input);
}
