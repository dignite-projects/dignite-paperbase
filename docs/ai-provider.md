# AI Provider

Paperbase delegates all chat-completion and embedding calls to `Microsoft.Extensions.AI`. AI configuration is split into two disjoint sections:

| Section | Owns | Consumed by |
| --- | --- | --- |
| `PaperbaseAI` | Provider wiring (endpoint, credentials, model ids, prompt-cache middleware switch) | Host only — `PaperbaseHostModule.ConfigureAI` reads it once at startup to register `IChatClient` and `IEmbeddingGenerator<string, Embedding<float>>` |
| `PaperbaseAIBehavior` | Workflow / Chat behavior knobs (prompt language, truncation, chunking, rerank, tool-call cap, …) | Application layer via `IOptions<PaperbaseAIBehaviorOptions>` — `PaperbaseApplicationModule.ConfigureServices` binds the section to the type |

The split keeps credentials (`ApiKey`) out of any `IOptions<>` flowing into business code and lets operators tune behavior independently of provider switches. Every downstream feature — [classification](classification.md), [embedding](embedding.md), [document chat](document-chat.md), business-module field extraction — shares the same `IChatClient` registration regardless of behavior tuning.

## Provider wiring (`PaperbaseAI`)

```json
"PaperbaseAI": {
  "Endpoint": "https://api.openai.com/v1",
  "ApiKey": "YOUR_API_KEY",
  "ChatModelId": "gpt-4o-mini",
  "EmbeddingModelId": "text-embedding-3-small",
  "PromptCachingEnabled": true
}
```

| Key | Description |
| --- | --- |
| `Endpoint` | API base URL. Any OpenAI-compatible `/v1` endpoint works (Azure OpenAI, Ollama, OpenRouter, vLLM, etc.) |
| `ApiKey` | API key for the provider |
| `ChatModelId` | Model used for classification, document chat answers, optional rerank, and any business-module field extractor |
| `EmbeddingModelId` | Model used to vectorize document chunks |
| `PromptCachingEnabled` | Wraps the chat client with `UseDistributedCache()` so repeated calls with identical inputs reuse the cached response. Uses the host's registered `IDistributedCache` (in-memory by default). Disable in development if you need every call to hit the model. |

The two model ids are independent — pair a small embedding model with a strong chat model freely. When changing the embedding model dimension, follow the steps in [Embedding pipeline → Switching the embedding model](embedding.md#switching-the-embedding-model).

## Where it is used

| Caller | Uses |
|---|---|
| `Documents/Pipelines/Classification/DocumentClassificationWorkflow` | `IChatClient` |
| `Documents/Pipelines/Embedding/DocumentEmbeddingWorkflow` | `IEmbeddingGenerator` |
| `Chat/DocumentChatAppService` + `Chat/Search/DocumentRerankWorkflow` | `IChatClient` |
| Business-module field extractors (e.g. `ContractDocumentHandler`) | `IChatClient` (caller constructs its own `ChatClientAgent`) |

A single `PaperbaseAI` block serves all of them. There is no per-pipeline endpoint switch — picking different models per pipeline is a host-level customization that replaces the registrations in `PaperbaseHostModule.ConfigureServices`.

## Trying alternative providers

- **Azure OpenAI**: set `Endpoint` to `https://<resource>.openai.azure.com/openai/deployments/<deployment>/` and use the deployment name as `ChatModelId`.
- **Ollama (local)**: run `ollama serve`, set `Endpoint` to `http://localhost:11434/v1`, leave `ApiKey` empty, and pick a locally pulled model id.
- **Any OpenAI-compatible gateway** (OpenRouter, vLLM, LM Studio, etc.) works the same way — only the keys in `PaperbaseAI` need to change.

## Cross-cutting LLM behavior (`PaperbaseAIBehavior`)

These knobs describe *how Paperbase calls the model* (language hint, JSON-mode strategy). They are bound to `PaperbaseAIBehaviorOptions` and reach every pipeline through `IOptions<>`.

```json
"PaperbaseAIBehavior": {
  "DefaultLanguage": "ja",
  "UseStrictJsonMode": true
}
```

| Key | Default | Description |
| --- | --- | --- |
| `DefaultLanguage` | `"ja"` | Language hint appended to every system prompt (Classification, Q&A, Rerank). Match this to your primary user base — Paperbase prompts are written language-agnostic and switch via this hint. |
| `UseStrictJsonMode` | `true` | Pass `ChatOptions.ResponseFormat = Json` so the SDK enforces the typed schema on structured-output calls (Classification, Rerank). Disable only when targeting a provider that does not implement OpenAI JSON mode — Paperbase falls back to in-prompt JSON-schema text in that case. |

Per-pipeline tuning also lives in `PaperbaseAIBehavior` — see the feature docs for the keys each pipeline reads:
- Classification truncation and prompt size → [classification.md](classification.md)
- Chunking → [embedding.md](embedding.md)
- Chat retrieval, rerank, tool-calling → [document-chat.md](document-chat.md)
