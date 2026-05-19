namespace Dignite.Paperbase.Documents;

/// <summary>
/// OCR 流水线的配置选项。Host 在 ConfigureServices 中绑定到 PaperbaseOcr 配置节；
/// per-tenant 覆盖通过 ABP Setting Management（key = <see cref="ConfidenceThresholdSettingName"/>）。
/// </summary>
public class PaperbaseOcrOptions
{
    /// <summary>
    /// OCR 置信度门槛默认值（部署级）。0.0 - 1.0。<see cref="DocumentReadyEto"/>
    /// 发布前必须 ≥ 此值（或操作员手动通过审核）；不达标的文档进待人工审核队列。
    /// </summary>
    public double DefaultConfidenceThreshold { get; set; } = 0.85;

    /// <summary>
    /// per-tenant 覆盖时使用的 ABP Setting key。在多租户场景下，租户可通过 Setting Management
    /// API 设置自己的门槛（运营运维需求：高保真合同租户可拉到 0.95；信息密度低的草稿租户调到 0.7）。
    /// <para>
    /// 必须是 <c>const</c>：这是 ABP Setting 表的行键 + appsettings 配置节绑定 key。
    /// 任何运行时改动都会让已写入 DB 的 setting 行按旧 key 存、新代码按新 key 读，全部读不到。
    /// </para>
    /// </summary>
    public const string ConfidenceThresholdSettingName = "Paperbase.Ocr.ConfidenceThreshold";
}
