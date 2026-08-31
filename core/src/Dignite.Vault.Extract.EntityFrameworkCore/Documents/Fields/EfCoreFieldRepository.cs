using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Vault.Extract.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace Dignite.Vault.Extract.Documents.Fields;

public class EfCoreFieldRepository
    : EfCoreRepository<VaultExtractDbContext, Field, Guid>, IFieldRepository
{
    public EfCoreFieldRepository(IDbContextProvider<VaultExtractDbContext> dbContextProvider)
        : base(dbContextProvider) { }

    public virtual async Task<List<Field>> GetListAsync(
        Guid documentTypeId,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet
            .Where(f => f.DocumentTypeId == documentTypeId)
            // ThenBy(Name) for a total order, carried over from the v2 repository (#499): DisplayOrder
            // defaults to 0, so ties are ordinary, and an unstable tail would make the same data render in
            // a different order on different days across every path that reads this sequence. Name is
            // unique per (TenantId, DocumentTypeId), so this is total.
            .OrderBy(f => f.DisplayOrder)
            .ThenBy(f => f.Name)
            .ToListAsync(GetCancellationToken(cancellationToken));
    }

    public virtual async Task<Field?> FindByNameAsync(
        Guid documentTypeId,
        string name,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        // Compared in SQL, so the column's collation decides - SQL Server's default is case-insensitive.
        // Deliberately not rewritten as an ordinal in-memory match: that would silently tighten the
        // lookup relative to every other path that reaches a field by name.
        return await dbSet.FirstOrDefaultAsync(
            f => f.DocumentTypeId == documentTypeId && f.Name == name,
            GetCancellationToken(cancellationToken));
    }

    public virtual async Task<List<Field>> GetListByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
        {
            return new List<Field>();
        }

        var dbSet = await GetDbSetAsync();
        return await dbSet
            .Where(f => idList.Contains(f.Id))
            .ToListAsync(GetCancellationToken(cancellationToken));
    }

    public virtual async Task<bool> NameExistsAsync(
        Guid documentTypeId,
        string name,
        Guid? excludedId = null,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet.AnyAsync(
            f => f.DocumentTypeId == documentTypeId
                 && f.Name == name
                 && (excludedId == null || f.Id != excludedId.Value),
            GetCancellationToken(cancellationToken));
    }
}
