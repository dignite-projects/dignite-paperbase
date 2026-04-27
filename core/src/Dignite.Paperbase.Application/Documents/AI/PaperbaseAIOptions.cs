namespace Dignite.Paperbase.Documents.AI;

public class PaperbaseAIOptions
{
    /// <summary>
    /// 分类提示词中最多包含的候选类型数量，超出时按 Priority 降序截断。
    /// </summary>
    public int MaxDocumentTypesInClassificationPrompt { get; set; } = 50;

    /// <summary>
    /// 文本分块大小（字符数），约 400 个日文字符。
    /// </summary>
    public int ChunkSize { get; set; } = 800;

    /// <summary>
    /// 相邻 Chunk 重叠字符数，保证语义连续性。
    /// </summary>
    public int ChunkOverlap { get; set; } = 100;

    /// <summary>
    /// 结构化提取单次调用最大文本长度，超出时截断。
    /// </summary>
    public int MaxTextLengthPerExtraction { get; set; } = 8000;

    /// <summary>
    /// 向量检索返回的最大 Chunk 数（Top-K）。
    /// </summary>
    public int QaTopKChunks { get; set; } = 5;

    /// <summary>
    /// AI 交互默认语言（影响系统提示词语言）。
    /// </summary>
    public string DefaultLanguage { get; set; } = "ja";

    /// <summary>
    /// 关系推断最低置信度阈值；低于此值的推断项将被硬性过滤，即使 LLM 在 prompt 中已被要求排除。
    /// </summary>
    public double RelationInferenceMinConfidence { get; set; } = 0.5;
}
