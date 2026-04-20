namespace Dignite.Paperbase.Documents;

public enum DocumentLifecycleStatus
{
    /// <summary>文件已落盘，尚未启动任何流水线</summary>
    Uploaded = 10,

    /// <summary>至少一条关键流水线仍在运行或尚未开始</summary>
    Processing = 20,

    /// <summary>
    /// 所有关键流水线（TextExtraction、Classification）都成功完成。
    /// 文档对业务可用；非关键流水线（如 embedding）可能仍在进行。
    /// </summary>
    Ready = 30,

    /// <summary>至少一条关键流水线最终失败（重试用尽仍失败）</summary>
    Failed = 99
}
