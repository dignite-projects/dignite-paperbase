# Paperbase RAG Provider Design

Issue #51 settles the open-source RAG provider shape:

```text
core/src/
  Dignite.Paperbase.Rag/
  Dignite.Paperbase.Rag.Qdrant/
```

`Dignite.Paperbase.Rag` is the provider-neutral business contract. It defines Paperbase-specific RAG semantics such as tenant isolation, document identity, document type filters, score normalization, and source citation fields.

`Dignite.Paperbase.Rag.Qdrant` is the only open-source provider. It owns Qdrant SDK usage, collection startup checks, payload encoding, point id generation, upsert, vector search, and delete-by-document operations.

## Boundaries

- `Dignite.Paperbase.Rag` must not reference Qdrant or any other provider SDK.
- The Qdrant provider must not depend on Paperbase Domain just to observe document delete events.
- Document delete cleanup is handled in the Application layer through `DocumentDeletingEventHandler`, which depends only on `IDocumentKnowledgeIndex`.
- Middleware stays in the host, not reusable modules.
- Public and protected members in reusable modules remain virtual.

## Qdrant Provider Capabilities

First phase capabilities:

```csharp
SupportsVectorSearch = true
SupportsKeywordSearch = false
SupportsHybridSearch = false
SupportsStructuredFilter = true
SupportsDeleteByDocumentId = true
NormalizesScore = true
SupportsSearchSimilarDocuments = false
```

`SearchSimilarDocumentsAsync` throws `NotSupportedException` in this phase. Relation inference checks `SupportsSearchSimilarDocuments` and skips cleanly when the provider does not support document-level similarity.

## Search Mode Fallback

Application services must resolve requested modes against provider capabilities before calling the provider:

- `Vector` requires `SupportsVectorSearch`.
- `Keyword` may fall back to `Vector` when keyword search is unsupported and vector search is available.
- `Hybrid` may fall back to `Vector` when hybrid search is unsupported and vector search is available.
- Providers without structured filters are rejected, because Paperbase requires tenant/document/type filters to avoid leakage.

Both `DocumentQaAppService` and `DocumentTextSearchAdapter` use the same resolver.

## Payload Schema

Qdrant points use a stable payload schema:

| Field | Encoding | Index |
| --- | --- | --- |
| `tenant_id` | tenant `Guid` formatted with `D`; host data as `__host__` | keyword string, tenant index |
| `document_id` | document `Guid` formatted with `D` | keyword string |
| `document_type_code` | document type code string | keyword string |
| `chunk_index` | integer | integer |
| `text` | chunk text | payload only |
| `title` | optional citation title | payload only |
| `page_number` | optional page number | payload only |

Every search and delete filter includes `tenant_id`.

## Write And Delete Semantics

Qdrant writes are outside the relational database transaction, so provider operations are designed to be idempotent:

- `UpsertDocumentAsync` uses stable point ids derived from `tenant_id + document_id + chunk_index`.
- Retrying the same document upsert writes the same point ids.
- After upserting current chunks, stale chunks for the same tenant/document are deleted by filter.
- Passing an empty chunk list deletes the document's points.

Document deletion keeps after-commit semantics:

1. `DocumentAppService.DeleteAsync` publishes `DocumentDeletingEvent` while the main Unit of Work is still active.
2. Application-layer `DocumentDeletingEventHandler` registers `IUnitOfWork.OnCompleted`.
3. Qdrant points are deleted only after the relational transaction commits.
4. If the main transaction rolls back, Qdrant delete is not attempted.

## Score Semantics

Qdrant cosine search scores are treated as relevance scores where higher is better. The provider clamps returned scores to `[0, 1]` and reports `NormalizesScore = true`. It does not invert scores with `1 - score`.

## Non-Goals

- Do not add `Rag.Storage`.
- Do not add `Qdrant.Domain`, `Qdrant.Domain.Shared`, or `Qdrant.EntityFrameworkCore`.
- Do not add a provider-specific EF Core context for Qdrant.
- Do not reintroduce provider-specific repositories into Application code.

## Future Work

Keyword, hybrid, and document-level similarity can be added later by extending `Dignite.Paperbase.Rag.Qdrant` capabilities and tests. Until then, Application layer fallback is the compatibility path.
