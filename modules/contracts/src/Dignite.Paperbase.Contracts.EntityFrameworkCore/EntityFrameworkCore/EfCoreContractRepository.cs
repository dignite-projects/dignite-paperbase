using System;
using System.Threading.Tasks;
using Dignite.Paperbase.Contracts.Contracts;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace Dignite.Paperbase.Contracts.EntityFrameworkCore;

public class EfCoreContractRepository :
    EfCoreRepository<IContractsDbContext, Contract, Guid>,
    IContractRepository
{
    public EfCoreContractRepository(IDbContextProvider<IContractsDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public virtual async Task<Contract?> FindByDocumentIdAsync(Guid documentId)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet.FirstOrDefaultAsync(x => x.DocumentId == documentId);
    }
}
