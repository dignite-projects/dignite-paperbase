using Volo.Abp.Reflection;

namespace Dignite.Paperbase.Permissions;

public class PaperbasePermissions
{
    public const string GroupName = "Paperbase";

    public static string[] GetAll()
    {
        return ReflectionHelper.GetPublicConstantsRecursively(typeof(PaperbasePermissions));
    }
}
