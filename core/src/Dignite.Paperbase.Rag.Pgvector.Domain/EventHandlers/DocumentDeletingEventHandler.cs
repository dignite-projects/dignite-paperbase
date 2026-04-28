using System.Threading.Tasks;
using Dignite.Paperbase.Documents.Events;
using Dignite.Paperbase.Rag.Pgvector.Documents;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace Dignite.Paperbase.Rag.Pgvector.EventHandlers;

/// <summary>
/// 订阅 <see cref="DocumentDeletingEvent"/>，将 pgvector chunk 和 document-level vector 清理
/// 推迟到主事务提交之后执行。
///
/// <para>
/// <b>事务语义（关键）：</b>
/// <list type="bullet">
///   <item><description>
///     handler 在主 UoW 仍活跃时同步被调用，通过 <see cref="IUnitOfWork.OnCompleted"/> 注册
///     after-commit 回调。
///   </description></item>
///   <item><description>
///     OnCompleted 在 <c>CommitTransactionsAsync</c> 成功后触发；此时主 UoW 的
///     <c>IsCompleted == true</c>，回调内必须以 <c>requiresNew:true</c> 开启全新独立 UoW。
///   </description></item>
///   <item><description>
///     主事务回滚时 <c>OnCompleted</c> <b>不会</b>触发——正确语义。
///   </description></item>
///   <item><description>
///     若回调内清理失败，chunks 和 DocumentVector 会残留为孤儿，由
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
    private readonly IDocumentVectorRepository _vectorRepository;
    private readonly ICurrentTenant _currentTenant;

    public DocumentDeletingEventHandler(
        IUnitOfWorkManager unitOfWorkManager,
        IDocumentChunkRepository chunkRepository,
        IDocumentVectorRepository vectorRepository,
        ICurrentTenant currentTenant)
    {
        _unitOfWorkManager = unitOfWorkManager;
        _chunkRepository = chunkRepository;
        _vectorRepository = vectorRepository;
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
                await _vectorRepository.DeleteByDocumentIdAsync(documentId);
            }

            await uow.CompleteAsync();
        });

        return Task.CompletedTask;
    }
}
