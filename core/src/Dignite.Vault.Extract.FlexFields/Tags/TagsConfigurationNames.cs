namespace Dignite.Vault.Extract.FlexFields.Tags;

/// <summary>
/// Configuration keys for <see cref="TagsFieldType"/>.
/// <para>
/// <b>These strings are persisted data.</b> They are stored inside every field definition's
/// configuration dictionary, so renaming one orphans the value already written under the old key -
/// a wire-format break, not a refactor, and nothing in the build catches it. Same rule as the
/// FlexFields kernel's own configuration names.
/// </para>
/// </summary>
public static class TagsConfigurationNames
{
    public const string MaxCount = "Tags.MaxCount";

    public const string MaxLength = "Tags.MaxLength";

    public const string Placeholder = "Tags.Placeholder";
}
