using Dignite.Paperbase.Contracts;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace Dignite.Paperbase.Contracts.EntityFrameworkCore;

[ConnectionStringName(PaperbaseContractsDbProperties.ConnectionStringName)]
public interface IPaperbaseContractsDbContext : IEfCoreDbContext
{
    DbSet<Contract> Contracts { get; }
}
