using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// Issue #115 L1: <see cref="DocumentIdentifier"/> 的自定义仓储接口。
/// 定义跨文档标识符反查、按文档清理、存在性判定三个 L2 Pipeline 必需的查询方法。
/// </summary>
public interface IDocumentIdentifierRepository : IRepository<DocumentIdentifier, Guid>
{
    /// <summary>
    /// 反查：当前租户内所有持有 (identifierType, identifierValue) 的文档 ID。
    /// L2 Pipeline 用此方法实现"同合同编号的发票自动关联到对应采购订单"。
    /// 多租户由 ABP ambient <c>DataFilter</c> 自动过滤；如有禁用 filter 的代码路径，
    /// 实现侧应附加显式 <c>TenantId == CurrentTenant.Id</c> 谓词。
    /// </summary>
    Task<List<Guid>> FindDocumentIdsAsync(
        string identifierType,
        string identifierValue,
        CancellationToken cancellationToken = default);

    /// <summary>当前租户内某个文档持有的所有标识符。用于诊断、UI 展示、清理前的回看。</summary>
    Task<List<DocumentIdentifier>> GetListByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 幂等性判断：当前租户内 (documentId, identifierType, identifierValue) 是否已存在。
    /// <c>RegisterAsync</c> 用此方法在插入前避免重复行。
    /// </summary>
    Task<bool> ExistsAsync(
        Guid documentId,
        string identifierType,
        string identifierValue,
        CancellationToken cancellationToken = default);

    /// <summary>批量删除某文档名下所有标识符（文档被硬删 / 重新提取场景）。</summary>
    Task RemoveByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);
}
