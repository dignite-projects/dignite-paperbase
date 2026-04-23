using System.Collections.Generic;
using System.Linq;
using Dignite.Paperbase.Abstractions.AI;

namespace Dignite.Paperbase.AI.Prompts;

public static class ClassificationPrompts
{
    public const string KeyDocumentClassificationV1 = "classification.document.v1";
    public const string DocumentClassificationV1Version = "1.0.0";

    public static readonly string DocumentClassificationV1System =
        "あなたは文書分類の専門家です。" +
        "文書テキストを分析し、最も適合する文書タイプをJSONで回答してください。" +
        "確信が持てない場合は confidence を低く設定し、typeCode は null にしてください。";

    public static string BuildDocumentClassificationV1User(
        IEnumerable<DocumentTypeHint> types,
        string extractedText,
        int maxTextLength = 2000)
    {
        var typeDescriptions = types.Select(t =>
            $"- TypeCode: {t.TypeCode}\n" +
            $"  Name: {t.DisplayName}" +
            (t.Keywords.Count > 0
                ? $"\n  Keywords: {string.Join(", ", t.Keywords)}"
                : string.Empty));

        var truncatedText = extractedText.Length > maxTextLength
            ? extractedText[..maxTextLength]
            : extractedText;

        return $$"""
                ## 登録済み文書タイプ一覧
                {{string.Join("\n", typeDescriptions)}}

                ## 分類対象文書（先頭{{maxTextLength}}文字）
                {{truncatedText}}

                ## 回答形式（JSON のみ、説明不要）
                {
                  "typeCode": "最も適合するTypeCode、該当なしの場合は null",
                  "confidence": 0.0から1.0の数値,
                  "reason": "判定根拠を一文で",
                  "candidates": [
                    {"typeCode": "候補TypeCode", "confidence": 0.0から1.0}
                  ]
                }
                """;
    }
}
