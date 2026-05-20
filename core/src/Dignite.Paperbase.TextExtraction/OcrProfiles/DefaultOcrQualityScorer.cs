using System;
using Dignite.Paperbase.Ocr;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.TextExtraction.OcrProfiles;

public class DefaultOcrQualityScorer : IOcrQualityScorer, ITransientDependency
{
    // 全量 OCR 已完成，此处只决定"要不要再花一次定向重试"，故低置信触发线放得较宽松。
    private const double LowConfidenceThreshold = 0.72;
    private const double BetterScoreMargin = 0.03;
    private const int StructureMarkerThreshold = 3;

    public virtual OcrQualityAssessment Score(OcrResult result, string currentProfileCode)
    {
        var signals = result.QualitySignals
            ?? OcrQualitySignalBuilder.FromMarkdown(result.Markdown, result.Confidence, result.PageCount);

        var confidenceScore = signals.Confidence ?? 0;
        var textScore = signals.HasMeaningfulText ? 0.20 : 0;
        var lengthScore = Math.Clamp(signals.MarkdownLength / 1200d, 0, 0.10);
        var score = Math.Clamp((confidenceScore * 0.70) + textScore + lengthScore, 0, 1);

        var diagnosis = Diagnose(signals);
        var retryProfile = ResolveRetryProfile(diagnosis, currentProfileCode);
        var isLowQuality = !signals.HasMeaningfulText || confidenceScore < LowConfidenceThreshold;

        return new OcrQualityAssessment
        {
            Score = score,
            IsLowQuality = isLowQuality,
            DiagnosisCode = diagnosis,
            TargetedRetryProfileCode = isLowQuality ? retryProfile : null,
            Signals = signals
        };
    }

    public virtual bool IsBetter(OcrQualityAssessment candidate, OcrQualityAssessment current)
    {
        if (candidate.Signals.HasMeaningfulText != current.Signals.HasMeaningfulText)
        {
            return candidate.Signals.HasMeaningfulText;
        }

        if (candidate.Score >= current.Score + BetterScoreMargin)
        {
            return true;
        }

        if (candidate.Score + BetterScoreMargin < current.Score)
        {
            return false;
        }

        return (candidate.Signals.Confidence ?? 0) > (current.Signals.Confidence ?? 0);
    }

    private static string Diagnose(OcrQualitySignals signals)
    {
        if (!signals.HasMeaningfulText)
        {
            return OcrQualityDiagnosisCodes.EmptyText;
        }

        if (signals.TableMarkerCount >= StructureMarkerThreshold &&
            signals.TableMarkerCount >= signals.FormLikeLineCount)
        {
            return OcrQualityDiagnosisCodes.TableStructure;
        }

        if (signals.FormLikeLineCount >= StructureMarkerThreshold)
        {
            return OcrQualityDiagnosisCodes.FormKeyValue;
        }

        if (signals.Confidence is < LowConfidenceThreshold)
        {
            return OcrQualityDiagnosisCodes.LowConfidence;
        }

        return OcrQualityDiagnosisCodes.None;
    }

    private static string? ResolveRetryProfile(string diagnosis, string currentProfile)
    {
        var target = diagnosis switch
        {
            OcrQualityDiagnosisCodes.TableStructure => OcrProfileCodes.TableHeavy,
            OcrQualityDiagnosisCodes.FormKeyValue => OcrProfileCodes.FormKeyValue,
            OcrQualityDiagnosisCodes.EmptyText => OcrProfileCodes.HighAccuracy,
            OcrQualityDiagnosisCodes.LowConfidence => OcrProfileCodes.HighAccuracy,
            _ => null
        };

        return target == currentProfile ? null : target;
    }
}
