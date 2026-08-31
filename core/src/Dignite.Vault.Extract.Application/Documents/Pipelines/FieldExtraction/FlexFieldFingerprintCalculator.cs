using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dignite.Abp.FlexFields;
using Dignite.Vault.Extract.Documents;
using Dignite.Vault.Extract.Documents.Fields;
using Dignite.Vault.Extract.FlexFields;

namespace Dignite.Vault.Extract.Documents.Pipelines.FieldExtraction;

/// <summary>
/// <see cref="Document.FieldFingerprint"/> (#411) computed from the v3 value bag — the replacement for
/// <see cref="FieldFingerprintCalculator"/>, which reads the v2 child rows.
/// <para>
/// <b>The fingerprint is a stored hash compared by string equality, so the canonical string this builds
/// is a contract, not an implementation detail.</b> Change how a value is normalized and every document
/// hashed under the old rule stops matching documents hashed under the new one — the corpus silently
/// splits into two populations that can never be duplicates of each other. That is why the migration
/// recomputes every fingerprint rather than trying to preserve equality across the storage change (#561):
/// preserving it byte-for-byte would have frozen the v2 row layout as a permanent constraint on this file.
/// </para>
/// <para>
/// The determinism rules are carried over unchanged from v2, because they are what make the hash stable
/// rather than merely reproducible on one machine: unique-key fields hash in <see cref="Field.Id"/> order,
/// and each field's values in bag order, so enumeration order never leaks into the hash; and a
/// <b>partial key</b> (a declared unique-key field with no usable value) returns <c>null</c>, because an
/// incomplete key would collide unrelated documents.
/// </para>
/// <para>
/// #564: the value-normalization rules themselves — what counts as "no usable value", and whether a value
/// is one string or several — now live on each field type's own
/// <see cref="IVaultExtractFieldTypeExtension.CanonicalizeForFingerprint"/>; this keeps only the
/// unique-key selection/ordering and the partial-key rule that applies across every type alike.
/// </para>
/// </summary>
public static class FlexFieldFingerprintCalculator
{
    // The separator between the values of one multi-valued field, and the separator between fields,
    // come from FieldValueFormats, shared with the v2 calculator so the frozen hash-contract literals
    // cannot drift between the two while both are live.

    /// <summary>
    /// Returns the fingerprint for <paramref name="document"/>, or <c>null</c> when its type declares no
    /// unique-key fields or the key is partial.
    /// </summary>
    /// <param name="document">The document whose bag is hashed.</param>
    /// <param name="definitions">
    /// The document type's current fields. Only <see cref="Field.IsUniqueKey"/> ones participate.
    /// </param>
    /// <param name="registry">Resolves each unique-key field's own canonicalization rule.</param>
    public static string? Compute(
        IHasFlexFields document, IReadOnlyCollection<Field> definitions, IVaultExtractFieldTypeRegistry registry)
    {
        var uniqueKeyFields = definitions
            .Where(d => d.IsUniqueKey)
            .OrderBy(d => d.Id)
            .ToList();

        if (uniqueKeyFields.Count == 0)
        {
            // Duplicate detection is opt-in per type.
            return null;
        }

        var builder = new StringBuilder();
        foreach (var field in uniqueKeyFields)
        {
            var value = document.GetField(field.Name);

            var canonicalValues = value != null && registry.TryGet(field.FieldTypeName, out var extension)
                ? extension.CanonicalizeForFingerprint(value)
                : Array.Empty<string>();

            // No value, or a value that normalizes to nothing, both make the key partial.
            if (canonicalValues.Count == 0)
            {
                return null;
            }

            builder.Append(field.Id.ToString("N"));
            builder.Append('=');
            for (var i = 0; i < canonicalValues.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(FieldValueFormats.FingerprintValueSeparator);
                }

                builder.Append(canonicalValues[i]);
            }

            builder.Append(FieldValueFormats.FingerprintFieldSeparator);
        }

        return ContentHasher.Sha256Hex(Encoding.UTF8.GetBytes(builder.ToString()));
    }
}
