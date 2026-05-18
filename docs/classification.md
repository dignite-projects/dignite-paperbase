# Document Classification

When a document finishes [text extraction](text-extraction.md), Paperbase classifies it against a registered set of `DocumentTypeDefinition`s. The Host deployer registers the types they care about (e.g. `host.general`, `host.contract`, `host.invoice`); tenants can also register their own private types. Paperbase ships **no built-in types** — every type is owned by the deployer or tenant, never by Paperbase itself.

The resulting `DocumentTypeCode` is the routing signal that drives the next channel stages — Host field extraction (#168) for type-bound Host fields and tenant field extraction (#169) for tenant-defined fields — and is also broadcast via `DocumentClassifiedEto` over `DistributedEventBus` so downstream business consumers (in their own repositories) can subscribe and persist their own derived records.

This page covers the classification pipeline as a *feature*: how it works, how to tune it, and what happens when the LLM is unhappy. For low-level orchestration code see `core/src/Dignite.Paperbase.Application/Documents/Pipelines/Classification/`.

## How it works

```
Document.Markdown ──► DocumentClassificationBackgroundJob ──► DocumentClassificationWorkflow
                                                              (ChatClientAgent + structured output)
                                                                         │
                                                                         ▼
                                            ConfidenceScore ≥ Type.ConfidenceThreshold ?
                                                ├─ yes ─► DocumentClassifiedEto + enqueue Host / tenant field extraction
                                                └─ no  ─► PendingReview (human triage)

                              transient LLM error          ──► rethrow → ABP Job retry (MaxTryCount)
                              schema deserialization error ──► PendingReview (no retry)
```

Two design properties matter:

- **The LLM consumes Markdown directly.** For structured documents (contracts, reports, layout-aware OCR output), headings, tables and lists in `Document.Markdown` are kept as **real semantic signals** the LLM exploits. The system prompt explicitly tells the model "input is Markdown". For unstructured content (loose OCR paragraphs, plain text), the Markdown wrapper is a container name — it keeps the classifier on one prompt template, but no extra signal is being conveyed beyond what plain paragraphs would carry.
- **Transient LLM failures rely on ABP Job retry, not a keyword fallback.** Network errors, timeouts and cancellations bubble out of `DocumentClassificationBackgroundJob`; the `PipelineRun` is marked `Failed` for operator visibility, and ABP reschedules the job per `BackgroundJobOptions.JobTypes` retry policy. When the LLM recovers, the next attempt produces a real classification — far better than freezing a document on a low-confidence keyword guess. Schema deserialization errors short-circuit straight to `PendingReview` because retrying the same malformed output wastes quota.

## Registering document types

Host deployers register the types their deployment recognizes (per CLAUDE.md "Host 部署类型") inside `PaperbaseHostModule.ConfigureServices`:

```csharp
Configure<DocumentTypeOptions>(options =>
{
    options.Register(new DocumentTypeDefinition(
        "host.general",
        LocalizableString.Create<PaperbaseResource>("DocumentType:HostGeneral"))
    {
        ConfidenceThreshold = 0.80,
        Priority = 10
    });

    // Hosts add more types as their deployment grows (e.g. host.contract / host.invoice).
    // Paperbase ships none — the deployer owns this list end-to-end.
});
```

Tenants register their own private types at runtime through the operator UI / admin API (per CLAUDE.md "租户级类型"); those flow into the same registry under the tenant scope.

| Field | Used by |
|---|---|
| `TypeCode` | Downstream consumers (DistributedEventBus subscribers) match on this code; Host / tenant field-definition tables also key on it. Convention: `<owner>.<sub-type>` (e.g. `host.general`, `tenant-acme.case-file`). |
| `DisplayName` (`ILocalizableString`) | Sent to the LLM as the candidate name. Resolved through `IStringLocalizerFactory` under `PaperbaseAIBehavior:DefaultLanguage`. UI also reads it under the user's current culture. |
| `Priority` | Higher = appears earlier in the LLM prompt; tie-break when truncated to `MaxDocumentTypesInClassificationPrompt`. |
| `ConfidenceThreshold` | LLM result must clear this to auto-classify; below it the document goes to `PendingReview`. |

## Configuration

```json
"PaperbaseAIBehavior": {
  "MaxDocumentTypesInClassificationPrompt": 50,
  "MaxTextLengthPerExtraction": 8000
}
```

| Key | Default | Description |
| --- | --- | --- |
| `MaxDocumentTypesInClassificationPrompt` | `50` | When more than this many types are registered, the prompt keeps the top N by `Priority`. Tune this against your LLM's context window — more types means a longer prompt and slower / more expensive calls. |
| `MaxTextLengthPerExtraction` | `8000` | Markdown longer than this is truncated before being sent. The first N characters usually contain the most discriminative content (title, table-of-contents, opening clauses). Increase if your documents bury the type signal deep, but watch token cost. |

The prompt language follows `PaperbaseAIBehavior:DefaultLanguage` (see [ai-provider.md](ai-provider.md#cross-cutting-llm-behavior-paperbaseaibehavior)).

## Outcomes

| Outcome | Pipeline state | What happens next |
|---|---|---|
| LLM result, confidence ≥ threshold | `DocumentPipelineRun` completes | `DocumentClassifiedEto` published; Host & tenant field extraction enqueued; downstream `DistributedEventBus` subscribers (in their own repos) receive the event |
| LLM result, confidence < threshold | `PendingReview` | `PipelineRunExtraPropertyNames.ClassificationCandidates` is populated for the UI ([pipeline-runs.md](pipeline-runs.md)) |
| LLM unreachable (transient) | `Failed`, exception rethrown | ABP retries the job per `BackgroundJobOptions.JobTypes` `MaxTryCount`. Next attempt does a fresh LLM classification once the provider recovers. |
| LLM returned malformed JSON | `PendingReview` | No retry — a human resolves the type code in the UI |

## See also

- [Text extraction](text-extraction.md) — produces the `Document.Markdown` consumed here
- [Structured extraction](structured-extraction.md) — the validator + retry middleware contract that downstream consumers use when reacting to `DocumentClassifiedEto`
- [Pipeline runs](pipeline-runs.md) — the `Candidates` payload schema for the review UI
