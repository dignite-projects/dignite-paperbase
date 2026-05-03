# Text Extraction

Every document uploaded to Paperbase passes through a text-extraction stage that converts the raw bytes into **Markdown**. The Markdown then drives every downstream AI capability — classification, embedding, document chat, and business-module field extraction.

## Markdown-first contract

Paperbase is an AI-native platform. Headings, tables and lists are not formatting decoration — they are semantic signals that LLMs and chunkers rely on. Therefore:

- Every text-extraction provider — built-in or third-party — **must** populate `TextExtractionResult.Markdown`. Plain-text fallbacks are a design violation.
- Even when the source has no structure (e.g. a low-quality scan of a printed page), the provider must still emit flat Markdown paragraphs rather than raw text.
- `Document.Markdown` is the **only** text field on the `Document` aggregate. Consumers that need plain text strip on demand via `Dignite.Paperbase.Documents.MarkdownStripper.Strip(...)`; nothing is persisted in stripped form.

Source contract: [`ITextExtractor`](../core/src/Dignite.Paperbase.Abstractions/TextExtraction/ITextExtractor.cs), [`IMarkdownTextProvider`](../core/src/Dignite.Paperbase.TextExtraction/IMarkdownTextProvider.cs).

## Two extraction paths

```
Upload → DocumentTextExtractionBackgroundJob
              │
              ├─→ digital text layer? (PDF / DOCX / HTML / TXT / CSV / RTF / EPUB …)
              │     └─→ IMarkdownTextProvider (e.g. ElBruno MarkItDown)
              │
              └─→ image / scan?
                    └─→ IOcrProvider (PaddleOCR / Azure Document Intelligence)

Both paths write the same shape: TextExtractionResult { Markdown, Confidence, ... }
                                  → Document.Markdown
```

The two paths are dispatched by file kind. Hosts wire one digital provider plus one OCR provider via `[DependsOn(...)]`; switching providers is a host-level swap with no Application or Domain changes.

## Digital extraction — ElBruno MarkItDown

`PaperbaseElBrunoMarkItDownModule` is the default `IMarkdownTextProvider` and handles digital files (PDF with text layer, DOCX, HTML, TXT, CSV, RTF, EPUB). It is enabled automatically by the host module and needs no configuration.

If a digital PDF has no text layer (scanned PDF), the digital path returns empty Markdown and the pipeline falls through to the OCR provider.

## OCR — choosing a provider

Paperbase ships two OCR providers. Pick one in `host/src/PaperbaseHostModule.cs` based on the deployment scenario.

| | PaddleOCR (default) | Azure Document Intelligence |
|---|---|---|
| Where data goes | Local sidecar — never leaves the network | Cloud (Azure region) |
| Setup cost | `docker compose up paddleocr` | Azure subscription + AI resource |
| Best language coverage | Chinese + Japanese (PP-StructureV3 OmniDocBench) | Japanese / Chinese / English |
| Markdown output | Native (PP-StructureV3 / VL); flat (PP-OCRv4) | Native |
| Cold start | ~30–60 s first run (model download ~600 MB) | Instant |
| Per-page cost | Free | F0 free tier (500 pages/month, **first 2 pages only** per request) → S0 ~$1.50 / 1000 pages |
| Throughput | ~3.7 s/page on CPU | Subject to Azure tier (F0 ≈ 1–2 TPS) |

> Cloud LLM OCR (Gemini / Mistral) and Google Document AI were evaluated and rejected — see issue #79 for the rationale (Japanese-language quality, region access, dependency footprint, free-tier shape).

### PaddleOCR — local sidecar

Default for development. `PP-StructureV3` runs on CPU and emits native Markdown out of the box.

```json
"PaddleOcr": {
  "Endpoint": "http://localhost:8866",
  "ModelName": "PP-StructureV3",
  "Languages": [ "ja", "en" ]
}
```

| Key | Default | Description |
| --- | --- | --- |
| `Endpoint` | `http://localhost:8866` | PaddleOCR sidecar REST endpoint |
| `ModelName` | `PP-StructureV3` | One of: `PP-StructureV3` (CPU + native Markdown, default), `PP-OCRv4` (lightest, no Markdown structure), `PaddleOCR-VL-1.5` (highest quality; requires GPU + ~2 GB model download; native Markdown) |
| `Languages` | `["ja", "en"]` | Default recognition languages (BCP 47); overridden per call by `OcrOptions.LanguageHints` |

**Bring up the sidecar:**

```bash
docker compose up paddleocr
```

The first run downloads ~600 MB of model weights and takes 30–60 seconds. Subsequent starts are instant.

**Resource footprint** (PP-StructureV3, CPU): ~3.7 s/page on a modern Intel CPU, ~2 GB RAM working set.

### Azure Document Intelligence — cloud

Recommended for production workloads where data is allowed to leave the network and the team prefers not to operate a sidecar.

1. Create an Azure AI Document Intelligence resource (F0 for trial, S0 for production).
2. Copy the **Endpoint** and **API Key**.
3. In `host/src/PaperbaseHostModule.cs`, swap `PaperbasePaddleOcrModule` for `PaperbaseAzureDocumentIntelligenceModule`. Re-enable the matching `ProjectReference` in `host/src/Dignite.Paperbase.Host.csproj`.
4. Add to `host/src/appsettings.Development.json` (or `appsettings.Production.json`):

```json
"AzureDocumentIntelligence": {
  "Endpoint": "https://<your-resource>.cognitiveservices.azure.com/",
  "ApiKey": "YOUR_KEY",
  "ModelId": "prebuilt-read"
}
```

`PaperbaseAzureDocumentIntelligenceModule` binds this section automatically.

> ⚠️ **F0 limitations** — each request only processes the **first 2 pages**, only one F0 resource per subscription per region, ~1–2 TPS throughput. Suitable only for demos and short documents (≤ 2 pages). Switch to S0 for sustained development or any larger document.

## Adding a custom OCR / digital provider

Implement `IOcrProvider` (for image/scan input) or `IMarkdownTextProvider` (for files with a digital text layer). Both contracts are documented in their source files; both demand Markdown output.

The provider lives in its own module project (`Dignite.Paperbase.Ocr.<Vendor>` or `Dignite.Paperbase.TextExtraction.<Vendor>`) and is enabled by the host through `[DependsOn(...)]`.

## See also

- [Embedding pipeline](embedding.md) — what `Document.Markdown` flows into next
- [Classification pipeline](classification.md) — how the LLM consumes the Markdown
- [Deployment checklist](deployment-checklist.md) — verifying OCR after a sidecar upgrade
