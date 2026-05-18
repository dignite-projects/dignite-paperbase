# Local Development Setup

This guide covers everything needed to run Paperbase on a local machine.

> **Channel positioning**: Paperbase is a paper → digitized-data channel. It persists into **SQL Server**; downstream RAG / vector-store / chat features live in the consumer's own deployment, not here. See `CLAUDE.md` → "OUT of scope".

## Prerequisites

| Requirement | Minimum version | Notes |
|-------------|----------------|-------|
| [.NET SDK](https://dotnet.microsoft.com/download/dotnet) | 10.0 | |
| [Node.js](https://nodejs.org) | 18 | Required for Angular frontend |
| SQL Server | 2019+ | LocalDB / Express / Developer all work. `appsettings.json` defaults to `(LocalDb)\MSSQLLocalDB` |
| [Docker Desktop](https://www.docker.com/products/docker-desktop) | any recent | Runs the PaddleOCR sidecar (and optional observability backend) |
| ABP CLI | latest | `dotnet tool install -g Volo.Abp.Cli` |

---

## Infrastructure services (Docker)

The repo's `host/docker-compose.yml` runs two sidecars — one required for OCR, one optional for OpenTelemetry visualization:

| Service | Port | Purpose | Profile |
|---------|------|---------|---------|
| **PaddleOCR** | 8866 | OCR sidecar for scanned documents (PP-StructureV3, CPU mode). Required if you upload image / scanned-PDF documents | default |
| **aspire-dashboard** | 18888 (UI), 4317 (OTLP gRPC) | Local OpenTelemetry backend — renders traces / metrics / logs at `http://localhost:18888`. Optional but recommended | `observability` |

Start the required sidecar:

```bash
cd host
docker compose up -d paddleocr
```

Add the dashboard when you want to see traces / metrics:

```bash
docker compose --profile observability up -d aspire-dashboard
```

See [observability.md](./observability.md) for what's emitted and how to point at a different OTLP backend (Jaeger / Datadog / Tempo / Azure Monitor).

Verify PaddleOCR is ready:

```bash
curl http://localhost:8866/ping
# Expected: {"status":"success"}
```

To stop the sidecars:

```bash
docker compose down
```

> If your machine can run a SQL Server container (Windows Pro / Linux), the deployment Compose layout at `host/etc/docker/docker-compose.yml` brings up SQL Server + the API + Angular together. For pure dev iteration the lighter "PaddleOCR sidecar + local SQL Server + `dotnet run`" loop is faster.

---

## Backend configuration

Create `host/src/appsettings.Development.json`. This file is git-ignored.

### Minimal configuration (no AI features)

If you only want to play with the upload / text-extraction pipeline against a local SQL Server LocalDB instance, the committed `appsettings.json` already covers it; you only need to override the connection string if your local SQL Server differs:

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
    "DefaultPassPhrase": "any-random-string-here"
  }
}
```

### Full configuration (with AI)

Add the `PaperbaseAI` block to enable the classification / Host field extraction / tenant field extraction / title-generation LLM paths:

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
    "DefaultPassPhrase": "any-random-string-here"
  },
  "PaperbaseAI": {
    "Endpoint": "https://api.openai.com/v1",
    "ApiKey": "sk-...",
    "ChatModelId": "gpt-4o-mini",
    "TitleGeneratorModelId": "gpt-4o-mini",
    "StructuredModelId": "gpt-4o-mini"
  }
}
```

To use a non-OpenAI provider (Azure OpenAI, SiliconFlow, OpenRouter, Ollama, etc.), see [docs/ai-provider.md](./ai-provider.md) — for OpenAI-protocol providers you only swap `Endpoint` + `ApiKey` + model ids; for non-OpenAI wire protocols you override `ConfigureAI` in `PaperbaseHostModule`.

> **OpenIddict certificates**: In Development mode, temporary signing and encryption certificates are generated automatically. No `.pfx` file is needed.

---

## Running the backend

Install client-side libraries (run once after cloning, or when dependencies change):

```bash
cd host/src
abp install-libs
```

Apply database migrations and seed initial data, then start the API:

```bash
cd host/src
dotnet run
```

The API will be available at `https://localhost:44348`. Swagger UI: `https://localhost:44348/swagger`.

---

## Running the Angular frontend

```bash
cd host/angular
npm install
npm start
```

The SPA will be available at `http://localhost:4200`.

Default credentials (seeded on first run):

| Field | Value |
|-------|-------|
| Username | `admin` |
| Password | `1q2w3E*` |

---

## Full startup checklist

1. SQL Server is running and the target database (e.g. `Paperbase-Dev`) is reachable
2. `docker compose up -d paddleocr` completed successfully in `host/` (only required if you upload scanned documents)
3. `host/src/appsettings.Development.json` exists with valid connection string and passphrase
4. `dotnet run` started without errors in `host/src`
5. `npm start` started in `host/angular`

---

## Troubleshooting

### Port conflicts

If Docker fails to bind a port, another process is already using it. Check with:

```bash
# Windows
netstat -ano | findstr ":8866 :18888 :4317"

# Linux / WSL
ss -tlnp | grep -E '8866|18888|4317'
```

### Database migration errors

Migrations require the SQL Server account to have `CREATE TABLE` / `CREATE INDEX` privileges on the target database. For LocalDB the default user typically has full rights; for a shared SQL Server instance grant the application user `db_owner` on the Paperbase database.

### PaddleOCR slow on first request

PaddleOCR loads the PP-StructureV3 model into memory on the first request. Subsequent requests are fast. The first upload after a cold start may take 30–60 seconds.
