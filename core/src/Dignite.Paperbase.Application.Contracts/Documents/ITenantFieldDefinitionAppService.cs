using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// 租户自定义字段定义管理（B 机制）——租户用此 API 给特定文档类型挂载字段 schema。
/// <para>
/// 安全约束：所有路径强制走当前租户上下文；不允许跨租户读写（CurrentTenant.Id 绑定）。
/// </para>
/// </summary>
public interface ITenantFieldDefinitionAppService : IApplicationService
{
    Task<List<TenantFieldDefinitionDto>> GetByDocumentTypeAsync(string documentTypeCode);

    Task<TenantFieldDefinitionDto> CreateAsync(CreateTenantFieldDefinitionDto input);

    Task<TenantFieldDefinitionDto> UpdateAsync(Guid id, UpdateTenantFieldDefinitionDto input);

    Task DeleteAsync(Guid id);
}
