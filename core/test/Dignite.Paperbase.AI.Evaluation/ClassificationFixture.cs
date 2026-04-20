using System.Collections.Generic;

namespace Dignite.Paperbase.AI.Evaluation;

public class ClassificationFixture
{
    public string Id { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/pdf";
    public string FileExtension { get; set; } = ".pdf";
    public string? SampleText { get; set; }
    public ExpectedClassification Expected { get; set; } = new();
    public List<string> Tags { get; set; } = new();
}

public class ExpectedClassification
{
    public string TypeCode { get; set; } = string.Empty;
    public double MinConfidence { get; set; } = 0.5;
}
