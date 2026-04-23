using Dignite.Paperbase.Contracts.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace Dignite.Paperbase.Contracts.Permissions;

public class ContractsPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var myGroup = context.AddGroup(ContractsPermissions.GroupName, L("Permission:Contracts"));
        var contractsPermission = myGroup.AddPermission(
            ContractsPermissions.Contracts.Default,
            L("Permission:Contracts.Contracts"));

        contractsPermission.AddChild(
            ContractsPermissions.Contracts.Update,
            L("Permission:Contracts.Contracts.Update"));

        contractsPermission.AddChild(
            ContractsPermissions.Contracts.Confirm,
            L("Permission:Contracts.Contracts.Confirm"));

        contractsPermission.AddChild(
            ContractsPermissions.Contracts.Export,
            L("Permission:Contracts.Contracts.Export"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<ContractsResource>(name);
    }
}
