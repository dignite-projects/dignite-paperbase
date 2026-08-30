using System.ComponentModel.DataAnnotations;
using Dignite.Abp.FlexFields;
using Volo.Abp.Validation;

namespace Dignite.Vault.Extract.Documents.Fields;

public class UpdateFieldDefinitionDto
{
    /// <summary>
    /// Field machine name. Renames are allowed since #207; the regex allowlist is enforced by the entity,
    /// and same-layer same-type uniqueness by the AppService.
    /// <para>
    /// Costlier than it was: under v2 the value rows keyed on the immutable field id, so a rename touched
    /// nothing else. In v3 the name <b>is</b> the key each document stores this field's value under, so the
    /// AppService rewrites every bag through <c>IFlexFieldValueMigrator</c> as part of the same update.
    /// </para>
    /// </summary>
    [Required]
    [DynamicStringLength(typeof(FieldDefinitionConsts), nameof(FieldDefinitionConsts.MaxNameLength))]
    public string Name { get; set; } = default!;

    [Required]
    [DynamicStringLength(typeof(FieldDefinitionConsts), nameof(FieldDefinitionConsts.MaxDisplayNameLength))]
    public string DisplayName { get; set; } = default!;

    /// <summary>Extraction instruction, <b>optional</b>. When blank, the LLM infers what to extract from <see cref="Name"/> and the field type alone. #447: length uncapped — admin-authored configuration (may be long, structured Markdown).</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Registration key of the field type. Changing it on a field that already holds values is rejected:
    /// the stored values were validated against the old type and would neither re-validate nor re-index
    /// under the new one.
    /// </summary>
    [Required]
    public string FieldTypeName { get; set; } = default!;

    public FieldConfigurationDictionary? Configuration { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsRequired { get; set; }

    /// <summary>Whether this field's values are decomposed into the query index, and so filterable. Switching it on re-derives this field's index rows; switching it off drops them.</summary>
    public bool IsSearchable { get; set; } = true;

    /// <summary>Whether this field is part of the type's duplicate-detection unique key (#411). The normalized values of all unique-key fields are hashed into the document's fingerprint to flag duplicate re-uploads.</summary>
    public bool IsUniqueKey { get; set; }
}
