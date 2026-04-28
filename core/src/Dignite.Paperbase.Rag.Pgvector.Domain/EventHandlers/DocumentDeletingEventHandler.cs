using System.Threading.Tasks;
using Dignite.Paperbase.Documents.Events;
using Dignite.Paperbase.Rag.Pgvector.Documents;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace Dignite.Paperbase.Rag.Pgvector.EventHandlers;

/// <summary>
/// 订阅 <see cref="DocumentDeletingEvent"/>，将 pgvector chunk 清理推迟到主事务提交之后执行。
///
/// <para>
/// <b>事务语义（关键）：</b>
/// <list type="bullet">
///   <item><description>
///     handler 在主 UoW 仍活跃时同步被调用（<c>onUnitOfWorkComplete:false</c> 投递），
///     通过 <see cref="IUnitOfWork.OnCompleted"/> 注册 after-commit 回调。
///   </description></item>
///   <item><description>
///     OnCompleted 在 <c>CommitTransactionsAsync</c> 成功后触发；此时主 UoW 的
///     <c>IsCompleted == true</c>，任何对 ambient UoW 的操作均会失败。
///     因此回调内必须以 <c>requiresNew:true</c> 开启全新独立 UoW。
///   </description></item>
///   <item><description>
///     主事务回滚（<c>RollbackAsync</c>）或 <c>CompleteAsync</c> 抛异常时，
///     <c>OnCompleted</c> <b>不会</b>触发，chunk 不会被删除——正确语义。
///   </description></item>
///   <item><description>
///     若回调内的 chunk 删除本身失败（PgvectorRag DB 超时 / 网络闪断），
///     Document 已从主 DB 删除，orphaned chunks 由
///     <c>PgvectorReconciliationBackgroundJob</c> 兜底清理。
///   </description></item>
/// </list>
/// </para>
/// </summary>
public class DocumentDeletingEventHandler
    : ILocalEventHandler<DocumentDeletingEvent>, ITransientDependency
{
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IDocumentChunkRepository _chunkRepository;
    private readonly ICurrentTenant _currentTenant;

    public DocumentDeletingEventHandler(
        IUnitOfWorkManager unitOfWorkManager,
        IDocumentChunkRepository chunkRepository,
        ICurrentTenant currentTenant)
    {
        _unitOfWorkManager = unitOfWorkManager;
        _chunkRepository = chunkRepository;
        _currentTenant = currentTenant;
    }

    public virtual Task HandleEventAsync(DocumentDeletingEvent eventData)
    {
        if (_unitOfWorkManager.Current == null)
            return Task.CompletedTask;

        var documentId = eventData.DocumentId;
        var tenantId = eventData.TenantId;

        _unitOfWorkManager.Current.OnCompleted(async () =>
        {
            // 主 UoW 此时已 Complete（IsCompleted == true）且事务已提交。
            // 必须以 requiresNew:true 开启全新 UoW，不能依赖 ambient。
            using var uow = _unitOfWorkManager.Begin(
                new AbpUnitOfWorkOptions { IsTransactional = true },
                requiresNew: true);

            using (_currentTenant.Change(tenantId))
            {
                await _chunkRepository.DeleteByDocumentIdAsync(documentId);
            }

            await uow.CompleteAsync();
        });

        return Task.CompletedTask;
    }
}
