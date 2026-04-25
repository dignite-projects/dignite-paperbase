namespace Dignite.Paperbase.Documents;

/// <summary>
/// 分类流水线产出的 top-K 候选项 JSON payload schema，持久化在
/// <see cref="DocumentPipelineRun.ExtraProperties"/>[<see cref="PipelineRunExtraPropertyNames.ClassificationCandidates"/>]，
/// 主要供 Angular 端展示使用。
/// </summary>
public record PipelineRunCandidate(string TypeCode, double ConfidenceScore);
