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
    /// <see cref="Abstractions.Documents.DocumentClassifiedEto"/>（经 OutboxEventManager 去重）。
    /// 下游业务消费方可订阅 DocumentClassifiedEto 来重跑各自的字段抽取。
    /// </summary>
    Task<DocumentDto> ReclassifyAsync(Guid id, ReclassifyDocumentInput input);

    /// <summary>
    /// 操作员通过待审核文档（OCR confidence 不达标或分类无法确认导致进队）。
    /// 通过后下游 pipeline 由调用方按业务决定是否重新调度。
    /// </summary>
    Task<DocumentDto> ApproveReviewAsync(Guid id);

    /// <summary>
    /// 操作员拒绝待审核文档——文档落到 Failed 生命周期。
    /// </summary>
    Task<DocumentDto> RejectReviewAsync(Guid id, RejectReviewInput input);

    Task RetryPipelineAsync(Guid id, RetryPipelineInput input);
}
