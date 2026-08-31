namespace Dignite.Vault.Extract.Documents.DocumentTypes.Packs;

/// <summary>
/// Constants for the document-type config "pack" mechanism (#444). The <see cref="ProvenanceSourceKey"/> /
/// <see cref="ProvenanceVersionKey"/> live in the type's / field's <c>ExtraProperties</c> (config metadata
/// on the DocumentType / FieldDefinition aggregates — NOT the Document truth source, so the Markdown-first
/// single-payload rules do not apply here).
/// </summary>
public static class DocumentTypePackConsts
{
    /// <summary>
    /// Current pack schema version emitted by export. Version 2 is field architecture v3 (#559): a field
    /// carries <c>fieldTypeName</c> + <c>configuration</c> + <c>description</c> + <c>isSearchable</c> where
    /// version 1 carried <c>dataType</c> + <c>allowMultiple</c> + <c>prompt</c>.
    /// </summary>
    public const int CurrentVersion = 2;

    /// <summary>
    /// Oldest pack schema version import still accepts. A version-1 pack is upconverted on the way in by
    /// the same v2-to-v3 type mapping that migrated the live rows, so packs already exported and committed
    /// to people's repositories keep working. Anything outside
    /// [<see cref="MinSupportedVersion"/>, <see cref="CurrentVersion"/>] loud-fails on import
    /// (<c>DocumentTypePackUnsupportedVersion</c>) rather than being mis-mapped.
    /// </summary>
    public const int MinSupportedVersion = 1;

    /// <summary>Fail-closed cap on how many packs one import call may carry.</summary>
    public const int MaxPacksPerImport = 100;

    /// <summary><c>ExtraProperties</c> key marking a type/field as pack-sourced.</summary>
    public const string ProvenanceSourceKey = "__packSource";

    /// <summary>Value stored under <see cref="ProvenanceSourceKey"/>.</summary>
    public const string ProvenanceSourceValue = "pack";

    /// <summary><c>ExtraProperties</c> key recording the pack schema version last applied.</summary>
    public const string ProvenanceVersionKey = "__packVersion";
}
