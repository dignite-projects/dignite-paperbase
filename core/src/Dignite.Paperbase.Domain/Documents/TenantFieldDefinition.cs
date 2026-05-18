using System;
using Dignite.Paperbase.Abstractions.Documents;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// 租户自定义字段定义（B 机制）——租户在 Host 类型 OR 该租户自定义类型下挂载的字段 schema。
/// <para>
/// 与 <see cref="HostFieldDefinition"/>（#168）的对比：
/// <list type="bullet">
///   <item>Host 字段是部署级配置，所有租户共享</item>
///   <item>租户字段 per-tenant 私有，租户通过 API/UI 自助配置</item>
///   <item>两者共享同一个 LLM 抽取引擎，但存储分开</item>
/// </list>
/// </para>
/// <para>
/// 安全约束（CLAUDE.md 强制）：
/// <list type="bullet">
///   <item>所有查询路径显式 TenantId 谓词（不依赖 ambient DataFilter）</item>
///   <item>Prompt 字段是租户用户控制的文本——LLM 抽取时必须经 <c>PromptBoundary.WrapField()</c> 包裹</item>
///   <item>每文档类型每租户下 (TenantId, DocumentTypeCode, Name) 唯一</item>
/// </list>
/// </para>
/// </summary>
public class TenantFieldDefinition : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; private set; }

    public virtual string DocumentTypeCode { get; private set; } = default!;

    public virtual string Name { get; private set; } = default!;

    /// <summary>LLM 抽取指令。租户可编辑——抽取时必须经 PromptBoundary 包裹。</summary>
    public virtual string Prompt { get; private set; } = default!;

    public virtual FieldDataType DataType { get; private set; }

    public virtual int DisplayOrder { get; private set; }

    public virtual bool IsRequired { get; private set; }

    protected TenantFieldDefinition() { }

    public TenantFieldDefinition(
        Guid id,
        Guid? tenantId,
        string documentTypeCode,
        string name,
        string prompt,
        FieldDataType dataType,
        int displayOrder = 0,
        bool isRequired = false)
        : base(id)
    {
        TenantId = tenantId;
        DocumentTypeCode = Check.NotNullOrWhiteSpace(documentTypeCode, nameof(documentTypeCode), TenantFieldConsts.MaxDocumentTypeCodeLength);
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), TenantFieldConsts.MaxNameLength);
        Prompt = Check.NotNullOrWhiteSpace(prompt, nameof(prompt), TenantFieldConsts.MaxPromptLength);
        DataType = dataType;
        DisplayOrder = displayOrder;
        IsRequired = isRequired;
    }

    public void Update(string prompt, FieldDataType dataType, int displayOrder, bool isRequired)
    {
        Prompt = Check.NotNullOrWhiteSpace(prompt, nameof(prompt), TenantFieldConsts.MaxPromptLength);
        DataType = dataType;
        DisplayOrder = displayOrder;
        IsRequired = isRequired;
    }
}
