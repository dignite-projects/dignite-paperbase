namespace Dignite.Paperbase.Domain.Documents;

/// <summary>
/// <see cref="DocumentPipelineRun.ExtraProperties"/> 的 key 常量。
/// 每种 pipeline 约定自己使用的 key；业务模块若新增 pipeline，建议前缀 "{moduleCode}." 以避免冲突。
/// </summary>
public static class PipelineRunExtraPropertyNames
{
    /// <summary>
    /// 分类流水线 top-K 候选结果。值类型：<see cref="System.Collections.Generic.List{T}"/> of
    /// <see cref="PipelineRunCandidate"/>。
    /// </summary>
    public const string ClassificationCandidates = "Candidates";
}
