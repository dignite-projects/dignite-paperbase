using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Dignite.Abp.FlexFields;
using Dignite.Vault.Extract.FlexFields.Localization;

namespace Dignite.Vault.Extract.FlexFields.Tags;

/// <summary>
/// A multi-valued field whose vocabulary is <b>open</b>: party names, keywords, tags - values read out
/// of the document rather than chosen from a list the administrator configured in advance.
/// <para>
/// This exists because the kernel's <c>SelectFieldType</c> deliberately cannot serve this case. Select
/// is the closed-vocabulary multi-value type: its <c>Validate</c> rejects any value outside its
/// configured <c>Options</c>, which is exactly what makes it useful (its option list also becomes the
/// LLM extraction schema's <c>enum</c>) and exactly what makes it unable to carry values discovered in
/// the document. The two are complements, not alternatives - closed vocabulary goes to Select, open
/// vocabulary comes here (resolution 2 on #559).
/// </para>
/// <para>
/// This is Vault Extract's replacement for v2's <c>FieldDefinition.AllowMultiple</c> flag on a Text
/// field. That flag was never orthogonal to the field type - v2's own <c>ValidateMultiValue</c>
/// loud-failed for every type except Text - so v3 makes "is this multi-valued" a property of the type
/// itself and drops the flag.
/// </para>
/// <para>
/// <b>Registration key is persisted data.</b> <see cref="ControlName"/> is stored in every field
/// definition bound to this type; renaming it orphans them all.
/// </para>
/// </summary>
public class TagsFieldType : FieldTypeBase
{
    public const string ControlName = "Tags";

    public TagsFieldType()
    {
        LocalizationResource = typeof(VaultExtractFlexFieldsResource);
    }

    public override string Name => ControlName;

    public override string DisplayName => L["FieldType:Tags"];

    /// <summary>
    /// Indexable as strings: each value is decomposed into its own index row, so "documents where this
    /// field contains X" is a plain index seek.
    /// </summary>
    public override FlexFieldValueType? IndexValueType => FlexFieldValueType.String;

    public override IReadOnlyList<ValidationResult> Validate(FieldValidationArgs args)
    {
        var configuration = new TagsConfiguration(args.Field.Configuration);
        var errors = new List<ValidationResult>();
        var values = ReadStringList(args.Field.Value);

        if (values.Count == 0)
        {
            if (args.Field.Required)
            {
                errors.Add(new ValidationResult(
                    L["Validate:Required", args.Field.DisplayName],
                    new[] { args.Field.Name }));
            }

            return errors;
        }

        // Hard cap on how many values one document may carry for this field. The values come from an
        // LLM reading an untrusted document, so this is the write-path equivalent of a bounded result
        // set - see TagsConfiguration.MaxCount. The group is rejected as a whole rather than silently
        // truncated: dropping the tail would present a partial extraction as a complete one.
        if (values.Count > configuration.MaxCount)
        {
            errors.Add(new ValidationResult(
                L["Validate:Tags:CountExceedsLimit", args.Field.DisplayName, configuration.MaxCount],
                new[] { args.Field.Name }));
        }

        // Reported once for the field rather than once per offending value: the message names the
        // field and the limit, and a per-value list would be unbounded in exactly the case
        // (a runaway model response) this is guarding against.
        if (values.Any(value => value.Length > configuration.MaxLength))
        {
            errors.Add(new ValidationResult(
                L["Validate:Tags:LengthExceedsLimit", args.Field.DisplayName, configuration.MaxLength],
                new[] { args.Field.Name }));
        }

        return errors;
    }

    public override FieldConfigurationBase GetConfiguration(FieldConfigurationDictionary fieldConfiguration)
    {
        return new TagsConfiguration(fieldConfiguration);
    }

    /// <summary>
    /// One searchable value per element, so a filter matches a document that carries the value
    /// anywhere in its list. Mirrors <c>SelectFieldType</c>'s override; the base implementation would
    /// yield the whole list as a single opaque value.
    /// </summary>
    public override IEnumerable<object> GetSearchableValues(FlexFieldValue field)
    {
        if (!field.Searchable || field.Value == null)
        {
            yield break;
        }

        foreach (var value in ReadStringList(field.Value))
        {
            yield return value;
        }
    }
}
