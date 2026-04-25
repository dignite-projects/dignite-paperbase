namespace Dignite.Paperbase;

public static class PaperbaseErrorCodes
{
    public const string ExtractedTextIsImmutable = "Paperbase:ExtractedTextIsImmutable";
    public const string DocumentRelationDocumentIdRequired = "Paperbase:DocumentRelationDocumentIdRequired";
    public const string DocumentRelationCannotTargetSelf = "Paperbase:DocumentRelationCannotTargetSelf";
    public const string DocumentRelationConfidenceOutOfRange = "Paperbase:DocumentRelationConfidenceOutOfRange";
    public const string DocumentChunkDocumentIdRequired = "Paperbase:DocumentChunkDocumentIdRequired";
    public const string DocumentChunkIndexOutOfRange = "Paperbase:DocumentChunkIndexOutOfRange";
    public const string EmbeddingDimensionMismatch = "Paperbase:EmbeddingDimensionMismatch";
    public const string InvalidDocumentTypeCode = "Paperbase:InvalidDocumentTypeCode";
}
