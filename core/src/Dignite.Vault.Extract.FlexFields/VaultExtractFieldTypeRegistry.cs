using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Dignite.Abp.FlexFields;
using Volo.Abp.DependencyInjection;

namespace Dignite.Vault.Extract.FlexFields;

/// <summary>
/// Indexes every registered <see cref="IVaultExtractFieldTypeExtension"/> by name.
/// <para>
/// "Supported" and "has an implementation" are the same fact here by construction: an extension's mere
/// presence in the constructor-injected collection is what <see cref="IsSupported"/> answers, so the two
/// can no longer drift the way the hand-written allow-list this replaces could from the dispatch chains it
/// was policing (#564). Adding Vault-Extract-side support for a field type - a kernel built-in Vault
/// Extract never wired up, or a bolt-on type a downstream consumer defines - is "implement one interface,
/// let ABP's conventional registration find it," not "find and correctly edit five files."
/// </para>
/// <para>
/// Transient like the kernel's own <c>FieldTypeResolver</c>: cheap to construct, and the underlying
/// <see cref="IEnumerable{T}"/> resolution is already what the container caches.
/// </para>
/// </summary>
public class VaultExtractFieldTypeRegistry : IVaultExtractFieldTypeRegistry, ITransientDependency
{
    private readonly Dictionary<string, IVaultExtractFieldTypeExtension> _extensions;

    public VaultExtractFieldTypeRegistry(IEnumerable<IVaultExtractFieldTypeExtension> extensions)
    {
        _extensions = extensions.ToDictionary(e => e.FieldTypeName, StringComparer.Ordinal);
    }

    public IReadOnlySet<string> SupportedFieldTypeNames => _extensions.Keys.ToHashSet(StringComparer.Ordinal);

    public bool IsSupported(string fieldTypeName) => _extensions.ContainsKey(fieldTypeName);

    public bool TryGet(string fieldTypeName, [NotNullWhen(true)] out IVaultExtractFieldTypeExtension? extension)
        => _extensions.TryGetValue(fieldTypeName, out extension);

    public IVaultExtractFieldTypeExtension Get(string fieldTypeName)
    {
        if (!_extensions.TryGetValue(fieldTypeName, out var extension))
        {
            throw new ArgumentOutOfRangeException(
                nameof(fieldTypeName), fieldTypeName,
                "No Vault Extract field-type extension is registered for this name.");
        }

        return extension;
    }

    public bool IsMultiValue(string fieldTypeName, FieldConfigurationDictionary? configuration = null)
        => Get(fieldTypeName).IsMultiValue(configuration);
}
