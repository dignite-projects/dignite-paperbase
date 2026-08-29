using Volo.Abp.Localization;

namespace Dignite.Vault.Extract.FlexFields.Localization;

/// <summary>
/// Localization for Vault Extract's own field types. Deliberately separate from the main
/// <c>VaultExtract</c> resource: this project depends only on the FlexFields kernel's Abstractions and
/// must not pull in <c>Dignite.Vault.Extract.Domain.Shared</c>, where that resource lives. Same shape
/// as the kernel's own bolt-on resources (e.g. <c>FlexFieldsCKEditor</c>).
/// </summary>
[LocalizationResourceName("VaultExtractFlexFields")]
public class VaultExtractFlexFieldsResource
{
}
