-- =============================================================================
-- Slice D cutover：把 PaperbaseDocumentChunks 的 EF Core 迁移所有权
--                  从主 PaperbaseHostDbContext 移交到独立的 PgvectorRagDbContext。
-- =============================================================================
--
-- 设计依据：GitHub Issue #37 终态架构 + Issue #41 Slice D 范围。
--
-- 适用范围：**仅现有生产部署**（已经在主 __EFMigrationsHistory 中记录了
--           Slice 4 / Refine_DocumentChunk_Aggregate / Slice 7 / Slice B 的部署）。
--           全新部署不需要执行本脚本——PgvectorRagDbContext 的 SliceD_Init_PgvectorRag
--           迁移会自己创建表与所有索引。
--
-- 关键约束：
--   1. 本脚本**必须在新代码上线前**手动执行（先停服 → 跑脚本 → 部署 → DbMigrator 启动）。
--      原因：新代码删除了主 PaperbaseDbContext 上的 DocumentChunk mapping。
--      如果先上线再跑脚本，新版应用启动时主 EF 仍会读取 __EFMigrationsHistory 决策，
--      但状态不对齐可能导致后续 ef migrations 行为异常。
--
--   2. 必须与 Slice E 配套部署到生产。
--      原因：本脚本删除 FK_PaperbaseDocumentChunks_PaperbaseDocuments_DocumentId 后，
--      Document 删除时不再 CASCADE 清理 chunks。Slice E 引入 DocumentDeletingEventHandler
--      + after-commit UoW 替代 FK CASCADE；Slice D 与 Slice E 之间有 chunks 孤儿窗口期。
--
--   3. 幂等：脚本可重复执行，结果一致。所有 DDL 都用 IF NOT EXISTS / IF EXISTS 防御。
--
-- 执行步骤（建议在事务中执行，单条事务内完成全部步骤）：
--   $ psql -d paperbase -1 -f migrate-chunks-to-pgvector-context.sql
--   （-1 = single transaction；如果脚本中途失败，整个变更回滚）
--
-- 验证：
--   - SELECT * FROM "__EFMigrationsHistory_PgvectorRag";
--     => 应包含 '20260428103809_SliceD_Init_PgvectorRag' 一行
--   - SELECT "MigrationId" FROM "__EFMigrationsHistory" WHERE "MigrationId" LIKE '%DocumentChunk%' OR "MigrationId" LIKE '%EmbeddingsAndRelations%';
--     => 应返回 0 行
--   - SELECT conname FROM pg_constraint WHERE conname = 'FK_PaperbaseDocumentChunks_PaperbaseDocuments_DocumentId';
--     => 应返回 0 行
--   - SELECT indexname FROM pg_indexes WHERE tablename='PaperbaseDocumentChunks' ORDER BY indexname;
--     => 应仅含 PK + (DocumentId, ChunkIndex) UNIQUE + (TenantId, DocumentTypeCode) +
--        SearchVector GIN + EmbeddingVector_HNSW；ivfflat 索引应已消失。
--   - \d "PaperbaseDocumentChunks"
--     => 表与列、索引完整保留（含 SearchVector tsvector / HNSW / GIN）
-- =============================================================================

BEGIN;

-- -----------------------------------------------------------------------------
-- 步骤 1：建立独立的 EF Core 迁移历史表 __EFMigrationsHistory_PgvectorRag
--         结构与 EF Core 默认 history 表一致：
--           - MigrationId varchar(150) PRIMARY KEY
--           - ProductVersion varchar(32) NOT NULL
--         主键约束确保 INSERT ... ON CONFLICT DO NOTHING 可幂等。
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory_PgvectorRag" (
    "MigrationId" varchar(150) NOT NULL,
    "ProductVersion" varchar(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory_PgvectorRag" PRIMARY KEY ("MigrationId")
);

-- -----------------------------------------------------------------------------
-- 步骤 2：把 PgvectorRagDbContext 的初始迁移标记为「已应用」。
--         避免 EF DbMigrator 在新代码启动时尝试再次创建已存在的 chunks 表。
--         ProductVersion 必须与生成 migration 时使用的 EF Core 版本一致——
--         以本仓库当前为 10.0.4（PgvectorRagDbContextModelSnapshot 的 ProductVersion 注解）。
--         未来生成新 PgvectorRag migration 时，本脚本不再适用（仅用于一次性 cutover）。
-- -----------------------------------------------------------------------------
INSERT INTO "__EFMigrationsHistory_PgvectorRag" ("MigrationId", "ProductVersion")
VALUES ('20260428103809_SliceD_Init_PgvectorRag', '10.0.4')
ON CONFLICT ("MigrationId") DO NOTHING;

-- -----------------------------------------------------------------------------
-- 步骤 3：从主 __EFMigrationsHistory 删除 chunks 相关 migration 记录。
--         主 host migrations 中这些 ID 的 .cs 文件本次 PR 已退化为 no-op，
--         因此即使 EF 看不到这些 history 行也不会触发任何 DDL。
--         删除是为了让主 history 与「主 context 不再拥有 chunks」的语义对齐——
--         未来 grep 主 __EFMigrationsHistory 应该零 chunks 痕迹。
-- -----------------------------------------------------------------------------
DELETE FROM "__EFMigrationsHistory"
WHERE "MigrationId" IN (
    '20260423055110_Slice4_EmbeddingsAndRelations',
    '20260425042822_Refine_DocumentChunk_Aggregate',
    '20260428004038_Slice7_AddDocumentChunkSearchVector',
    '20260428071403_SliceB_DocumentChunkDenormalize'
);

-- -----------------------------------------------------------------------------
-- 步骤 4：删除跨 DbContext 的外键约束。
--         FK_PaperbaseDocumentChunks_PaperbaseDocuments_DocumentId 在
--         Refine_DocumentChunk_Aggregate 迁移中被创建，跨 DbContext 部署
--         （甚至跨 DBMS）时无法成立。Slice E 引入 DocumentDeletingEventHandler
--         + IUnitOfWorkManager.OnCompleted 后，chunks 在 Document 删除时由
--         after-commit 回调（非 EF FK CASCADE）清理。
--
--         本步骤后到 Slice E 上线之间是已知中间状态：Document 删除不再清理 chunks。
--         本 Slice 必须与 Slice E 配套部署到生产以收敛该窗口。
-- -----------------------------------------------------------------------------
ALTER TABLE "PaperbaseDocumentChunks"
    DROP CONSTRAINT IF EXISTS "FK_PaperbaseDocumentChunks_PaperbaseDocuments_DocumentId";

-- -----------------------------------------------------------------------------
-- 步骤 5：清理 Slice 4 时代的 ivfflat 向量索引。
--         Refine_DocumentChunk_Aggregate 迁移在线上库已用 HNSW 替代 ivfflat
--         作为 ANN 主路径，但 ivfflat 索引并未被同时删除——现有部署上同时存在
--         HNSW + ivfflat，全新部署只有 HNSW（PgvectorRagDbContext 初始迁移不创建 ivfflat）。
--         这是真正的 schema 偏差：两个部署的查询计划不可重复，且 ivfflat 占磁盘 / 拖慢写入。
--         在此显式 DROP，让 cutover 后的现有部署 schema 与全新部署 100% 等价。
-- -----------------------------------------------------------------------------
DROP INDEX IF EXISTS "ix_paperbase_document_chunks_embedding_ivfflat";

-- -----------------------------------------------------------------------------
-- 步骤 6（可选，等价性收尾）：去掉 EF 第一次添加 NOT NULL 列时残留在 PG 列定义里的
--          DEFAULT 值。EF 添加 NOT NULL 列要求 DEFAULT 才能创建，但表 backfill 完成后
--          这些 DEFAULT 在业务逻辑里没用——所有写路径都走 EF / app 显式赋值。
--          全新部署的 PgvectorRagDbContext 初始迁移用 CreateTable 时这些列没有 DEFAULT；
--          cutover 这里清理掉，让两条路径列定义完全等价。
-- -----------------------------------------------------------------------------
ALTER TABLE "PaperbaseDocumentChunks" ALTER COLUMN "ConcurrencyStamp" DROP DEFAULT;
ALTER TABLE "PaperbaseDocumentChunks" ALTER COLUMN "CreationTime" DROP DEFAULT;
ALTER TABLE "PaperbaseDocumentChunks" ALTER COLUMN "ExtraProperties" DROP DEFAULT;

COMMIT;

-- =============================================================================
-- 幂等性自检：
--   重新执行整个脚本：
--     - 步骤 1：CREATE TABLE IF NOT EXISTS → 表存在则跳过
--     - 步骤 2：INSERT ... ON CONFLICT DO NOTHING → 行存在则跳过
--     - 步骤 3：DELETE ... WHERE → 第二次没有匹配行 = 删 0 行
--     - 步骤 4：DROP CONSTRAINT IF EXISTS → 约束不存在则跳过
--   最终状态与第一次执行后相同。
-- =============================================================================
