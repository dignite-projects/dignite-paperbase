namespace Dignite.Paperbase.Rag.Pgvector;

public static class PgvectorRagErrorCodes
{
    public const string DocumentChunkDocumentIdRequired = "Paperbase:DocumentChunkDocumentIdRequired";
    public const string DocumentChunkTenantImmutable = "Paperbase:DocumentChunkTenantImmutable";
    public const string DocumentChunkDocumentImmutable = "Paperbase:DocumentChunkDocumentImmutable";
    public const string DocumentChunkIndexOutOfRange = "Paperbase:DocumentChunkIndexOutOfRange";
    public const string EmbeddingDimensionMismatch = "Paperbase:EmbeddingDimensionMismatch";
}
