using Volo.Abp.Reflection;

namespace Dignite.Paperbase.Permissions;

public class PaperbasePermissions
{
    public const string GroupName = "Paperbase";

    public static class Documents
    {
        public const string Default = GroupName + ".Documents";
        public const string Upload = Default + ".Upload";
        public const string Delete = Default + ".Delete";
        public const string Export = Default + ".Export";
        public const string ConfirmClassification = Default + ".ConfirmClassification";
        public const string Ask = Default + ".Ask";
    }

    public static class DocumentRelations
    {
        public const string Default = GroupName + ".DocumentRelations";
        public const string Create = Default + ".Create";
        public const string Delete = Default + ".Delete";
        public const string ConfirmRelation = Default + ".ConfirmRelation";
    }

    public static string[] GetAll()
    {
        return ReflectionHelper.GetPublicConstantsRecursively(typeof(PaperbasePermissions));
    }
}
