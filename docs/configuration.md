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
| `ChatModelId` | Model used for classification, relation inference, and QA |
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
  "QaTopKChunks": 5,
  "QaMinScore": 0.65,
  "EnableLlmRerank": false,
  "RecallExpandFactor": 4,
  "DefaultLanguage": "ja"
}
```

| Key | Default | Description |
| --- | --- | --- |
| `ChunkSize` | `800` | Characters per text chunk for embedding |
| `ChunkOverlap` | `100` | Overlap between adjacent chunks |
| `ChunkBoundaryTolerance` | `120` | Backtrack tolerance to snap chunk boundaries to natural breaks |
| `QaTopKChunks` | `5` | Final number of chunks used as QA context |
| `QaMinScore` | `0.65` | Minimum score applied only when the provider reports normalized scores |
| `EnableLlmRerank` | `false` | Expands recall and lets an LLM rerank candidates before QA |
| `RecallExpandFactor` | `4` | Recall multiplier when rerank is enabled |
| `DefaultLanguage` | `"ja"` | Language hint appended to AI prompts |

## RAG Defaults

`Dignite.Paperbase.Rag` is provider-neutral. The open-source host wires it to Qdrant through `Dignite.Paperbase.Rag.Qdrant`.

```json
"PaperbaseRag": {
  "EmbeddingDimension": 1536,
  "DefaultTopK": 5,
  "MinScore": 0.65,
  "DefaultSearchMode": "Vector"
}
```

`DefaultSearchMode` may be set to `Keyword` or `Hybrid`, but the Qdrant provider currently supports only vector search. Application services perform capability-based fallback to `Vector` when possible.

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

Startup ensure creates these payload indexes:

| Payload | Encoding | Index |
| --- | --- | --- |
| `tenant_id` | `Guid.ToString("D")` for tenants, `__host__` for host data | keyword string, tenant index |
| `document_id` | `Guid.ToString("D")` | keyword string |
| `document_type_code` | document type code string | keyword string |
| `chunk_index` | integer | integer |

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
