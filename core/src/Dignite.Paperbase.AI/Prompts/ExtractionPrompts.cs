using System.Collections.Generic;
using System.Linq;
using Dignite.Paperbase.Abstractions.AI;

namespace Dignite.Paperbase.AI.Prompts;

public static class ExtractionPrompts
{
    public const string KeyGenericV1 = "extraction.generic.v1";
    public const string GenericV1Version = "1.0.0";

    public static readonly string GenericV1System =
        "あなたは文書情報抽出の専門家です。" +
        "指定されたフィールドの情報をJSONで回答してください。" +
        "情報が見つからない場合は null を使用してください。";

    public static string BuildGenericV1User(
        IList<FieldSchema> fieldSchemas,
        string extractedText,
        int maxTextLength = 8000)
    {
        var fieldDescriptions = fieldSchemas.Select(f =>
            $"- {f.Name}" +
            (f.Description != null ? $"（{f.Description}）" : string.Empty) +
            $" 型: {f.Type}" +
            (f.Required ? " [必須]" : " [任意]"));

        var truncatedText = extractedText.Length > maxTextLength
            ? extractedText[..maxTextLength]
            : extractedText;

        return $"""
                ## 抽出対象フィールド
                {string.Join("\n", fieldDescriptions)}

                ## 文書テキスト
                {truncatedText}

                ## 回答形式（JSON のみ）
                フィールド名をキーとした JSON オブジェクトで回答してください。
                日付は ISO 8601 形式（YYYY-MM-DD）で出力してください。
                金額は数値型で出力してください（単位・カンマ不要）。
                """;
    }
}
