using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dignite.Abp.FlexFields;

namespace Dignite.Vault.Extract.FlexFields;

/// <summary>
/// Everything Vault Extract itself does with one field type's value, beyond what the kernel's
/// <c>IFieldType</c> already covers (validation, configuration, indexing). One implementation per
/// supported <c>Field.FieldTypeName</c>, auto-registered the same way the kernel's own
/// <c>FieldTypeBase : IFieldType, ITransientDependency</c> is - any assembly, including a downstream
/// consumer's, adds Vault-Extract-side support for a field type by declaring one class here, not by
/// finding and correctly editing five separate dispatch chains (#564).
/// <para>
/// Bundled as one interface rather than five, deliberately: a type that only implements some of these
/// would still compile and still pass <see cref="IVaultExtractFieldTypeRegistry.IsSupported"/>, then break
/// at whichever call site it never got a branch for - exactly the "accepted at the door, broken at every
/// use" shape already documented for the kernel's own <c>Tree</c> built-in, which Vault Extract does not
/// implement this interface for.
/// </para>
/// <para>
/// Deliberately carries no DI wiring of its own - implement <see cref="VaultExtractFieldTypeExtensionBase"/>
/// instead of this interface directly. ABP's default conventional registrar only exposes an implemented
/// interface as a resolvable service when the class name ends with the interface name minus its leading
/// <c>I</c> (see <c>ExposeServicesAttribute.GetDefaultServices</c>) - the kernel's own <c>TagsFieldType</c>
/// satisfies this by construction (it ends in <c>FieldType</c>, matching <c>IFieldType</c>), but a class
/// named e.g. <c>TextFieldTypeExtension</c> does not end in <c>VaultExtractFieldTypeExtension</c>, so the
/// interface is silently never exposed and <see cref="IVaultExtractFieldTypeRegistry"/>'s constructor
/// collects nothing - every implementation still self-registers, so the failure is invisible until
/// something actually asks the registry to resolve one. <see cref="VaultExtractFieldTypeExtensionBase"/>
/// carries an explicit <c>[ExposeServices]</c> declaration once, the same way the kernel's own
/// <c>FieldTypeBase : IFieldType, ITransientDependency</c> carries its lifetime marker once, so an
/// implementer - Vault Extract's own or a downstream consumer's - only has to inherit it correctly, never
/// re-solve this convention pitfall per class.
/// </para>
/// </summary>
public interface IVaultExtractFieldTypeExtension
{
    /// <summary>The registration key this extension answers for, e.g. <c>"Text"</c>.</summary>
    string FieldTypeName { get; }

    /// <summary>Whether a field of this type holds a list rather than a scalar.</summary>
    bool IsMultiValue(FieldConfigurationDictionary? configuration);

    /// <summary>
    /// Reads <paramref name="value"/> - from the LLM, or an operator edit - into the CLR shape the v3 bag
    /// stores. Strict, with no coercion: the field type is a promise about the shape of the value, and
    /// quietly accepting a near-miss would make that promise untrue for every later bag reader.
    /// </summary>
    /// <returns>
    /// <c>true</c> with <paramref name="result"/> set when the value is acceptable. <c>false</c> when it is
    /// not - the caller decides what a rejection means (logged-and-skipped during extraction, an
    /// interactive error on an operator edit).
    /// </returns>
    bool TryRead(JsonElement value, FieldConfigurationDictionary configuration, out object? result);

    /// <summary>The JSON-schema fragment that constrains this field's value in the LLM extraction call.</summary>
    JsonObject BuildExtractionSchema(FieldConfigurationDictionary configuration);

    /// <summary>
    /// Renders a non-null <b>scalar</b> bag value as the canonical <see cref="JsonElement"/> the
    /// <c>ExtractedFields</c> egress carries - the exact inverse of <see cref="TryRead"/>. Null only for a
    /// value that does not actually parse as this type (a defensive fallback for a corrupted or
    /// pre-migration bag entry, never expected for a value that passed <see cref="TryRead"/> on the way in).
    /// <para>
    /// The shared caller checks <see cref="IsMultiValue"/> before ever reaching here and serializes a
    /// multi-valued field's whole list generically on its own - a multi-valued type's own implementation of
    /// this method is therefore unreachable through the normal call path, kept only so the interface has no
    /// partial implementations.
    /// </para>
    /// </summary>
    JsonElement? WriteJson(object value, FieldConfigurationDictionary configuration);

    /// <summary>
    /// Renders a non-null <b>scalar</b> bag value as an export cell string, or <c>null</c> if it does not
    /// normalize to anything.
    /// <para>
    /// The shared caller detects a list-shaped value on its own (by the value's runtime shape, not by
    /// <see cref="IsMultiValue"/>) and renders every element generically without calling this method at all
    /// - every multi-valued field's elements are plain strings, so no per-type formatting is needed for
    /// them. A multi-valued type's own implementation of this method is therefore unreachable through the
    /// normal call path, kept only so the interface has no partial implementations.
    /// </para>
    /// </summary>
    string? RenderForExport(object value, FieldConfigurationDictionary configuration);

    /// <summary>
    /// Canonicalizes a non-null bag value into the zero, one, or many strings it contributes to a
    /// fingerprint. Returning an empty list makes the whole key partial - see
    /// <c>FlexFieldFingerprintCalculator</c> for why that must never be confused with "this field
    /// contributes no characters".
    /// </summary>
    IReadOnlyList<string> CanonicalizeForFingerprint(object value);
}
