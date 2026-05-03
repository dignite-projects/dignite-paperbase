# Deployment / Release Smoke Test Checklist

This file collects recurring verification items that should be re-run when deploying to a new environment, upgrading critical dependencies, or shipping changes that affect the core pipeline.

Items here are not gated by GitHub Issue status. They live alongside the deployable artifact and are re-run per release. When a feature ships with end-to-end verification that cannot be automated yet, copy its checks into a new section here, tagged with the originating issue.

**How to use**: when cutting a release, copy the relevant section(s) into the release ticket and tick boxes as you verify. When a check graduates into automation (CI, smoke test job, etc.), remove it from this file with a note in the commit.

---

## PaddleOCR PP-StructureV3 sidecar (#80)

Verifies the default OCR provider after a sidecar upgrade, model swap, or fresh-clone bring-up. Run end-to-end through `docker compose up` against real samples — no synthetic fixtures.

### Out-of-the-box bring-up

- [ ] Fresh clone → `docker compose up paddleocr` succeeds; first start downloads ~600 MB of model weights without any external credentials
- [ ] `docker compose up` (full stack) cold-start time recorded; first-run model download bandwidth recorded
- [ ] Upload a scanned document via the host → `Document.Markdown` is populated with non-empty Markdown, no Azure / cloud credentials required

### Markdown output quality

- [ ] Chinese contract scan with seal → `OcrResult.Markdown` contains heading / paragraph / table Markdown markers; seal regions surface as image placeholders
- [ ] Chinese invoice scan → tables rendered as HTML / Markdown tables; amount / date fields read correctly
- [ ] Japanese scan → Markdown output preserved across the language switch (no pipeline error)

### Model variant compatibility

- [ ] `PaddleOcr:ModelName = "PP-OCRv4"` → OCR still runs; `OcrResult.Markdown` is `null` (backward compatible, no Markdown structure)
- [ ] `PaddleOcr:ModelName = "PaddleOCR-VL-1.5"` (GPU environment only) → OCR still runs; #78 Markdown-output acceptance preserved

### Performance

- [ ] CPU performance baseline: end-to-end latency reported for 1-page and 5–10-page PDFs on a developer-laptop spec; recorded against the previous baseline if any

### Downstream pipeline

- [ ] End-to-end: scan upload → TextExtraction → Embedding → Qdrant chunks split on Markdown headings (verify chunk boundaries align with `## ` / `### ` markers, not arbitrary character offsets)

### Provider switch-back

- [ ] Switch back to Azure DI by uncommenting `PaperbaseAzureDocumentIntelligenceModule` in `PaperbaseHostModule` + matching `ProjectReference` in `host/src/Dignite.Paperbase.Host.csproj` + restoring the `AzureDocumentIntelligence` config block → cloud OCR path still passes acceptance
