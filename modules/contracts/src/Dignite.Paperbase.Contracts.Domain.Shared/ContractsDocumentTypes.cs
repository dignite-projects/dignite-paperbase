namespace Dignite.Paperbase.Contracts;

public static class ContractsDocumentTypes
{
    /// <summary>
    /// 合同模块所有文档类型的命名空间前缀（含尾点）。
    /// 业务模块订阅 DocumentClassifiedEto 时按此前缀认领事件，避免与其他模块冲突。
    /// </summary>
    public const string Prefix = "contract.";

    public const string General = Prefix + "general";
}
