using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Vault.Extract.Mcp.Documents;

/// <summary>
/// Fail-closed admission gate for an MCP call that carries an explicit <c>tenantId</c> (a tool parameter
/// or a mandatory <c>{tenantId}</c> resource-uri segment). <see cref="McpTenantScope"/> calls this
/// strictly before switching <c>ICurrentTenant</c>, so the check evaluates the caller's own identity, not
/// whatever the target tenant happens to grant a same-named role (#524). Extract has no concept of
/// cross-tenant membership itself — that is deployment/business policy — so this interface exists purely
/// as the attachment point for it; the built-in <see cref="DenyAllMcpTenantAccessValidator"/> denies
/// every explicit-tenant request until a deployment supplies its own implementation.
/// <para>
/// Unlike <see cref="VaultExtractMcpOptions.ResourceListContributors"/> (an ordered, additive list where
/// every entry independently contributes), this seam is single-implementation and replace-one: exactly
/// one <see cref="IMcpTenantAccessValidator"/> answers "is this allowed" at a time, so two candidates
/// competing for the slot is a bug, not a feature. The default is registered with a single explicit
/// <c>TryAddTransient</c> call in <c>VaultExtractMcpModule</c>; a downstream module overrides it with
/// <c>context.Services.Replace(ServiceDescriptor.Transient&lt;IMcpTenantAccessValidator,
/// TheirValidator&gt;())</c> in its own module (which runs after this one, since it depends on it) —
/// never by adding another <c>ITransientDependency</c> implementation and hoping ABP's interface-exposure
/// naming convention (a class ending in <c>McpTenantAccessValidator</c> auto-exposes as this interface)
/// sorts out which one wins. Two such classes would make resolution order depend on assembly-scan
/// ordering instead of an explicit call. <see cref="DenyAllMcpTenantAccessValidator"/> therefore
/// deliberately carries no lifetime marker of its own, so it can only ever reach the container through
/// that one explicit registration.
/// </para>
/// </summary>
public interface IMcpTenantAccessValidator
{
    /// <summary>
    /// Returns whether the current caller may switch the ambient tenant to <paramref name="targetTenantId"/>
    /// for the duration of one MCP call. Implementations resolve the caller's own identity (e.g. via
    /// constructor-injected <c>ICurrentUser</c> / <c>ICurrentPrincipalAccessor</c>) rather than receiving
    /// it as a parameter here, consistent with how other services in this codebase read ambient identity.
    /// Called before the tenant-store existence/active check, so a denied caller cannot use the
    /// difference between "denied" and "tenant does not exist" to probe which tenant ids are real —
    /// <b>that guarantee only holds if this implementation upholds its own half of it</b>: the answer (and
    /// its timing) must depend only on the caller's own claimed relationships, never on whether
    /// <paramref name="targetTenantId"/> itself exists. A membership check backed by a query joined
    /// against the tenant table, for example, must not let a nonexistent tenant id resolve measurably
    /// faster or differently than a real one it denies — that would reopen the same enumeration channel
    /// #524 closed, one layer below where <see cref="McpTenantScope"/> can see it.
    /// </summary>
    Task<bool> IsAllowedAsync(Guid targetTenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The default admission policy: denies every explicit-tenant request. Extract ships with no notion of
/// cross-tenant membership, so "off until a deployment supplies its own policy" is the only fail-closed
/// default (#524). See <see cref="IMcpTenantAccessValidator"/> for why this class intentionally carries
/// no <c>ITransientDependency</c> / lifetime marker and is instead registered explicitly by
/// <c>VaultExtractMcpModule</c>.
/// </summary>
public sealed class DenyAllMcpTenantAccessValidator : IMcpTenantAccessValidator
{
    public Task<bool> IsAllowedAsync(Guid targetTenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
