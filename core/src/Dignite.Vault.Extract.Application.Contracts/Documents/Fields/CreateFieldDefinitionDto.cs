using System;
using System.ComponentModel.DataAnnotations;
using Dignite.Abp.FlexFields;
using Volo.Abp.Validation;

namespace Dignite.Vault.Extract.Documents.Fields;

public class CreateFieldDefinitionDto
{
    /// <summary>Immutable parent document type id (#207: creates the Field.DocumentTypeId FK and binds by id rather than renameable TypeCode).</summary>
    [Required]
    public Guid DocumentTypeId { get; set; }

    [Required]
    [DynamicStringLength(typeof(FieldDefinitionConsts), nameof(FieldDefinitionConsts.MaxNameLength))]
    public string Name { get; set; } = default!;

    [Required]
    [DynamicStringLength(typeof(FieldDefinitionConsts), nameof(FieldDefinitionConsts.MaxDisplayNameLength))]
    public string DisplayName { get; set; } = default!;

    /// <summary>Extraction instruction, <b>optional</b>. When blank, the LLM infers what to extract from <see cref="Name"/> and the field type alone. #447: length uncapped — admin-authored configuration (may be long, structured Markdown).</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Registration key of the field type. Rejected when no type is registered under it: an unknown key
    /// would produce a field that nothing can read, validate or index.
    /// </summary>
    [Required]
    public string FieldTypeName { get; set; } = DefaultFieldTypeName;

    public FieldConfigurationDictionary? Configuration { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsRequired { get; set; }

    /// <summary>Whether this field's values are decomposed into the query index, and so filterable. Defaults to true, matching every field migrated from v2.</summary>
    public bool IsSearchable { get; set; } = true;

    /// <summary>Whether this field is part of the type's duplicate-detection unique key (#411). The normalized values of all unique-key fields are hashed into the document's fingerprint to flag duplicate re-uploads.</summary>
    public bool IsUniqueKey { get; set; }

    // The type a field takes when the caller states none, the same default the v2 DataType enum carried.
    // A literal rather than TextFieldType.ControlName: Application.Contracts must not depend on the field-type
    // implementations to name a default, and the key is a frozen registration string either way.
    private const string DefaultFieldTypeName = "Text";
}
