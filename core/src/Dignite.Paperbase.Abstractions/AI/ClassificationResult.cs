using System.Collections.Generic;

namespace Dignite.Paperbase.Abstractions.AI;

public class ClassificationResult
{
    public string? TypeCode { get; set; }
    public double ConfidenceScore { get; set; }

    /// <summary>Top-K 候选类型（含主结果），用于 LowConfidence 人工确认 UI</summary>
    public IList<TypeCandidate> Candidates { get; set; } = new List<TypeCandidate>();

    /// <summary>能力私有元数据（Provider、Model、Cost、Latency 等）</summary>
    public IDictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
}

public class TypeCandidate
{
    public string TypeCode { get; set; } = default!;
    public double ConfidenceScore { get; set; }
}
