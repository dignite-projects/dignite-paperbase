using Volo.Abp.Reflection;

namespace Dignite.Paperbase.Contracts.Permissions;

public class ContractsPermissions
{
    public const string GroupName = "Contracts";

    public static class Contracts
    {
        public const string Default = GroupName + ".Contracts";
        public const string Update = Default + ".Update";
        public const string Confirm = Default + ".Confirm";
        public const string Export = Default + ".Export";
    }

    public static string[] GetAll()
    {
        return ReflectionHelper.GetPublicConstantsRecursively(typeof(ContractsPermissions));
    }
}
