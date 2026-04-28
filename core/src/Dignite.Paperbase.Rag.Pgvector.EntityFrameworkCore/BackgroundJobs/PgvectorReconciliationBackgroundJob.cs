using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Paperbase.Documents;
using Dignite.Paperbase.Rag.Pgvector.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace Dignite.Paperbase.Rag.Pgvector.BackgroundJobs;

/// <summary>
/// 扫描 pgvector DB 中所有 DocumentChunk 和 DocumentVector，删除对应 Document 已不存在的孤儿记录。
///
/// <para>
/// <b>设计目标：</b>当 <c>DocumentDeletingEventHandler</c> 的 after-commit 回调
/// 因 PgvectorRag DB 抖动失败时，chunks 或 DocumentVector 会残留成为孤儿。本 job
/// 作为兜底，定期扫除这些孤儿记录。
/// </para>
///
/// <para>
/// <b>多租户安全：</b>用 <see cref="IDataFilter.Disable{T}"/> 禁用 <see cref="IMultiTenant"/>
/// 过滤器，以跨租户全量扫描，不会有租户数据泄漏（无 SELECT 输出，仅内部比对后 DELETE）。
/// </para>
/// </summary>
[BackgroundJobName("Paperbase.PgvectorReconciliation")]
public class PgvectorReconciliationBackgroundJob
    : AsyncBackgroundJob<PgvectorReconciliationJobArgs>, ITransientDependency
{
    private const int PageSize = 200;

    private readonly IDbContextProvider<PgvectorRagDbContext> _ragDbContextProvider;
    private readonly IDocumentRepository _documentRepository;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IDataFilter _dataFilter;

    public PgvectorReconciliationBackgroundJob(
        IDbContextProvider<PgvectorRagDbContext> ragDbContextProvider,
        IDocumentRepository documentRepository,
        IUnitOfWorkManager unitOfWorkManager,
        IDataFilter dataFilter)
    {
        _ragDbContextProvider = ragDbContextProvider;
        _documentRepository = documentRepository;
        _unitOfWorkManager = unitOfWorkManager;
        _dataFilter = dataFilter;
    }

    public override async Task ExecuteAsync(PgvectorReconciliationJobArgs args)
    {
        var totalOrphaned = 0;

        totalOrphaned += await ReconcileChunksAsync();
        totalOrphaned += await ReconcileDocumentVectorsAsync();

        Logger.LogInformation(
            "PgvectorReconciliation completed: {TotalOrphaned} orphaned document(s) cleaned up.",
            totalOrphaned);
    }

    private async Task<int> ReconcileChunksAsync()
    {
        int page = 0, orphaned = 0;

        while (true)
        {
            List<Guid> chunkDocumentIds;
            using (_dataFilter.Disable<IMultiTenant>())
            using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
            {
                var dbCtx = await _ragDbContextProvider.GetDbContextAsync();
                chunkDocumentIds = await dbCtx.DocumentChunks
                    .Select(c => c.DocumentId)
                    .Distinct()
                    .OrderBy(id => id)
                    .Skip(page * PageSize)
                    .Take(PageSize)
                    .ToListAsync();
                await uow.CompleteAsync();
            }

            if (chunkDocumentIds.Count == 0)
                break;

            List<Guid> orphanedIds;
            using (_dataFilter.Disable<IMultiTenant>())
            using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
            {
                var docQuery = await _documentRepository.GetQueryableAsync();
                var existingIds = await docQuery
                    .Where(d => chunkDocumentIds.Contains(d.Id))
                    .Select(d => d.Id)
                    .ToListAsync();
                orphanedIds = chunkDocumentIds.Except(existingIds).ToList();
                await uow.CompleteAsync();
            }

            foreach (var orphanedDocumentId in orphanedIds)
            {
                using (_dataFilter.Disable<IMultiTenant>())
                using (var uow = _unitOfWorkManager.Begin(
                    new AbpUnitOfWorkOptions { IsTransactional = true },
                    requiresNew: true))
                {
                    var dbCtx = await _ragDbContextProvider.GetDbContextAsync();
                    await dbCtx.DocumentChunks
                        .Where(c => c.DocumentId == orphanedDocumentId)
                        .ExecuteDeleteAsync();
                    await uow.CompleteAsync();
                }

                orphaned++;
                Logger.LogInformation(
                    "Reconciliation: deleted orphaned chunks for DocumentId={DocumentId}",
                    orphanedDocumentId);
            }

            if (chunkDocumentIds.Count < PageSize)
                break;

            page++;
        }

        return orphaned;
    }

    private async Task<int> ReconcileDocumentVectorsAsync()
    {
        int page = 0, orphaned = 0;

        while (true)
        {
            List<Guid> vectorDocumentIds;
            using (_dataFilter.Disable<IMultiTenant>())
            using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
            {
                var dbCtx = await _ragDbContextProvider.GetDbContextAsync();
                vectorDocumentIds = await dbCtx.DocumentVectors
                    .Select(dv => dv.Id)
                    .OrderBy(id => id)
                    .Skip(page * PageSize)
                    .Take(PageSize)
                    .ToListAsync();
                await uow.CompleteAsync();
            }

            if (vectorDocumentIds.Count == 0)
                break;

            List<Guid> orphanedIds;
            using (_dataFilter.Disable<IMultiTenant>())
            using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
            {
                var docQuery = await _documentRepository.GetQueryableAsync();
                var existingIds = await docQuery
                    .Where(d => vectorDocumentIds.Contains(d.Id))
                    .Select(d => d.Id)
                    .ToListAsync();
                orphanedIds = vectorDocumentIds.Except(existingIds).ToList();
                await uow.CompleteAsync();
            }

            foreach (var orphanedDocumentId in orphanedIds)
            {
                using (_dataFilter.Disable<IMultiTenant>())
                using (var uow = _unitOfWorkManager.Begin(
                    new AbpUnitOfWorkOptions { IsTransactional = true },
                    requiresNew: true))
                {
                    var dbCtx = await _ragDbContextProvider.GetDbContextAsync();
                    await dbCtx.DocumentVectors
                        .Where(dv => dv.Id == orphanedDocumentId)
                        .ExecuteDeleteAsync();
                    await uow.CompleteAsync();
                }

                orphaned++;
                Logger.LogInformation(
                    "Reconciliation: deleted orphaned DocumentVector for DocumentId={DocumentId}",
                    orphanedDocumentId);
            }

            if (vectorDocumentIds.Count < PageSize)
                break;

            page++;
        }

        return orphaned;
    }
}

public class PgvectorReconciliationJobArgs
{
    // 全局扫描，无需参数。
}
