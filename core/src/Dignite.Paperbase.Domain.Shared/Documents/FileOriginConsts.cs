namespace Dignite.Paperbase.Documents;

public static class FileOriginConsts
{
    public static int MaxUploadedByUserNameLength { get; set; } = 256;

    public static int MaxOriginalFileNameLength { get; set; } = 512;

    public static int MaxContentTypeLength { get; set; } = 256;
}
