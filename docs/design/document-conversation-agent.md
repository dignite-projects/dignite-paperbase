# Document Conversation Agent

Paperbase's AI direction is to provide a ChatGPT-like document conversation experience driven by Microsoft Agent Framework (MAF). This is separate from one-shot OCR extraction in business modules and builds on Paperbase's existing RAG infrastructure (`Dignite.Paperbase.Rag` + `IDocumentKnowledgeIndex` with Qdrant hybrid search).

## Goal

Users should be able to ask follow-up questions over their documents, compare related documents, and request analysis while the agent retrieves relevant context from Paperbase's document knowledge index across multiple turns of a single conversation.

Examples:

- "What are the payment terms in this contract?"
- "Compare this contract with the previous version."
- "Which invoices appear to belong to this contract?"
- "List risks in the selected documents and cite the supporting pages."

## Three Conversation Modes — Boundary Table

The platform now distinguishes three AI invocation patterns. Their boundaries are normative.

| Mode | Entry point | Retrieval ownership | Multi-turn | Agent shape | Use when |
|---|---|---|---|---|---|
| **A. Explicit QA** (existing) | `DocumentQaAppService.AskAsync` | AppService owns retrieval | No | `ChatClientAgent.RunAsync(prompt)` one-shot | Deterministic, auditable single-turn QA over a known document |
| **B. Document Conversation Agent** (new) | `DocumentChatAppService` | MAF `TextSearchProvider` owns retrieval; AppService only fixes the scope | Yes | `ChatClientAgent` with `AIContextProviders` and `ChatHistoryProvider` driven by an `AgentSession` | Multi-turn dialog where each user message may need a fresh retrieval over the same scope |
| **C. Module Field Extraction** (existing) | Business module `IDistributedEventHandler<DocumentClassifiedEto>` (e.g., `ContractDocumentHandler`) | None — uses only `eventData.ExtractedText` | No | `ChatClientAgent.RunAsync<TStructuredResult>` | OCR-to-entity field extraction in a business module |

**Discriminator**: "May the LLM autonomously pull context from documents the caller did not name?" → Yes = B; No = A or C.

Mode C **must not** attach a `TextSearchProvider`. Cross-document retrieval would pollute structured fields such as parties, dates, and amounts. If a module needs related context, it must implement a module-specific service (e.g., `IContractContextRetriever`) that explicitly chooses what to surface to the extraction agent.

## Architecture Overview

### Layered placement

The document conversation agent lives entirely in core. Business modules **must not** depend on it.

- `Dignite.Paperbase.Domain.Shared/Documents/Chat/` — enums, consts, localization keys
- `Dignite.Paperbase.Domain/Documents/Chat/` — `ChatConversation` aggregate root, `ChatMessage` child entity, repository interface
- `Dignite.Paperbase.Application.Contracts/Documents/Chat/` — `IDocumentChatAppService`, DTOs
- `Dignite.Paperbase.Application/Documents/Chat/` — `DocumentChatAppService`, mappers, internal MAF wiring
- `Dignite.Paperbase.Application/Documents/AI/DocumentTextSearchAdapter.cs` — unchanged location; bridge from `IDocumentKnowledgeIndex` to MAF `TextSearchProvider`
- `Dignite.Paperbase.EntityFrameworkCore/` — `EfCoreChatConversationRepository`, EF configuration
- `Dignite.Paperbase.HttpApi/Documents/DocumentChatController.cs` — REST surface

### Per-turn flow

```
HTTP request  (POST /document-chat/conversations/{id}/messages)
  └─ DocumentChatAppService.SendMessageAsync
       ├─ Load ChatConversation aggregate (TenantId, CreatorId, ConcurrencyStamp,
       │      scope, serialized AgentSession)
       ├─ Fail-closed authorization gate. ALL must hold; on any mismatch
       │      respond 404 (do not leak conversation existence):
       │      1. Permission       — Documents.Chat.SendMessage
       │      2. Tenant match     — conversation.TenantId == CurrentTenant.Id
       │      3. Ownership        — conversation.CreatorId == CurrentUser.Id
       ├─ Idempotency check — if a ChatMessage with the same ClientTurnId
       │      already exists on this conversation, short-circuit and return
       │      the prior turn result; do not re-invoke the model
       ├─ Build TextSearchProvider via DocumentTextSearchAdapter.CreateForTenant(
       │      conversation.TenantId,
       │      DocumentSearchScope { DocumentId, DocumentTypeCode, TopK, MinScore })
       ├─ Construct ChatClientAgentOptions with:
       │      - Instructions = IPromptProvider.GetQaPrompt(...) + PromptBoundary.BoundaryRule
       │      - AIContextProviders = [textSearchProvider]
       │      - ChatHistoryProvider = stage-1: InMemoryChatHistoryProvider
       │                              stage-3: PaperbasePostgresChatHistoryProvider
       ├─ Restore or create AgentSession from ChatConversation.AgentSessionJson
       ├─ Invoke ChatClientAgent.RunAsync(message, session)
       ├─ Persist user message + assistant message + citations + serialized
       │      AgentSession under optimistic concurrency on ConcurrencyStamp.
       │      A stamp mismatch surfaces as AbpDbConcurrencyException and is
       │      returned to the client as 409 Conflict; the client retries with
       │      the same ClientTurnId, which the next turn deduplicates above.
       └─ Return ChatTurnResultDto
```

The tenant id and search scope are captured in the `TextSearchProvider` closure at the start of the turn. They do **not** read from ABP `ICurrentTenant` during MAF callbacks. Reason: the MAF retrieval delegate may execute on threads where ABP ambient context has drifted. **This is a propagation guarantee, not an authorization boundary** — the authorization boundary is the fail-closed gate above.

## Aggregate & Persistence Model

Conversations are persisted. Reasons:

- Multi-turn semantics require cross-request memory; pure in-memory state would leak via DI scope rules.
- Users need a "previous conversations" list and history search.
- Compliance and audit demand replayable conversation history.

### `ChatConversation` (aggregate root)

A pure infrastructure aggregate root, parallel to `Document`. It carries no business-domain fields.

- Identity: `Guid` Id; `IMultiTenant` with immutable `TenantId`
- `Title` — short label, defaultable from first user message
- `DocumentId?` and `DocumentTypeCode?` — search scope, mutually exclusive (DocumentId wins when both set; validated at construction time)
- `TopK?` and `MinScore?` — overrides on `PaperbaseRagOptions` defaults; null means "use options default at request time"
- `AgentSessionJson?` — serialized MAF `AgentSession` (stage 1; removed once `PaperbasePostgresChatHistoryProvider` lands)
- `ConcurrencyStamp` — implements ABP `IHasConcurrencyStamp`; rotated on every state-mutating method. Provides optimistic concurrency for the per-turn write
- `Messages` — owned `ChatMessage` collection; access only through aggregate root methods (no separate repository for child entities)

Mutations: `Rename`, `AppendUserMessage(IClock, content, clientTurnId)`, `AppendAssistantMessage(IClock, content, citationsJson)`, `UpdateAgentSession(json)`. All public/protected members are `virtual` per module conventions.

### `ChatMessage` (child entity)

- Identity: `Guid` Id; FK to `ConversationId`
- `Role` — `User` or `Assistant`
- `Content` — bounded by `DocumentChatConsts.MaxMessageLength`
- `CitationsJson?` — JSON array of citations; one record per cited chunk; bounded snippet length (~200 chars per citation, ≤ TopK citations)
- `CreationTime` — set via aggregate-root method using `Clock.Now`, never `DateTime.Now`
- `ClientTurnId?` — `Guid`. Required and unique per conversation for user messages; `null` for assistant messages. Provides idempotency for client retries

The citations are stored as JSON because the access pattern is read-with-message; there is no independent query against citation rows.

### Indexing

- `(TenantId, CreatorId, CreationTime DESC)` on conversations — drives "my recent conversations"
- `(ConversationId, CreationTime ASC)` on messages — drives chronological loading
- `(ConversationId, ClientTurnId)` unique on messages where `ClientTurnId` is not null — enforces idempotency at the database level

Storage estimate: typical conversation = 20 turns × ~1.5 KB per turn including citations ≈ 30 KB; large session JSON in stage 1 may add another 50–100 KB before stage 3 retires the field.

## Chat History Memory Strategy

`Microsoft.Agents.AI.ChatHistoryProvider` is an abstract base class (`Microsoft.Agents.AI.Abstractions.dll`). MAF requires that any per-session state lives in `AgentSession.StateBag`, not on the provider instance.

Two-stage rollout:

| Stage | Provider | Persistence | Notes |
|---|---|---|---|
| Stage 1 (MVP) | `InMemoryChatHistoryProvider` | App-side double-write: serialize `AgentSession` to `ChatConversation.AgentSessionJson` and append `ChatMessage` rows | Fastest to ship; verifies end-to-end flow |
| Stage 3 | `PaperbasePostgresChatHistoryProvider : ChatHistoryProvider` | MAF writes `ChatMessage` rows directly via EF Core; `AgentSessionJson` is dropped | Removes double-write; requires `IServiceScopeFactory` to obtain a scoped DbContext from a process-wide provider |

Stage 1 is intentionally suboptimal. It is justified by the value of validating the rest of the design (scope propagation, citation extraction, ownership checks, prompt boundary) before introducing a custom history provider.

## TextSearchProvider Configuration

`TextSearchProviderOptions.SearchTime` controls when retrieval runs:

- `BeforeAIInvoke` — Retrieval runs before every agent invocation; results are injected as additional messages.
- `OnDemandFunctionCalling` — Retrieval is exposed as a function tool the model may call.

**MVP picks `BeforeAIInvoke`** with `RecentMessageMemoryLimit = 3..5` so the retrieval input includes recent turns. Reasons:

- Citations are deterministic; UI can rely on the citation list always being populated.
- Prompt boundary handling (`PromptBoundary.WrapDocument`) is easier to apply when the injection point is known.
- `OnDemandFunctionCalling` saves tokens but yields unstable retrieval coverage; the model may decline to call the tool.

`OnDemandFunctionCalling` becomes available behind a `PaperbaseAIOptions.ChatSearchBehavior` flag in a later stage; it is not in MVP.

## Search Scope & Tenant Propagation

Scope is fixed when the conversation is created and stored on the aggregate root. It does not change mid-conversation. To change scope, users create a new conversation.

For each turn, the AppService passes:

- `tenantId` from `ChatConversation.TenantId` (not from `ICurrentTenant.Id`)
- `DocumentSearchScope { DocumentId, DocumentTypeCode, TopK, MinScore }` from the aggregate

The scope flows into `DocumentTextSearchAdapter.CreateForTenant(...)` and is captured in the `TextSearchProvider` closure. Each search delegate invocation therefore observes the tenant and scope as they were at conversation creation, regardless of which request thread MAF dispatches on.

### Cross-tenant defense

Cross-tenant access is prevented by **two independent layers, both of which must succeed before any retrieval delegate is built**:

1. ABP `IMultiTenant` data filter restricts the conversation lookup to the caller's tenant. A row outside the caller's tenant cannot be loaded under normal request flow.
2. The AppService re-asserts `conversation.TenantId == CurrentTenant.Id` and `conversation.CreatorId == CurrentUser.Id` after load, **before** any `TextSearchProvider` is constructed. Any mismatch fails closed with `404 Not Found` (existence privacy) and emits an audit log entry.

The conversation-bound tenant id captured in the `TextSearchProvider` closure is a **propagation correctness** guarantee: it ensures that once authorization passes, MAF callbacks on drifting threads still see the right tenant. It is **not** itself a defense against a filter bypass — if step 2 above is skipped, an attacker who can load a foreign-tenant conversation would have a `TextSearchProvider` deliberately constructed against the foreign tenant. The defense is the fail-closed guard, not the closure.

## Citation Handling & Prompt Boundary

The citation list returned to the UI must equal the chunks actually injected into the prompt. To enforce this:

1. The AppService captures the retrieval results returned for the turn through an adapter callback (no instance fields on the adapter — those would be a concurrency hazard).
2. Citations are extracted from those captured `VectorSearchResult` objects, not parsed from the assistant text.
3. `TextSearchProviderOptions.ContextFormatter` wraps each chunk with a `<document id="..." chunk="..." page="...">…</document>` envelope using `PromptBoundary` to escape `<` characters.
4. The system instructions append `PromptBoundary.BoundaryRule` so the model treats wrapped content as data, not instructions.

Source links: `TextSearchResult.SourceLink` is initially `null` (honest). A future host-level chunk preview URL (e.g., `/api/paperbase/documents/{id}/preview?page={n}`) is exposed via an `IDocumentChunkLinkProvider` abstraction. Application has a null implementation; host injects the real one. This keeps the Application project free of host URL concerns.

## Public API Surface

Routes (REST, ABP Auto API):

| Verb | Route | Purpose |
|---|---|---|
| POST | `/api/paperbase/document-chat/conversations` | Create conversation |
| GET | `/api/paperbase/document-chat/conversations` | List own conversations (paged) |
| GET | `/api/paperbase/document-chat/conversations/{id}` | Get conversation header |
| DELETE | `/api/paperbase/document-chat/conversations/{id}` | Delete conversation |
| POST | `/api/paperbase/document-chat/conversations/{id}/messages` | Send a message and receive the turn result |
| GET | `/api/paperbase/document-chat/conversations/{id}/messages` | List messages (paged) |

`SendChatMessageInput` carries a required client-generated `ClientTurnId : Guid` for idempotency. Status codes used by the send endpoint:

- `200 OK` — turn completed; body contains user message id, assistant message id, answer, citations
- `200 OK` (idempotent replay) — same `ClientTurnId` already produced a turn; the prior `ChatTurnResultDto` is returned without re-invoking the model
- `404 Not Found` — fail-closed authorization (missing conversation, wrong tenant, or wrong owner; existence is not disclosed)
- `409 Conflict` — optimistic concurrency violation (a competing turn raced and committed first); the client should retry with the same `ClientTurnId`

Streaming endpoints (`SendMessageStreamingAsync`) are out of scope for MVP.

## Permissions

`DocumentQaAppService` (Mode A) keeps `Paperbase.Documents.Ask`. The new chat surface introduces a parallel permission tree to keep the permission resource semantically distinct:

- `Paperbase.Documents.Chat` (default — read own conversations)
  - `Paperbase.Documents.Chat.Create` — create a new conversation
  - `Paperbase.Documents.Chat.SendMessage` — send a message in an existing conversation
  - `Paperbase.Documents.Chat.Delete` — delete an owned conversation

Ownership is enforced at the AppService boundary: only the conversation creator may send messages or delete it. Future delegation/sharing is out of scope for MVP and would be additive.

## Roadmap

The roadmap describes coarse increments. Detailed task breakdown lives in GitHub Issues under the `area:doc-chat` label, not in this document.

1. **Design freeze** — this document.
2. **MVP end-to-end** — aggregates, EF migration, permissions, AppService with `InMemoryChatHistoryProvider` and `BeforeAIInvoke` retrieval.
3. **Citation hardening** — `ContextFormatter`, `PromptBoundary` integration on the retrieval injection path, citation provenance from injected chunks.
4. **Persistence cutover** — `PaperbasePostgresChatHistoryProvider` replaces double-write; `AgentSessionJson` is retired.
5. **Client surfaces** — Angular proxy regeneration; minimal demo page in host.
6. **Optional** — streaming responses; opt-in `OnDemandFunctionCalling`.

## Risk Register

| # | Risk | Mitigation |
|---|---|---|
| R1 | Reusable business modules import core Application types (e.g., `DocumentTextSearchAdapter`) | csproj review; `abp-architecture-reviewer` agent rule |
| R2 | A field-extraction agent attaches `TextSearchProvider` and pollutes structured fields | `maf-workflow-reviewer` agent rule; reference `ContractDocumentHandler` as the canonical pattern |
| R3 | Cross-tenant access through a leaked or guessed conversation id | Two independent layers: (a) ABP `IMultiTenant` data filter blocks cross-tenant load; (b) AppService fail-closed guard re-asserts `conversation.TenantId == CurrentTenant.Id` and `conversation.CreatorId == CurrentUser.Id` before any retrieval delegate is built; mismatch returns 404. The conversation-bound tenant id is *propagation correctness*, not a defense after a bypass — the defense is the fail-closed guard |
| R4 | Prompt injection through user message or retrieved chunk | `PromptBoundary` wraps user input and chunks; system prompt declares the boundary rule |
| R5 | Tool/function calling drifting outside intended scope | MVP forbids `OnDemandFunctionCalling`; option behind a flag in later stage |
| R6 | Manual `AddScoped/AddTransient` outside ABP conventions | DI uses ABP marker interfaces; `ChatHistoryProvider` implementation registered via the framework |
| R7 | Middleware leaking into Application layer | All middleware (including future SSE) stays in host |
| R8 | `ChatConversation` accumulating business fields | Treated as a pure infrastructure aggregate root, same boundary rule as `Document` |
| R9 | `Document` aggregate accreting chat-related state (e.g., `LastConversationId`) | Forbidden — enforced by `abp-document-boundary-check` |
| R10 | Tests calling real LLM endpoints | `IChatClient` substituted in tests; CI cannot reach external endpoints |
| R11 | Concurrent sends to the same conversation lose or fork multi-turn state | Optimistic concurrency on `ChatConversation.ConcurrencyStamp`: the second writer fails with 409 Conflict. Client-supplied `ClientTurnId` provides idempotent retries — a replayed `ClientTurnId` returns the prior result without re-invoking the model. Per-conversation distributed locks are *not* in MVP scope |
| R12 | Idempotent retry returns inconsistent prior result (e.g., partial citations) | The same transaction that appends user + assistant messages also persists `CitationsJson`; replay reconstructs `ChatTurnResultDto` from the persisted rows, never from in-memory state |

## Out of Scope (MVP)

- Streaming responses (SSE / SignalR)
- `OnDemandFunctionCalling` retrieval mode
- Cross-tenant or shared conversations
- Multimodal input (images, audio)
- Conversation summarization or vector-indexing of chat history itself
- Per-conversation distributed locks or send queues (MVP relies on optimistic concurrency + idempotency key; if collision rates become a real-world concern, a queue is added later)

## Decision Log

- **Naming**: `DocumentChatAppService` (short; aligns with ChatGPT vocabulary). `Conversation` is reserved for the aggregate-root noun (`ChatConversation`).
- **`DocumentTextSearchAdapter` location**: kept under `Application/Documents/AI/`. Renaming was rejected to minimize churn; the existing XML doc already states its role.
- **Persistence**: relational (PostgreSQL via existing host DbContext). No object/blob storage for messages in MVP; large session JSON is acceptable as a temporary cost.
- **Permissions**: new tree under `Documents.Chat`, not reused `Documents.Ask`. Resources are semantically distinct.
- **`SearchTime`**: `BeforeAIInvoke` for MVP. `OnDemandFunctionCalling` is feature-flagged in a later stage.
- **Citation source**: from injected `VectorSearchResult` list, not parsed from assistant text. The two paths (Mode A's `[chunk N]` parsing and Mode B's injection-side capture) coexist; they are not unified in MVP.
- **Concurrency / idempotency**: optimistic concurrency on `ChatConversation.ConcurrencyStamp` plus a required client-supplied `ClientTurnId`. Two-line rationale: (a) per-conversation server-side locks would force a single-instance host or introduce a distributed-lock dependency, both rejected for MVP; (b) without a stamp, two near-simultaneous sends could each restore the same `AgentSessionJson`, both invoke the model, and the later writer would silently overwrite the earlier one with stale assistant context. `ClientTurnId` makes legitimate client retries (network blips, 409 retries) safe; the server returns the prior result rather than re-invoking the model.
- **Authorization model**: fail-closed gate at the AppService boundary, returning 404 on tenant/ownership mismatch (existence privacy). The conversation-bound tenant id captured in the search closure is a *propagation* concern, not the security boundary; it must not be cited as a defense in the risk register or in code comments.
