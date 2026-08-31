using System.Collections.Generic;
using System.Linq;
using Dignite.Abp.FlexFields;
using Dignite.Vault.Extract.Ai;
using Dignite.Vault.Extract.Documents.Fields;
using Dignite.Vault.Extract.FlexFields;

namespace Dignite.Vault.Extract.Mcp.Documents;

/// <summary>
/// Projects field definitions into their LLM-facing schema shape. Shared by the document-type
/// <see cref="DocumentTypeResources">resource</see> and <see cref="DocumentTypeTools">tool</see> because
/// they describe the same fields to the same clients: when the two drift, a client that discovered a
/// field through one and filtered on it through the other gets a different answer about what the field
/// accepts.
/// </summary>
public static class DocumentTypeFieldSchemaProjector
{
    public static List<DocumentTypeFieldSchema> Project(
        IEnumerable<FieldDefinitionDto> fields,
        IFieldTypeResolver fieldTypeResolver,
        IVaultExtractFieldTypeRegistry fieldTypeExtensionRegistry)
    {
        return fields
            .OrderBy(f => f.DisplayOrder)
            .Select(f => Project(f, fieldTypeResolver, fieldTypeExtensionRegistry))
            .ToList();
    }

    public static DocumentTypeFieldSchema Project(
        FieldDefinitionDto field,
        IFieldTypeResolver fieldTypeResolver,
        IVaultExtractFieldTypeRegistry fieldTypeExtensionRegistry)
    {
        // GetAll rather than Get: a stored field could name a type this deployment no longer registers
        // (a package downgrade, a renamed key), and Get throws on that. Describing such a field as
        // non-filterable is right — nothing can index it here — while throwing would take down the whole
        // type listing over one stale row.
        var fieldType = fieldTypeResolver.GetAll()
            .FirstOrDefault(t => string.Equals(t.Name, field.FieldTypeName, System.StringComparison.Ordinal));

        return new DocumentTypeFieldSchema
        {
            Name = field.Name,
            FieldType = field.FieldTypeName,
            IsMultiValue = fieldTypeExtensionRegistry.IsMultiValue(field.FieldTypeName, field.Configuration),
            IsFilterable = field.IsSearchable && fieldType?.IndexValueType != null,
            // DisplayName is admin-configured user-derived text, so PromptBoundary wrapping prevents
            // indirect prompt injection. Name and FieldType are system-controlled (allow-listed name
            // pattern / registration key), so they are emitted raw.
            DisplayName = PromptBoundary.WrapField(field.DisplayName),
            IsRequired = field.IsRequired
        };
    }
}
