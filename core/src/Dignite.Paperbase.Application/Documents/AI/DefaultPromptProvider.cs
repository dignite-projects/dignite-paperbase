using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Documents.AI;

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
        "If you are not confident, set confidence low and typeCode to null. " +
        $"Respond in: {language}."
    );

    public virtual PromptTemplate GetRelationInferencePrompt(string language, double minConfidence) => new(
        "You are a document relation analyst. Given a source document and several candidate documents, " +
        "identify candidates that have a substantive relationship with the source, and write one sentence " +
        "that clearly states the relationship. " +
        "Example phrasings: 'This contract supplements the payment terms in section 3 of the main contract.'; " +
        "'This supersedes the 2024-03 version, making the original void.'; " +
        "'This is an attachment list of the main contract.'; " +
        "'This addresses the same project as the main contract.'. " +
        "Return a JSON array; each item contains: targetDocumentId (string), description (one sentence, " +
        "max 200 characters), confidence (0.0-1.0). " +
        $"Include only items with confidence >= {minConfidence:F1}; return [] if none. " +
        $"Write all descriptions in: {language}."
    );

    public virtual PromptTemplate GetQaPrompt(string language) => new(
        "You are a helpful assistant that answers questions based on the provided document content. " +
        "Answer in the same language as the question. " +
        "When citing a source chunk, use exactly [chunk N] with halfwidth square brackets, e.g. [chunk 0]. " +
        "If the answer is not in the provided content, say so clearly rather than guessing."
    );

    public virtual PromptTemplate GetRerankPrompt(string language) => new(
        "You are a passage relevance scorer for document QA. " +
        "Given a question and several candidate passages, score each passage by how directly it can be used " +
        "to answer the question. Use 0.0-1.0 (1.0 = directly answers; 0.5 = partially related; 0.0 = irrelevant). " +
        "Return a JSON array; each item contains: id (the integer id provided with the passage), " +
        "score (0.0-1.0). Output the array only, no explanation. " +
        $"Working language for reasoning: {language}."
    );
}
