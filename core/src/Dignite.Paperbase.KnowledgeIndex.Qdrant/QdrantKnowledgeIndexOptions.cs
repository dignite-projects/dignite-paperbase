namespace Dignite.Paperbase.KnowledgeIndex.Qdrant;

public class QdrantKnowledgeIndexOptions
{
    public string Endpoint { get; set; } = "http://localhost:6334";

    public string? ApiKey { get; set; }

    public string CollectionName { get; set; } = "paperbase_document_chunks";

    public string Distance { get; set; } = "Cosine";

    public int VectorDimension { get; set; } = 1536;

    public bool EnsureCollectionOnStartup { get; set; } = true;

    public bool EnableHybridSearch { get; set; } = false;
}
