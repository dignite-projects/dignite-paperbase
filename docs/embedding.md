# Embedding Pipeline

After a document is classified, Paperbase splits its Markdown into chunks, vectorizes each chunk, and writes the vectors to the [vector store](vectors.md). This is what makes the document retrievable from [document chat](chat.md) and from any other RAG-style query.

This page is the *what and why*. For chunker source, see `core/src/Dignite.Paperbase.Application/Documents/Pipelines/Embedding/TextChunker.cs`.

## Pipeline overview

```
DocumentClassifiedEto ──► DocumentEmbeddingBackgroundJob ──► DocumentEmbeddingWorkflow
                                                              │
                                                              ├─► TextChunker.Chunk(Markdown)
                                                              │      └─ Markdown-aware: AST blocks +
                                                              │         heading-path prefix per chunk
                                                              │
                                                              ├─► IEmbeddingGenerator.GenerateAsync(chunks)
                                                              │
                                                              └─► VectorStoreCollection<Guid, DocumentChunkRecord>
                                                                     UpsertAsync(records) +
                                                                     GetAsync(filter) → DeleteAsync(stragglers)
                                                                     (whole-document replace; deterministic ids)
```

Two design properties matter:

- **Markdown-aware chunking.** `TextChunker` parses the Markdown AST, splits on top-level blocks, and prefixes every chunk with the heading path it came from (e.g. `> # Contract > ## Article 5 > ### Payment Terms`). The heading prefix becomes part of the dense embedding — so a query like "payment terms" matches the heading even if the body uses the word "remittance". (The sparse BM25 path is gone; the vector store is dense-only — see [vectors.md → No hybrid search](vectors.md#no-hybrid-search).)
- **Whole-document replace.** Re-embedding a document upserts the new chunk set with deterministic point ids (overwriting prior versions) and then deletes any stragglers under `(tenant_id, document_id)` whose key is no longer in the new set. Re-running the job for any reason — Markdown updated, embedding model swapped, manual retry — converges to the latest content with no orphan chunks.

## Configuration

```json
"PaperbaseAIBehavior": {
  "ChunkSize": 800,
  "ChunkOverlap": 100,
  "ChunkBoundaryTolerance": 120
}
```

| Key | Default | Description |
| --- | --- | --- |
| `ChunkSize` | `800` | Target characters per chunk. Roughly 400 Japanese / Chinese characters or 100–150 English words. Larger chunks pack more context per embedding but dilute relevance scores; smaller chunks rank crisper but lose surrounding cues. |
| `ChunkOverlap` | `100` | Characters carried from the previous chunk to preserve continuity across boundaries. Prevents queries that straddle a split from missing both sides. |
| `ChunkBoundaryTolerance` | `120` | Backtrack window in characters. Within `[ChunkSize − tolerance, ChunkSize)` the chunker snaps to the nearest natural break (paragraph break > strong sentence end > weak punctuation > hard split). Set to `0` for fixed-length chunking; recommended ≈ 15 % of `ChunkSize`. |

The chunker only falls back to character-level splitting when a single Markdown block (e.g. a giant code fence or a paragraph with no internal punctuation) exceeds `ChunkSize`. Normal documents split cleanly at block boundaries.

## Switching the embedding model

Embedding dimension is part of the storage schema, so changing models touches three configuration surfaces and requires re-embedding existing documents. Walk through the steps in order:

1. Update `PaperbaseAI:EmbeddingModelId` in [`ai-provider.md`](ai-provider.md) (e.g. `text-embedding-3-small` → `text-embedding-3-large`).
2. Update `PaperbaseVectorStore:EmbeddingDimension` to the new model's dimension (e.g. `1536` → `3072`).
3. Either pick a fresh `PaperbaseVectorStore:CollectionName` or delete the existing Qdrant collection — collections are dimension-locked.
4. Re-run the embedding job for every document (e.g. via the host's pipeline-rerun mechanism). Until a document is re-embedded, it is invisible to chat retrieval.

There is no "rolling re-embed" mode. Plan downtime around step 4–5 or run two collections in parallel and cut over once the second is fully populated.

## See also

- [AI provider](ai-provider.md) — where the embedding model id is configured
- [Vector store](vectors.md) — where the vectors are stored, plus payload-index schema
- [Pipeline runs](pipeline-runs.md) — embedding-job state and history
