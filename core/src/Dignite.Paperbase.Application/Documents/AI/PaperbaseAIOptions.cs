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
    /// 分块边界回溯容差（字符数）。在 <c>[ChunkSize - ChunkBoundaryTolerance, ChunkSize]</c>
    /// 范围内向后查找最近的自然断点（段落/句末/标点）作为切点，避免硬切句子。
    /// 设为 0 退化为原"固定字符长度"分块。建议值约 ChunkSize 的 15%。
    /// </summary>
    public int ChunkBoundaryTolerance { get; set; } = 120;

    /// <summary>
    /// 结构化提取单次调用最大文本长度，超出时截断。
    /// </summary>
    public int MaxTextLengthPerExtraction { get; set; } = 8000;

    /// <summary>
    /// 向量检索返回的最大 Chunk 数（Top-K）。
    /// </summary>
    public int QaTopKChunks { get; set; } = 5;

    /// <summary>
    /// QA 检索的最低 cosine 相似度阈值，取值范围 [0, 1]，越大越严格。
    /// 命中 chunk 的相似度低于此值时将被丢弃，避免无关上下文污染 prompt。
    /// 设为 0 关闭阈值过滤（保持 Slice 4 之前的行为）。
    /// </summary>
    public double QaMinScore { get; set; } = 0.65;

    /// <summary>
    /// 启用 LLM 精排：先按 <see cref="QaTopKChunks"/> × <see cref="RecallExpandFactor"/>
    /// 召回扩大，再让 LLM 给每个候选 chunk 打分，取分数最高的 <see cref="QaTopKChunks"/> 个进入 RAG。
    /// 默认关闭以保守 token 成本；中文/多语言场景或召回质量不佳时建议启用。
    /// </summary>
    public bool EnableLlmRerank { get; set; } = false;

    /// <summary>
    /// 启用 <see cref="EnableLlmRerank"/> 时的召回扩大倍数（实际召回 = QaTopKChunks × 此值）。
    /// 默认 4，意味着 TopK=5 时召回 20 个候选，由 LLM 重排后取前 5。
    /// </summary>
    public int RecallExpandFactor { get; set; } = 4;

    /// <summary>
    /// AI 交互默认语言（影响系统提示词语言）。
    /// </summary>
    public string DefaultLanguage { get; set; } = "ja";

    /// <summary>
    /// 关系推断最低置信度阈值；低于此值的推断项将被硬性过滤，即使 LLM 在 prompt 中已被要求排除。
    /// </summary>
    public double RelationInferenceMinConfidence { get; set; } = 0.5;

    /// <summary>
    /// 关系推断候选文档召回上限（Top-K 文档数），与 QaTopKChunks 解耦，允许独立调优。
    /// </summary>
    public int RelationInferenceCandidateTopK { get; set; } = 20;

    /// <summary>
    /// 每个候选文档摘要的最大字符数，超出时截断。
    /// </summary>
    public int MaxRelationCandidateSummaryLength { get; set; } = 500;

    /// <summary>
    /// 关系推断 Prompt 总字符预算（源文档摘要 + 所有候选摘要之和），
    /// 超出时从低优先级候选末尾依次丢弃并记录警告。
    /// </summary>
    public int MaxRelationInferencePromptCharacters { get; set; } = 30000;

    /// <summary>
    /// 启用时向 LLM 传递 <c>ChatOptions.ResponseFormat = Json</c>，
    /// 由 SDK 注入类型 schema 约束，同时从 prompt 中移除手写的 JSON schema 文本。
    /// 关闭时回退到旧的 prompt 内联 schema（适用于不支持 JSON mode 的 Provider）。
    /// </summary>
    public bool UseStrictJsonMode { get; set; } = true;

    /// <summary>
    /// 启用 <c>UseDistributedCache()</c> 中间件，对相同输入的 LLM 请求直接返回缓存响应，
    /// 省去重复 token 消耗。使用宿主中已注册的 <c>IDistributedCache</c>（默认内存缓存）。
    /// 开发/测试环境若需每次强制请求 LLM，可在 appsettings 中将此项设为 false。
    /// </summary>
    public bool PromptCachingEnabled { get; set; } = true;
}
