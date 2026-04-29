# Benchmarks

Benchmark artifacts in this folder are optional development aids. The current open-source RAG provider is Qdrant and supports vector search only in its first phase.

Provider-neutral synthetic tests may still exercise `RrfFusion` and search-mode dispatch, but they do not prove Qdrant keyword or hybrid support.

Future provider-specific benchmarks should state the provider, collection schema, embedding model, filter coverage, and score semantics explicitly.
