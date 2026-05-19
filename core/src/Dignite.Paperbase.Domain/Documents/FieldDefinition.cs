using System;
using System.Text.RegularExpressions;
using Dignite.Paperbase.Abstractions.Documents;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// 字段定义实体（字段架构 v2）。两层独立单层模型：
/// <list type="bullet">
///   <item><c>TenantId == null</c> → Host 字段定义（Host admin 通过 IFieldDefinitionAppService 自助 CRUD；CurrentTenant.Id IS NULL）</item>
///   <item><c>TenantId != null</c> → 租户字段定义（租户 admin 通过 IFieldDefinitionAppService 自助 CRUD）</item>
/// </list>
/// Host 与 tenant 各自独立宇宙——字段抽取按 Document.TenantId 严格匹配单层，不跨层混合。
/// 唯一约束 <c>(TenantId, DocumentTypeCode, Name)</c>：同层同类型下字段名不重复；跨层同名是合法的两行。
/// <para>
/// <see cref="DocumentTypeCode"/> 字符串引用 <see cref="DocumentType.TypeCode"/>，按 DDD reference-by-id 原则不加 navigation。
/// 父类型必须存在于同层（同 TenantId）；不存在"租户字段挂在 Host 类型上"的关系。
/// </para>
/// <para>
/// 安全约束（CLAUDE.md "## 安全约定"）：
/// <list type="bullet">
///   <item>所有查询路径显式 TenantId 谓词</item>
///   <item><see cref="Prompt"/> 是用户控制文本，LLM 抽取时由 Workflow 经 <c>PromptBoundary.WrapField</c> 包裹</item>
///   <item><see cref="Name"/> 受 <see cref="FieldDefinitionConsts.NamePattern"/> 白名单约束——字面拼进 LLM prompt 的 JSON schema，必须无 prompt injection 控制字符</item>
/// </list>
/// </para>
/// </summary>
public class FieldDefinition : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    private static readonly Regex NameRegex = new(
        FieldDefinitionConsts.NamePattern,
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public virtual Guid? TenantId { get; private set; }

    public virtual string DocumentTypeCode { get; private set; } = default!;

    /// <summary>
    /// 字段名。**Immutable**——只在构造时通过 <see cref="ValidateName"/> 写入，<see cref="Update"/> 路径不重设。
    /// 设计意图：Name 进 LLM prompt schema、作为抽取结果字典的 JSON 键、被下游业务消费方依赖；
    /// rename 会破坏向后兼容。需要"换名"请新建字段并迁移数据。
    /// </summary>
    public virtual string Name { get; private set; } = default!;

    /// <summary>LLM 抽取指令——告诉模型从文档中找什么值。</summary>
    public virtual string Prompt { get; private set; } = default!;

    public virtual FieldDataType DataType { get; private set; }

    public virtual int DisplayOrder { get; private set; }

    public virtual bool IsRequired { get; private set; }

    protected FieldDefinition() { }

    public FieldDefinition(
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
        DocumentTypeCode = Check.NotNullOrWhiteSpace(documentTypeCode, nameof(documentTypeCode), FieldDefinitionConsts.MaxDocumentTypeCodeLength);
        Name = ValidateName(name);
        Prompt = Check.NotNullOrWhiteSpace(prompt, nameof(prompt), FieldDefinitionConsts.MaxPromptLength);
        DataType = dataType;
        DisplayOrder = displayOrder;
        IsRequired = isRequired;
    }

    public void Update(string prompt, FieldDataType dataType, int displayOrder, bool isRequired)
    {
        Prompt = Check.NotNullOrWhiteSpace(prompt, nameof(prompt), FieldDefinitionConsts.MaxPromptLength);
        DataType = dataType;
        DisplayOrder = displayOrder;
        IsRequired = isRequired;
    }

    private static string ValidateName(string name)
    {
        Check.NotNullOrWhiteSpace(name, nameof(name), FieldDefinitionConsts.MaxNameLength);
        if (!NameRegex.IsMatch(name))
        {
            throw new BusinessException(PaperbaseErrorCodes.InvalidFieldDefinitionName)
                .WithData("name", name)
                .WithData("pattern", FieldDefinitionConsts.NamePattern);
        }
        return name;
    }
}
