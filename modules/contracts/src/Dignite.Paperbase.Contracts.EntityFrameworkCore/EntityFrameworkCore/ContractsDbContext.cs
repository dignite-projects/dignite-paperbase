using Dignite.Paperbase.Contracts.Contracts;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace Dignite.Paperbase.Contracts.EntityFrameworkCore;

[ConnectionStringName(ContractsDbProperties.ConnectionStringName)]
public class ContractsDbContext : AbpDbContext<ContractsDbContext>, IContractsDbContext
{
    public DbSet<Contract> Contracts { get; set; }

    public ContractsDbContext(DbContextOptions<ContractsDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ConfigureContracts();
    }
}
