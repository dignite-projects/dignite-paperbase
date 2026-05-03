# Knowledge Index

The knowledge index is where Paperbase stores document chunk embeddings for retrieval. The contract is `IDocumentKnowledgeIndex` (`Dignite.Paperbase.KnowledgeIndex`) and the only built-in implementation is `Dignite.Paperbase.KnowledgeIndex.Qdrant`.

This page covers what hosts need to configure to run the knowledge index in production. To **add another vendor** (Pinecone, Milvus, etc.), see [knowledge-index-provider.md](knowledge-index-provider.md). For hybrid (dense + BM25) search behavior, see [hybrid-search.md](hybrid-search.md).

## Provider-neutral defaults

`PaperbaseKnowledgeIndex` is the abstraction layer's configuration block. Any provider — Qdrant or otherwise — reads it.

```json
"PaperbaseKnowledgeIndex": {
  "EmbeddingDimension": 1536,
  "DefaultTopK": 5,
  "MinScore": 0.65
}
```

| Key | Default | Description |
| --- | --- | --- |
| `EmbeddingDimension` | `1536` | Must match the dimension of the embedding model in [`PaperbaseAI:EmbeddingModelId`](ai-provider.md). Provider startup validates the match. |
| `DefaultTopK` | `5` | Default number of chunks returned per search call when the caller passes no per-request `topK`. Document chat conversations may override this via `DocumentSearchScope.TopK`. |
| `MinScore` | `0.65` | Normalized cosine threshold. Results with `Score < MinScore` are dropped. Bypassed automatically when the provider returns `Score = null` (e.g. RRF hybrid mode — see [hybrid-search.md](hybrid-search.md)). |

## Qdrant provider

`Dignite.Paperbase.KnowledgeIndex.Qdrant` is the default provider for the open-source host. Storage is a single Qdrant collection.

```json
"QdrantKnowledgeIndex": {
  "Endpoint": "http://localhost:6334",
  "ApiKey": "",
  "CollectionName": "paperbase_document_chunks",
  "Distance": "Cosine",
  "VectorDimension": 1536,
  "EnsureCollectionOnStartup": true,
  "EnableHybridSearch": false
}
```

| Key | Default | Description |
| --- | --- | --- |
| `Endpoint` | `http://localhost:6334` | Qdrant gRPC endpoint |
| `ApiKey` | empty | Optional Qdrant API key |
| `CollectionName` | `paperbase_document_chunks` | Collection storing document chunk points. Use a fresh name when changing embedding dimension. |
| `Distance` | `Cosine` | Distance metric; this provider phase supports `Cosine` only |
| `VectorDimension` | `1536` | Must equal `PaperbaseKnowledgeIndex:EmbeddingDimension` (validated at startup) |
| `EnsureCollectionOnStartup` | `true` | Creates or validates the collection and payload indexes on startup |
| `EnableHybridSearch` | `false` | Combine dense recall with BM25 keyword recall via RRF fusion. Requires Qdrant ≥ 1.10. See [hybrid-search.md](hybrid-search.md). |

## Payload index schema

`EnsureCollectionOnStartup` creates these payload indexes on the Qdrant side. Knowing the schema is useful when querying Qdrant directly (e.g. for debugging) or when designing migrations.

| Payload field | Encoding | Index type |
|---|---|---|
| `tenant_id` | `Guid.ToString("D")` for tenants, literal `__host__` for host-level data | keyword string with tenant index |
| `document_id` | `Guid.ToString("D")` | keyword string |
| `document_type_code` | document type code string | keyword string |
| `chunk_index` | integer | integer |
| `text` | chunk text | full-text (tokenized) |

Search and delete filters always include `tenant_id`, so host-level documents — which have no tenant — use the literal string `__host__`. This keeps cross-tenant isolation independent of ABP's ambient `DataFilter`; the filter is explicit on every Qdrant call.

## Operational notes

- **Qdrant has no EF Core migration history.** Schema evolution happens at startup through `EnsureCollectionAsync` in `QdrantKnowledgeIndexModule`.
- **Collection delete is destructive.** Switching the embedding model dimension means dropping or replacing the collection; plan for downtime or run two collections in parallel until cutover. Step-by-step: [embedding.md → Switching the embedding model](embedding.md#switching-the-embedding-model).
- **Document delete is after-commit.** When a document is deleted, the relational transaction commits first, then the Application-layer delete-event handler calls `IDocumentKnowledgeIndex.DeleteByDocumentIdAsync` filtered on `(tenant_id, document_id)`. This avoids deleting Qdrant points for a relational transaction that later rolls back.

## See also

- [Embedding pipeline](embedding.md) — what writes into the knowledge index
- [Document chat](document-chat.md) — primary reader of the knowledge index
- [Hybrid search](hybrid-search.md) — BM25 + dense RRF on the Qdrant provider
- [Knowledge-index provider authoring](knowledge-index-provider.md) — how to add a non-Qdrant backend
