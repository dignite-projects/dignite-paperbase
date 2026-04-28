# Configuration Guide

This guide covers the configuration options for `Dignite.Paperbase.Host`. The host project is a reference implementation — you can build your own host and adapt any section to fit your infrastructure.

---

## AI (LLM + Embedding)

Paperbase uses [Microsoft.Extensions.AI](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai) to integrate LLM and embedding services. Any OpenAI-compatible provider works out of the box.

### appsettings.json

```json
"PaperbaseAI": {
  "Endpoint": "https://api.openai.com/v1",
  "ApiKey": "YOUR_API_KEY",
  "ChatModelId": "gpt-4o-mini",
  "EmbeddingModelId": "text-embedding-3-small"
}
```

| Key | Description |
|-----|-------------|
| `Endpoint` | API base URL (OpenAI-compatible `/v1` format) |
| `ApiKey` | API key for the provider |
| `ChatModelId` | Model used for classification, relation inference, and QA |
| `EmbeddingModelId` | Model used for vectorization |

### PaperbaseModule.cs

```csharp
private void ConfigureAI(ServiceConfigurationContext context, IConfiguration configuration)
{
    var openAIClient = new OpenAIClient(
        new System.ClientModel.ApiKeyCredential(configuration["PaperbaseAI:ApiKey"]!),
        new OpenAIClientOptions { Endpoint = new Uri(configuration["PaperbaseAI:Endpoint"]!) });

    context.Services
        .AddChatClient(_ => openAIClient
            .GetChatClient(configuration["PaperbaseAI:ChatModelId"]!)
            .AsIChatClient())
        .UseFunctionInvocation()
        .UseLogging();

    context.Services
        .AddEmbeddingGenerator(_ => openAIClient
            .GetEmbeddingClient(configuration["PaperbaseAI:EmbeddingModelId"]!)
            .AsIEmbeddingGenerator())
        .UseLogging();
}
```

### Compatible providers

Any provider that exposes an OpenAI-compatible `/v1` API works without code changes — only `appsettings.json` needs to be updated:

| Provider | Endpoint | Notes |
|----------|----------|-------|
| OpenAI | `https://api.openai.com/v1` | Default |
| Azure OpenAI | `https://<resource>.openai.azure.com/openai` | Requires `AzureOpenAIClient` instead |
| DeepSeek | `https://api.deepseek.com/v1` | Chat only, no embedding API |
| Ollama | `http://localhost:11434/v1` | Local, requires `ollama pull <model>` |
| Qwen / Zhipu / etc. | Provider-specific | Check provider docs for endpoint |

> **Note:** If the chat and embedding models are from different providers, register two separate clients in `ConfigureAI` instead of sharing one `OpenAIClient`.

> **Claude (Anthropic):** Not OpenAI-compatible. Requires `Anthropic.SDK` NuGet package and a custom `IChatClient` adapter. Embedding must be sourced from a separate provider.

---

## AI Behavior Options

Fine-tune AI pipeline behavior via `PaperbaseAIOptions`. These affect processing logic, not the LLM provider.

### appsettings.json

```json
"PaperbaseAI": {
  "MaxDocumentTypesInClassificationPrompt": 50,
  "ChunkSize": 800,
  "ChunkOverlap": 100,
  "ChunkBoundaryTolerance": 120,
  "MaxTextLengthPerExtraction": 8000,
  "QaTopKChunks": 5,
  "QaMinScore": 0.65,
  "EnableLlmRerank": false,
  "RecallExpandFactor": 4,
  "EmbeddingVectorDimension": 1536,
  "DefaultLanguage": "ja"
}
```

| Key | Default | Description |
|-----|---------|-------------|
| `MaxDocumentTypesInClassificationPrompt` | `50` | Max candidate document types included in classification prompt; truncated by priority when exceeded |
| `ChunkSize` | `800` | Characters per text chunk for vectorization (~400 Japanese characters) |
| `ChunkOverlap` | `100` | Overlap between adjacent chunks to preserve semantic continuity |
| `ChunkBoundaryTolerance` | `120` | Characters of backtrack tolerance to snap chunk boundaries to natural breaks (paragraph / sentence end / clause). Set to `0` to disable and fall back to fixed-width slicing |
| `MaxTextLengthPerExtraction` | `8000` | Max characters sent per structured extraction call; truncated when exceeded |
| `QaTopKChunks` | `5` | Number of top vector-matched chunks retrieved for QA context |
| `QaMinScore` | `0.65` | Minimum cosine similarity (0-1) a chunk must reach to enter the RAG prompt; chunks below are discarded. Set to `0` to disable threshold filtering |
| `EnableLlmRerank` | `false` | When `true`, recall `QaTopKChunks × RecallExpandFactor` candidates and let the LLM rescore them, picking the top `QaTopKChunks` for the RAG prompt. Trades extra LLM tokens for better precision |
| `RecallExpandFactor` | `4` | Multiplier applied to `QaTopKChunks` for the recall stage when `EnableLlmRerank` is `true` |
| `EmbeddingVectorDimension` | `1536` | Embedding model output dimension. **Must match the schema-side `PaperbaseDbProperties.EmbeddingVectorDimension`** — startup validation fails otherwise. See "Switching the embedding model" below |
| `DefaultLanguage` | `"ja"` | Language hint appended to AI prompts (BCP 47 format) |

### Switching the embedding model

Switching to a model with a different vector dimension (e.g. `text-embedding-3-small` → `bge-m3`, `multilingual-e5-large`) is a four-step process. Skipping any step results in either startup failure or runtime SQL errors:

1. **Update `appsettings.json`**: change `PaperbaseAI:EmbeddingModelId` and `PaperbaseAI:EmbeddingVectorDimension` to the new model's dimension.
2. **Update `PaperbaseDbProperties.EmbeddingVectorDimension`** (in `core/src/Dignite.Paperbase.Domain/PaperbaseDbProperties.cs`) to the same value. This is the constant the EF Core mapping writes into the `vector(N)` column type.
3. **Generate a new EF Core migration** that alters the `EmbeddingVector` column type and rebuilds the HNSW index. Apply via the DB migrator host.
4. **Re-embed existing documents**: previously stored vectors have the old dimension and cannot be queried against the new column type. Trigger a rebuild via `DocumentEmbeddingBackgroundJob` for each document, or run a maintenance task that re-runs the embedding pipeline tenant-wide.

> **Why startup validation:** the `Validate(...).ValidateOnStart()` check in `PaperbaseApplicationModule` rejects mismatched configurations before any vector INSERT/SELECT runs. Without it, the host would boot and only fail on the first embedding-related SQL.

### Code

```csharp
Configure<PaperbaseAIOptions>(options =>
{
    options.DefaultLanguage = "zh-Hans";
    options.ChunkSize = 600;
});
```

---

## OCR — Azure Document Intelligence

### Default configuration (appsettings.json)

```json
"AzureDocumentIntelligence": {
  "Endpoint": "https://<your-resource>.cognitiveservices.azure.com/",
  "ApiKey": "YOUR_KEY",
  "ModelId": "prebuilt-read"
}
```

| Key | Default | Description |
|-----|---------|-------------|
| `Endpoint` | — | Azure Cognitive Services endpoint URL |
| `ApiKey` | — | Azure resource API key |
| `ModelId` | `"prebuilt-read"` | Prebuilt model ID; use `"prebuilt-document"` for better Japanese recognition |

The module (`PaperbaseAzureDocumentIntelligenceModule`) binds this section automatically — no extra code required.

### Inline configuration (code)

You can also configure options inline when calling `UseAzureDocumentIntelligence`:

```csharp
Configure<PaperbaseOcrOptions>(options =>
{
    options.UseAzureDocumentIntelligence(configure: o =>
    {
        o.Endpoint = "https://<your-resource>.cognitiveservices.azure.com/";
        o.ApiKey   = "YOUR_KEY";
        o.ModelId  = "prebuilt-document";
    });
});
```

Inline options are applied **after** `appsettings.json`, so they take precedence. You can mix both — configure common settings in appsettings and override specific values in code.

### OCR language hints

```csharp
Configure<PaperbaseOcrOptions>(options =>
{
    options.DefaultLanguageHints = new List<string> { "zh-Hans", "en" };
});
```

---

## Connection Strings

Paperbase uses two named connection strings — one for the main DB and one for the vector DB:

```json
"ConnectionStrings": {
  "Default":      "Host=127.0.0.1;Port=5432;Database=paperbase;Username=postgres;Password=postgres",
  "Paperbase":    "Host=127.0.0.1;Port=5432;Database=paperbase;Username=postgres;Password=postgres",
  "PaperbaseRag": "Host=127.0.0.1;Port=5432;Database=paperbase;Username=postgres;Password=postgres"
}
```

| Key | Used by | Notes |
|-----|---------|-------|
| `Default` | ABP fallback when a named string is missing | Keep set in development |
| `Paperbase` | `PaperbaseHostDbContext`, `ContractsDbContext` (and any other business-module DbContext) | Main DB |
| `PaperbaseRag` | `PgvectorRagDbContext` (chunks + document-level vectors) | Must allow `CREATE EXTENSION vector` — pgvector is required |

For local development, all three point at the same database. For production splits — vector
DB on a different cluster, mixed DBMS, or a different vector provider altogether — see the
[Mixed-DB Deployment Guide](deployment-mixed-db.md). The sample
[`host/src/appsettings.MixedDb.Sample.json`](../host/src/appsettings.MixedDb.Sample.json)
shows the same-DBMS cross-database topology end to end.

> **Independent migration history.** `PgvectorRagDbContext` records its applied migrations in
> `__EFMigrationsHistory_PgvectorRag`; the main DbContext continues to use the default
> `__EFMigrationsHistory`. The two contexts can share one physical database without colliding,
> and they can split onto separate clusters without any code change.

---

## Authentication

```json
"AuthServer": {
  "Authority": "https://localhost:44348",
  "SwaggerClientId": "Paperbase_Swagger",
  "CertificatePassPhrase": ""
}
```

In production, set `CertificatePassPhrase` and place `openiddict.pfx` in the host working directory. The certificate is loaded automatically when the environment is not `Development`.
