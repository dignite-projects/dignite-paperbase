using System.Collections.Generic;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// 核心层定义的流水线标识常量。
/// 业务模块可注册自定义 PipelineCode（建议命名前缀 "{moduleCode}."），
/// 但不会被计入生命周期派生。
/// </summary>
public static class PaperbasePipelines
{
    /// <summary>文本提取（OCR 或原生提取）。关键流水线。</summary>
    public const string TextExtraction = "text-extraction";

    /// <summary>文档分类（规则匹配 / AI）。关键流水线。</summary>
    public const string Classification = "classification";

    /// <summary>文本分块 + 向量化。非关键流水线，失败降级为全文检索。</summary>
    public const string Embedding = "embedding";

    /// <summary>AI 关系推断。非关键流水线。</summary>
    public const string RelationInference = "relation-inference";

    /// <summary>生命周期派生时视为"关键"的流水线集合。</summary>
    public static readonly IReadOnlyCollection<string> KeyPipelines = new[]
    {
        TextExtraction,
        Classification
    };
}
