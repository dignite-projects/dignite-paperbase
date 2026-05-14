using System;
using Volo.Abp;
using Volo.Abp.Localization;

namespace Dignite.Paperbase.Abstractions.Documents;

/// <summary>
/// 文档类型定义。由业务模块在启动时注册到 <see cref="DocumentTypeOptions"/>。
/// </summary>
/// <remarks>
/// <para>
/// <b>TypeCode 命名约定（强制）</b>：必须使用 <c>&lt;owner-module&gt;.&lt;sub-type&gt;</c>
/// 形式，如 <c>contract.general</c>、<c>contract.nda</c>。前缀（owner-module）标识
/// 该类型所属的业务模块，分类完成后业务模块可基于该前缀认领事件、避免与其他模块冲突。
/// </para>
/// <para>
/// 构造函数会校验 TypeCode 必须包含至少一个 <c>.</c> 且前后段非空。
/// </para>
/// <para>
/// <b>DisplayName 为 <see cref="ILocalizableString"/>（延迟本地化）</b>：注册侧用
/// <c>LocalizableString.Create&lt;TResource&gt;("Key")</c>，UI / prompt 消费侧通过
/// <c>IStringLocalizerFactory</c> 按当前 culture 解析。这是 ABP 的标准模式
/// （对齐 <c>PermissionDefinition.DisplayName</c> / <c>MenuItem.DisplayName</c> /
/// <c>SettingDefinition.DisplayName</c>），定义对象本身长生命周期、不持有
/// 已翻译字符串，确保多租户多语言下展示正确。
/// </para>
/// </remarks>
public class DocumentTypeDefinition
{
    /// <summary>文档类型唯一标识，须遵循 <c>&lt;owner-module&gt;.&lt;sub-type&gt;</c> 命名约定。</summary>
    public string TypeCode { get; set; } = default!;

    /// <summary>显示名称（用于 UI 展示与 LLM prompt）。运行时通过 <see cref="IStringLocalizerFactory"/> 解析。</summary>
    public ILocalizableString DisplayName { get; set; } = default!;

    /// <summary>分类置信度阈值（低于此值进入 LowConfidence 队列）</summary>
    public double ConfidenceThreshold { get; set; } = ClassificationDefaults.DefaultConfidenceThreshold;

    /// <summary>类型匹配优先级（数字越大优先级越高）</summary>
    public int Priority { get; set; } = 0;

    public DocumentTypeDefinition(string typeCode, ILocalizableString displayName)
    {
        TypeCode = ValidateTypeCode(typeCode);
        DisplayName = Check.NotNull(displayName, nameof(displayName));
    }

    /// <summary>
    /// 校验 TypeCode 必须遵循 <c>&lt;owner-module&gt;.&lt;sub-type&gt;</c> 命名约定。
    /// </summary>
    private static string ValidateTypeCode(string typeCode)
    {
        Check.NotNullOrWhiteSpace(typeCode, nameof(typeCode));

        var dotIndex = typeCode.IndexOf('.');
        if (dotIndex <= 0 || dotIndex == typeCode.Length - 1)
        {
            throw new ArgumentException(
                $"TypeCode must follow the '<owner-module>.<sub-type>' convention (e.g. 'contract.general'). Got: '{typeCode}'.",
                nameof(typeCode));
        }

        return typeCode;
    }
}
