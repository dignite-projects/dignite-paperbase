# Hybrid Search Benchmark Archive

This document is retained as a historical note for the pre-#51 hybrid-search experiments.

The current open-source provider is `Dignite.Paperbase.Rag.Qdrant`, and its first phase intentionally reports:

```text
SupportsKeywordSearch = false
SupportsHybridSearch = false
SupportsSearchSimilarDocuments = false
```

The application layer may accept `Keyword` or `Hybrid` configuration, but it must resolve those modes against provider capabilities and fall back to `Vector` when possible.

The remaining synthetic benchmark tests exercise provider-neutral `RrfFusion` math through an in-memory hybrid test store. They are not an acceptance gate for the Qdrant first phase and do not imply that the Qdrant provider supports hybrid search today.

When Qdrant keyword or hybrid support is implemented later, this benchmark should be replaced with provider-specific validation that covers:

- vector-only fallback behavior,
- exact identifier recall,
- semantic recall non-regression,
- score normalization,
- tenant/document/type filters.
