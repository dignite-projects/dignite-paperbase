namespace Dignite.Paperbase.Contracts;

public static class PaperbaseContractsDbProperties
{
    public static string DbTablePrefix { get; set; } = "Paperbase";

    public static string? DbSchema { get; set; } = null;

    public const string ConnectionStringName = "PaperbaseContracts";
}
