# Dignite Paperbase

A modular, extensible ABP-based application for paperless workflows — built **AI-native** for the LLM era.

## Design Principle: Markdown-first

Paperbase is an AI-driven enterprise document platform. Every text-bearing document — whether a digital PDF, a Word file, or a scanned image — flows through the pipeline as **Markdown**, never plain text. Headings, tables and lists carry semantic structure that downstream consumers (vector chunking, LLM classification / Q&A / rerank, business-module field extraction) all rely on. Plain-text fallback paths are a design violation; nullable-text projections happen on the consumer side via `MarkdownStripper.Strip(...)` only when truly needed (e.g. keyword fallback classifiers). See `CLAUDE.md` → "Markdown-first 数据流" for the full contract.

## Solution Structure

```
dignite-paperbase/
├── core/       # Core ABP module — domain models, repositories, application services, HTTP API
├── modules/    # Reusable business modules
├── host/       # Host application for development and deployment
│   ├── src/    # ASP.NET Core API backend
│   └── angular/# Angular SPA frontend
└── docs/       # Developer documentation and design documents
```

## Pre-requirements

* [.NET 10.0+ SDK](https://dotnet.microsoft.com/download/dotnet)
* [Node.js v18 or later](https://nodejs.org/en)
* PostgreSQL 16+ with the **pgvector** extension

### Installing pgvector

**Ubuntu / Debian (including WSL):**

```bash
sudo apt install -y postgresql-16-pgvector
```

If the package is not found, add the official PGDG repository first:

```bash
sudo sh -c 'echo "deb https://apt.postgresql.org/pub/repos/apt $(lsb_release -cs)-pgdg main" > /etc/apt/sources.list.d/pgdg.list'
wget -qO- https://www.postgresql.org/media/keys/ACCC4CF8.asc | sudo tee /etc/apt/trusted.gpg.d/postgresql.asc > /dev/null
sudo apt update
sudo apt install -y postgresql-16-pgvector
```

**Docker:** use the `pgvector/pgvector:pg17` image instead of `postgres:17` — pgvector is pre-installed.

**Other platforms:** see the [pgvector installation guide](https://github.com/pgvector/pgvector#installation).

## Getting Started (Local Development)

### 1. Configure the database

Create `host/src/appsettings.Development.json` with your local database connection:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug"
    }
  },
  "ConnectionStrings": {
    "Default": "Server=YOUR_DB_SERVER;Database=Paperbase-Dev;User ID=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=true"
  },
  "StringEncryption": {
    "DefaultPassPhrase": "YOUR_ENCRYPTION_KEY"
  }
}
```

> This file is git-ignored. In Development mode, the application automatically generates temporary OpenIddict certificates — no `.pfx` file is needed.

### 2. Install client-side libraries

The host project includes a login UI that requires client-side libraries. Run once after cloning or when dependencies change:

```bash
cd host/src
abp install-libs
```

### 3. Run the backend

```bash
cd host/src
dotnet run
```

### 4. Install frontend dependencies and run Angular

```bash
cd host/angular
npm install
npm start
```

## Choosing an OCR Provider

Paperbase ships two OCR providers; pick one according to your deployment scenario.

### Local option: PaddleOCR (default, recommended for development)

- **When to use**: zero-config local development / data must not leave the network / fully offline.
- **Requirements**: Docker only — `PP-StructureV3` runs on **CPU** and produces native Markdown out of the box.
- **Setup**:
  1. `docker compose up paddleocr` to build the sidecar (first run downloads ~600 MB of model weights, ~30–60 s cold start).
  2. `dotnet run` — the host module already wires `PaperbasePaddleOcrModule` and binds the `PaddleOcr` section in `appsettings.json` to `http://localhost:8866`.
  3. Upload a PDF.

- **Quality**: `PP-StructureV3` (default) preserves headings, paragraphs, tables and seal placeholders as Markdown; especially strong on Chinese (OmniDocBench edit distance ≈ 0.21, ~3× better than Docling). For pure line-level OCR with no Markdown structure, set `PaddleOcr:ModelName` to `PP-OCRv4`. For the highest-quality VLM pipeline (requires GPU + ~2 GB model download), set it to `PaddleOCR-VL-1.5`.
- **Performance**: ~3.7 s/page on CPU (Intel Xeon class), ~2 GB RAM working set.

### Cloud option: Azure Document Intelligence

- **When to use**: production workloads where you want a managed OCR backend / data is allowed to leave the network boundary / you don't want to operate a sidecar.
- **Free tier (F0)**: 500 pages/month, resets on the first of each month.

  > ⚠️ **F0 limitations**: each request only processes the **first 2 pages** of the input. This means:
  > - Uploading a 50-page PDF will only return OCR for the first 2 pages.
  > - F0 is suitable only for demos and short documents (≤ 2 pages).
  > - For larger documents or sustained development, switch to the **S0** paid tier (~$1.50 / 1000 pages); no page truncation.
  > - Only one F0 resource is allowed per subscription per region.
  > - Throughput is low (about 1–2 TPS); high-frequency calls will be throttled.

- **Setup**:
  1. Sign up for Azure: https://azure.microsoft.com/free/
  2. Create an Azure AI Document Intelligence resource (F0 for trial; S0 for serious development).
  3. Copy the **Endpoint** and **API Key**.
  4. Enable `PaperbaseAzureDocumentIntelligenceModule` in `host/src/PaperbaseHostModule.cs` (and the corresponding `ProjectReference` in `host/src/Dignite.Paperbase.Host.csproj`); comment out `PaperbasePaddleOcrModule`.
  5. Add to `host/src/appsettings.Development.json`:
     ```json
     "AzureDocumentIntelligence": {
       "Endpoint": "<your-endpoint>",
       "ApiKey": "<your-key>"
     }
     ```
  6. `dotnet run` and upload a PDF.

- **Quality**: native Markdown output (titles, tables, lists preserved), strong on Japanese / Chinese / English.
- **Production**: when F0 is not enough (page quota exhausted or large documents truncated), upgrade to S0 (billed at ~$1.50 / 1000 pages).

> Why only these two? Cloud LLM OCR providers (Gemini / Mistral) and Google Document AI were evaluated and rejected — see issue #79 for the rationale (Japanese-language quality, region access, dependency footprint, free-tier shape).

## Deploying to Production

### Generating a Signing Certificate

In the production environment, you need a signing certificate. Generate one with:

```bash
dotnet dev-certs https -v -ep openiddict.pfx -p <your-certificate-passphrase>
```

> Replace `<your-certificate-passphrase>` with a strong random password. Save it — you will need it in `appsettings.Production.json`.

For more information, refer to: [OpenIddict Certificate Configuration](https://documentation.openiddict.com/configuration/encryption-and-signing-credentials.html#registering-a-certificate-recommended-for-production-ready-scenarios)

### Production Configuration

The `appsettings.Production.json` file is git-ignored. Create it manually in the deployment directory alongside `openiddict.pfx`:

```json
{
  "ConnectionStrings": {
    "Default": "<your production database connection string>"
  },
  "AuthServer": {
    "CertificatePassPhrase": "<your-certificate-passphrase>"
  },
  "StringEncryption": {
    "DefaultPassPhrase": "<your encryption key — never change this after data has been written>"
  }
}
```

### Deploy with Docker

Build images locally:

```bash
cd host/etc/build
./build-images-locally.ps1
```

Start with Docker Compose:

```bash
cd host/etc/docker
./run-docker.ps1
```

Stop containers:

```bash
cd host/etc/docker
./stop-docker.ps1
```

## Resources

* [Configuration Guide](./docs/configuration.md)
* [Angular Application](./host/angular/README.md)
* [ABP Framework Documentation](https://abp.io/docs/latest)
* [Application (Single Layer) Startup Template](https://abp.io/docs/latest/solution-templates/application-single-layer)
* [Configuring OpenIddict for Production](https://abp.io/docs/latest/Deployment/Configuring-OpenIddict#production-environment)
