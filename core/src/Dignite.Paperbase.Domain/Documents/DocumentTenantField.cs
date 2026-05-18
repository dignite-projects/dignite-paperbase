using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// 单个文档单个租户字段的抽取结果（B 机制存储）。
/// <para>
/// 设计选择：行式存储（每字段一行）而非 JSON 列。
/// 理由：租户可能添加/删除字段，行式更新无需重写全 JSON；keyword 搜索可直接索引 Value 列。
/// </para>
/// </summary>
public class DocumentTenantField : FullAuditedEntity<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; private set; }

    public virtual Guid DocumentId { get; private set; }

    public virtual string FieldName { get; private set; } = default!;

    /// <summary>提取值（JSON 序列化以保留类型；解析时按字段定义的 DataType）。</summary>
    public virtual string? Value { get; private set; }

    /// <summary>LLM 自评的抽取置信度（0.0 - 1.0）。NULL 时未评估。</summary>
    public virtual double? Confidence { get; private set; }

    protected DocumentTenantField() { }

    public DocumentTenantField(
        Guid id,
        Guid? tenantId,
        Guid documentId,
        string fieldName,
        string? value,
        double? confidence = null)
        : base(id)
    {
        TenantId = tenantId;
        DocumentId = documentId;
        FieldName = Check.NotNullOrWhiteSpace(fieldName, nameof(fieldName), TenantFieldConsts.MaxNameLength);
        Value = value;
        Confidence = confidence;
    }

    public void UpdateValue(string? value, double? confidence)
    {
        Value = value;
        Confidence = confidence;
    }
}
