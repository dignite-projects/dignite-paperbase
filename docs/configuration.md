# Configuration Guide

This guide covers the runtime configuration used by `Dignite.Paperbase.Host`.

## AI Provider

Paperbase uses `Microsoft.Extensions.AI` for chat and embeddings. Any OpenAI-compatible endpoint can be used by changing configuration only.

```json
"PaperbaseAI": {
  "Endpoint": "https://api.openai.com/v1",
  "ApiKey": "YOUR_API_KEY",
  "ChatModelId": "gpt-4o-mini",
  "EmbeddingModelId": "text-embedding-3-small"
}
```

| Key | Description |
| --- | --- |
| `Endpoint` | API base URL, usually OpenAI-compatible `/v1` format |
| `ApiKey` | API key for the provider |
| `ChatModelId` | Model used for classification, relation inference, and document chat |
| `EmbeddingModelId` | Model used for document chunk embeddings |

## AI Behavior

`PaperbaseAIOptions` controls orchestration behavior, not the vector database.

```json
"PaperbaseAI": {
  "MaxDocumentTypesInClassificationPrompt": 50,
  "ChunkSize": 800,
  "ChunkOverlap": 100,
  "ChunkBoundaryTolerance": 120,
  "MaxTextLengthPerExtraction": 8000,
  "DefaultLanguage": "ja",
  "EnableLlmRerank": false,
  "RecallExpandFactor": 4
}
```

| Key | Default | Description |
| --- | --- | --- |
| `ChunkSize` | `800` | Characters per text chunk for embedding |
| `ChunkOverlap` | `100` | Overlap between adjacent chunks |
| `ChunkBoundaryTolerance` | `120` | Backtrack tolerance to snap chunk boundaries to natural breaks |
| `DefaultLanguage` | `"ja"` | Language hint appended to AI prompts |
| `EnableLlmRerank` | `false` | When enabled, document chat retrieves an expanded candidate set, asks the chat model to rerank the chunks, and injects only the final TopK into the prompt |
| `RecallExpandFactor` | `4` | Multiplier applied to `PaperbaseRag:DefaultTopK` or per-chat `DocumentSearchScope.TopK` before LLM rerank |

## RAG Defaults

`Dignite.Paperbase.Rag` is provider-neutral. The open-source host wires it to Qdrant through `Dignite.Paperbase.Rag.Qdrant`.

```json
"PaperbaseRag": {
  "EmbeddingDimension": 1536,
  "DefaultTopK": 5,
  "MinScore": 0.65
}
```

`MinScore` is a normalized cosine threshold and only applies when the provider returns normalized scores. The Qdrant provider returns `null` on the hybrid (RRF) path so this threshold is bypassed automatically — no override needed when toggling `EnableHybridSearch`.

## Qdrant RAG Provider

```json
"QdrantRag": {
  "Endpoint": "http://localhost:6334",
  "ApiKey": "",
  "CollectionName": "paperbase_document_chunks",
  "Distance": "Cosine",
  "VectorDimension": 1536,
  "EnsureCollectionOnStartup": true
}
```

| Key | Default | Description |
| --- | --- | --- |
| `Endpoint` | `http://localhost:6334` | Qdrant gRPC endpoint |
| `ApiKey` | empty | Optional Qdrant API key |
| `CollectionName` | `paperbase_document_chunks` | Collection storing document chunk points |
| `Distance` | `Cosine` | Distance metric. The first Qdrant provider phase supports `Cosine` only |
| `VectorDimension` | `1536` | Must equal `PaperbaseRag:EmbeddingDimension` |
| `EnsureCollectionOnStartup` | `true` | Creates or validates the collection and payload indexes on startup |
| `EnableHybridSearch` | `false` | When `true`, combines dense-vector recall with full-text keyword recall using RRF fusion. Requires Qdrant ≥ 1.10. See [hybrid-search.md](hybrid-search.md) for score-threshold caveats. |

Startup ensure creates these payload indexes:

| Payload | Encoding | Index |
| --- | --- | --- |
| `tenant_id` | `Guid.ToString("D")` for tenants, `__host__` for host data | keyword string, tenant index |
| `document_id` | `Guid.ToString("D")` | keyword string |
| `document_type_code` | document type code string | keyword string |
| `chunk_index` | integer | integer |
| `text` | chunk text | full-text (tokenized) |

Search and delete filters always include `tenant_id`, so host-level documents use the string value `__host__`.

## Switching Embedding Models

When switching to a model with a different embedding dimension:

1. Update `PaperbaseAI:EmbeddingModelId`.
2. Update `PaperbaseRag:EmbeddingDimension`.
3. Update `QdrantRag:VectorDimension`.
4. Use a fresh Qdrant collection name or delete/recreate the existing collection.
5. Re-run document embedding jobs so all points are written with the new dimension.

`QdrantRagModule` validates that `PaperbaseRag:EmbeddingDimension` and `QdrantRag:VectorDimension` match at startup.

## Connection Strings

Only the relational application database is configured through connection strings.

```json
"ConnectionStrings": {
  "Default": "Host=127.0.0.1;Port=5432;Database=paperbase;Username=postgres;Password=postgres",
  "Paperbase": "Host=127.0.0.1;Port=5432;Database=paperbase;Username=postgres;Password=postgres"
}
```

Qdrant is configured by `QdrantRag`, not by an EF Core connection string. See `host/src/appsettings.Qdrant.Sample.json` for a deployment-oriented sample.

## OCR

Azure Document Intelligence is configured separately:

```json
"AzureDocumentIntelligence": {
  "Endpoint": "https://<your-resource>.cognitiveservices.azure.com/",
  "ApiKey": "YOUR_KEY",
  "ModelId": "prebuilt-read"
}
```

The module binds this section automatically.

## Authentication

```json
"AuthServer": {
  "Authority": "https://localhost:44348",
  "SwaggerClientId": "Paperbase_Swagger",
  "CertificatePassPhrase": ""
}
```

In production, set `CertificatePassPhrase` and place `openiddict.pfx` in the host working directory.
