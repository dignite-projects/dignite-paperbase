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

Chat-related knobs live in the same `PaperbaseAI` block as the rest of the AI settings.

```json
"PaperbaseAI": {
  "EnableLlmRerank": false,
  "RecallExpandFactor": 4,
  "ChatSearchBehavior": "BeforeAIInvoke",
  "MaxToolCallsPerTurn": 10
}
```

| Key | Default | Description |
| --- | --- | --- |
| `EnableLlmRerank` | `false` | When enabled, document chat retrieves an expanded candidate set, asks the chat model to rerank chunks by question relevance, and injects only the final `TopK` into the answer prompt. Off by default to conserve tokens; enable when retrieval quality is the bottleneck (often in mixed-language corpora). |
| `RecallExpandFactor` | `4` | Multiplier applied to the conversation's `topK` (or `PaperbaseKnowledgeIndex:DefaultTopK`) before LLM rerank. With the defaults `topK=5` × `4` = 20 candidates rescored. |
| `ChatSearchBehavior` | `BeforeAIInvoke` | `BeforeAIInvoke` runs retrieval before every model call (citations always populated). `OnDemandFunctionCalling` exposes search as a tool the model decides when to call (saves tokens but the model may produce an answer with no citations — `ChatTurnResultDto.IsDegraded = true` in that case). |
| `MaxToolCallsPerTurn` | `10` | Hard cap on tool-call rounds within a single turn. Once reached, the next completion request strips tools, forcing the model to produce a final answer rather than looping. `0` means unlimited (not recommended for production). |

For prompt language and JSON-mode behavior, see [ai-provider.md → Cross-cutting LLM behavior](ai-provider.md#cross-cutting-llm-behavior). For retrieval `topK` / `minScore` defaults, see [knowledge-index.md](knowledge-index.md). For BM25-augmented hybrid retrieval, see [hybrid-search.md](hybrid-search.md).

## When the answer is degraded

`ChatTurnResultDto.IsDegraded = true` flags answers that ran without retrieval grounding. Two cases produce it:

| Cause | What happened | What to do |
|---|---|---|
| Knowledge index unavailable | `IDocumentKnowledgeIndex.SearchAsync` threw — Qdrant down, network fault, etc. | Treat as a transient infrastructure incident. The model still produced an answer using only conversation history. |
| `OnDemandFunctionCalling` mode + model declined to search | The chat behavior was set to "let the model decide" and the model answered without calling the search tool. | Either accept it (the model judged search unnecessary) or switch to `BeforeAIInvoke` if your team prefers always-grounded answers. |

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
