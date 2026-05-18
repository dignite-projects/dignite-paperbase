using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace Dignite.Paperbase.Documents;

public interface ITenantFieldDefinitionRepository : IRepository<TenantFieldDefinition, Guid>
{
    /// <summary>
    /// 按文档类型查询当前租户的字段定义（显式 TenantId 谓词，不依赖 ambient DataFilter）。
    /// </summary>
    Task<List<TenantFieldDefinition>> GetByDocumentTypeAsync(
        Guid? tenantId,
        string documentTypeCode,
        CancellationToken cancellationToken = default);

    Task<TenantFieldDefinition?> FindByNameAsync(
        Guid? tenantId,
        string documentTypeCode,
        string name,
        CancellationToken cancellationToken = default);
}
