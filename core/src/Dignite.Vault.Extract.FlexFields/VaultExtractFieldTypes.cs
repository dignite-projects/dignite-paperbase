using System;
using System.Collections.Generic;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.Boolean;
using Dignite.Abp.FlexFields.CKEditor;
using Dignite.Abp.FlexFields.Date;
using Dignite.Abp.FlexFields.Number;
using Dignite.Abp.FlexFields.Select;
using Dignite.Abp.FlexFields.Text;
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
    /// Field types Vault Extract actually wired up — the #559 decision list, plus its own <c>Tags</c>.
    /// <para>
    /// Not the same question as "what does <c>IFieldTypeResolver.GetAll()</c> return". The kernel's own
    /// domain module registers <c>Tree</c> unconditionally, as one of its built-ins, whether or not a
    /// downstream ever asked for it — and Vault Extract never did: <c>FlexFieldValueReader</c>,
    /// <c>FlexFieldValueSchemaBuilder</c> and <c>FlexFieldValueJsonWriter</c> have no branch for it. A
    /// field created with <c>FieldTypeName: "Tree"</c> would pass an "is this registered?" check and then
    /// be unreadable, unwritable and absent from the extraction schema — accepted at the door and broken
    /// at every use. <c>EnsureFieldTypeRegistered</c> and <c>GetFieldTypesAsync</c> both filter through
    /// this set instead of trusting the resolver's full list, so a kernel release that adds an eighth
    /// built-in cannot silently widen what Vault Extract accepts.
    /// </para>
    /// </summary>
    public static readonly IReadOnlySet<string> SupportedFieldTypeNames = new HashSet<string>(StringComparer.Ordinal)
    {
        TextFieldType.ControlName,
        NumberFieldType.ControlName,
        BooleanFieldType.ControlName,
        DateTimeFieldType.ControlName,
        SelectFieldType.ControlName,
        CKEditorFieldType.ControlName,
        TagsFieldType.ControlName,
    };

    /// <summary>Whether <paramref name="fieldTypeName"/> is one Vault Extract actually supports — see <see cref="SupportedFieldTypeNames"/>.</summary>
    public static bool IsSupported(string fieldTypeName) => SupportedFieldTypeNames.Contains(fieldTypeName);

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
