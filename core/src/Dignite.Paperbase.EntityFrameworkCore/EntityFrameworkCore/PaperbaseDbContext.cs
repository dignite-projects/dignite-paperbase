using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace Dignite.Paperbase.EntityFrameworkCore;

[ConnectionStringName(PaperbaseDbProperties.ConnectionStringName)]
public class PaperbaseDbContext : AbpDbContext<PaperbaseDbContext>, IPaperbaseDbContext
{
    /* Add DbSet for each Aggregate Root here. Example:
     * public DbSet<Question> Questions { get; set; }
     */

    public PaperbaseDbContext(DbContextOptions<PaperbaseDbContext> options)
        : base(options)
    {

    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ConfigurePaperbase();
    }
}
