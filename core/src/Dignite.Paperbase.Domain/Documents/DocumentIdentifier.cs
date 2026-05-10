using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// Issue #115 L1: 跨文档标识符索引项。每条记录表示"DocumentId 持有 (IdentifierType, IdentifierValue) 这个业务标识符"。
///
/// <para>
/// 用途：业务模块的字段抽取器在抽取结构化字段（合同编号、订单号、甲乙方名称等）时顺路写入此索引；
/// L2 关系发现 Pipeline 据此做"跨文档同标识符"反查，自动推断 AiSuggested DocumentRelation。
/// </para>
///
/// <para>
/// 不内嵌于 <see cref="Document"/>：Document 聚合根在 Markdown 提取后保持只读语义，
/// 业务标识符是与 Document 平级的索引数据，由独立聚合根承载。
/// </para>
///
/// <para>
/// 不持久化为 navigation property，仅以 <see cref="DocumentId"/> 引用 Document（遵循 ABP DDD "Reference by Id" 规则）。
/// </para>
/// </summary>
public class DocumentIdentifier : CreationAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; private set; }

    /// <summary>持有该标识符的文档 ID。</summary>
    public virtual Guid DocumentId { get; private set; }

    /// <summary>
    /// 标识符类型（如 "ContractNumber"、"PoNumber"、"InvoiceNumber"、"PartyName"）。
    /// 由业务模块自行约定字符串常量，无核心枚举（避免核心层耦合任意业务字段类型）。
    /// </summary>
    public virtual string IdentifierType { get; private set; } = default!;

    /// <summary>
    /// 标识符值（如 "HT-2024-001"、"上海某某有限公司"）。
    /// 大小写策略由 SQL Server 默认 collation（CI/AI）决定，业务模块如需精确大小写匹配自行规范化后再传入。
    /// </summary>
    public virtual string IdentifierValue { get; private set; } = default!;

    protected DocumentIdentifier() { }

    public DocumentIdentifier(
        Guid id,
        Guid? tenantId,
        Guid documentId,
        string identifierType,
        string identifierValue)
        : base(id)
    {
        TenantId = tenantId;

        if (documentId == Guid.Empty)
        {
            throw new BusinessException(PaperbaseErrorCodes.DocumentIdentifierDocumentIdRequired);
        }
        DocumentId = documentId;

        IdentifierType = NormalizeAndCheck(
            identifierType,
            DocumentIdentifierConsts.MaxTypeLength,
            PaperbaseErrorCodes.DocumentIdentifierTypeRequired);

        IdentifierValue = NormalizeAndCheck(
            identifierValue,
            DocumentIdentifierConsts.MaxValueLength,
            PaperbaseErrorCodes.DocumentIdentifierValueRequired);
    }

    private static string NormalizeAndCheck(string raw, int maxLength, string requiredErrorCode)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new BusinessException(requiredErrorCode);
        }

        var trimmed = raw.Trim();
        if (trimmed.Length > maxLength)
        {
            // Defensive truncation rather than throw — business modules should validate before calling,
            // but a noisy extractor that emits a 1KB "PartyName" must not crash the pipeline.
            trimmed = trimmed.Substring(0, maxLength);
        }

        return trimmed;
    }
}
