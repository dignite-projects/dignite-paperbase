using System;
using System.Collections.Generic;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization.Permissions.Resources;

namespace Dignite.Vault.Extract.Documents.DocumentTypes;

public class DocumentTypeDto : EntityDto<Guid>, IHasResourcePermissions
{
    public Guid? TenantId { get; set; }
    public string TypeCode { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string? Description { get; set; }
    public double ConfidenceThreshold { get; set; }
    public int Priority { get; set; }

    /// <summary>
    /// The caller's own resource-permission grants on this type (#629), keyed by permission name — phase 1
    /// defines exactly one, <see cref="Permissions.VaultExtractPermissions.DocumentTypes.Resources.Upload"/>.
    /// Filled by <c>DocumentTypeAppService.GetVisibleAsync</c> through ABP's <c>ResourcePermissionPopulator</c>,
    /// so the UI does not have to guess which types it may act on. Absent from the dictionary means the same
    /// as <c>false</c>.
    /// <para>
    /// This is <b>not</b> the unconstrained extension bag CLAUDE.md forbids: it lives on a read DTO rather
    /// than on <c>Document</c>, it is never persisted, and every key is a registered permission definition —
    /// the populator enumerates them from <c>IPermissionDefinitionManager</c>, so nothing can write an
    /// arbitrary key into it.
    /// </para>
    /// </summary>
    public Dictionary<string, bool> ResourcePermissions { get; set; } = new();
}
