using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Dignite.Paperbase.Data;

public class PaperbaseDbContextFactory : IDesignTimeDbContextFactory<PaperbaseDbContext>
{
    public PaperbaseDbContext CreateDbContext(string[] args)
    {
        PaperbaseGlobalFeatureConfigurator.Configure();
        PaperbaseModuleExtensionConfigurator.Configure();

        PaperbaseEfCoreEntityExtensionMappings.Configure();
        var configuration = BuildConfiguration();

        var builder = new DbContextOptionsBuilder<PaperbaseDbContext>()
            .UseSqlServer(configuration.GetConnectionString("Default"));

        return new PaperbaseDbContext(builder.Options);
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables();

        return builder.Build();
    }
}