using System.Threading.Tasks;
using Dignite.Paperbase.Contracts.Permissions;
using Dignite.Paperbase.Permissions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;

namespace Dignite.Paperbase.Host.Data;

public class PaperbaseHostRoleDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly IdentityRoleManager _roleManager;
    private readonly IPermissionManager _permissionManager;

    public PaperbaseHostRoleDataSeedContributor(
        IIdentityRoleRepository roleRepository,
        IdentityRoleManager roleManager,
        IPermissionManager permissionManager)
    {
        _roleRepository = roleRepository;
        _roleManager = roleManager;
        _permissionManager = permissionManager;
    }

    public virtual async Task SeedAsync(DataSeedContext context)
    {
        await SeedRoleAsync("ContractManager", new[]
        {
            PaperbasePermissions.Documents.Default,
            PaperbasePermissions.Documents.Upload,
            PaperbasePermissions.Documents.Export,
            ContractsPermissions.Contracts.Default,
            ContractsPermissions.Contracts.Update,
            ContractsPermissions.Contracts.Confirm,
            ContractsPermissions.Contracts.Export,
        });

        await SeedRoleAsync("Viewer", new[]
        {
            PaperbasePermissions.Documents.Default,
            ContractsPermissions.Contracts.Default,
        });
    }

    private async Task SeedRoleAsync(string roleName, string[] permissions)
    {
        var role = await _roleRepository.FindByNormalizedNameAsync(roleName.ToUpperInvariant());
        if (role == null)
        {
            await _roleManager.CreateAsync(new IdentityRole(System.Guid.NewGuid(), roleName));
            role = await _roleRepository.FindByNormalizedNameAsync(roleName.ToUpperInvariant());
        }

        foreach (var permission in permissions)
        {
            await _permissionManager.SetForRoleAsync(roleName, permission, true);
        }
    }
}
