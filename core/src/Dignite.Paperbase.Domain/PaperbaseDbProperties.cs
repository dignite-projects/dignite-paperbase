namespace Dignite.Paperbase;

public static class PaperbaseDbProperties
{
    public static string DbTablePrefix { get; set; } = "Paperbase";

    public static string? DbSchema { get; set; } = null;

    public const string ConnectionStringName = "Paperbase";
}
