using Dignite.Paperbase.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace Dignite.Paperbase.Permissions;

public class PaperbasePermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(PaperbasePermissions.GroupName, L("Permission:Paperbase"));

        var documents = group.AddPermission(PaperbasePermissions.Documents.Default, L("Permission:Documents"));
        documents.AddChild(PaperbasePermissions.Documents.Upload, L("Permission:Documents.Upload"));
        documents.AddChild(PaperbasePermissions.Documents.Delete, L("Permission:Documents.Delete"));

        var relations = group.AddPermission(PaperbasePermissions.DocumentRelations.Default, L("Permission:DocumentRelations"));
        relations.AddChild(PaperbasePermissions.DocumentRelations.Create, L("Permission:DocumentRelations.Create"));
        relations.AddChild(PaperbasePermissions.DocumentRelations.Delete, L("Permission:DocumentRelations.Delete"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<PaperbaseResource>(name);
    }
}
