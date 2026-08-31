using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dignite.Abp.FlexFields;
using Volo.Abp.DependencyInjection;

namespace Dignite.Vault.Extract.FlexFields;

/// <summary>
/// The base every <see cref="IVaultExtractFieldTypeExtension"/> implementation should inherit, rather than
/// implementing the interface directly - see the interface's own doc comment for why. Carries the DI wiring
/// the naming convention cannot pick up on its own: <see cref="ISingletonDependency"/> for the lifetime
/// (these are stateless, so one shared instance per type is enough), and an explicit
/// <c>[ExposeServices]</c> so the interface itself, not just the concrete class, is what the registry's
/// constructor-injected <c>IEnumerable&lt;IVaultExtractFieldTypeExtension&gt;</c> actually collects.
/// </summary>
[ExposeServices(typeof(IVaultExtractFieldTypeExtension), IncludeDefaults = true, IncludeSelf = true)]
public abstract class VaultExtractFieldTypeExtensionBase : IVaultExtractFieldTypeExtension, ISingletonDependency
{
    /// <inheritdoc />
    public abstract string FieldTypeName { get; }

    /// <inheritdoc />
    public abstract bool IsMultiValue(FieldConfigurationDictionary? configuration);

    /// <inheritdoc />
    public abstract bool TryRead(JsonElement value, FieldConfigurationDictionary configuration, out object? result);

    /// <inheritdoc />
    public abstract JsonObject BuildExtractionSchema(FieldConfigurationDictionary configuration);

    /// <inheritdoc />
    public abstract JsonElement? WriteJson(object value, FieldConfigurationDictionary configuration);

    /// <inheritdoc />
    public abstract string? RenderForExport(object value, FieldConfigurationDictionary configuration);

    /// <inheritdoc />
    public abstract IReadOnlyList<string> CanonicalizeForFingerprint(object value);
}
