namespace Dignite.Paperbase.Domain.Documents;

/// <summary>
/// 分类流水线产出的 top-K 候选项，持久化在
/// <see cref="DocumentPipelineRun.ExtraProperties"/>[<see cref="PipelineRunExtraPropertyNames.ClassificationCandidates"/>]。
/// </summary>
public class PipelineRunCandidate
{
    public string TypeCode { get; set; } = default!;
    public double ConfidenceScore { get; set; }
}
