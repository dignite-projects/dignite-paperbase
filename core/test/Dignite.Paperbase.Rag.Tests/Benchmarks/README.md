# Benchmarks

This directory contains two independent tiers of RAG retrieval benchmarks.

## Tier 1 — CI synthetic harness (no infrastructure required)

**Files**: `HybridSearchBenchmark.cs`, `InMemoryHybridDocumentVectorStore.cs`,
`BenchmarkDataset.cs`, `RetrievalMetrics.cs`

Runs as a standard xUnit Fact with no external services:

```
dotnet test --filter HybridSearchBenchmark
```

What it validates:
- `RrfFusion.Merge()` math produces the expected ranking lift on precise-text
  queries (rare IDs, contract numbers) without regressing semantic queries.
- Every `VectorSearchResult.Score` stays in `[0, 1]` across Vector and Hybrid modes.

What it deliberately does NOT validate:
- Real embedding model behavior (uses bigram cosine, not dense vectors).
- Real Qdrant or any other provider behavior.
- Recall saturation at production corpus scale.

The current open-source RAG provider is Qdrant and supports vector search only
in its first phase; this harness therefore tests provider-neutral C# algebra only.

## Tier 2 — Production gold-dataset benchmark (Issue #30, not yet implemented)

**Files**: `generate-dataset.mjs`, `rag-gold-dataset-sample.json`,
`ProductionBenchmarkDataset.cs`, `ProductionBenchmarkDatasetTests.cs`

Infrastructure scaffolding for a future Qdrant-backed benchmark against a real
desensitized corpus with real embeddings. The actual test class
(`ProductionHybridSearchBenchmark`) does not yet exist — see
[Issue #30](https://github.com/dignite-projects/dignite-paperbase/issues/30).

### Generating the gold dataset

Run once from the repo root (requires Node.js ≥ 18):

```
node core/test/Dignite.Paperbase.Rag.Tests/Benchmarks/generate-dataset.mjs
```

This writes `rag-gold-dataset.json` using
[paraphrase-multilingual-MiniLM-L12-v2](https://huggingface.co/sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2)
(dimension 384, zero-padded to `PaperbaseRagOptions.EmbeddingDimension = 1536`).
The output file is **not committed** (`rag-gold-dataset.json` is gitignored).
See `rag-gold-dataset-sample.json` for the expected schema.

### Future provider-specific benchmarks

Any benchmark targeting a specific provider must document:
- Provider name and collection schema
- Embedding model name and dimension
- Filter fields exercised
- Score semantics (cosine similarity, dot product, etc.)
