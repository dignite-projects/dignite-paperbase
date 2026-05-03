# Deployment

This page covers what a host operator needs to configure to run Paperbase: the relational database, the vector database (Qdrant), authentication signing certificates, and the Docker layout. For per-feature configuration (OCR, AI provider, embedding, chat), see the matching feature doc.

## Topology

```text
Paperbase Host (ASP.NET Core)
  ├─► PostgreSQL — relational application database (entities, audit, identity, OpenIddict)
  ├─► Qdrant     — vector + payload storage for document chunks (paperbase_document_chunks)
  └─► OCR sidecar (PaddleOCR) or Azure Document Intelligence — text extraction
```

The relational database holds all Paperbase business entities. Qdrant holds chunk embeddings and the payload fields used for filtered RAG search. There is no separate "RAG database" — Paperbase used to ship one and it has been retired in favor of Qdrant.

## Connection strings

Only the relational database goes through ASP.NET Core connection strings. Qdrant is configured via [`QdrantKnowledgeIndex`](knowledge-index.md), and the OCR backend is configured per provider (see [text-extraction.md](text-extraction.md)).

```json
"ConnectionStrings": {
  "Default": "Host=db-main;Port=5432;Database=paperbase;Username=paperbase_app;Password=__SET_FROM_SECRETS__",
  "Paperbase": "Host=db-main;Port=5432;Database=paperbase;Username=paperbase_app;Password=__SET_FROM_SECRETS__"
}
```

Both names point at the same database in the open-source host — `Default` is consumed by ABP modules that hard-code that name, `Paperbase` is the project's named connection. Production deployments should source the password from the platform's secret store, not from `appsettings.Production.json`.

## Authentication and signing certificate

Paperbase uses OpenIddict. Development mode auto-generates ephemeral certificates; production needs a real signing certificate.

Generate one with:

```bash
dotnet dev-certs https -v -ep openiddict.pfx -p <your-certificate-passphrase>
```

Place `openiddict.pfx` in the host working directory and configure:

```json
"AuthServer": {
  "Authority": "https://your-host.example.com",
  "SwaggerClientId": "Paperbase_Swagger",
  "CertificatePassPhrase": "<your-certificate-passphrase>"
}
```

`CertificatePassPhrase` should also come from the platform's secret store, not from a checked-in file.

For deeper OpenIddict configuration (token lifetimes, encryption-credential rotation, etc.) see the upstream guide: [Configuring OpenIddict for Production](https://abp.io/docs/latest/Deployment/Configuring-OpenIddict#production-environment).

## String encryption key

ABP stores some configuration values (e.g. tenant connection strings) encrypted at rest using `StringEncryption:DefaultPassPhrase`. **Never change this key once data has been written** — encrypted values become unreadable.

```json
"StringEncryption": {
  "DefaultPassPhrase": "<a strong random passphrase, never rotated>"
}
```

`appsettings.Development.json` is git-ignored; `appsettings.Production.json` should be created at deploy time and never committed.

## Docker

The host ships with a Docker Compose layout that wires PostgreSQL, Qdrant, and the PaddleOCR sidecar.

```bash
# Build images locally
cd host/etc/build
./build-images-locally.ps1

# Start the stack
cd host/etc/docker
./run-docker.ps1

# Stop containers
cd host/etc/docker
./stop-docker.ps1
```

For local development without the full Docker stack, you can run each piece separately:

```bash
# Database — use the pgvector image (pgvector is required for some optional indexes)
docker run -d --name paperbase-db -p 5432:5432 -e POSTGRES_PASSWORD=postgres pgvector/pgvector:pg17

# Qdrant
docker run -d --name paperbase-qdrant -p 6334:6334 qdrant/qdrant:v1.10.0

# PaddleOCR sidecar (only if using the local OCR option)
docker compose up paddleocr     # from the repository root
```

## Migration boundary between PostgreSQL and Qdrant

PostgreSQL has standard EF Core migrations (under `host/src/Migrations/`). Qdrant has none — collections and payload indexes are reconciled at startup by `QdrantKnowledgeIndexModule.OnPostApplicationInitialization`.

Implications:

- **Schema rollback differs.** A bad EF migration can be reverted with `dotnet ef database update <previous>`. A bad Qdrant payload-index change requires either deleting the collection or dropping/re-adding the affected index manually.
- **Embedding-model changes are downtime.** Qdrant collections are dimension-locked. Switching the embedding model means recreating the collection — see [embedding.md → Switching the embedding model](embedding.md#switching-the-embedding-model).
- **Document delete is after-commit.** The Application-layer event handler calls `IDocumentKnowledgeIndex.DeleteByDocumentIdAsync` only after the relational transaction commits, avoiding stranded points if the relational write later rolls back.

## Verifying a release

When deploying to a new environment, upgrading critical dependencies, or shipping changes that touch the core pipeline, run through [deployment-checklist.md](deployment-checklist.md). Treat it as a per-release ticket template — copy the relevant sections and tick boxes as you verify.

## See also

- [Text extraction](text-extraction.md) — choosing and configuring an OCR provider
- [AI provider](ai-provider.md) — wiring `IChatClient` and `IEmbeddingGenerator`
- [Knowledge index](knowledge-index.md) — Qdrant schema, payload indexes, hybrid search toggle
- [Deployment checklist](deployment-checklist.md) — release smoke tests
