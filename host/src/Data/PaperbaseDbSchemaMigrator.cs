using Volo.Abp.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Dignite.Paperbase.Data;

public class PaperbaseDbSchemaMigrator : ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public PaperbaseDbSchemaMigrator(
        IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task MigrateAsync()
    {
        
        /* We intentionally resolving the PaperbaseDbContext
         * from IServiceProvider (instead of directly injecting it)
         * to properly get the connection string of the current tenant in the
         * current scope.
         */

        await _serviceProvider
            .GetRequiredService<PaperbaseDbContext>()
            .Database
            .MigrateAsync();

    }
}
