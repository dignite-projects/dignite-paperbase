using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Volo.Abp.MultiTenancy;

namespace Dignite.Vault.Extract.Mcp.Documents;

/// <summary>
/// #524: shared setup for the 7 tenant-scoped MCP tool/resource unit-test modules. Opts into the
/// explicit-tenant capability and stubs an always-allow <see cref="IMcpTenantAccessValidator"/> + an
/// always-active <see cref="ITenantStore"/>, so each suite's ad hoc <see cref="Guid.NewGuid"/> tenant ids
/// clear the admission gate without every module re-wiring the same three stubs by hand. The gate itself
/// is covered separately by <c>McpTenantAdmission_Tests</c> / <c>McpPermissionResolution_Tests</c> /
/// <c>McpExplicitTenantScopeResolution_Tests</c>.
/// </summary>
internal static class McpTenantScopeTestSetup
{
    public static void AllowAnyExplicitTenant(this IServiceCollection services)
    {
        services.Configure<VaultExtractMcpOptions>(options => options.AllowExplicitTenantScope = true);

        var accessValidator = Substitute.For<IMcpTenantAccessValidator>();
        accessValidator.IsAllowedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        services.AddSingleton(accessValidator);

        var tenantStore = Substitute.For<ITenantStore>();
        tenantStore.FindAsync(Arg.Any<Guid>()).Returns(ci => new TenantConfiguration(ci.Arg<Guid>(), "test-tenant"));
        services.AddSingleton(tenantStore);
    }
}
