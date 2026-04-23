namespace Dignite.Paperbase.Features;

public static class PaperbaseAIFeatures
{
    public const string GroupName = "PaperbaseAI";

    /// <summary>
    /// 租户每月 AI 调用预算上限（美元）。
    /// 默认值 decimal.MaxValue 表示无限制（私有化部署场景）。
    /// SaaS 场景按订阅计划设定具体数值。
    /// </summary>
    public const string MonthlyBudgetUsd = GroupName + ".MonthlyBudgetUsd";
}
