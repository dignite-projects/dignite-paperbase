using System;
using Dignite.Abp.FlexFields;
using Volo.Abp.Application.Dtos;

namespace Dignite.Vault.Extract.Documents.Fields;

/// <summary>
/// A type-bound field definition on the egress. This names the concept, not the entity: the entity behind
/// it is now <see cref="Field"/> (#559), but the REST route, the DTO and the app-service contract keep
/// their names so the break is confined to the members that genuinely changed shape.
/// </summary>
public class FieldDefinitionDto : EntityDto<Guid>
{
    public Guid? TenantId { get; set; }

    /// <summary>Immutable id of the owning document type (#207: internal stable handle; TypeCode can be renamed by admins and is not used as a reference key).</summary>
    public Guid DocumentTypeId { get; set; }
    public string Name { get; set; } = default!;
    public string DisplayName { get; set; } = default!;

    /// <summary>
    /// The LLM extraction instruction — what v2 called <c>Prompt</c>. Renamed to match the FlexFields
    /// contract member it maps to (#559 resolution 4).
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Registration key of the field type — <c>Text</c>, <c>Number</c>, <c>DateTime</c>, <c>Boolean</c>,
    /// <c>Select</c>, <c>CKEditor</c> (long text) or <c>Tags</c> (multi-valued). Replaces the v2
    /// <c>DataType</c> enum together with its <c>AllowMultiple</c> flag: what a field accepts and whether
    /// it holds one value or many are both properties of the type in v3, not two independent switches.
    /// </summary>
    public string FieldTypeName { get; set; } = default!;

    /// <summary>Type-specific configuration, interpreted by the field type (e.g. Select options, DateTime input mode).</summary>
    public FieldConfigurationDictionary Configuration { get; set; } = new();

    public int DisplayOrder { get; set; }
    public bool IsRequired { get; set; }

    /// <summary>
    /// Whether this field's values are decomposed into the query index, and so filterable. New in v3:
    /// under v2 every extracted value was indexed with no opt-out, so migrated fields carry <c>true</c>.
    /// A field whose type is not indexable at all (long text) yields nothing either way.
    /// </summary>
    public bool IsSearchable { get; set; }

    /// <summary>Whether this field is part of the type's duplicate-detection unique key (#411). The normalized values of all unique-key fields are hashed into the document's fingerprint to flag duplicate re-uploads.</summary>
    public bool IsUniqueKey { get; set; }
}
