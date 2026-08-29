using Dignite.Abp.FlexFields;

namespace Dignite.Vault.Extract.FlexFields.Tags;

/// <summary>
/// Strongly-typed view over a <see cref="TagsFieldType"/> field's configuration.
/// </summary>
public class TagsConfiguration : FieldConfigurationBase
{
    /// <summary>
    /// Maximum number of values one document may carry for this field. Default 100, carried over from
    /// the v2 <c>DocumentExtractedFieldConsts.MaxMultiValueCount</c>.
    /// <para>
    /// This is a security guardrail, not a UI nicety: the values are produced by an LLM reading an
    /// untrusted document, and a malicious document can induce the model to emit a huge array that
    /// inflates one document's stored values and index rows element by element. Enforced by
    /// <see cref="TagsFieldType.Validate"/>; the extraction schema should also carry it as a
    /// <c>maxItems</c> hint, the same "schema hint plus validator fallback" pairing v2 used.
    /// </para>
    /// </summary>
    public int MaxCount {
        get => ConfigurationDictionary.GetConfiguration(TagsConfigurationNames.MaxCount, 100);
        set => ConfigurationDictionary.SetConfiguration(TagsConfigurationNames.MaxCount, value);
    }

    /// <summary>
    /// Maximum length of a single value. Default 256, carried over from the v2
    /// <c>DocumentExtractedFieldConsts.MaxTextValueLength</c>.
    /// <para>
    /// Keep this at or below the query index's own string-slot limit
    /// (<c>FlexFieldConsts.MaxStringValueLength</c>, 512 by default): these values are indexable, so a
    /// value longer than the slot could not round-trip through the index it is searched by.
    /// </para>
    /// </summary>
    public int MaxLength {
        get => ConfigurationDictionary.GetConfiguration(TagsConfigurationNames.MaxLength, 256);
        set => ConfigurationDictionary.SetConfiguration(TagsConfigurationNames.MaxLength, value);
    }

    public string? Placeholder {
        get => ConfigurationDictionary.GetConfiguration<string?>(TagsConfigurationNames.Placeholder, null);
        set => ConfigurationDictionary.SetConfiguration(TagsConfigurationNames.Placeholder, value);
    }

    public TagsConfiguration(FieldConfigurationDictionary fieldConfiguration)
        : base(fieldConfiguration)
    {
    }

    public TagsConfiguration()
        : base()
    {
    }
}
