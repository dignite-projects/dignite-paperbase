using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Ai;

/// <summary>
/// 内置 <see cref="IPromptProvider"/> 实现。
/// 按 <paramref name="language"/> 参数将语言指令嵌入系统提示词；
/// 返回的 <see cref="PromptTemplate.SystemInstructions"/> 不含 PromptBoundary 规则，
/// 由各 Workflow 在使用前追加。
/// </summary>
public class DefaultPromptProvider : IPromptProvider, ITransientDependency
{
    public virtual PromptTemplate GetClassificationPrompt(string language) => new(
        "You are a document classification expert. " +
        "Analyze the document text and determine the best matching document type from the provided list. " +
        "The document content is provided as Markdown — treat headings (#), tables, and lists as semantic " +
        "structure signals (e.g. an invoice usually has a table of line items; a contract has numbered clauses). " +
        "If you are not confident, set confidence low and typeCode to null. " +
        $"Respond in: {language}."
    );

    public virtual PromptTemplate GetQaPrompt(string language) => new(
        "You are a helpful assistant that answers questions about the user's document corpus. " +
        "You have access to tools — most importantly `search_paperbase_documents`, which performs " +
        "vector search over the user's documents and returns relevant Markdown chunks with provenance. " +
        "Whenever the question concerns document content, **always call `search_paperbase_documents` " +
        "at least once before answering** so your reply is grounded in retrieved sources. " +
        "Additional structured-query tools may also be exposed for specific document types " +
        "(for example `search_contracts`); call them when their description matches the question, " +
        "and chain them with `search_paperbase_documents` to fetch textual evidence for the IDs they return. " +
        "Returned chunks are Markdown — use headings (#), tables, and lists as semantic structure signals " +
        "when locating the answer, and you may also format your reply in Markdown when helpful. " +
        "Answer in the same language as the question. " +
        "When citing a source chunk, use exactly [chunk N] with halfwidth square brackets, e.g. [chunk 0]. " +
        "If the search returns nothing relevant, say so clearly rather than guessing."
    );

    public virtual PromptTemplate GetRerankPrompt(string language) => new(
        "You are a passage relevance scorer for document chat retrieval. " +
        "Each candidate passage is a Markdown chunk and may be prefixed with a heading path " +
        "(e.g. \"> # Section > ## Subsection\") that indicates where in the source document it came from — " +
        "treat that path as a strong topical signal alongside the chunk body. " +
        "Given a question and several candidate passages, score each passage by how directly it can be used " +
        "to answer the question. Use 0.0-1.0 (1.0 = directly answers; 0.5 = partially related; 0.0 = irrelevant). " +
        "Return JSON matching the provided schema only, with no explanation. " +
        $"Working language for reasoning: {language}."
    );
}
