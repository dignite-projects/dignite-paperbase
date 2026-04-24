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
  "MaxTextLengthPerExtraction": 8000,
  "QaTopKChunks": 5,
  "DefaultLanguage": "ja"
}
```

| Key | Default | Description |
|-----|---------|-------------|
| `MaxDocumentTypesInClassificationPrompt` | `50` | Max candidate document types included in classification prompt; truncated by priority when exceeded |
| `ChunkSize` | `800` | Characters per text chunk for vectorization (~400 Japanese characters) |
| `ChunkOverlap` | `100` | Overlap between adjacent chunks to preserve semantic continuity |
| `MaxTextLengthPerExtraction` | `8000` | Max characters sent per structured extraction call; truncated when exceeded |
| `QaTopKChunks` | `5` | Number of top vector-matched chunks retrieved for QA context |
| `DefaultLanguage` | `"ja"` | Language hint appended to AI prompts (BCP 47 format) |

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

## Connection String

```json
"ConnectionStrings": {
  "Default": "Host=127.0.0.1;Port=5432;Database=paperbase;Username=postgres;Password=postgres"
}
```

Paperbase uses PostgreSQL with the `pgvector` extension for vector storage. Ensure `pgvector` is installed before running migrations.

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
