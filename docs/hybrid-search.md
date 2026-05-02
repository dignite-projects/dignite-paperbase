# Hybrid Search

Paperbase supports **hybrid search** for Qdrant: dense-vector recall and full-text keyword recall are run in parallel and merged with Reciprocal Rank Fusion (RRF), improving precision when the query contains specific terms that a semantic embedding might dilute.

Hybrid search is opt-in and disabled by default. Pure dense-vector search remains the default.

> See also: [`configuration.md`](configuration.md#qdrant-knowledge-index-provider) for the full Qdrant configuration surface and [`knowledge-index-provider.md`](knowledge-index-provider.md) for how non-Qdrant providers fit into this picture.

## How It Works

When hybrid search is enabled, each call to `IDocumentKnowledgeIndex.SearchAsync` that carries a non-null `VectorSearchRequest.QueryText` executes two prefetch passes in a single Qdrant request:

| Prefetch | Query | Vector | Filter | Limit |
|---|---|---|---|---|
| Dense recall | Query embedding vector | Unnamed dense (cosine) | Base filter (tenant / document / type) | `TopK × 3` |
| Sparse BM25 recall | TF sparse vector (FNV-1a hashed terms) | `bm25` (IDF-normalized by Qdrant) | Base filter | `TopK × 3` |

Both candidate lists are fused with **Qdrant RRF** (Reciprocal Rank Fusion). The outer query applies `scoreThreshold` and `limit = TopK` to the merged results.

### Sparse BM25 encoding

`SparseBm25Encoder` converts a query string to a sparse vector without a shared vocabulary table:

1. Tokenize with `[^\w]+` split, lowercased, minimum length 2.
2. Compute per-token TF (term frequency / total tokens).
3. Map each token to a `uint` via FNV-1a hash — deterministic across .NET versions and consistent between indexing and query time.
4. Qdrant applies IDF normalization server-side via `Modifier.Idf` on the `bm25` sparse vector field.

The same encoder runs at **index time** (inside `DocumentEmbeddingBackgroundJob`) and at **query time** whenever application code passes `VectorSearchRequest.QueryText`, so term→index mapping is always consistent.

`VectorSearchRequest.QueryText` is a caller-controlled field. The current explicit QA path sets it when building the search request. The MAF document conversation path should set it through `DocumentTextSearchAdapter`, which passes the raw agent search query from `TextSearchProvider` to Paperbase RAG.

## Prerequisites

- **Qdrant ≥ 1.10** — RRF fusion via the Query API was introduced in Qdrant 1.10. Sparse vector support (`SparseVectorParams`, `Modifier.Idf`) requires Qdrant ≥ 1.7.
- **`EnsureCollectionOnStartup: true`** — `QdrantClientGateway.EnsureCollectionAsync` adds a `bm25` sparse vector configuration to the collection at startup (creating the collection with it if new, or calling `UpdateCollection` if the collection already exists). Points indexed before enabling hybrid search will not have a `bm25` vector and will not contribute to sparse recall until re-indexed.

## Enabling Hybrid Search

Set the following key in `appsettings.json` (or environment variable):

```json
"QdrantKnowledgeIndex": {
  "EnableHybridSearch": true
}
```

No code changes are needed. The `QdrantDocumentKnowledgeIndex` picks up the option at startup.

### Complete Qdrant section after enabling

```json
"QdrantKnowledgeIndex": {
  "Endpoint": "http://localhost:6334",
  "ApiKey": "",
  "CollectionName": "paperbase_document_chunks",
  "Distance": "Cosine",
  "VectorDimension": 1536,
  "EnsureCollectionOnStartup": true,
  "EnableHybridSearch": true
}
```

## Score Semantics Under RRF

RRF scores are not comparable to cosine-similarity scores — they rank candidates relative to each other in a small positive range (typically `0.01–0.065`), not normalized relevance probabilities.

The Qdrant provider hides this from callers: on the hybrid path it returns `VectorSearchResult.Score = null`, signalling that no normalized score is available. `MinScore` thresholds (cosine-scale) are skipped automatically and ranking is preserved through `TopK`. **No configuration change is required when toggling `EnableHybridSearch`.**

## Disabling Hybrid Search on a Per-Request Basis

Set `VectorSearchRequest.QueryText = null` before calling `IDocumentKnowledgeIndex.SearchAsync` to force dense-only search regardless of the `EnableHybridSearch` flag. This is useful for similarity lookups where a free-form text query is not available.

## Fallback for Non-Qdrant Providers

`VectorSearchRequest.QueryText` is a provider-neutral field defined in `Dignite.Paperbase.KnowledgeIndex`. Providers that do not support hybrid search ignore it and perform pure dense-vector search. No configuration change is needed on providers that do not implement `QueryHybridAsync`. See [`knowledge-index-provider.md`](knowledge-index-provider.md) for how to add a new provider.
