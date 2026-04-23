using System.Collections.Generic;
using System.Text;
using Dignite.Paperbase.Abstractions.AI;

namespace Dignite.Paperbase.AI.Prompts;

public static class RelationInferencePrompts
{
    public const string KeyRelationInferenceV1 = "relation-inference.v1";
    public const string RelationInferenceV1Version = "1.0.0";

    public const string RelationInferenceV1System =
        "You are a document relationship analyst. Given a source document and a list of candidate documents, " +
        "identify which candidates have a meaningful relationship with the source. " +
        "Relation types: " +
        "\"supplements\" (adds information to), " +
        "\"supersedes\" (replaces or amends), " +
        "\"belongs-to\" (attachment or sub-document of), " +
        "\"related-to\" (general relevance). " +
        "Return a JSON array. Each item must have: targetDocumentId (string), relationType (string), confidence (0.0-1.0). " +
        "Only include pairs with confidence >= 0.5. Return [] if none qualify.";

    public static string BuildRelationInferenceV1User(
        string sourceText, IEnumerable<DocumentSummary> candidates)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Source document excerpt:");
        var truncated = sourceText.Length > 600 ? sourceText[..600] + "..." : sourceText;
        sb.AppendLine(truncated);
        sb.AppendLine();
        sb.AppendLine("Candidate documents:");

        foreach (var candidate in candidates)
        {
            sb.AppendLine($"- id: {candidate.DocumentId}, type: {candidate.DocumentTypeCode ?? "unknown"}");
            sb.AppendLine($"  excerpt: {candidate.Summary}");
        }

        return sb.ToString();
    }
}
