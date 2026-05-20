using Dignite.Paperbase.Ocr;

namespace Dignite.Paperbase.TextExtraction.OcrProfiles;

public class OcrQualityAssessment
{
    public double Score { get; set; }
    public bool IsLowQuality { get; set; }
    public string DiagnosisCode { get; set; } = OcrQualityDiagnosisCodes.None;
    public string? TargetedRetryProfileCode { get; set; }
    public OcrQualitySignals Signals { get; set; } = new();
}
