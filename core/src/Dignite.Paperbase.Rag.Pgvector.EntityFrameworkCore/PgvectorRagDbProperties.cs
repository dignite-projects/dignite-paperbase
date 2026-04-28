namespace Dignite.Paperbase.Rag.Pgvector;

/// <summary>
/// pgvector-backed RAG provider 的数据库相关常量。Slice C 期间存放在 EF Core 项目；
/// Slice F 会将与 schema 相关的常量（含 <see cref="MigrationsHistoryTableName"/>）移到
/// <c>Rag.Pgvector.Domain.Shared</c> 以便 Slice D 的 cutover SQL 脚本与 EF 配置共用同一组常量。
/// </summary>
public static class PgvectorRagDbProperties
{
    /// <summary>
    /// 与主 <c>PaperbaseDbProperties.DbTablePrefix</c> 保持一致，确保 chunk 表名跨 DbContext 切换时不变。
    /// </summary>
    public static string DbTablePrefix { get; set; } = "Paperbase";

    public static string? DbSchema { get; set; } = null;

    /// <summary>
    /// 显式独立的 connection string 名称（不与主 <c>"Paperbase"</c> 共用）。
    /// <para>
    /// 注意 ABP connection string 的 fallback 行为：找不到指定 name 时回退到 <c>"Default"</c>，
    /// <b>不会</b> 回退到 <c>"Paperbase"</c>。开发环境 appsettings 必须显式配置这两个 key
    /// （单库部署时两者值相同；生产可指向不同实例从而启用跨数据库部署）。
    /// </para>
    /// </summary>
    public const string ConnectionStringName = "PaperbaseRag";

    /// <summary>
    /// 独立的 EF Core 迁移历史表名。
    /// <para>
    /// EF Core 默认所有 DbContext 共用 <c>__EFMigrationsHistory</c>——两个 context 不显式配置
    /// 会共用同一张 history 表，导致 Slice D 的 cutover 路径与实际 EF 行为对不上。
    /// 必须在 <c>UseNpgsql(...)</c> 内显式 <c>b.MigrationsHistoryTable(PgvectorRagDbProperties.MigrationsHistoryTableName)</c>。
    /// </para>
    /// <para>
    /// 主 <c>PaperbaseDbContext</c> 沿用默认 <c>__EFMigrationsHistory</c>（不需修改）。
    /// </para>
    /// </summary>
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory_PgvectorRag";
}
