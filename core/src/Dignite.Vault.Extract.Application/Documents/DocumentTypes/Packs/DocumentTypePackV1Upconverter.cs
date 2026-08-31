using System;
using Dignite.Vault.Extract.Documents.Fields;
using Dignite.Vault.Extract.Documents.Fields.Migration;

namespace Dignite.Vault.Extract.Documents.DocumentTypes.Packs;

/// <summary>
/// Rewrites a pack schema version 1 field into the version 2 shape, so packs exported before field
/// architecture v3 (#559) still import. Deliberately reuses <see cref="FieldDefinitionToFieldMapper"/>:
/// a v1 pack and a v2 database row describe a field the same way, so upconverting a pack and migrating a
/// row must land on the same field type and configuration, or an exported-then-reimported type would stop
/// matching the type it came from.
/// </summary>
public static class DocumentTypePackV1Upconverter
{
    /// <summary>
    /// Fills <c>FieldTypeName</c> / <c>Configuration</c> / <c>Description</c> from the version-1 members,
    /// in place. Idempotent, and a no-op for a field that already carries a field type: a caller that
    /// mistakenly runs this over a version-2 pack cannot damage it.
    /// </summary>
    public static void Upconvert(DocumentTypePackFieldDto field)
    {
        if (!string.IsNullOrWhiteSpace(field.FieldTypeName))
        {
            return;
        }

        var (fieldTypeName, configuration) = FieldDefinitionToFieldMapper.MapType(
            field.DataType ?? FieldDataType.Text,
            field.AllowMultiple);

        field.FieldTypeName = fieldTypeName;
        field.Configuration = configuration;
        field.Description ??= field.Prompt;

        // v1 had no notion of searchability — every extracted value was indexed — so the DTO's own
        // default (true) would be the faithful conversion, except for a v3 target type that cannot be
        // indexed at all (LongText -> CKEditor). Sharing FieldDefinitionToFieldMapper.IsSearchableFor
        // with the v2 row migrator keeps this in sync with CheckSearchable's own allow/reject rule,
        // instead of restating it and drifting: a pack with a LongText field used to fail
        // ImportFieldsAsync's searchable guard outright (#562's CheckSearchable, still enforced above).
        field.IsSearchable = FieldDefinitionToFieldMapper.IsSearchableFor(fieldTypeName);
    }
}
