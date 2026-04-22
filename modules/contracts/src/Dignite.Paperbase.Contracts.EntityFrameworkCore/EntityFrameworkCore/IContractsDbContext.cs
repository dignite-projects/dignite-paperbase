using Dignite.Paperbase.Contracts.Contracts;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace Dignite.Paperbase.Contracts.EntityFrameworkCore;

[ConnectionStringName(ContractsDbProperties.ConnectionStringName)]
public interface IContractsDbContext : IEfCoreDbContext
{
    DbSet<Contract> Contracts { get; }
}
