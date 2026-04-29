# Document Conversation Agent

Paperbase's AI direction is to provide a ChatGPT-like document conversation experience driven by Microsoft Agent Framework (MAF). This is separate from one-shot OCR extraction in business modules and builds on Paperbase's existing RAG infrastructure.

## Goal

Users should be able to ask follow-up questions over their documents, compare related documents, and request analysis while the agent can retrieve relevant context from Paperbase's document knowledge index.

Examples:

- "What are the payment terms in this contract?"
- "Compare this contract with the previous version."
- "Which invoices appear to belong to this contract?"
- "List risks in the selected documents and cite the supporting pages."

## Current Explicit RAG Path

The existing QA flow is application-controlled:

```text
AppService
  -> builds VectorSearchRequest
  -> calls IDocumentKnowledgeIndex.SearchAsync(...)
  -> formats retrieved chunks as context
  -> calls ChatClientAgent / workflow
  -> returns the answer
```

This path is simple, predictable, and appropriate for one-shot QA where the application owns the retrieval policy.

## MAF Document Conversation Path

The document conversation agent should move retrieval into the MAF context-provider/tool pipeline:

```text
DocumentChatAppService
  -> creates ChatClientAgent
  -> attaches TextSearchProvider
  -> TextSearchProvider invokes DocumentTextSearchAdapter
  -> DocumentTextSearchAdapter calls IDocumentKnowledgeIndex.SearchAsync(...)
  -> MAF injects or exposes the retrieved context
  -> ChatClientAgent answers the user
```

`TextSearchProvider` does not replace `Dignite.Paperbase.Rag`. It is the MAF-facing search provider. `Dignite.Paperbase.Rag` remains the Paperbase-owned retrieval abstraction for tenant isolation, document/type filtering, vector search, hybrid search, scoring, and provider implementations such as Qdrant.

`DocumentTextSearchAdapter` is the bridge between those two layers. It converts a raw agent search query into a `VectorSearchRequest`, generates the query embedding, carries the explicit tenant id, passes `QueryText` for hybrid search, and maps `VectorSearchResult` into MAF `TextSearchResult` citations.

## Why Use TextSearchProvider

Use `TextSearchProvider` for conversational agents because retrieval becomes part of the agent runtime rather than a pre-step owned by an app service.

It enables:

- automatic retrieval before each agent invocation with `BeforeAIInvoke`
- model-controlled retrieval with `OnDemandFunctionCalling`
- consistent integration with MAF context providers, chat history memory, skills, and tools
- a single place to format retrieved context and citations for the agent
- future multi-turn behavior where each user message may need different search input

## When Not To Use It

Do not use `TextSearchProvider` as the primary mechanism for deterministic OCR-to-entity extraction.

Business modules such as contracts and financial invoices receive the current document's OCR text and normalize it into structured fields. That flow should stay explicit:

```text
DocumentClassifiedEto.ExtractedText
  -> ChatClientAgent.RunAsync<TStructuredResult>
  -> module entity fields
```

For extraction, the agent should usually use only the current document text. Letting the model freely search can mix in unrelated documents and pollute fields such as parties, dates, and amounts.

If a module needs related context, prefer a module-specific service such as `IContractContextRetriever` or `IInvoiceMatchingService` that explicitly controls what can be searched and how the results are provided to the extraction agent.

## Design Decision

Keep `DocumentTextSearchAdapter` as infrastructure for the document conversation agent path. It should not be treated as dead code, but it should also not be confused with the current explicit QA path or business-module extraction workflows.

The next implementation step is to add a real document conversation entry point, such as `DocumentChatAppService` or `DocumentConversationAppService`, that creates a MAF `ChatClientAgent` with:

- `TextSearchProvider` backed by `DocumentTextSearchAdapter`
- chat history memory
- selected Paperbase skills/tools
- tenant-aware document scope controls
- citation-aware response formatting
