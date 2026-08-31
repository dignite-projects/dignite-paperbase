using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Dignite.Vault.Extract.Documents.Fields.Migration;
using Volo.Abp;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;

namespace Dignite.Vault.Extract.Host.Data;

public class VaultExtractHostDbMigrationService : ITransientDependency
{
    public ILogger<VaultExtractHostDbMigrationService> Logger { get; set; }

    private readonly IDataSeeder _dataSeeder;
    private readonly VaultExtractHostDbSchemaMigrator _dbSchemaMigrator;
    private readonly ICurrentTenant _currentTenant;
    private readonly FieldArchitectureV3Migrator _fieldArchitectureV3Migrator;
    private readonly ITenantRepository _tenantRepository;

    public VaultExtractHostDbMigrationService(
        IDataSeeder dataSeeder,
        VaultExtractHostDbSchemaMigrator dbSchemaMigrator,
        ICurrentTenant currentTenant,
        FieldArchitectureV3Migrator fieldArchitectureV3Migrator,
        ITenantRepository tenantRepository)
    {
        _dataSeeder = dataSeeder;
        _dbSchemaMigrator = dbSchemaMigrator;
        _currentTenant = currentTenant;
        _fieldArchitectureV3Migrator = fieldArchitectureV3Migrator;
        _tenantRepository = tenantRepository;

        Logger = NullLogger<VaultExtractHostDbMigrationService>.Instance;
    }

    public async Task MigrateAsync()
    {
        var initialMigrationAdded = AddInitialMigrationIfNotExist();

        if (initialMigrationAdded)
        {
            return;
        }

        Logger.LogInformation("Started database migrations...");

        await MigrateDatabaseSchemaAsync();
        await SeedDataAsync();

        // Field architecture v2 -> v3 (#561). Part of "bring the database up to date", not of startup:
        // this runs only under --migrate-database. Idempotent, so it is safe to leave wired -- a repeat
        // run migrates nothing and re-derives the query index, which is deliberate: skipping the rebuild
        // when nothing moved would strand a run that had failed partway through it.
        //
        // The migrator itself lives in the module rather than here, because the vault Pro edition hosts
        // the same tables in its own DbContext and has to run exactly this code from its own migration
        // path - a host-local implementation would have migrated one deployment and silently left the
        // other on the v2 layout.
        //
        // Every layer, not just the host one. The migrator handles exactly one layer per call because it
        // will not pierce the ambient tenant filter, so iterating the layers is the caller's job -- and
        // running the dev database proved this is not theoretical: it holds 28 tenant-layer field
        // definitions and 2 tenant-layer documents that a host-only pass silently left on the v2 layout.
        await MigrateFieldArchitectureAsync();

        Logger.LogInformation($"Successfully completed host database migrations.");
        Logger.LogInformation("You can safely end this process...");
    }

    private async Task MigrateDatabaseSchemaAsync()
    {
        await _dbSchemaMigrator.MigrateAsync();
    }

    private async Task MigrateFieldArchitectureAsync()
    {
        await MigrateFieldArchitectureLayerAsync(null);

        // Tenants are read in the host layer, then each layer is migrated inside its own
        // ICurrentTenant.Change scope so the migrator's queries see that layer and only that layer.
        var tenants = await _tenantRepository.GetListAsync();
        foreach (var tenant in tenants)
        {
            using (_currentTenant.Change(tenant.Id, tenant.Name))
            {
                await MigrateFieldArchitectureLayerAsync(tenant.Id);
            }
        }
    }

    private async Task MigrateFieldArchitectureLayerAsync(Guid? expectedTenantId)
    {
        var result = await _fieldArchitectureV3Migrator.MigrateAsync();

        // The migrator reports the layer it actually handled, which is worth checking rather than
        // trusting: a mismatch would mean the ambient tenant scope did not take, and one layer's data
        // would have been migrated twice while another was skipped entirely.
        if (result.TenantId != expectedTenantId)
        {
            throw new AbpException(
                $"Field architecture migration ran against layer {result.TenantId?.ToString() ?? "host"} " +
                $"but layer {expectedTenantId?.ToString() ?? "host"} was requested.");
        }

        Logger.LogInformation(
            "Field architecture v3 migration ({Layer}): {Definitions} field definitions, {Documents} documents, {Values} field values.",
            result.TenantId?.ToString() ?? "host",
            result.DefinitionsMigrated,
            result.DocumentsMigrated,
            result.FieldValuesMigrated);

        // #561 step 6, the cutover's last step and deliberately separate from MigrateAsync: v3's fingerprint
        // is hashed from the value bag, v2's from value rows in Order sequence, and duplicate detection
        // compares fingerprints by string equality. Running this while the v2 extraction pipeline was still
        // live would have re-split the corpus on the next re-extraction, so the migrator leaves it to the
        // caller and its own doc comment says to run it "once v3 owns extraction — never before".
        //
        // That condition now holds: nothing calls the v2 FieldFingerprintCalculator or Document.SetFields on
        // any live path, and FieldExtractionService writes v3-shaped fingerprints exclusively. So the safe
        // moment is here, immediately after this layer's bags exist, rather than in a runbook step nobody
        // is reminded to run — leaving it unwired is what would silently split the corpus, since every
        // document keeps its v2 fingerprint until re-extracted and stops matching the ones that were.
        //
        // Idempotent, like MigrateAsync: recomputing an already-v3-shaped fingerprint reproduces it.
        var recomputed = await _fieldArchitectureV3Migrator.RecomputeFingerprintsAsync();

        Logger.LogInformation(
            "Field architecture v3 fingerprint recomputation ({Layer}): {Recomputed} documents.",
            result.TenantId?.ToString() ?? "host",
            recomputed);
    }

    private async Task SeedDataAsync()
    {
        await _dataSeeder.SeedAsync(new DataSeedContext()
            .WithProperty(IdentityDataSeedContributor.AdminEmailPropertyName, VaultExtractHostConsts.AdminEmailDefaultValue)
            .WithProperty(IdentityDataSeedContributor.AdminPasswordPropertyName, VaultExtractHostConsts.AdminPasswordDefaultValue)
        );
    }

    private bool AddInitialMigrationIfNotExist()
    {
        try
        {
            if (!DbMigrationsProjectExists())
            {
                return false;
            }
        }
        catch (Exception)
        {
            return false;
        }

        try
        {
            if (!MigrationsFolderExists())
            {
                AddInitialMigration();
                return true;
            }
            else
            {
                return false;
            }
        }
        catch (Exception e)
        {
            Logger.LogWarning("Couldn't determinate if any migrations exist : " + e.Message);
            return false;
        }
    }

    private bool DbMigrationsProjectExists()
    {
        return Directory.Exists(GetEntityFrameworkCoreProjectFolderPath());
    }

    private bool MigrationsFolderExists()
    {
        var dbMigrationsProjectFolder = GetEntityFrameworkCoreProjectFolderPath();

        return Directory.Exists(Path.Combine(dbMigrationsProjectFolder, "Migrations"));
    }

    private void AddInitialMigration()
    {
        Logger.LogInformation("Creating initial migration...");

        string argumentPrefix;
        string fileName;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            argumentPrefix = "-c";
            fileName = "/bin/bash";
        }
        else
        {
            argumentPrefix = "/C";
            fileName = "cmd.exe";
        }

        var procStartInfo = new ProcessStartInfo(fileName,
            $"{argumentPrefix} \"abp create-migration-and-run-migrator \"{GetEntityFrameworkCoreProjectFolderPath()}\" --nolayers\""
        );

        try
        {
            Process.Start(procStartInfo);
        }
        catch (Exception)
        {
            throw new Exception("Couldn't run ABP CLI...");
        }
    }

    private string GetEntityFrameworkCoreProjectFolderPath()
    {
        var slnDirectoryPath = GetSolutionDirectoryPath();

        if (slnDirectoryPath == null)
        {
            throw new Exception("Solution folder not found!");
        }

        return Path.Combine(slnDirectoryPath, "src");
    }

    private string GetSolutionDirectoryPath()
    {
        var currentDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (Directory.GetParent(currentDirectory.FullName) != null)
        {
            currentDirectory = Directory.GetParent(currentDirectory.FullName);

            if (Directory.GetFiles(currentDirectory.FullName).FirstOrDefault(f => f.EndsWith(".sln") || f.EndsWith(".slnx")) != null)
            {
                return currentDirectory.FullName;
            }
        }

        return null;
    }
}
