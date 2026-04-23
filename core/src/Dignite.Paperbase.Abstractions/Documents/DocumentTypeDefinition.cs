using System.Collections.Generic;

namespace Dignite.Paperbase.Abstractions.Documents;

/// <summary>
/// 文档类型定义。由业务模块在启动时注册到 <see cref="DocumentTypeOptions"/>。
/// </summary>
public class DocumentTypeDefinition
{
    /// <summary>文档类型唯一标识，如 "contract.general"、"contract.nda"</summary>
    public string TypeCode { get; set; } = default!;

    /// <summary>显示名称（用于 UI 展示）</summary>
    public string DisplayName { get; set; } = default!;

    /// <summary>关键词列表，用于规则分类器匹配</summary>
    public IList<string> MatchKeywords { get; set; } = new List<string>();

    /// <summary>分类置信度阈值（低于此值进入 LowConfidence 队列）</summary>
    public double ConfidenceThreshold { get; set; } = 0.7;

    /// <summary>类型匹配优先级（数字越大优先级越高）</summary>
    public int Priority { get; set; } = 0;

    public DocumentTypeDefinition(string typeCode, string displayName)
    {
        TypeCode = typeCode;
        DisplayName = displayName;
    }
}
