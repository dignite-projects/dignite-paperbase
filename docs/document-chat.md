# Document Chat

Paperbase exposes a conversational endpoint that lets users ask questions over their document corpus. The chat runs as a MAF `ChatClientAgent` with retrieval-augmented generation: each turn pulls relevant chunks from the [knowledge index](knowledge-index.md), feeds them into the prompt, and returns a grounded answer with citations.

This page covers the chat as a *feature* — what it does, how to tune it, and what knobs are safe to flip. For end-to-end HTTP request/response shapes (idempotency, retry, error handling), see [document-chat-client.md](document-chat-client.md).

## What it can do

- **Conversation-scoped retrieval.** A conversation can be unscoped (search across all the user's documents), scoped to a `documentTypeCode` (e.g. only contracts), or scoped to a single `documentId`.
- **Citations.** Every answer carries the chunk(s) that grounded it. The agent prompt enforces `[chunk N]` citations and the result is post-processed into a structured `citations` array (page number, chunk index, snippet, source name).
- **Tool calling.** Business modules contribute structured query tools through `IDocumentChatToolContributor`. Example: a `ContractChatToolContributor` can let the model call `search_contracts` to filter by counterparty or amount, alongside the built-in `search_paperbase_documents` semantic-search tool. Each tool runs **fail-closed**: explicit permission check + explicit tenant predicate + result-row cap inside the tool body. See [`.claude/rules/doc-chat-anti-patterns.md`](../.claude/rules/doc-chat-anti-patterns.md) for the contract.
- **Idempotent turns.** The client generates a `clientTurnId` per turn; replays with the same id never re-invoke the model.
- **Optional LLM rerank.** Off by default. When enabled, retrieval recall is expanded `RecallExpandFactor`× and the chat model rescues the most relevant `TopK` before the answer prompt.

## Permissions

| Permission | Grants |
|---|---|
| `Paperbase.Documents.Chat` | Read own conversations and messages (default) |
| `Paperbase.Documents.Chat.Create` | Create a new conversation |
| `Paperbase.Documents.Chat.SendMessage` | Send a message in an existing conversation |
| `Paperbase.Documents.Chat.Delete` | Delete an owned conversation |

A user holding only `Paperbase.Documents.Chat` can read but not act. Tool contributors enforce their own per-feature permissions on top of this — for example `search_contracts` requires `Contracts.Default` even though the chat permission is already held.

## Configuration

Chat-related knobs live in `PaperbaseAIBehavior` alongside the other Application-layer behavior settings (see [ai-provider.md](ai-provider.md) for the full split between `PaperbaseAI` provider wiring and `PaperbaseAIBehavior` behavior knobs).

```json
"PaperbaseAIBehavior": {
  "EnableLlmRerank": false,
  "RecallExpandFactor": 4
}
```

| Key | Default | Description |
| --- | --- | --- |
| `EnableLlmRerank` | `false` | When enabled, document chat retrieves an expanded candidate set, asks the chat model to rerank chunks by question relevance, and injects only the final `TopK` into the answer prompt. Off by default to conserve tokens; enable when retrieval quality is the bottleneck (often in mixed-language corpora). |
| `RecallExpandFactor` | `4` | Multiplier applied to the conversation's `topK` (or `PaperbaseKnowledgeIndex:DefaultTopK`) before LLM rerank. With the defaults `topK=5` × `4` = 20 candidates rescored. |

Document chat uses a single MAF tool-calling path: the agent exposes `search_paperbase_documents` (RAG) plus any business-module contributor tools, with `ChatToolMode.Auto` so the model picks when (and with what query / `documentIds`) to invoke them. There is no operator switch for "always retrieve before answering" — see *When the answer is degraded* below for the honest-signal contract that replaced it.

The hard cap on tool-call rounds within a single turn is configured at host wiring time via `PaperbaseAI:MaxToolIterations` (default `10`); see [ai-provider.md → Provider wiring](ai-provider.md#provider-wiring-paperbaseai). For prompt language behavior, see [ai-provider.md → Cross-cutting LLM behavior](ai-provider.md#cross-cutting-llm-behavior-paperbaseaibehavior). For retrieval `topK` / `minScore` defaults, see [knowledge-index.md](knowledge-index.md). For BM25-augmented hybrid retrieval, see [hybrid-search.md](hybrid-search.md).

## Citation-to-source navigation

`ChatCitationDto` is the UI-facing citation contract. The current fields are sufficient for the first clickable citation implementation:

| Field | Navigation meaning |
| --- | --- |
| `documentId` | The source document to open. A citation click must navigate to this document even when the active conversation is scoped by `documentTypeCode` and the cited document is not currently displayed. |
| `pageNumber` | Optional 1-based source page hint. For PDFs, prefer the original file/PDF view when this value exists. If the UI cannot position to the exact page yet, open the document and display the page number as context. |
| `chunkIndex` | Optional knowledge-index chunk ordinal. It is useful for display/debug context, but it is not a long-term stable anchor after re-embedding. Do not use it as the only Markdown highlight key. |
| `snippet` | Preferred Markdown fallback for first-version highlighting. Search the current document Markdown for this text and highlight the first matching range when possible. |
| `sourceName` | Display label only. Do not parse it for routing or positioning. |

Fallback order:

1. If `documentId` is missing or the document cannot be loaded, keep the citation as non-navigable display text.
2. If `pageNumber` exists and the UI has a PDF/source viewer, open `documentId` in that viewer and position to the page.
3. Otherwise open `documentId` in the document detail view, show the persisted Markdown source as-is (no client-side rendering — the Markdown source IS the AI's view of the document, surfacing it raw is intentional), and try to locate `snippet`.
4. If `snippet` cannot be found, show the document without a highlight and keep `chunkIndex` / `pageNumber` visible as citation context.

This deliberately does not introduce a separate `DocumentSourceLocation` DTO, persisted chunk IDs, or stored character offsets. Add those only after a real PDF/Markdown viewer needs exact positioning that cannot be satisfied by `documentId + pageNumber + snippet` fallback.

**PDF page navigation is browser-dependent.** The Angular client appends `#page=N` to the blob URL and renders it in a sandboxed `<iframe>`, which works in Chromium-based browsers and Firefox (PDF.js honors the `Open Parameters` fragment). Safari and embedded WebViews may ignore the fragment and open the document on page 1; in that case the badge `p.{n}` next to the source pane keeps the page hint visible to the user. Document this when shipping to clients with strict browser requirements.

**Snippet match is whole-document `indexOf`.** The first occurrence of the snippet in the persisted Markdown is highlighted. Re-extracting the document with a different OCR run can shift the persisted Markdown enough that the snippet no longer matches; the UI surfaces this as a visible warning without breaking the chat.

## When the answer is degraded

`ChatTurnResultDto.IsDegraded = true` flags answers that ran without retrieval grounding. Two cases produce it:

| Cause | What happened | What to do |
|---|---|---|
| Knowledge index unavailable | `IDocumentKnowledgeIndex.SearchAsync` threw — Qdrant down, network fault, etc. | Treat as a transient infrastructure incident. The model still produced an answer using only conversation history. |
| Model declined to invoke `search_paperbase_documents` | The model judged the question answerable without retrieval (greetings, follow-up clarifications, contributor-tool answers that don't need RAG). | Accept it: citations reflect what the model *actually used*; an empty list with `IsDegraded = true` is the honest signal. If a class of questions is consistently answered without search where you want it grounded, tighten the QA system prompt in `DefaultPromptProvider` rather than forcing pre-injection. |

`isDegraded` is surfaced to the API client so UIs can show a "no sources used" banner.

## Adding a tool contributor (business modules)

To let the chat answer business-domain questions ("show contracts with Acme Corp expiring this quarter"), a business module implements `IDocumentChatToolContributor`. Three rules apply, each enforced at PR review:

1. **`ContributeTools` returns `AIFunction`s with static descriptions** — never interpolate user-controlled text into the description (prompt-injection vector).
2. **Each tool method is fail-closed**: explicit `IAuthorizationService.CheckAsync(...)` for the feature permission + explicit `Where(x => x.TenantId == _tenantId)` (do not rely on ABP's ambient `DataFilter`) + a hard `Take(N)` row cap.
3. **No raw SQL.** Compose queries via `IRepository<T>.GetQueryableAsync()` so all framework filters (soft-delete, tenant, audit) stay in effect.

Reference implementation: `modules/contracts/src/Dignite.Paperbase.Contracts.Application/Chat/ContractChatToolContributor.cs`. Counter-examples and the rationale: [`.claude/rules/doc-chat-anti-patterns.md`](../.claude/rules/doc-chat-anti-patterns.md).

## See also

- [HTTP client guide](document-chat-client.md) — request/response shapes, idempotency, 409 retry pattern
- [Knowledge index](knowledge-index.md) — what backs retrieval
- [Hybrid search](hybrid-search.md) — BM25 + dense recall fusion
- [Embedding pipeline](embedding.md) — where chunks come from
