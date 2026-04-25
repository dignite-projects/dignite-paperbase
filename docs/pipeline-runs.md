# Pipeline Runs

`DocumentPipelineRun` records the execution history of document processing pipelines. Common state such as `PipelineCode`, `Status`, `AttemptNumber`, `StartedAt`, `CompletedAt`, and `StatusMessage` is stored as first-class properties.

Pipeline-specific outputs are stored in `DocumentPipelineRun.ExtraProperties`. Keys are defined in `PipelineRunExtraPropertyNames`; each pipeline owns the shape of its own payload.

## Classification Candidates

`PipelineRunExtraPropertyNames.ClassificationCandidates` uses the key:

```csharp
Candidates
```

This payload is written when classification finishes with low confidence. It exists so the Angular UI can show the top candidate document types to a reviewer.

The server does not currently consume this payload for business decisions after writing it. Treat it as a UI-facing JSON payload schema, not as a domain rule source.

### JSON Shape

The payload is a JSON array. Each item follows the `PipelineRunCandidate` schema:

```json
[
  {
    "TypeCode": "contract.general",
    "ConfidenceScore": 0.64
  },
  {
    "TypeCode": "invoice.standard",
    "ConfidenceScore": 0.31
  }
]
```

| Property | Type | Description |
|----------|------|-------------|
| `TypeCode` | `string` | Candidate document type code |
| `ConfidenceScore` | `number` | Classification confidence, expected in the `0.0` to `1.0` range |

### Server-Side Notes

When writing the payload, use `PipelineRunCandidate`:

```csharp
run.SetProperty(
    PipelineRunExtraPropertyNames.ClassificationCandidates,
    candidates);
```

After EF Core persistence, ABP restores non-primitive `ExtraProperties` values as `System.Text.Json.JsonElement`. Do not rely on `GetProperty<List<PipelineRunCandidate>>()` for this payload. If server-side code ever needs to read it, read the non-generic property value and parse the JSON explicitly.

### Angular Notes

The value is exposed through `DocumentPipelineRunDto.ExtraProperties` under the `Candidates` key. Angular code should treat it as an array with `TypeCode` and `ConfidenceScore` fields and should tolerate the key being absent when a run has no low-confidence candidate list.
