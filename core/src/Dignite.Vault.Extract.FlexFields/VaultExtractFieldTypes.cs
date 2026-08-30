using System;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.Select;
using Dignite.Vault.Extract.FlexFields.Tags;

namespace Dignite.Vault.Extract.FlexFields;

/// <summary>
/// Questions about a field type that are answered by its registration key plus configuration, and asked
/// in more than one layer.
/// <para>
/// This exists because "does this field hold one value or many" has no home in the kernel: nothing on
/// <c>IFieldType</c> says so, and for <c>Select</c> the answer lives in the field's own configuration
/// rather than in its type at all. Every caller would otherwise re-derive the same two-branch test, and
/// getting only the first branch right is an easy mistake to make — the Tags branch is the obvious one.
/// </para>
/// </summary>
public static class VaultExtractFieldTypes
{
    /// <summary>
    /// Whether values of this field are a list rather than a scalar. Decides array-vs-scalar on the egress
    /// (<c>ExtractedFields</c>, the MCP field schema, an export cell), so a caller getting it wrong shows
    /// up as a client parsing the wrong JSON shape rather than as an error.
    /// <para>
    /// Two ways to be multi-valued, and both must be checked: <c>Tags</c>, Vault Extract's own
    /// open-vocabulary type, which is always a list; and the kernel's <c>Select</c>, which is a list only
    /// when its own configuration says <c>Multiple</c>. A name-only test silently mis-describes every
    /// multi-Select field.
    /// </para>
    /// </summary>
    public static bool IsMultiValue(string fieldTypeName, FieldConfigurationDictionary? configuration = null)
    {
        if (string.Equals(fieldTypeName, TagsFieldType.ControlName, StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(fieldTypeName, SelectFieldType.ControlName, StringComparison.Ordinal)
               && configuration != null
               && new SelectConfiguration(configuration).Multiple;
    }
}
