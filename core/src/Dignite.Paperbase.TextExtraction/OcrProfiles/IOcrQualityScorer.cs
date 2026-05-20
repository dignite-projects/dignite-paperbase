using Dignite.Paperbase.Ocr;

namespace Dignite.Paperbase.TextExtraction.OcrProfiles;

public interface IOcrQualityScorer
{
    OcrQualityAssessment Score(OcrResult result, string currentProfileCode);

    bool IsBetter(OcrQualityAssessment candidate, OcrQualityAssessment current);
}
