using System.Collections.Generic;
using System.Text;
using Dignite.Paperbase.Abstractions.AI;

namespace Dignite.Paperbase.AI.Prompts;

public static class QaPrompts
{
    public const string KeyDocumentQaV1 = "qa.document.v1";
    public const string DocumentQaV1Version = "1.0.0";

    public const string KeyGlobalQaV1 = "qa.global.v1";
    public const string GlobalQaV1Version = "1.0.0";

    public const string DocumentQaV1System =
        "You are a helpful assistant that answers questions based on the provided document content. " +
        "Answer in the same language as the question. " +
        "If citing a source, reference it by [chunk N]. " +
        "If the answer is not in the provided content, say so clearly rather than guessing.";

    public static string BuildRagUser(string question, IEnumerable<QaChunkData> chunks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Document content:");
        foreach (var chunk in chunks)
        {
            sb.AppendLine($"[chunk {chunk.ChunkIndex}]");
            sb.AppendLine(chunk.ChunkText);
            sb.AppendLine();
        }
        sb.AppendLine($"Question: {question}");
        return sb.ToString();
    }

    public static string BuildFullTextUser(string question, string? extractedText, int maxLength)
    {
        var text = extractedText ?? string.Empty;
        if (text.Length > maxLength)
            text = text[..maxLength] + "\n[... document truncated ...]";

        return $"Document content:\n{text}\n\nQuestion: {question}";
    }
}
