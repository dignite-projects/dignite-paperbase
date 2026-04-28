using Volo.Abp.Modularity;

namespace Dignite.Paperbase.Rag.Pgvector;

/// <summary>
/// pgvector RAG provider 的薄壳模块。
///
/// <para>
/// Slice C 起，<c>PgvectorDocumentVectorStore</c> 与 chunk repository 都已迁移到
/// <c>Dignite.Paperbase.Rag.Pgvector.EntityFrameworkCore</c>，本模块仅保留 <see cref="PgvectorRagOptions"/>
/// 的命名空间承载和未来 provider 级 tuning options 的扩展点。
/// </para>
///
/// <para>
/// 真正注册 <c>IDocumentVectorStore</c> 实现的入口在
/// <c>Dignite.Paperbase.Rag.Pgvector.EntityFrameworkCore.PgvectorRagEntityFrameworkCoreModule</c>。
/// </para>
/// </summary>
[DependsOn(typeof(PaperbaseRagModule))]
public class PgvectorRagModule : AbpModule
{
}
