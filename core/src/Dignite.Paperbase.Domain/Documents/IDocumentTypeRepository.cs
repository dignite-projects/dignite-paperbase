using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace Dignite.Paperbase.Documents;

public interface IDocumentTypeRepository : IRepository<DocumentType, Guid>
{
    /// <summary>
    /// 按 tenantId 精确匹配（NULL-safe equality）拿该层文档类型集合。
    /// 解读 X + 没有继承关系：Host 文档（tenantId IS NULL）用 Host 类型；租户文档用对应租户类型；
    /// 不存在跨层 union。用于分类候选集组装。
    /// 显式 TenantId 谓词不依赖 ambient DataFilter（安全约定：fail-closed）。
    /// </summary>
    Task<List<DocumentType>> GetByTenantAsync(
        Guid? tenantId,
        CancellationToken cancellationToken = default);

    Task<DocumentType?> FindByTypeCodeAsync(
        Guid? tenantId,
        string typeCode,
        CancellationToken cancellationToken = default);
}
