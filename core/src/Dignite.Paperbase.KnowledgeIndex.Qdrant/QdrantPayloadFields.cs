namespace Dignite.Paperbase.KnowledgeIndex.Qdrant;

public static class QdrantPayloadFields
{
    public const string TenantId = "tenant_id";
    public const string DocumentId = "document_id";
    public const string DocumentTypeCode = "document_type_code";
    public const string ChunkIndex = "chunk_index";
    public const string Text = "text";
    public const string PageNumber = "page_number";

    public const string HostTenantId = "__host__";

    public const string Bm25VectorName = "bm25";
}
