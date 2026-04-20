using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Paperbase.Abstractions.AI;

/// <summary>
/// 结构化字段提取能力端口。
/// 实现：Dignite.Paperbase.AI
/// </summary>
public interface IFieldExtractor
{
    Task<FieldExtractionResult> ExtractAsync(
        FieldExtractionRequest request,
        CancellationToken cancellationToken = default);
}

public class FieldExtractionRequest
{
    public string ExtractedText { get; set; } = default!;
    public string DocumentTypeCode { get; set; } = default!;
    public IList<FieldSchema> Fields { get; set; } = new List<FieldSchema>();
}

public class FieldSchema
{
    public string Name { get; set; } = default!;
    public string Type { get; set; } = "string";
    public string? Description { get; set; }
    public bool Required { get; set; }
}

public class FieldExtractionResult
{
    /// <summary>字段名 → 提取值（字符串，业务模块自行解析类型）</summary>
    public IDictionary<string, string?> Fields { get; set; } = new Dictionary<string, string?>();
    public IDictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
}
