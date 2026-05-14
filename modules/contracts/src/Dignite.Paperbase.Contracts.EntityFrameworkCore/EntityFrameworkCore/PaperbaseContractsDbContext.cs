using Dignite.Paperbase.Contracts;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace Dignite.Paperbase.Contracts.EntityFrameworkCore;

[ConnectionStringName(PaperbaseContractsDbProperties.ConnectionStringName)]
public class PaperbaseContractsDbContext : AbpDbContext<PaperbaseContractsDbContext>, IPaperbaseContractsDbContext
{
    public DbSet<Contract> Contracts { get; set; }

    public PaperbaseContractsDbContext(DbContextOptions<PaperbaseContractsDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ConfigureContracts();
    }
}
