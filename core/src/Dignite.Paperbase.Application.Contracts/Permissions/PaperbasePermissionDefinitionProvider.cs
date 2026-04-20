using Dignite.Paperbase.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace Dignite.Paperbase.Permissions;

public class PaperbasePermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var myGroup = context.AddGroup(PaperbasePermissions.GroupName, L("Permission:Paperbase"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<PaperbaseResource>(name);
    }
}
