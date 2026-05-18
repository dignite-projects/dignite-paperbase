using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace Dignite.Paperbase.Documents;

public interface IOutboxEventRepository : IRepository<OutboxEvent, Guid>
{
    /// <summary>
    /// 按去重 key 查询现有记录（显式 TenantId 谓词，不依赖 ambient DataFilter）。
    /// </summary>
    Task<OutboxEvent?> FindByKeyAsync(
        Guid? tenantId,
        Guid documentId,
        string eventType,
        CancellationToken cancellationToken = default);
}
