namespace Dignite.Paperbase.Abstractions.Documents;

/// <summary>
/// 字段数据类型——影响 LLM 抽取时的 schema 提示与下游解析行为。
/// 用于 <see cref="HostFieldDefinition"/> 和租户自定义字段定义（#169）。
/// </summary>
public enum FieldDataType
{
    String = 0,
    Integer = 1,
    Decimal = 2,
    Boolean = 3,
    Date = 4,
    DateTime = 5
}
