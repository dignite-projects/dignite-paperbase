using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql.EntityFrameworkCore.PostgreSQL;

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
            .UseNpgsql(configuration.GetConnectionString("Default"),
                o => o.UseVector());

        return new PaperbaseHostDbContext(builder.Options);
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