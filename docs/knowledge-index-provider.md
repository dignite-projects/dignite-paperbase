# Adding a Knowledge Index Provider

`Dignite.Paperbase.KnowledgeIndex` is the provider-neutral contract for "Paperbase document knowledge as a searchable vector index". It sits below the Application layer and above any concrete vector database. Today the only built-in provider is `Dignite.Paperbase.KnowledgeIndex.Qdrant`. Use this guide when adding another vendor.

The contract carries Paperbase-specific semantics (multi-tenancy, document aggregate identity, score normalization, citation fields) — it deliberately does not implement the generic `Microsoft.Extensions.VectorData.IVectorStore` shape, because callers (Chat search, embedding background job, delete handler) need a domain-aware facade rather than a generic record store.

## Project Structure

Create a single project `Dignite.Paperbase.KnowledgeIndex.<VendorName>` under `core/src/`. Do not add `Domain`, `Domain.Shared`, or `EntityFrameworkCore` sub-projects — the provider owns only its SDK glue, collection startup, payload encoding, point id generation, upsert, search, and delete-by-document operations.

The provider project must:

- Reference `Dignite.Paperbase.KnowledgeIndex` (the contract)
- Not reference Paperbase Domain
- Configure middleware only via the host (per ABP module convention)
- Expose all public and protected members as `virtual`

## The Interface

The provider must implement `IDocumentKnowledgeIndex`:

```csharp
public interface IDocumentKnowledgeIndex
{
    Task UpsertDocumentAsync(
        DocumentVectorIndexUpdate update,
        CancellationToken cancellationToken = default);

    Task DeleteByDocumentIdAsync(
        Guid documentId,
        Guid? tenantId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        VectorSearchRequest request,
        CancellationToken cancellationToken = default);
}
```

Three operations, all aggregated by **document** rather than by individual vector record. Inputs and outputs are POCOs defined in the contract project: `DocumentVectorIndexUpdate`, `DocumentVectorRecord`, `VectorSearchRequest`, `VectorSearchResult`.

## Implementation Requirements

### Tenant isolation (mandatory)

Every search and delete must filter on `TenantId`. The contract carries it explicitly so providers do not depend on ABP ambient context — calls from Hangfire jobs, CLI tools, or any non-HTTP path remain safe.

Encode `null` tenant (host-level data) as the literal string `__host__` in your filter representation, matching the Qdrant provider so cross-provider data migration stays straightforward.

### Whole-document upsert semantics

`UpsertDocumentAsync` replaces the **complete** index state for one document:

- Calling with the same `DocumentId` a second time replaces previous chunks
- Passing an empty `Chunks` list removes all index data for the document
- After upserting current chunks, delete stale chunks for the same `(TenantId, DocumentId)` whose `ChunkIndex` is not in the new set

The Application layer relies on this so re-running embedding for a re-extracted document automatically prunes orphan chunks.

### Stable point IDs

Provider writes happen outside the relational database transaction, so retries must be idempotent. Derive each point id deterministically from `(TenantId, DocumentId, ChunkIndex)`. Retrying the same upsert must write the same point ids — never new ones.

The Qdrant provider uses a SHA1-based UUIDv5-style derivation in `QdrantPointIdGenerator`. Other providers can use any deterministic scheme that satisfies the same property.

### Score normalization

Return relevance scores as `VectorSearchResult.Score` where higher = more relevant. Two cases:

| Case | Score |
| --- | --- |
| Provider returns a normalized similarity in `[0, 1]` (e.g. cosine similarity) | Clamp to `[0, 1]` and surface it |
| Provider returns scores on a non-comparable scale (e.g. RRF fused rank in `[0, ~0.07]`) | Return `Score = null`. Callers will skip `MinScore` filtering and rely on rank order alone. |

The Application layer interprets `null` as "no normalized score available" and bypasses any cosine-scale `MinScore` threshold automatically — no caller change is needed when toggling between dense and hybrid modes.

### Citation fields

Preserve `ChunkIndex`, `PageNumber`, and `DocumentTypeCode` through the round trip. `Chat/Search/DocumentTextSearchAdapter` uses these to format `[chunk N]` / page references in agent responses. Dropping them makes citations break silently.

## Optional: Hybrid Search

`VectorSearchRequest.QueryText` is provider-neutral. When non-null, providers that support hybrid search (e.g. Qdrant native dense + BM25 sparse with RRF) may combine dense recall with keyword recall. Providers that do not support it ignore the field and perform pure dense-vector search — no caller change required.

If your provider supports hybrid mode, gate it behind a per-provider option (see `QdrantKnowledgeIndexOptions.EnableHybridSearch`) so deployments can toggle it without code changes. See [hybrid-search.md](hybrid-search.md) for the Qdrant implementation details and score-semantics caveats.

## Register with ABP

Mark your implementation as the registered `IDocumentKnowledgeIndex` and let ABP's auto-DI pick it up:

```csharp
[ExposeServices(typeof(IDocumentKnowledgeIndex))]
public class MyVendorDocumentKnowledgeIndex
    : IDocumentKnowledgeIndex, ITransientDependency
{
    // ...
}
```

Then add a module class that depends on `PaperbaseKnowledgeIndexModule` and binds your provider's options + ensures the collection on startup:

```csharp
[DependsOn(typeof(PaperbaseKnowledgeIndexModule))]
public class MyVendorKnowledgeIndexModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services
            .AddOptions<MyVendorKnowledgeIndexOptions>()
            .BindConfiguration("MyVendorKnowledgeIndex")
            .Validate(/* required field checks */)
            .ValidateOnStart();
    }

    public override void OnPostApplicationInitialization(ApplicationInitializationContext context)
    {
        // Ensure collection / schema on startup, similar to QdrantKnowledgeIndexModule
    }
}
```

Consumers (host) reference the provider module instead of the Qdrant one in their `[DependsOn(...)]`. Switching providers is a host-level swap; no Application or Domain changes.

## Verifying Your Implementation

Run `core/test/Dignite.Paperbase.KnowledgeIndex.Tests` against your provider (the existing tests target Qdrant — copy the test fixture and parametrize over your provider). At minimum verify:

| Property | What to verify |
| --- | --- |
| Tenant isolation | A query with mismatched `TenantId` returns zero results, even when chunks for the same `DocumentId` exist under another tenant |
| Whole-doc replace | Re-upserting the same document with fewer chunks removes the surplus points |
| Empty-chunks delete | Calling Upsert with `Chunks = []` removes all chunks for that document |
| Stable point ids | Re-running the same upsert produces the same set of point ids (count and content) |
| After-commit delete | The Application-layer `DocumentDeletingEventHandler` calls `DeleteByDocumentIdAsync` only after the relational transaction commits — no need to handle this in the provider |

For hybrid mode (if implemented), additionally verify that `Score = null` flows back when the provider cannot supply a normalized score, and that `MinScore` filtering is bypassed in that case.
