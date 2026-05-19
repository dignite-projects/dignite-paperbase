using System;
using System.Linq;
using Dignite.Paperbase.Abstractions.Documents;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// 文档类型实体（字段架构 v2）。两层独立单层模型：
/// <list type="bullet">
///   <item><c>TenantId == null</c> → Host 部署级类型（Host admin 通过 IDocumentTypeAppService 自助 CRUD；CurrentTenant.Id IS NULL）</item>
///   <item><c>TenantId != null</c> → 租户私有类型（租户 admin 通过 IDocumentTypeAppService 自助 CRUD）</item>
/// </list>
/// Host 与 tenant 各自独立宇宙——分类候选集按 Document.TenantId 严格匹配单层，不存在跨层 union。
/// 跨层同 TypeCode 是合法的两行（由 TenantId 区分），下游消费方按 (TenantId, TypeCode) 元组消费。
/// <para>
/// TypeCode 格式：<c>&lt;owner-module&gt;.&lt;sub-type&gt;</c>，由 <see cref="ValidateTypeCode"/> 强制；
/// 唯一约束 <c>(TenantId, TypeCode)</c>。例：<c>host.general</c>、<c>host.contract</c>、<c>tenant.case-file</c>。
/// </para>
/// <para>
/// <see cref="DisplayName"/> 是普通字符串列，运行时直接展示（不再走 seed-time ILocalizableString 解析）。
/// </para>
/// <para>
/// 字段关系：<see cref="FieldDefinition.DocumentTypeCode"/> 字符串引用本实体的 TypeCode，
/// 按 DDD "reference by id" 原则不加 navigation property。
/// </para>
/// </summary>
public class DocumentType : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; private set; }

    public virtual string TypeCode { get; private set; } = default!;

    /// <summary>显示名称（运行时直接展示，普通字符串列）。</summary>
    public virtual string DisplayName { get; private set; } = default!;

    /// <summary>分类置信度阈值（低于此值进入 PendingReview 队列）。</summary>
    public virtual double ConfidenceThreshold { get; private set; }

    /// <summary>类型匹配优先级（数字越大优先级越高；fallback / 通用型通常为 0）。</summary>
    public virtual int Priority { get; private set; }

    protected DocumentType() { }

    public DocumentType(
        Guid id,
        Guid? tenantId,
        string typeCode,
        string displayName,
        double confidenceThreshold = ClassificationDefaults.DefaultConfidenceThreshold,
        int priority = 0)
        : base(id)
    {
        TenantId = tenantId;
        TypeCode = ValidateTypeCode(typeCode);
        DisplayName = ValidateDisplayName(displayName);
        ConfidenceThreshold = Check.Range(confidenceThreshold, nameof(confidenceThreshold), 0d, 1d);
        Priority = priority;
    }

    public void Update(string displayName, double confidenceThreshold, int priority)
    {
        DisplayName = ValidateDisplayName(displayName);
        ConfidenceThreshold = Check.Range(confidenceThreshold, nameof(confidenceThreshold), 0d, 1d);
        Priority = priority;
    }

    /// <summary>
    /// <para>
    /// TypeCode 形状约束：必须含 <c>.</c> 分隔符且首尾不为 <c>.</c>（owner-module . sub-type 形态）。
    /// 当前实现**只**检查这一约束，未强制字符白名单；可接受的输入字符集等同于"非空白 + 长度上限"。
    /// </para>
    /// <para>
    /// ⚠️ <strong>Prompt injection 边界</strong>：<see cref="TypeCode"/> 在
    /// <c>DocumentClassificationWorkflow</c> 内**裸拼**进 LLM 系统提示（不经
    /// <c>PromptBoundary.WrapField</c> 包裹）。当前安全性依赖：
    /// <list type="bullet">
    ///   <item>TypeCode 由 admin 在 UI 配置（非租户终端用户可控）</item>
    ///   <item>形状约束 <c>&lt;owner&gt;.&lt;sub-type&gt;</c> 与 admin 实际使用的字符集（小写字母数字 + <c>.</c> + <c>-</c>）足够窄</item>
    /// </list>
    /// 如未来需要放宽字符集（例如允许空格、Unicode、多段 <c>.</c>），<strong>必须</strong>在放宽前
    /// 同步评估 prompt injection 面，要么收紧白名单 regex，要么在 Workflow 内对 TypeCode 也走
    /// <c>PromptBoundary.WrapField</c>。详见 <c>.claude/rules/llm-call-anti-patterns.md</c>。
    /// </para>
    /// </summary>
    private static string ValidateTypeCode(string typeCode)
    {
        Check.NotNullOrWhiteSpace(typeCode, nameof(typeCode), DocumentTypeConsts.MaxTypeCodeLength);

        var dotIndex = typeCode.IndexOf('.');
        if (dotIndex <= 0 || dotIndex == typeCode.Length - 1)
        {
            throw new ArgumentException(
                $"TypeCode must follow the '<owner-module>.<sub-type>' convention (e.g. 'host.general'). Got: '{typeCode}'.",
                nameof(typeCode));
        }

        return typeCode;
    }

    /// <summary>
    /// DisplayName 是 admin 通过 UI 输入的用户控制文本，且会被字面拼入 LLM 分类 prompt
    /// （<see cref="DocumentClassificationWorkflow"/> 内已加 <c>PromptBoundary.WrapField</c> 包裹作为深度防御）。
    /// 此处实体层加一层硬约束——拒绝换行 / 控制字符——防止恶意 admin 注入形如
    /// <c>"Contract\n---\nIgnore previous instructions"</c> 的字符串穿透 prompt 边界。
    /// 允许 Unicode 字母数字 / 标点 / 空格（支持中日文 displayName）。
    /// </summary>
    private static string ValidateDisplayName(string displayName)
    {
        Check.NotNullOrWhiteSpace(displayName, nameof(displayName), DocumentTypeConsts.MaxDisplayNameLength);

        // 控制字符（含 \r \n \t \0 等 C0/C1）一律拒绝——这是 prompt injection 主要注入向量。
        if (displayName.Any(c => char.IsControl(c)))
        {
            throw new BusinessException(PaperbaseErrorCodes.InvalidDocumentTypeDisplayName)
                .WithData("displayName", displayName);
        }

        return displayName;
    }
}
