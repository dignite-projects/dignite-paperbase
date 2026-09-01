using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Vault.Extract.Documents;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using NSubstitute;
using Shouldly;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Dignite.Vault.Extract.Mcp.Documents;

[DependsOn(typeof(VaultExtractTestBaseModule))]
public class McpTenantAdmissionTestModule : AbpModule
{
    public static readonly Guid NonexistentTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid InactiveTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid ActiveTenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton(Substitute.For<IDocumentAppService>());

        // #524: reach past the (separately covered) AllowExplicitTenantScope and access-validator gates
        // so this suite isolates the tenant-store existence/active check specifically.
        Configure<VaultExtractMcpOptions>(options => options.AllowExplicitTenantScope = true);
        var accessValidator = Substitute.For<IMcpTenantAccessValidator>();
        accessValidator.IsAllowedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        context.Services.AddSingleton(accessValidator);

        var tenantStore = Substitute.For<ITenantStore>();
        tenantStore.FindAsync(NonexistentTenantId).Returns((TenantConfiguration?)null);
        tenantStore.FindAsync(InactiveTenantId)
            .Returns(new TenantConfiguration(InactiveTenantId, "inactive-tenant") { IsActive = false });
        tenantStore.FindAsync(ActiveTenantId)
            .Returns(new TenantConfiguration(ActiveTenantId, "active-tenant"));
        context.Services.AddSingleton(tenantStore);
    }
}

/// <summary>
/// #524 acceptance criterion: "deactivated / nonexistent tenant -> clear error, not a silent empty
/// result." <see cref="McpTenantScope"/> is <c>internal</c> (no <c>InternalsVisibleTo</c> for this test
/// project, deliberately — its behavior is a public contract only through the 7 tool/resource call
/// sites, never called directly), so this drives it indirectly through <see cref="DocumentSearchTool"/>,
/// the same way <see cref="DocumentSearchTool_Tests"/> covers the rest of the admission gate. Complements
/// <see cref="DocumentSearchTool_Tests"/> (happy path, always-active tenant stub) and
/// <see cref="McpExplicitTenantScopeResolution_Tests"/> (the access-validator denial itself, covered at
/// the integration level).
/// </summary>
public class McpTenantAdmission_Tests : VaultExtractTestBase<McpTenantAdmissionTestModule>
{
    private readonly IDocumentAppService _documentAppService;

    public McpTenantAdmission_Tests()
    {
        _documentAppService = GetRequiredService<IDocumentAppService>();
    }

    [Fact]
    public async Task Rejects_nonexistent_tenant_with_a_clear_error()
    {
        await Should.ThrowAsync<McpException>(() => DocumentSearchTool.SearchAsync(
            _documentAppService,
            documentTypeCode: "contract.general",
            tenantId: McpTenantAdmissionTestModule.NonexistentTenantId.ToString(),
            serviceProvider: ServiceProvider));
    }

    [Fact]
    public async Task Rejects_inactive_tenant_with_a_clear_error()
    {
        await Should.ThrowAsync<McpException>(() => DocumentSearchTool.SearchAsync(
            _documentAppService,
            documentTypeCode: "contract.general",
            tenantId: McpTenantAdmissionTestModule.InactiveTenantId.ToString(),
            serviceProvider: ServiceProvider));
    }

    [Fact]
    public async Task Allows_an_existing_active_tenant_through_to_the_app_service()
    {
        // Negative control for the two tests above: proves the tenant-store check itself (not some
        // other gate) is what rejects the nonexistent/inactive cases, by showing an active tenant with
        // the same access-validator/options configuration reaches the (mocked) AppService instead of
        // throwing.
        _documentAppService.GetListAsync(Arg.Any<GetDocumentListInput>())
            .Returns(new Volo.Abp.Application.Dtos.PagedResultDto<DocumentListItemDto>(
                0, new List<DocumentListItemDto>()));

        await DocumentSearchTool.SearchAsync(
            _documentAppService,
            documentTypeCode: "contract.general",
            tenantId: McpTenantAdmissionTestModule.ActiveTenantId.ToString(),
            serviceProvider: ServiceProvider);

        await _documentAppService.Received(1).GetListAsync(Arg.Any<GetDocumentListInput>());
    }
}
