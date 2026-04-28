using System;

namespace Dignite.Paperbase.Documents.Events;

/// <summary>
/// 文档即将被删除时发布的本地领域事件，供 vector store provider 在主事务提交后清理关联索引。
/// 由 <c>DocumentAppService.DeleteAsync</c> 在删除前以 onUnitOfWorkComplete:false 同步投递，
/// 确保 handler 能在主 UoW 仍活跃时注册 <see cref="Volo.Abp.Uow.IUnitOfWork.OnCompleted"/> 回调。
/// </summary>
public record DocumentDeletingEvent(Guid DocumentId, Guid? TenantId);
