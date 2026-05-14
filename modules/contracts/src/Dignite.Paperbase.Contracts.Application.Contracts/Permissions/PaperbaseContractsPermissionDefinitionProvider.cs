using Dignite.Paperbase.Contracts.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace Dignite.Paperbase.Contracts.Permissions;

public class PaperbaseContractsPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var myGroup = context.AddGroup(PaperbaseContractsPermissions.GroupName, L("Permission:Contracts"));
        var contractsPermission = myGroup.AddPermission(
            PaperbaseContractsPermissions.Contracts.Default,
            L("Permission:Contracts.Contracts"));

        contractsPermission.AddChild(
            PaperbaseContractsPermissions.Contracts.Update,
            L("Permission:Contracts.Contracts.Update"));

        contractsPermission.AddChild(
            PaperbaseContractsPermissions.Contracts.Confirm,
            L("Permission:Contracts.Contracts.Confirm"));

        contractsPermission.AddChild(
            PaperbaseContractsPermissions.Contracts.Export,
            L("Permission:Contracts.Contracts.Export"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<PaperbaseContractsResource>(name);
    }
}
