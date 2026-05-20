using System;
using System.Linq;

namespace Dignite.Paperbase.Ocr;

public static class OcrQualitySignalBuilder
{
    public static OcrQualitySignals FromMarkdown(string? markdown, double? confidence, int pageCount)
    {
        var value = markdown ?? string.Empty;
        var lines = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var tableMarkers = lines.Count(line => line.Count(c => c == '|') >= 2);
        var formLikeLines = lines.Count(IsFormLikeLine);
        var hasMeaningfulText = value.Any(c => char.IsLetterOrDigit(c));

        return new OcrQualitySignals
        {
            Confidence = confidence,
            PageCount = pageCount,
            MarkdownLength = value.Length,
            HasMeaningfulText = hasMeaningfulText,
            TableMarkerCount = tableMarkers,
            FormLikeLineCount = formLikeLines
        };
    }

    private static bool IsFormLikeLine(string line)
    {
        if (line.Length < 2)
        {
            return false;
        }

        return line.Contains(':', StringComparison.Ordinal)
            || line.Contains('：', StringComparison.Ordinal)
            || line.Contains("___", StringComparison.Ordinal)
            || line.Contains("[ ]", StringComparison.Ordinal);
    }
}
