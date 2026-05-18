namespace Dignite.Paperbase.Documents;

public static class OutboxEventConsts
{
    /// <summary>
    /// EventType 列最大长度——足以容纳常见 ETO 类型名（30 字符）+ 命名空间前缀。
    /// </summary>
    public const int MaxEventTypeLength = 128;
}
