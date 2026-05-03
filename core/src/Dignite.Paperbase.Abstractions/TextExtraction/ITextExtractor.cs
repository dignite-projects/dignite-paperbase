using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Paperbase.Abstractions.TextExtraction;

/// <summary>
/// 文本提取能力端口。纯能力——收文件流与上下文，返回提取结果；
/// 不知道 Document 聚合、不访问仓储。
/// 实现：Dignite.Paperbase.TextExtraction
/// </summary>
/// <remarks>
/// <para>
/// <b>Markdown-first 契约</b>：实现方<b>必须</b>在
/// <see cref="TextExtractionResult.Markdown"/> 中返回 Markdown 文本。
/// 这是项目"AI 驱动企业档案平台"定位下的核心数据流约束——
/// Markdown 同时被向量化（结构感知切块）、LLM 分类 / QA / Rerank、业务模块字段抽取消费，
/// 标题、表格、列表是 LLM 理解文档的关键语义信号。
/// </para>
/// <para>
/// 即使源文件没有结构（例如低质量扫描件 OCR 仅产出散段落），仍应以扁平 Markdown 段落输出，
/// 而<b>不能</b>退回到独立的"plain text"路径或在 <see cref="TextExtractionResult"/> 上引入并行的纯文本字段。
/// 下游需要纯文本时，统一通过 <c>Dignite.Paperbase.Documents.MarkdownStripper</c> 在消费侧投影。
/// </para>
/// </remarks>
public interface ITextExtractor
{
    /// <summary>
    /// 从文件流中提取 Markdown。
    /// </summary>
    /// <param name="fileStream">原始文件流。</param>
    /// <param name="context">业务无关的提取上下文（contentType / 文件名 / 期望语言等）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// 包含 <see cref="TextExtractionResult.Markdown"/> 的提取结果。
    /// 未识别到任何内容时 <see cref="TextExtractionResult.Markdown"/> 为空字符串，
    /// 但<b>不应</b>返回 <c>null</c>，也<b>不应</b>抛异常代替"无内容"。
    /// </returns>
    Task<TextExtractionResult> ExtractAsync(
        Stream fileStream,
        TextExtractionContext context,
        CancellationToken cancellationToken = default);
}
