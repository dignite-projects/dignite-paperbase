using Dignite.Paperbase.Contracts.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Host.Data;

public class PaperbaseHostDbSchemaMigrator : ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;

    public PaperbaseHostDbSchemaMigrator(
        IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task MigrateAsync()
    {
        /* We intentionally resolve DbContexts from IServiceProvider
         * to properly get the connection string of the current tenant
         * in the current scope.
         */

        await _serviceProvider
            .GetRequiredService<PaperbaseHostDbContext>()
            .Database
            .MigrateAsync();

        await _serviceProvider
            .GetRequiredService<ContractsDbContext>()
            .Database
            .MigrateAsync();
    }
}
