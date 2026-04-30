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
    /// AI 交互默认语言（影响系统提示词语言）。
    /// </summary>
    public string DefaultLanguage { get; set; } = "ja";

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

    /// <summary>
    /// Controls when the <c>TextSearchProvider</c> fetches document context during a
    /// chat turn. Defaults to <see cref="ChatSearchBehavior.BeforeAIInvoke"/> (retrieval
    /// before every AI invocation; citations always populated).
    /// <para>
    /// Switch to <see cref="ChatSearchBehavior.OnDemandFunctionCalling"/> to let the
    /// model decide when to search. This saves tokens but may yield an empty citation
    /// list when the model declines to call the tool
    /// (<c>ChatTurnResultDto.IsDegraded = true</c> in that case).
    /// </para>
    /// </summary>
    public ChatSearchBehavior ChatSearchBehavior { get; set; } = ChatSearchBehavior.BeforeAIInvoke;
}
