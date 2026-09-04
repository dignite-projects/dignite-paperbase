# Field architecture v2 → v3 migration

Runbook for upgrading a deployment across the field-storage change that ships in `0.5.0`
([#558](https://github.com/dignite-projects/vault-extract/issues/558),
[#561](https://github.com/dignite-projects/vault-extract/issues/561)). Read it before upgrading a
deployment that already holds extracted field values — a package upgrade alone does **not** migrate
them, and nothing in the tooling reminds you.

## Does this apply to you?

| Situation | Applies |
|---|---|
| Upgrading a deployment from `0.4.x` or earlier that has extracted field values | **Yes** — run every step |
| Fresh install of `0.5.0`+ | No — there is no v2 data; the migrator finds nothing and does nothing |
| Upgrading a deployment with document types but no extracted values yet | Yes, but only field definitions move |

## What changes

| | v2 (≤ `0.4.x`) | v3 (`0.5.0`+) |
|---|---|---|
| Field definitions | `VaultFieldDefinitions` | `VaultFields` |
| Field values | `VaultDocumentExtractedFields` (typed child rows) | `VaultDocuments.FlexFields` (JSON bag, authoritative) |
| Query index | the composite indexes on the child table | `VaultDocumentFlexFieldIndexes` (derived, rebuildable) |
| Validation warnings | `VaultDocumentFieldValidationWarnings` | unchanged rows; the FK re-points at `VaultFields` |

**This is expand-then-contract.** The migration is additive: `VaultFieldDefinitions` and
`VaultDocumentExtractedFields` keep every row and are **not** dropped. That is deliberate — while
they hold their rows they are the rollback path, and a stronger one than any tag, because the data is
still there. Dropping them is a separate, later change
([#593](https://github.com/dignite-projects/vault-extract/issues/593)).

## What runs the migration

`FieldArchitectureV3Migrator` — an `ITransientDependency` in the **Application layer**, not in a
host's data folder. It lives in the module because a downstream can host the same EF model in its own
`DbContext` and own its own migrations; a host-local script would migrate one deployment and silently
leave the other on the v2 layout.

**Nothing calls it automatically.**

- **This repository's host** calls it from `VaultExtractHostDbMigrationService`, under
  `--migrate-database` only. That path already iterates the host layer plus every tenant, and calls
  `RecomputeFingerprintsAsync` for each.
- **A downstream hosting the EF model in its own `DbContext`** must call it from its own migration
  path. The `vault` Pro edition does this through its own `IVaultFieldArchitectureV3Migrator`
  wrapper, invoked from its `DbMigrator`.

Three properties worth knowing before you run it:

- **Additive** — nothing is deleted.
- **Idempotent and resumable** — a second run migrates nothing new and will not overwrite a bag an
  operator has since edited. The derived-index rebuild runs *unconditionally*, including on a
  no-op run: an "already migrated, skip" shortcut is exactly what would strand a run that failed
  partway through the rebuild while reporting success.
- **One layer per call** — it does not pierce ABP's `IMultiTenant` filter, so iterating host + tenants
  is the caller's job. The result reports which layer it actually handled, so a caller can assert it
  matches the layer requested rather than trusting the scope took.

## Steps

### 1. Back up the database

Not optional. Once the v2 tables are dropped in a later release this is the only rollback, and the
verification in step 5 is what tells you whether you still need it.

### 2. Measure

Small deployments run single-shot. Read the counts anyway — it takes seconds and tells you which
plan you are on.

```sql
SELECT COUNT(*) AS Documents            FROM VaultDocuments;
SELECT COUNT(*) AS ExtractedFieldRows   FROM VaultDocumentExtractedFields;
SELECT COUNT(*) AS FieldDefinitions     FROM VaultFieldDefinitions;
SELECT COUNT(*) AS DocumentTypes        FROM VaultDocumentTypes;
```

**Threshold:** single-shot up to roughly **100k** rows in `VaultDocumentExtractedFields`. Beyond
that, batch the bag-write pass by document id and checkpoint — a single transaction rewriting every
document row is a long lock on the one table the whole product reads.

### 3. Apply the schema migration

Additive: it adds `VaultFields`, the `FlexFields` column on `VaultDocuments`, and
`VaultDocumentFlexFieldIndexes`, and re-points the validation-warning FK. It drops nothing.

```bash
dotnet ef database update
```

> The `FlexFields` column must default to `{}`, not `''`. An empty string is not valid JSON and every
> pre-existing document becomes unreadable **at read time**, long after the migration reported
> success. The shipped migration gets this right; if you hand-write one for your own `DbContext`,
> check it.

### 4. Run the data migration

For this repository's host:

```bash
dotnet run --project host/src -- --migrate-database
```

This applies the schema migration, then migrates the host layer and every tenant layer, then
recomputes fingerprints per layer. A downstream with its own `DbContext` runs its own equivalent
(`vault`: its `DbMigrator`).

**Fingerprint recomputation is required, not optional.** `Document.FieldFingerprint` is a stored
SHA-256 that drives duplicate detection ([#411](https://github.com/dignite-projects/vault-extract/issues/411)),
and v3 hashes it from the value bag where v2 hashed it from value rows in `Order` sequence.
Duplicates are decided by string equality of that stored hash, so leaving it unrecomputed silently
partitions the corpus: every document keeps its v2 fingerprint until it is re-extracted, and then
stops matching the ones that were not. It runs automatically in the host path above, immediately
after that layer's bags exist.

### 5. Verify

Run all four. The first is the one that matters.

**5a — every v2 value survived into a bag.** Compare per document + field name.

```sql
-- Rows returned = mismatches. Zero rows is the pass condition.
WITH V2 AS (
    SELECT  ef.DocumentId,
            fd.Name           AS FieldName,
            ef.[Order],
            COALESCE(
                ef.TextValue,
                ef.LongTextValue,
                CONVERT(nvarchar(max), ef.NumberValue),
                CONVERT(nvarchar(max), ef.DateValue,     126),
                CONVERT(nvarchar(max), ef.DateTimeValue, 126),
                CASE ef.BooleanValue WHEN 1 THEN 'true' WHEN 0 THEN 'false' END
            ) AS V2Value
    FROM VaultDocumentExtractedFields ef
    JOIN VaultFieldDefinitions fd ON fd.Id = ef.FieldDefinitionId
),
V3 AS (
    SELECT  d.Id AS DocumentId,
            bag.[key] AS FieldName,
            bag.[value] AS RawValue
    FROM VaultDocuments d
    CROSS APPLY OPENJSON(d.FlexFields) AS bag
)
SELECT V2.DocumentId, V2.FieldName, V2.V2Value
FROM V2
LEFT JOIN V3 ON V3.DocumentId = V2.DocumentId AND V3.FieldName = V2.FieldName
WHERE V3.DocumentId IS NULL;
```

> **Use `OPENJSON`, never `JSON_VALUE`, for these comparisons.** `JSON_VALUE` returns `nvarchar(4000)`
> and yields `NULL` for anything longer, so a long `LongText` value reads as empty and looks exactly
> like data loss. This produced a false alarm during the dev-database run; re-read through `OPENJSON`
> the value was intact.

**5b — definition counts match, per layer.**

```sql
SELECT TenantId, COUNT(*) AS V2 FROM VaultFieldDefinitions GROUP BY TenantId;
SELECT TenantId, COUNT(*) AS V3 FROM VaultFields           GROUP BY TenantId;
```

Ids are preserved, so these should agree row for row — including soft-deleted definitions, which are
migrated too (the validation-warning FK and the derived index both key on the id).

**5c — the derived index is populated.**

```sql
SELECT COUNT(*) FROM VaultDocumentFlexFieldIndexes;
```

Expect it to be **lower** than the `VaultDocumentExtractedFields` count, by exactly the number of
long-text values. `CKEditorFieldType.IndexValueType` is null, so those values never enter the index —
the same treatment v2's `LongTextValue` column got, which was excluded from every composite index.
A count of **zero** means the rebuild did not run; re-run the migration (it is idempotent, and the
rebuild is unconditional).

**5d — duplicate detection did not shift.**

```sql
-- Run before and after; the two numbers must be identical.
SELECT COUNT(*) FROM VaultDocuments WHERE ReviewReasons & 8 = 8;   -- 8 = DuplicateSuspected
```

A change here means the fingerprint recomputation changed which documents collide, which is the
failure this step exists to catch.

Finally, in the UI: open a handful of documents of different types and confirm their fields render
with the same values as before, and that a field-value filter on the document list returns the same
result set.

### 6. Stop

Do **not** drop `VaultFieldDefinitions` or `VaultDocumentExtractedFields`. They are the rollback path
until [#593](https://github.com/dignite-projects/vault-extract/issues/593) retires them in a later
release, after every known deployment has completed this runbook.

## Rollback

| Point of failure | Recovery |
|---|---|
| Schema migration failed | Restore the backup; nothing has been written |
| Data migration failed partway | Re-run it — additive and resumable, so it picks up where it stopped |
| Verification failed | Revert the application to the previous version. The v2 tables are intact and authoritative; no data was destroyed |
| After the v2 tables are dropped (a later release) | Restore from backup. This is why the drop is a separate release |

## Note for downstream consumers

If you host the Vault Extract EF model in your own `DbContext` and generate your own migrations,
there is a sequence that loses data silently and nothing prevents it:

1. You skip the coexistence release and upgrade straight to one where the v2 entities are gone.
2. `dotnet ef migrations add` diffs the model and emits `DROP TABLE VaultDocumentExtractedFields`
   **and** `CREATE TABLE VaultFields` in one migration.
3. `ef database update` drops the v2 tables.
4. `FieldArchitectureV3Migrator` reads those tables. It now has no window to run — the source rows
   are gone, the upgrade reports success, and every field value is lost.

So: upgrade to a release where v2 and v3 **coexist** (`0.5.0`), generate an additive-only migration,
apply it, invoke the migrator from your own migration path, and complete the verification above
*before* moving to any release that drops the v2 tables.
