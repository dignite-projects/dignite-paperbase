using System.Text.RegularExpressions;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// 把 Markdown 还原为纯文本（去除标记）。仅做语法层面的去除，不解析复杂结构。
/// 主要用于纯文本上下文（DTO 摘要、ContentLength 估算、不渲染 Markdown 的旧 UI 等）。
/// </summary>
public static class MarkdownStripper
{
    public static string Strip(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return string.Empty;

        var s = markdown;

        // 围栏代码块 ``` ... ``` —— 保留内部文本，去掉栅栏行
        s = Regex.Replace(s, @"^```[^\n]*\n", string.Empty, RegexOptions.Multiline);
        s = Regex.Replace(s, @"\n```\s*$", string.Empty, RegexOptions.Multiline);

        // 图片 ![alt](url) → alt
        s = Regex.Replace(s, @"!\[([^\]]*)\]\([^\)]*\)", "$1");

        // 链接 [text](url) → text
        s = Regex.Replace(s, @"\[([^\]]+)\]\([^\)]*\)", "$1");

        // 表格分隔行 |---|---|
        s = Regex.Replace(s, @"^\s*\|?[\s:\-\|]+\|\s*$", string.Empty, RegexOptions.Multiline);

        // 表格管道符 → 空格
        s = s.Replace("|", " ");

        // 标题 # ## ### ...
        s = Regex.Replace(s, @"^\s{0,3}#{1,6}\s*", string.Empty, RegexOptions.Multiline);

        // 引用 >
        s = Regex.Replace(s, @"^\s{0,3}>\s?", string.Empty, RegexOptions.Multiline);

        // 列表项 -, *, +, 1.
        s = Regex.Replace(s, @"^\s{0,3}([-*+]|\d+\.)\s+", string.Empty, RegexOptions.Multiline);

        // 水平线 ---, ***, ___
        s = Regex.Replace(s, @"^\s{0,3}([-*_]\s*){3,}$", string.Empty, RegexOptions.Multiline);

        // 加粗/斜体 **x**, __x__, *x*, _x_
        s = Regex.Replace(s, @"(\*\*|__)(.+?)\1", "$2");
        s = Regex.Replace(s, @"(?<!\w)([*_])(.+?)\1(?!\w)", "$2");

        // 行内代码 `code`
        s = Regex.Replace(s, @"`([^`]+)`", "$1");

        // 多余空行折叠
        s = Regex.Replace(s, @"\n{3,}", "\n\n");

        return s.Trim();
    }
}
