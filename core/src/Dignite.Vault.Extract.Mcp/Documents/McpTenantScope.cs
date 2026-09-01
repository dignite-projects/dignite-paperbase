using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using Volo.Abp.Authorization;
using Volo.Abp.MultiTenancy;

namespace Dignite.Vault.Extract.Mcp.Documents;

/// <summary>
/// Resolves an explicit MCP tenant argument and applies it as a temporarily switched ABP tenant context,
/// or a no-op when none was supplied. Split into an async <see cref="ResolveAsync"/> /
/// <see cref="ResolveRequiredAsync"/> step (parsing plus the #524 admission checks, all of which are
/// async: options lookup, the access validator, the tenant-store existence/active check) and a
/// synchronous <see cref="Enter"/> step that performs the actual <see cref="ICurrentTenant.Change(Guid?)"/>.
/// <para>
/// This two-step shape is required by <c>AsyncLocal</c> semantics, not a style choice: ABP's tenant
/// switch is backed by an <c>AsyncLocal</c>, and a mutation made <i>inside</i> an <c>async</c> method
/// that a caller <c>await</c>s is invisible to that caller once the awaited call returns — even when
/// every internal await of the callee completes synchronously (an <c>ExecutionContext</c> capture/restore
/// boundary is crossed at the await regardless). Concretely, <c>using var scope = await
/// EnterAsync(...)</c> with <c>ICurrentTenant.Change(...)</c> called inside that awaited method would
/// silently fail to scope anything the caller does afterward — the switch would appear to succeed (no
/// exception) but every subsequent query in the caller would still run under the caller's own ambient
/// tenant. <see cref="Enter"/> must therefore be called directly in each call site's own method body,
/// immediately after awaiting <see cref="ResolveAsync"/> / <see cref="ResolveRequiredAsync"/> — never
/// through another awaited indirection layer.
/// </para>
/// </summary>
internal static class McpTenantScope
{
    /// <summary>
    /// For the 4 optional tool parameters: a blank/whitespace/null <paramref name="tenantId"/>
    /// legitimately means "use the ambient tenant", so it resolves to <c>null</c> without running any
    /// admission check.
    /// </summary>
    public static Task<Guid?> ResolveAsync(
        string? tenantId, IServiceProvider? serviceProvider, CancellationToken cancellationToken = default)
    {
        return string.IsNullOrWhiteSpace(tenantId)
            ? Task.FromResult((Guid?)null)
            : ResolveCoreAsync(tenantId, serviceProvider, cancellationToken);
    }

    /// <summary>
    /// For the 3 mandatory <c>{tenantId}</c> resource-uri segments: unlike <see cref="ResolveAsync"/>, a
    /// blank/whitespace/null value is a caller error, not "use the ambient tenant" — silently falling
    /// back would read the ambient tenant's data under a uri that names a different one (#524).
    /// </summary>
    public static Task<Guid?> ResolveRequiredAsync(
        string? tenantId, IServiceProvider? serviceProvider, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new McpException("A tenant id is required in this resource uri and cannot be blank.");
        }

        return ResolveCoreAsync(tenantId, serviceProvider, cancellationToken);
    }

    /// <summary>
    /// Runs the admission checks in a fixed order that must not change. The access validator runs
    /// strictly before the tenant-store existence/active lookup, so a denied caller cannot use the
    /// difference between "denied" and "unknown tenant" to enumerate real tenant ids. Deliberately never
    /// calls <see cref="ICurrentTenant.Change(Guid?)"/> — see the type-level doc comment for why that has
    /// to happen in <see cref="Enter"/> instead, synchronously, in the call site's own frame.
    /// </summary>
    private static async Task<Guid?> ResolveCoreAsync(
        string tenantId, IServiceProvider? serviceProvider, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(tenantId, out var parsedTenantId))
        {
            throw new McpException($"Invalid tenant id: {tenantId}");
        }

        EnsureServiceProvider(serviceProvider);

        // Deployment-level opt-in. Off by default, so an authenticated caller cannot request an arbitrary
        // tenant id merely because the capability compiles into every install (#524).
        var options = serviceProvider.GetRequiredService<IOptions<VaultExtractMcpOptions>>().Value;
        if (!options.AllowExplicitTenantScope)
        {
            throw new McpException("Explicit tenant scope is not enabled on this deployment.");
        }

        // Fail-closed business/membership policy. Evaluated against the caller's own identity before
        // anything about the target tenant is looked up, per the ordering note above.
        var accessValidator = serviceProvider.GetRequiredService<IMcpTenantAccessValidator>();
        if (!await accessValidator.IsAllowedAsync(parsedTenantId, cancellationToken))
        {
            throw new AbpAuthorizationException();
        }

        // ITenantStore.FindAsync takes no CancellationToken — an ABP framework API shape, not something
        // this call site can change.
        var tenantStore = serviceProvider.GetRequiredService<ITenantStore>();
        var tenant = await tenantStore.FindAsync(parsedTenantId);
        if (tenant is null || !tenant.IsActive)
        {
            throw new McpException($"Unknown or inactive tenant: {tenantId}");
        }

        return parsedTenantId;
    }

    /// <summary>
    /// Applies the switch for an already-resolved tenant id (or no-ops for <c>null</c>). Synchronous by
    /// design: must be called directly in the call site's own method body, immediately after awaiting
    /// <see cref="ResolveAsync"/> / <see cref="ResolveRequiredAsync"/>, so <c>ICurrentTenant.Change</c>
    /// mutates the ambient tenant that the call site's own subsequent code (and the AppService call it
    /// makes) actually observes.
    /// </summary>
    public static IDisposable? Enter(Guid? tenantId, IServiceProvider? serviceProvider)
    {
        if (!tenantId.HasValue)
        {
            return null;
        }

        EnsureServiceProvider(serviceProvider);
        return serviceProvider.GetRequiredService<ICurrentTenant>().Change(tenantId);
    }

    private static void EnsureServiceProvider([NotNull] IServiceProvider? serviceProvider)
    {
        if (serviceProvider is null)
        {
            throw new InvalidOperationException(
                "An IServiceProvider is required when an explicit tenant id is supplied.");
        }
    }
}
