using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Dignite.Paperbase.Host.Data;

public class PaperbaseHostDbContextFactory : IDesignTimeDbContextFactory<PaperbaseHostDbContext>
{
    public PaperbaseHostDbContext CreateDbContext(string[] args)
    {
        PaperbaseHostGlobalFeatureConfigurator.Configure();
        PaperbaseHostModuleExtensionConfigurator.Configure();

        PaperbaseHostEfCoreEntityExtensionMappings.Configure();
        var configuration = BuildConfiguration();

        var builder = new DbContextOptionsBuilder<PaperbaseHostDbContext>()
            .UseSqlServer(configuration.GetConnectionString("Default"));

        return new PaperbaseHostDbContext(builder.Options);
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables();

        return builder.Build();
    }
}
