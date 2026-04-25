using System;
using System.Collections.Generic;
using Volo.Abp;

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
/// </remarks>
public class DocumentTypeDefinition
{
    /// <summary>文档类型唯一标识，须遵循 <c>&lt;owner-module&gt;.&lt;sub-type&gt;</c> 命名约定。</summary>
    public string TypeCode { get; set; } = default!;

    /// <summary>显示名称（用于 UI 展示）</summary>
    public string DisplayName { get; set; } = default!;

    /// <summary>关键词列表，用于规则分类器匹配</summary>
    public IList<string> MatchKeywords { get; set; } = new List<string>();

    /// <summary>分类置信度阈值（低于此值进入 LowConfidence 队列）</summary>
    public double ConfidenceThreshold { get; set; } = ClassificationDefaults.DefaultConfidenceThreshold;

    /// <summary>类型匹配优先级（数字越大优先级越高）</summary>
    public int Priority { get; set; } = 0;

    public DocumentTypeDefinition(string typeCode, string displayName)
    {
        TypeCode = ValidateTypeCode(typeCode);
        DisplayName = Check.NotNullOrWhiteSpace(displayName, nameof(displayName));
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
