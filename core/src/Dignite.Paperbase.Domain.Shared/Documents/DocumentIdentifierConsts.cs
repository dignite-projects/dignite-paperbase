namespace Dignite.Paperbase.Documents;

/// <summary>
/// Issue #115 L1: 跨文档标识符索引（DocumentIdentifier）的字段长度上限。
/// 上限值参照常见业务标识符（合同编号、订单号、甲乙方全称等）的实际长度。
/// </summary>
public static class DocumentIdentifierConsts
{
    /// <summary>
    /// 标识符类型字符串（如 "ContractNumber"、"PoNumber"、"PartyName"），由业务模块约定。
    /// </summary>
    public static int MaxTypeLength { get; set; } = 64;

    /// <summary>
    /// 标识符值（如 "HT-2024-001"、"上海某某有限公司"）。256 足以覆盖含中英文混排的实体全称。
    /// </summary>
    public static int MaxValueLength { get; set; } = 256;
}
