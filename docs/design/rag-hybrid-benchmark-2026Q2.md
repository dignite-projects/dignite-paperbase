# Hybrid Search Benchmark — 2026 Q2

**Status**: Synthetic baseline ✅ · Production validation ✅ ([#31](https://github.com/dignite-projects/dignite-paperbase/issues/31) closed 2026-04-28).

**Reference**: [`rag-vector-store-decoupling.md`](rag-vector-store-decoupling.md) § Slice 7. Implementation in [#28](https://github.com/dignite-projects/dignite-paperbase/issues/28).

## Why this report exists

Slice 7 ([#28](https://github.com/dignite-projects/dignite-paperbase/issues/28)) shipped hybrid search via `pgvector + tsvector + RRF`, with the design claim that hybrid retrieval should outperform pure vector search on **precise-text queries** (contract numbers, invoice IDs, names, dates, amounts) without regressing **semantic queries** (clause semantics, paraphrases). #30 established the synthetic baseline; [#31](https://github.com/dignite-projects/dignite-paperbase/issues/31) tracks the remaining production validation with real infrastructure and desensitized data.

This report establishes a **synthetic baseline** that can run in CI without external services. It validates the RRF math + mode dispatch in `Dignite.Paperbase.Rag.RrfFusion` and `PgvectorDocumentVectorStore`. It does **not** replace a real-data benchmark; that requires a脱敏 corpus + real LLM + Postgres and is explicitly out of scope here.

## Methodology

### Harness
- xUnit fact: `Dignite.Paperbase.Documents.Benchmarks.HybridSearchBenchmark.Vector_vs_Hybrid_On_Synthetic_Corpus`
- Re-run: `dotnet test --filter HybridSearchBenchmark`
- The same `RrfFusion` code that ships in production is used unchanged.
- The `IDocumentVectorStore` implementation under test is `InMemoryHybridDocumentVectorStore` (synthetic), not `PgvectorDocumentVectorStore`. See [Caveats](#caveats).

### Synthetic corpus (30 chunks)

| Bucket | Count | Surface design |
|--------|-------|----------------|
| Contracts | 12 | 10 "ABC-NNN" template chunks + 2 amount-bearing chunks. Templates share most Japanese vocabulary; the contract id is the discriminator. |
| Invoices | 10 | "INV-2026-04-NNN" template, similar shape. |
| Certificates | 5 | Name + employee/student/cert id. |
| Generic prose | 5 | Privacy / support / warranty / shipping / returns boilerplate. |

Files: [`BenchmarkDataset.cs`](../../core/test/Dignite.Paperbase.Application.Tests/Benchmarks/BenchmarkDataset.cs).

### Synthetic query set (30)

| Category | Count | Pattern |
|----------|-------|---------|
| Precise-text | 15 | Rare-token-led: contract id, invoice id, person name, employee number, certificate id, date. Each query has exactly one expected chunk. |
| Semantic | 15 | Topical paraphrases: "甲乙双方の責任範囲", "支払条件はどうなっていますか", "プライバシー保護について". Multi-chunk expected sets where any of N is a valid hit. |

### Scoring model — explicit assumptions

The synthetic store models the production failure mode rather than reproducing the exact embedding / Postgres math:

- **Vector path**: cosine similarity over **character-bigram bag-of-words**. This captures broad surface overlap (the strength of dense embeddings) and tends to under-discriminate when chunks share most of their vocabulary and differ only in a rare identifier (e.g., `ABC-001` vs `ABC-002` inside identical contract templates). That's the canonical hybrid-wins scenario.
- **Keyword path**: token-level exact match using the same `[\w\-]+` tokenization PostgreSQL's `simple` regconfig produces. Score = `matched_token_count / query_token_count`. Rare IDs win; semantic queries with broad vocabulary score more evenly across chunks.
- **Hybrid path**: each path recalls `TopK × 2` candidates, `RrfFusion.Merge` fuses the rankings (k=60), final scores min-max normalized to [0, 1].

### Metrics

- **Recall@1** — strict: did the top-1 result match an expected chunk?
- **Recall@5** — loose: did the top-5 results contain at least one expected chunk?
- **MRR** — continuous: `1/rank_of_first_hit`, 0 if no hit. Most sensitive metric for "right answer is in results, but not at top".

## Results (2026-04-28 run)

| Mode   | Category     | Queries | Recall@1 | Recall@5 | MRR   |
|--------|--------------|---------|----------|----------|-------|
| Hybrid | precise-text |      15 |    1.000 |    1.000 | 1.000 |
| Vector | precise-text |      15 |    0.933 |    1.000 | 0.950 |
| Hybrid | semantic     |      15 |    0.867 |    0.933 | 0.900 |
| Vector | semantic     |      15 |    0.867 |    0.933 | 0.900 |

### Headline

| Metric (precise-text) | Vector | Hybrid | Δ      |
|-----------------------|--------|--------|--------|
| Recall@1              | 0.933  | 1.000  | +0.067 |
| Recall@5              | 1.000  | 1.000  |  0.000 |
| MRR                   | 0.950  | 1.000  | +0.050 |

| Metric (semantic) | Vector | Hybrid | Δ      |
|-------------------|--------|--------|--------|
| Recall@1          | 0.867  | 0.867  |  0.000 |
| Recall@5          | 0.933  | 0.933  |  0.000 |
| MRR               | 0.900  | 0.900  |  0.000 |

### Reading the numbers

- **Hybrid lifts precise-text by closing the recall@1 / MRR gap to a perfect 1.000.** With pure vector retrieval, one of the 15 precise-text queries had its right chunk land at rank 2 (because the contract template surface dominated the bigram cosine, and only the rare ID could break the tie). The keyword path resolves this immediately, RRF fuses, and the hybrid run hits 15/15 at rank 1.
- **Recall@5 saturates at 1.000 for both modes.** With only 30 chunks, top-5 is generous enough that even pure vector recovers every right chunk. This is **not a property of vector search** — it's a property of the corpus being too small to differentiate at K=5. In production-scale corpora (thousands of chunks, more near-duplicates), recall@5 would also differentiate.
- **Semantic is unchanged.** RRF fusion with broad-vocabulary queries doesn't shuffle the dense ranking, because the keyword path returns similar evenly-distributed scores; RRF simply re-confirms the dense ordering.

### Acceptance gates (all pass)

| Gate | Threshold | Observed | Status |
|------|-----------|----------|--------|
| Precise-text MRR lift | ≥ 0.03 | 0.050 | ✅ |
| Precise-text Recall@1 lift | ≥ 0.03 | 0.067 | ✅ |
| Semantic MRR regression | ≤ 0.03 | 0.000 | ✅ |
| Semantic Recall@5 regression | ≤ 0.03 | 0.000 | ✅ |
| Score ∈ [0, 1] across all modes | invariant | held | ✅ |

## Caveats

The big one first:

**This is a synthetic harness.** The "vector" path is bigram cosine, not OpenAI text-embedding-3-small. The "keyword" path is token-overlap fraction, not PostgreSQL `ts_rank_cd` with IDF. The corpus is 30 hand-built chunks, not脱敏 production documents.

What the synthetic harness **does** validate:
- The `RrfFusion` math fuses two ranked lists into a list with the expected relative ordering when one list strongly prefers a document the other only moderately prefers.
- The mode dispatch in `IDocumentVectorStore.SearchAsync` and `DocumentQaAppService` correctly routes queries to vector / keyword / hybrid paths.
- The Score ∈ [0, 1] contract holds across all modes.
- The synthetic store implementation honors filter scoping.

What the synthetic harness **does not** validate:
- Real OpenAI embedding behavior on Japanese contract text. Embeddings can have surprising failure modes that bigram cosine doesn't model — e.g., normalization quirks, dimensional collapse on very short queries, sensitivity to punctuation.
- Real PostgreSQL `to_tsvector('simple', ...)` tokenization on mixed JP/EN. The `simple` regconfig has no Japanese tokenizer; long Japanese phrases become single tokens at the column level. Token-level overlap in this benchmark is more aggressive than what `ts_rank_cd` does in practice.
- Recall@5 differentiation at production scale (thousands of chunks). With small N, top-5 is too generous; the true Slice 7 win lives there.
- Latency, throughput, index hit rate. These are operational concerns the harness intentionally ignores.

## Recommended follow-up — production validation

To close out the Slice 7 design claim properly, run the same set of metrics against:

1. **A脱敏 production corpus.** ~50 contracts + ~50 invoices + ~30 certificates is a sensible first cut. Strip PII per existing data-handling policy.
2. **Real `IEmbeddingGenerator`.** Whatever the deployment uses (Azure OpenAI / OpenAI / Ollama). 1536-dim or 3072-dim — match the index.
3. **Real Postgres + pgvector + tsvector.** A docker dev environment is sufficient if production data is too sensitive for laptop runs.
4. **Same scoring code path.** `PgvectorDocumentVectorStore.SearchAsync` with `Mode = Hybrid` vs `Mode = Vector`, no harness in the middle.
5. **Same query categories.** Precise-text (rare IDs, names) and semantic (paraphrases, multi-condition).

The thresholds the synthetic harness proved out (precise-text MRR lift ≥ 0.03, semantic regression ≤ 0.03) are the **floor** for production validation, not the target. Production should comfortably exceed them.

If the production benchmark fails to clear those floors, the place to look first is **regconfig symmetry** between the migration column (`to_tsvector('simple', "ChunkText")`) and the query (`plainto_tsquery('simple', ...)`) — a mismatch silently disables the GIN index and degrades hybrid to vector + sequential scan. The `PgvectorDocumentVectorStore` source includes a comment asserting this.

## Reproducing the benchmark

```bash
dotnet test core/ --filter HybridSearchBenchmark
```

The test prints the markdown table to stdout and writes it to `bin/Debug/.../hybrid-benchmark-results.md`. Re-running produces the same numbers — the dataset and the in-memory scoring are fully deterministic, so any drift is a regression in `RrfFusion` or the dispatch code.

## See also

- [`rag-vector-store-decoupling.md`](rag-vector-store-decoupling.md) — full RAG decoupling design (Slices 2–8)
- [#28](https://github.com/dignite-projects/dignite-paperbase/issues/28) — Slice 7 implementation
- [#30](https://github.com/dignite-projects/dignite-paperbase/issues/30) — synthetic benchmark baseline
- [#31](https://github.com/dignite-projects/dignite-paperbase/issues/31) — production validation with real infrastructure and desensitized data
- [`HybridSearchBenchmark.cs`](../../core/test/Dignite.Paperbase.Application.Tests/Benchmarks/HybridSearchBenchmark.cs)
- [`InMemoryHybridDocumentVectorStore.cs`](../../core/test/Dignite.Paperbase.Application.Tests/Benchmarks/InMemoryHybridDocumentVectorStore.cs)
- [`BenchmarkDataset.cs`](../../core/test/Dignite.Paperbase.Application.Tests/Benchmarks/BenchmarkDataset.cs)
- [`ProductionHybridSearchBenchmark.cs`](../../core/test/Dignite.Paperbase.Application.Tests/Benchmarks/ProductionHybridSearchBenchmark.cs) — production runner ([#31](https://github.com/dignite-projects/dignite-paperbase/issues/31))
- [`docs/benchmarks/README.md`](../benchmarks/README.md) — dataset preparation guide




## Production Validation Results

*Run date: 2026-04-28 UTC · Infrastructure: PostgreSQL 16 + pgvector 0.7 (local dev) · Embedding model: `Xenova/paraphrase-multilingual-MiniLM-L12-v2` 384-dim → zero-padded to 1536*

| Mode   | Category     | Queries | Recall@1 | Recall@5 | MRR    |
|--------|--------------|---------|----------|----------|--------|
| Hybrid | precise-text |      15 |    0.933 |    1.000 |  0.967 |
| Vector | precise-text |      15 |    0.267 |    0.667 |  0.404 |
| Hybrid | semantic     |      15 |    0.800 |    0.933 |  0.856 |
| Vector | semantic     |      15 |    0.800 |    0.933 |  0.856 |

### Acceptance gates (all pass)

| Gate | Threshold | Observed | Status |
|------|-----------|----------|--------|
| Precise-text MRR lift | ≥ 0.03 | +0.563 | ✅ |
| Precise-text Recall@1 lift | ≥ 0.03 | +0.667 | ✅ |
| Semantic MRR regression | ≤ 0.03 | 0.000 | ✅ |
| Semantic Recall@5 regression | ≤ 0.03 | 0.000 | ✅ |

### Reading the numbers

**Hybrid increases precise-text Recall@1 from 0.267 → 0.933 (+0.667) and MRR from 0.404 → 0.967 (+0.563).** For ID-only queries ("CNT-2025-0001", "INV-2025-04-001", "EMP-10043"), pure vector search is highly confused: the 384-dim multilingual embeddings of an opaque identifier map to vectors that are semantically ambiguous across all contract/invoice/certificate chunks that share the same document template. The keyword path resolves this immediately — `plainto_tsquery('simple', 'CNT-2025-0001')` produces `'cnt' & '-2025' & '-0001'` and matches only the chunks that contain that exact identifier with surrounding whitespace. RRF fusion then surfaces those chunks to rank 1.

**Semantic is identical across modes (Recall@1=0.800, MRR=0.856).** Broad-vocabulary paraphrase queries ("甲乙双方の責任範囲", "プライバシー保護の方針") produce keyword `@@` matches across many chunks equally, so the keyword ranking adds no signal; RRF leaves the dense ordering undisturbed.

**Recall@5 = 1.000 for hybrid precise-text** — every expected chunk appears in the top 5 with hybrid search, versus 0.667 for pure vector. In a production corpus with thousands of similar contract templates, the vector-only gap would widen further.

### Tokenization note

The `simple` regconfig has no Japanese tokenizer. Mixed JP+ASCII text is tokenized by PostgreSQL's default text parser, which breaks on spaces and punctuation. For an ASCII identifier like `CNT-2025-0001` to be independently tokenizable (and thus matchable by `plainto_tsquery`), it must be preceded and followed by a space or punctuation in the stored chunk text — e.g. `契約番号 CNT-2025-0001 。` rather than `契約番号CNT-2025-0001。`. The gold dataset enforces this with a space before every ASCII identifier. Production pipelines should apply the same whitespace normalization when chunking.
