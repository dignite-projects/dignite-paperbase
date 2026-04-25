namespace Dignite.Paperbase.Documents;

/// <summary>
/// <see cref="DocumentPipelineRun.ExtraProperties"/> 的 key 常量。
/// 每种 pipeline 约定自己使用的 key；业务模块若新增 pipeline，建议前缀 "{moduleCode}." 以避免冲突。
/// </summary>
public static class PipelineRunExtraPropertyNames
{
    /// <summary>
    /// 分类流水线 top-K 候选结果。
    /// 写入时使用 <see cref="PipelineRunCandidate"/> 作为 JSON payload schema；
    /// 持久化并通过 API 暴露时约定为 JSON array，主要供 Angular 端展示。
    /// </summary>
    public const string ClassificationCandidates = "Candidates";
}
