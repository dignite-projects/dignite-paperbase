using Dignite.Paperbase.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Permissions;

public class PaperbasePermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var myGroup = context.AddGroup(PaperbasePermissions.GroupName);



        //Define your own permissions here. Example:
        //myGroup.AddPermission(PaperbasePermissions.MyPermission1, L("Permission:MyPermission1"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<PaperbaseResource>(name);
    }
}
