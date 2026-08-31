using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Dignite.Abp.FlexFields;

namespace Dignite.Vault.Extract.FlexFields;

/// <summary>
/// Resolves an <see cref="IVaultExtractFieldTypeExtension"/> by <c>Field.FieldTypeName</c> - the
/// Vault-Extract-side mirror of the kernel's own <c>IFieldTypeResolver</c>.
/// </summary>
public interface IVaultExtractFieldTypeRegistry
{
    /// <summary>Every registered field-type name. Derived from what is actually registered, never hand-written.</summary>
    IReadOnlySet<string> SupportedFieldTypeNames { get; }

    /// <summary>Whether an extension is registered for <paramref name="fieldTypeName"/>.</summary>
    bool IsSupported(string fieldTypeName);

    bool TryGet(string fieldTypeName, [NotNullWhen(true)] out IVaultExtractFieldTypeExtension? extension);

    /// <summary>Same as <see cref="TryGet"/>, throwing instead of returning <c>false</c>.</summary>
    IVaultExtractFieldTypeExtension Get(string fieldTypeName);

    /// <summary>Convenience for the single most common question asked without a full lookup.</summary>
    bool IsMultiValue(string fieldTypeName, FieldConfigurationDictionary? configuration = null);
}
