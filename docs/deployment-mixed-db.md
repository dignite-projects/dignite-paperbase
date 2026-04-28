# Mixed-DB Deployment Guide

How to run Paperbase with the **main DB** and the **vector DB** on different physical
databases — and what each variant of "different" actually costs you in engineering work.

> **Tl;dr.** The vector store sits behind `IDocumentKnowledgeIndex` and lives in its own
> `PgvectorRagDbContext` with its own connection string (`PaperbaseRag`) and its own
> migration history table (`__EFMigrationsHistory_PgvectorRag`). That isolation means
> some database swaps are pure config changes; others require migration work. This
> document maps each path to its actual cost.

---

## 1. Deployment Topology Spectrum

| # | Topology | Main DB | Vector DB | Connection strings | Migration cost |
|---|----------|---------|-----------|--------------------|----------------|
| 1 | **Dev / single instance** | PostgreSQL+pgvector (one cluster, one database) | same database | `Paperbase` and `PaperbaseRag` point at the same DB | None (default) |
| 2 | **Same DBMS, two databases** | PostgreSQL | PostgreSQL+pgvector (same cluster, different DB) | `Paperbase` → `paperbase`, `PaperbaseRag` → `paperbase_rag` | None — change appsettings only |
| 3 | **Same DBMS, two clusters** | PostgreSQL on cluster A | PostgreSQL+pgvector on cluster B (independent scaling, different backup policy) | Two distinct hostnames | None — change appsettings only |
| 4 | **Vector provider swap** | PostgreSQL | Qdrant / Azure AI Search / Milvus / … | `Paperbase` only; vector provider has its own config | One-line `[DependsOn]` swap in host module + new provider config; new `IDocumentKnowledgeIndex` implementation if not yet packaged |
| 5 | **Mixed DBMS** | SQL Server | PostgreSQL+pgvector | Two distinct providers per `DbContext` | **Not zero-code** — see §6 |

Topologies 1–3 are what `appsettings.MixedDb.Sample.json` covers and what the
`HybridDbDeployment_Tests` integration test verifies. Topology 4 is documented in §5.
Topology 5 is documented in §6 and explicitly **not** in scope for the
current implementation — Paperbase ships PostgreSQL migrations only.

---

## 2. Why the Boundary is Where it is

Two structural facts decide which switches are cheap and which are expensive:

1. **`IDocumentKnowledgeIndex` abstracts the vector store**. The application layer never
   touches `DocumentChunk` or any provider type directly. Swapping pgvector for Qdrant means
   replacing the implementation class, not the callers.
2. **EF Core migrations are provider-specific**. PostgreSQL migrations contain `text` /
   `uuid` / `timestamptz` / `vector(N)` types and `HasPostgresExtension(...)` calls. They do
   not run on SQL Server. Switching the **main** DB provider therefore requires you to
   regenerate or maintain a separate migrations set — independent of any abstraction work.

Topologies 1–3 don't cross either boundary, so they're appsettings-only changes.
Topology 4 crosses the abstraction boundary but stays on PostgreSQL for the main DB.
Topology 5 crosses the migration boundary on the main DB; that's the expensive one.

---

## 3. Cross-DB Consistency Constraints

Once main and vector DBs are physically separate, you cannot wrap a write to both in a
single transaction. Paperbase compensates with three deliberate mechanisms:

### 3.1 `OnCompleted` + `Begin(requiresNew: true)` for fan-out cleanup

`Document` deletion publishes a `DocumentDeletingEvent`.
`Rag.Pgvector.Domain.DocumentDeletingEventHandler` does **not** delete chunks
synchronously; it registers an `OnCompleted` callback on the current Unit of Work, and
inside that callback it opens **a new transactional UoW** (`requiresNew: true`):

```csharp
_unitOfWorkManager.Current.OnCompleted(async () =>
{
    using var uow = _unitOfWorkManager.Begin(
        new AbpUnitOfWorkOptions { IsTransactional = true },
        requiresNew: true);

    using (_currentTenant.Change(tenantId))
    {
        await _chunkRepository.DeleteByDocumentIdAsync(documentId);
        await _vectorRepository.DeleteByDocumentIdAsync(documentId);
    }

    await uow.CompleteAsync();
});
```

Why the explicit `requiresNew: true`: by the time `OnCompleted` fires, the main UoW has
already completed and disposed. Implicitly inheriting it would throw "UoW already
completed". Why the new UoW must be transactional: `ExecuteDeleteAsync` on the vector DB
runs outside the EF change tracker, but we still want chunk + DocumentVector deletes to
commit together.

If the main transaction rolls back, `OnCompleted` doesn't run — so chunks are not
prematurely deleted. If the main transaction commits but the cleanup fails, you get
orphans. Reconciliation (§3.3) is the safety net.

### 3.2 `UpsertDocumentAsync` is whole-document idempotent

The vector index write API is **document-scoped**, not chunk-batch:

```csharp
Task UpsertDocumentAsync(DocumentVectorIndexUpdate update, CancellationToken ct = default);

public sealed class DocumentVectorIndexUpdate
{
    public Guid DocumentId { get; init; }
    public Guid? TenantId { get; init; }
    public string? DocumentTypeCode { get; init; }
    public IReadOnlyList<DocumentVectorRecord> Chunks { get; init; }
}
```

Each call is a whole-document atomic replace: delete the document's existing chunks,
insert the new chunks, recompute the mean-pooled `DocumentVector`. If a Hangfire retry
fires the same `DocumentEmbeddingBackgroundJob` twice in a row, the second call simply
reaches the same final state. There's no chunk-level partial upsert API to misuse.

This is the reason `IDocumentKnowledgeIndex` does not expose
`UpsertAsync(IReadOnlyList<DocumentVectorRecord>)` any more — partial chunk batches would
let the document-level vector go stale.

### 3.3 Reconciliation is the eventual-consistency safety net

`Rag.Pgvector.EntityFrameworkCore.PgvectorReconciliationBackgroundJob` scans the vector
DB for chunks and `DocumentVector` rows whose `DocumentId` no longer exists in the main
DB and removes them. It is the **only** path that can recover from:

- A crash between the main-DB commit and the vector-DB cleanup.
- A vector-DB outage during `OnCompleted` callback execution.
- A historical period before this code shipped (e.g. cutover from a prior schema).

It does **not** try to be transactional across the two DBs. Run it on a schedule
(nightly is typical). It uses `IDataFilter.Disable<IMultiTenant>()` for cross-tenant scan
because orphan detection must be global; no tenant data leaks because the job has no
SELECT output — only internal comparison and DELETE.

---

## 4. Same-DBMS Cross-Database Setup (Topologies 1–3)

This is what the integration test `HybridDbDeployment_Tests` validates and what
`host/src/appsettings.MixedDb.Sample.json` configures.

### 4.1 appsettings

Two connection strings, always. There is no fallback from `PaperbaseRag` to `Paperbase`
(ABP's connection-string fallback only goes to `Default`, never to a named string).

```json
{
  "ConnectionStrings": {
    "Default":      "Host=db-main;Port=5432;Database=paperbase;Username=...;Password=...",
    "Paperbase":    "Host=db-main;Port=5432;Database=paperbase;Username=...;Password=...",
    "PaperbaseRag": "Host=db-vector;Port=5432;Database=paperbase_rag;Username=...;Password=..."
  }
}
```

Topology 1 sets `Paperbase` and `PaperbaseRag` to the same string. Topology 2 differs in
`Database`. Topology 3 differs in `Host`. The application code is identical across all
three.

### 4.2 Vector cluster prerequisites

The vector DB needs the `pgvector` extension installed before migrations run:

```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

EF Core migrations include `HasPostgresExtension("vector")`, which handles this
automatically once your role has the privilege. If you're using a managed Postgres that
gates extensions (Azure DB for PostgreSQL, RDS, …), the extension must be enabled in the
cluster configuration first.

### 4.3 Migration history is per-context

Each context maintains its own history table:

| DbContext | History table | Connection string |
|-----------|---------------|--------------------|
| `PaperbaseHostDbContext` (host) | `__EFMigrationsHistory` (default) | `Default` / `Paperbase` |
| `PgvectorRagDbContext` | `__EFMigrationsHistory_PgvectorRag` | `PaperbaseRag` |

The `PgvectorRagDbContext` history table name is set in
`PgvectorRagEntityFrameworkCoreModule` via
`b.MigrationsHistoryTable(PgvectorRagDbProperties.MigrationsHistoryTableName)`. Removing
this configuration would make both contexts share `__EFMigrationsHistory` — even on
a single physical DB — and break the cutover semantics in
[`host/scripts/migrate-chunks-to-pgvector-context.sql`](../host/scripts/migrate-chunks-to-pgvector-context.sql).

### 4.4 Running migrations

The host applies migrations on startup via `PaperbaseHostDbSchemaMigrator`:

```csharp
// host/src/Data/PaperbaseHostDbSchemaMigrator.cs
await _serviceProvider.GetRequiredService<PaperbaseHostDbContext>().Database.MigrateAsync();
await _serviceProvider.GetRequiredService<ContractsDbContext>().Database.MigrateAsync();
await _serviceProvider.GetRequiredService<PgvectorRagDbContext>().Database.MigrateAsync();
```

ABP resolves each `DbContext` against its own `[ConnectionStringName]`, so on a cross-DB
deployment the three calls hit two different physical clusters. To run from the CLI:

```bash
# From the repo root
dotnet run --project host/src --migrate-database
```

Or invoke EF Core directly per context (when you need to inspect SQL before running):

```bash
# Main DB — ABP host context
dotnet ef database update --project host/src --context PaperbaseHostDbContext

# Vector DB — independent context, independent history
dotnet ef database update \
  --project core/src/Dignite.Paperbase.Rag.Pgvector.EntityFrameworkCore \
  --context PgvectorRagDbContext
```

### 4.5 Running the integration test locally

The integration test
([`core/test/Dignite.Paperbase.Application.Tests/Documents/HybridDbDeployment_Tests.cs`](../core/test/Dignite.Paperbase.Application.Tests/Documents/HybridDbDeployment_Tests.cs))
spins up two PostgreSQL containers (with pgvector) and exercises the cross-DB scenario.
It is marked `[Trait("Category","Manual")]` because most CI runners cannot pull two
`pgvector/pgvector:pg17` images and boot two clusters within the standard test budget.

Prerequisites: Docker Desktop or a Docker-compatible runtime running locally.

```bash
dotnet test core/Dignite.Paperbase.slnx --filter "Category=Manual"
```

Expected runtime on a warm Docker cache: 60–90 seconds.

---

## 5. Switching the Vector Provider (Topology 4)

This is a one-line change in the host module plus provider configuration. The
abstraction is `IDocumentKnowledgeIndex`; the implementation is registered via
`[DependsOn]`.

### 5.1 Replace the registration

In `host/src/PaperbaseHostModule.cs`:

```diff
 [DependsOn(
     // …
-    typeof(PgvectorRagModule),
-    typeof(PgvectorRagEntityFrameworkCoreModule),
+    typeof(QdrantRagModule),                  // hypothetical
+    typeof(QdrantRagInfrastructureModule),    // hypothetical
     // …
 )]
 public class PaperbaseHostModule : AbpModule { … }
```

### 5.2 Implement the new provider (if it doesn't exist yet)

Implement `IDocumentKnowledgeIndex`. The contract:

| Method | Required behaviour |
|--------|--------------------|
| `Capabilities` | Return `DocumentKnowledgeIndexCapabilities` flags accurately (search modes, similarity, normalised score). Application paths read these to know what queries to issue. |
| `UpsertDocumentAsync` | Whole-document atomic replace, idempotent, mean-pooled document vector recomputed. |
| `DeleteByDocumentIdAsync` | Remove all index data for the document. |
| `SearchAsync` | Dispatch on `VectorSearchRequest.Mode`. Score must be in `[0, 1]` if `NormalizesScore = true`. |
| `SearchSimilarDocumentsAsync` | Document-level similarity using the document-level mean-pooled vector. |

If your provider lacks a built-in keyword-search index, set
`SupportsKeywordSearch = false` and `SupportsHybridSearch = false`. Application code
already adapts at runtime (e.g. `DocumentRelationInferenceBackgroundJob` checks
`SupportsSearchSimilarDocuments` before attempting the call).

### 5.3 Application code is unchanged

Nothing in `Dignite.Paperbase.Application` knows the implementation. There is no main DB
schema dependency on the vector store — the main DB no longer references `DocumentChunk`
at all. (See [`docs/design/rag-vector-store-decoupling.md`](design/rag-vector-store-decoupling.md)
for the historical decoupling slices.)

### 5.4 Re-embedding is required

Existing pgvector data does not carry over to a new provider. Trigger
`DocumentEmbeddingBackgroundJob` for each document, or run a maintenance task that
re-runs the embedding pipeline tenant-wide. Application read paths continue serving
results from the old provider until the cutover; once the host module is updated, the
old provider stops receiving writes and reads. Plan a maintenance window or a dual-write
shim if you need zero-downtime cutover.

---

## 6. Switching the Main DB Provider (Topology 5) — NOT Zero-Code

This section documents what's required, deliberately, to **stop** anyone from believing
"swap appsettings to SQL Server" works.

### 6.1 What breaks

EF Core migrations are provider-specific. The current main DB migrations
(`host/src/Migrations/*.cs`) target Npgsql:

- `column.HasColumnType("text")` ↔ SQL Server expects `nvarchar(max)`.
- `uuid` columns ↔ SQL Server expects `uniqueidentifier`.
- `timestamptz` / `timestamp without time zone` mappings differ.
- Any raw `Sql(...)` block in a migration is per-DBMS.

Running these against SQL Server fails immediately, even with the right connection
string and provider package referenced.

### 6.2 What's required

You must produce a parallel migrations set for the main DbContext targeting SQL Server.
EF Core supports two strategies:

**Strategy A — per-provider migrations directories.** Use `--output-dir` and a
provider-discriminator in your `IDesignTimeDbContextFactory` to keep PostgreSQL and SQL
Server migrations side by side:

```text
host/src/Migrations/Postgres/   (existing)
host/src/Migrations/SqlServer/  (new — generated against a SQL Server design-time factory)
```

At runtime, dispatch by provider:

```csharp
Configure<AbpDbContextOptions>(options =>
{
    options.Configure<PaperbaseHostDbContext>(opts =>
    {
        if (opts.ConnectionString!.Contains("Server="))   // SQL Server
            opts.UseSqlServer(b => b.MigrationsAssembly("YourSqlServerMigrationsAsm"));
        else                                              // PostgreSQL
            opts.UseNpgsql(b => b.UseVector());
    });
});
```

**Strategy B — separate migrations assemblies.** Move PostgreSQL migrations into one
project and SQL Server migrations into another; the host references whichever matches
its runtime provider.

Either way, you maintain **two migration sets in lockstep**. New schema changes require
generating both.

### 6.3 What stays cheap

- `PgvectorRagDbContext` does not change. Vector DB stays on PostgreSQL+pgvector;
  cosine-distance ANN query plans, HNSW index, `tsvector` keyword search are all
  Postgres-specific anyway. Mixed-DBMS Paperbase is "main on SQL Server, vector on
  PostgreSQL", not "everything on SQL Server".
- `Dignite.Paperbase.Application` is unchanged. No application code references provider
  types.
- Cross-DB consistency mechanics (§3) work the same — they were already designed for
  no-shared-transaction.

### 6.4 Connection strings

```json
{
  "ConnectionStrings": {
    "Paperbase":    "Server=sql-main;Database=Paperbase;User Id=...;Password=...;TrustServerCertificate=true",
    "PaperbaseRag": "Host=db-vector;Port=5432;Database=paperbase_rag;Username=...;Password=..."
  }
}
```

`Paperbase` is now SQL Server; `PaperbaseRag` is still PostgreSQL+pgvector. Each
`DbContext` resolves its own provider via `Configure<AbpDbContextOptions>`.

### 6.5 Realistic effort estimate

If your main schema doesn't already use Postgres-specific features beyond what's mapped
above, **regenerating** the SQL Server migrations from scratch (against a fresh
`Add-Migration Initial`) is typically less work than incrementally porting the existing
ones. Verify against:

```bash
dotnet ef migrations script --idempotent  # against your SQL Server target
```

This is project-scope work, not a configuration change. Allocate time accordingly.

---

## 7. Operational Runbook (Same-DBMS Cross-DB)

### 7.1 Fresh deployment

1. Provision two PostgreSQL clusters (or two databases on one cluster).
2. Install `pgvector` on the cluster that will host `PaperbaseRag`.
3. Set `ConnectionStrings:Paperbase` and `ConnectionStrings:PaperbaseRag` in the host's
   environment-specific appsettings.
4. Run the host with `--migrate-database`. Both contexts apply their migrations in order:
   first `PaperbaseHostDbContext`, then `ContractsDbContext`, then `PgvectorRagDbContext`.
5. Verify migration history tables:
   ```sql
   -- on the main cluster
   SELECT * FROM "__EFMigrationsHistory";
   -- on the vector cluster
   SELECT * FROM "__EFMigrationsHistory_PgvectorRag";
   ```

### 7.2 Splitting an existing single-DB deployment

The "current" prerequisite — chunks were already moved out of the main `PaperbaseDbContext`
into `PgvectorRagDbContext` — is the work of Slice D and was applied via
[`host/scripts/migrate-chunks-to-pgvector-context.sql`](../host/scripts/migrate-chunks-to-pgvector-context.sql).
After that cutover, the data is already in the right tables; only the **physical** split
remains.

1. Take a snapshot of the source database.
2. Restore the snapshot into the new vector cluster (`paperbase_rag`).
3. Drop everything except `PaperbaseDocumentChunks`, `PaperbaseDocumentVectors`, and
   `__EFMigrationsHistory_PgvectorRag` from the new cluster. (Order matters for FKs;
   verify against `\d+` in psql.)
4. On the original cluster, drop `PaperbaseDocumentChunks`, `PaperbaseDocumentVectors`,
   and `__EFMigrationsHistory_PgvectorRag` (the chunks were moved here at cutover; the
   history table was created by the `PgvectorRagDbContext` migration when both contexts
   pointed at one DB).
5. Update the host's `ConnectionStrings:PaperbaseRag` to point at the new cluster.
6. Restart. Verify a re-run of `PgvectorRagDbContext.Database.MigrateAsync()` is a
   no-op (the history table on the new cluster carries over).

### 7.3 Backup / retention

- The main DB is the source of truth for documents and pipeline metadata. Standard PITR.
- The vector DB is **derivable** from main-DB content + the embedding model: re-running
  `DocumentEmbeddingBackgroundJob` for each document recreates the index. You can therefore
  give the vector DB a shorter PITR window than the main DB, accepting a re-embedding
  cost on disaster recovery in exchange for storage savings.

### 7.4 Reconciliation schedule

Run `PgvectorReconciliationBackgroundJob` on a cron — nightly is typical. The job is
already registered as a transient ABP background job; schedule it via your job scheduler
(Hangfire recurring job, k8s CronJob enqueue, …). It is idempotent and safe to run on a
hot DB.

---

## 8. Acceptance Verification — Issue #37 Baseline

Slice H closes Issue #37. The 13-item acceptance baseline maps to the following code
locations and slices:

| # | Baseline | Status |
|---|----------|--------|
| 1 | Main `Paperbase.EntityFrameworkCore` zero `Pgvector` reference | ✅ Slice D — package reference removed; `core/src/Dignite.Paperbase.EntityFrameworkCore/Dignite.Paperbase.EntityFrameworkCore.csproj` |
| 2 | `DocumentChunk` physically in `Rag.Pgvector.Domain` | ✅ Slice A; current path `core/src/Dignite.Paperbase.Rag.Pgvector.Domain/Documents/DocumentChunk.cs` |
| 3 | Independent `PgvectorRagDbContext` (`ConnectionStringName = "PaperbaseRag"`) + Migrations | ✅ Slice C; `PgvectorRagDbProperties.ConnectionStringName` |
| 4 | Both contexts have explicit `MigrationsHistoryTable(...)` (`__EFMigrationsHistory_*`) | ✅ Slice C; `PgvectorRagDbProperties.MigrationsHistoryTableName` |
| 5 | Application layer zero `IDocumentChunkRepository` dependency | ✅ Slice G; `DocumentRelationInferenceBackgroundJob` no longer injects it |
| 6 | Application layer no longer mean-pools vectors; uses `SearchSimilarDocumentsAsync` | ✅ Slice G; logic moved into `PgvectorDocumentKnowledgeIndex.MeanPool` |
| 7 | `IDocumentKnowledgeIndex` exposes only the agreed surface; **no chunks-batch `UpsertAsync`** | ✅ Slice G; old `UpsertAsync` removed; `UpsertDocumentAsync` is the sole write path |
| 8 | `Document` deletion via LocalEvent + `OnCompleted` + `Begin(requiresNew: true)` (no FK CASCADE) | ✅ Slice E; `DocumentDeletingEventHandler` |
| 9 | Reconciliation in `Rag.Pgvector.EntityFrameworkCore` (not Domain) | ✅ Slice E; `PgvectorReconciliationBackgroundJob` |
| 10 | `Rag` abstraction has no `Volo.Abp.MultiTenancy` dependency | ✅ Slice F |
| 11 | `PgvectorRagDbProperties.EmbeddingVectorDimension` is the schema constant | ✅ Slice F |
| 12 | `PaperbaseRagOptions.EmbeddingDimension` is config + startup validation only | ✅ Slice F |
| 13 | Vector-provider swap and same-DBMS cross-DB split are zero-code; main-DB provider swap explicitly documented as not zero-code | ✅ Slice H — this document, `appsettings.MixedDb.Sample.json`, `HybridDbDeployment_Tests` |

---

## 9. Terminology

This document and current code use **`IDocumentKnowledgeIndex`**. The earlier abstraction
was named `IDocumentVectorStore` (renamed in Slice F when document-level similarity
joined the contract). Historical design documents under `docs/design/` retain the old
name as part of the design-history record. Treat any `IDocumentVectorStore` reference in
those documents as the predecessor of `IDocumentKnowledgeIndex`.
