# Adding a Knowledge Index Provider

Paperbase separates the document knowledge index abstraction from its vector storage implementation via two projects:

- `Dignite.Paperbase.KnowledgeIndex` — provider-neutral contract (tenant isolation, document identity, type filters, score normalization, source citation)
- `Dignite.Paperbase.KnowledgeIndex.<VendorName>` — a concrete provider implementation

The built-in provider is `Dignite.Paperbase.KnowledgeIndex.Qdrant`. To add a second provider, follow the steps below.

## Project Structure

Create a single project `Dignite.Paperbase.KnowledgeIndex.<VendorName>` under `core/src/`. Do not add `Domain`, `Domain.Shared`, or `EntityFrameworkCore` sub-projects — the provider owns only its SDK glue, collection startup, payload encoding, point id generation, upsert, vector search, and delete-by-document operations.

## Boundaries

- The provider project must not reference Paperbase Domain.
- Document delete cleanup must not be handled inside the provider. It is handled by the Application-layer `DocumentDeletingEventHandler`, which depends only on `IDocumentKnowledgeIndex`.
- Middleware configuration stays in the host.
- All public and protected members must be `virtual`.

## Implement `IDocumentKnowledgeIndex`

The core interface your provider must implement:

```csharp
public interface IDocumentKnowledgeIndex
{
    DocumentKnowledgeIndexCapabilities Capabilities { get; }

    Task UpsertDocumentAsync(string tenantId, Guid documentId, string documentTypeCode,
        IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentSearchResult>> SearchAsync(string tenantId,
        DocumentSearchRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentSimilarityResult>> SearchSimilarDocumentsAsync(string tenantId,
        Guid documentId, DocumentSimilarityRequest request, CancellationToken cancellationToken = default);

    Task DeleteDocumentAsync(string tenantId, Guid documentId,
        CancellationToken cancellationToken = default);
}
```

## Declare Capabilities

Every provider declares what it supports via `DocumentKnowledgeIndexCapabilities`. Be conservative — only set a flag to `true` when the behavior is fully implemented and tested:

```csharp
public DocumentKnowledgeIndexCapabilities Capabilities => new()
{
    SupportsVectorSearch = true,
    SupportsKeywordSearch = false,
    SupportsHybridSearch = false,
    SupportsStructuredFilter = true,   // required — Paperbase uses tenant/document/type filters
    SupportsDeleteByDocumentId = true,
    SupportsSearchSimilarDocuments = false,
    NormalizesScore = true,
};
```

`SupportsStructuredFilter` must be `true`. Providers that cannot filter by `tenant_id` are rejected at runtime because Paperbase requires tenant isolation on every search and delete.

## Payload Schema

Store the following fields per chunk. Index the fields marked as filterable:

| Field | Type | Index |
|---|---|---|
| `tenant_id` | tenant `Guid` as `D`-format string; host data as `__host__` | filterable keyword |
| `document_id` | document `Guid` as `D`-format string | filterable keyword |
| `document_type_code` | string | filterable keyword |
| `chunk_index` | integer | filterable integer |
| `text` | chunk text | payload only |
| `title` | optional citation title | payload only |
| `page_number` | optional integer | payload only |

Every search and delete filter must include `tenant_id`.

## Write and Delete Semantics

Provider writes happen outside the relational database transaction, so operations must be idempotent:

- Use stable point ids derived from `tenant_id + document_id + chunk_index`. Retrying the same upsert writes the same point ids.
- After upserting the current chunks, delete stale chunks for the same tenant/document by filter.
- Passing an empty chunk list must delete all of the document's points.

Document deletion uses after-commit semantics — Qdrant points are deleted only after the relational transaction commits. The Application layer manages this via `IUnitOfWork.OnCompleted`; the provider does not need to handle it.

## Score Semantics

Return relevance scores where higher is better. If your provider returns distance scores (lower is better), invert them before returning and set `NormalizesScore = true`. Clamp scores to `[0, 1]`.

## Search Mode Fallback

The Application layer resolves the requested search mode against `Capabilities` before calling the provider. Your provider does not need to implement fallback logic:

- `Vector` requires `SupportsVectorSearch`
- `Keyword` falls back to `Vector` when `SupportsKeywordSearch` is false
- `Hybrid` falls back to `Vector` when `SupportsHybridSearch` is false

Throw `NotSupportedException` from `SearchSimilarDocumentsAsync` when `SupportsSearchSimilarDocuments` is false.

## Register in Host

```csharp
// Program.cs or AppModule.ConfigureServices
context.Services.AddSingleton<IDocumentKnowledgeIndex, MyVendorDocumentKnowledgeIndex>();
```

Remove or comment out the Qdrant registration if you are replacing it rather than adding alongside it.

## Validation Checklist

Before enabling a new provider in production, run the benchmarks under `core/test/Dignite.Paperbase.Application.Tests/Benchmarks/` with real de-identified data and verify:

- Vector search: exact identifier recall ≥ 99 %
- Semantic search: relevant result in top-3 for all semantic queries in the gold dataset
- Tenant isolation: queries with a mismatched `tenant_id` return zero results
- Stale chunk cleanup: re-upserting a document with fewer chunks removes the old surplus points
- After-commit delete: rolling back the main DB transaction leaves Qdrant points intact

If you are adding keyword or hybrid search, also verify:
- Keyword fallback does not activate when keyword search is enabled
- Score normalization — all returned scores are in `[0, 1]`
- Filters remain effective under keyword and hybrid modes
