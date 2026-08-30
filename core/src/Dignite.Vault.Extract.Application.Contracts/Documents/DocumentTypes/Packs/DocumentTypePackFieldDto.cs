using System.ComponentModel.DataAnnotations;
using Dignite.Abp.FlexFields;
using Dignite.Vault.Extract.Documents.Fields;
using Volo.Abp.Validation;

namespace Dignite.Vault.Extract.Documents.DocumentTypes.Packs;

/// <summary>
/// One field definition inside a <see cref="DocumentTypePackDto"/>. Mirrors the mutable shape of a
/// <c>Field</c> minus identity/layer (resolved on import from the caller's layer + owning type).
/// Matched on import by (owning type, <see cref="Name"/>).
/// <para>
/// Field architecture v3 (#559) moved this to pack schema version 2. Version 1 packs — the ones already
/// exported and sitting in people's repositories — are still imported: their <see cref="DataType"/> /
/// <see cref="AllowMultiple"/> / <see cref="Prompt"/> are upconverted to <see cref="FieldTypeName"/> +
/// <see cref="Configuration"/> + <see cref="Description"/> on the way in, by the same mapping that
/// migrated the live rows. Export only ever emits version 2, and never populates the legacy members.
/// </para>
/// </summary>
public class DocumentTypePackFieldDto
{
    [Required]
    [RegularExpression(FieldDefinitionConsts.NamePattern)]
    [DynamicStringLength(typeof(FieldDefinitionConsts), nameof(FieldDefinitionConsts.MaxNameLength))]
    public string Name { get; set; } = default!;

    [Required]
    [DynamicStringLength(typeof(FieldDefinitionConsts), nameof(FieldDefinitionConsts.MaxDisplayNameLength))]
    public string DisplayName { get; set; } = default!;

    /// <summary>The LLM extraction instruction (v2 of the pack schema; v1 called it <see cref="Prompt"/>).</summary>
    public string? Description { get; set; }

    /// <summary>Registration key of the field type. Left null by a version-1 pack, which carries <see cref="DataType"/> instead.</summary>
    public string? FieldTypeName { get; set; }

    public FieldConfigurationDictionary? Configuration { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsRequired { get; set; }

    /// <summary>Defaults to true so a version-1 pack, which has no such notion, imports as v2 did: every field indexed.</summary>
    public bool IsSearchable { get; set; } = true;

    public bool IsUniqueKey { get; set; }

    // ── pack schema version 1 only, ignored when the pack declares version 2 ──────────────────────────

    /// <summary>Version-1 extraction instruction. Superseded by <see cref="Description"/>.</summary>
    public string? Prompt { get; set; }

    /// <summary>Version-1 data type. Superseded by <see cref="FieldTypeName"/> + <see cref="Configuration"/>.</summary>
    public FieldDataType? DataType { get; set; }

    /// <summary>Version-1 multi-value flag. Superseded by the field type itself (a multi-valued v1 text field becomes Tags).</summary>
    public bool AllowMultiple { get; set; }
}
