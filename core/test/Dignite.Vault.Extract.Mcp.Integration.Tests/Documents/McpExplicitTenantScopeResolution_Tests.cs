using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Dignite.Vault.Extract.Documents;
using Dignite.Vault.Extract.Documents.DocumentTypes;
using Dignite.Vault.Extract.Permissions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Guids;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Security.Claims;
using Volo.Abp.Users;
using Xunit;

namespace Dignite.Vault.Extract.Mcp.Documents;

/// <summary>
/// #524 opts in to the explicit-tenant capability so the admission checks below that gate are actually
/// reached, and registers the REAL default-deny <see cref="DenyAllMcpTenantAccessValidator"/> the same
/// way <c>VaultExtractMcpModule</c> does (an explicit <c>TryAddTransient</c>, not a mock standing in for
/// it) — this suite exercises the shipped fail-closed default end to end. The disabled-by-default case
/// (<see cref="VaultExtractMcpOptions.AllowExplicitTenantScope"/> == <c>false</c>) is covered separately
/// by <see cref="McpPermissionResolution_Tests"/>, which reuses <see cref="McpPermissionPipelineTestModule"/>
/// unmodified and therefore already exercises the real, unconfigured default.
/// </summary>
[DependsOn(typeof(McpPermissionPipelineTestModule))]
public class McpExplicitTenantScopeTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<VaultExtractMcpOptions>(options => options.AllowExplicitTenantScope = true);
        context.Services.TryAddTransient<IMcpTenantAccessValidator, DenyAllMcpTenantAccessValidator>();
    }
}

/// <summary>
/// #524: an explicit <c>tenantId</c> MCP request must be denied by the admission gate before the ambient
/// tenant is ever switched and before the target application service's own authorization runs — the
/// fix for the #519 review finding that a caller whose token role name happens to also be granted in the
/// target tenant would otherwise pass <c>CheckPolicyAsync</c> there. Driven through the real
/// <see cref="DocumentSearchTool.SearchAsync"/> dispatch shape, the real permission pipeline (see
/// <see cref="McpPermissionResolution_Tests"/> for why this project exists), and a second seeded tenant.
/// </summary>
public class McpExplicitTenantScopeResolution_Tests
    : McpPermissionPipelineTestBase<McpExplicitTenantScopeTestModule>
{
    private const string TypeCode = "contract.explicit-tenant";

    // Tenant A is where the principal is legitimately granted and has real data. Tenant B is the
    // explicit target of the MCP call and — deliberately, this is the point of the test — ALSO grants
    // the same principal Documents.Default (mirroring the actual #519 shape: a caller whose role/user is
    // granted in more than one tenant). Without a Tenant B grant this test cannot tell the fix apart from
    // a full revert to the pre-#524 code: with no grant anywhere in Tenant B, even the OLD unconditional
    // switch would end up denied by DocumentAppService.GetListAsync's own CheckPolicyAsync post-switch,
    // throwing the identical AbpAuthorizationException the assertion below checks for. Granting Tenant B
    // too means a reverted fix would find that grant and let the call through instead of throwing — only
    // the admission gate running BEFORE the switch (the actual fix) makes this test fail-closed.
    private static readonly Guid TenantAId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid TenantBId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid PrincipalId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    private readonly IDocumentAppService _documentAppService;
    private readonly IDocumentTypeRepository _documentTypeRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly IPermissionGrantRepository _permissionGrantRepository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ICurrentPrincipalAccessor _principalAccessor;

    public McpExplicitTenantScopeResolution_Tests()
    {
        _documentAppService = GetRequiredService<IDocumentAppService>();
        _documentTypeRepository = GetRequiredService<IDocumentTypeRepository>();
        _documentRepository = GetRequiredService<IDocumentRepository>();
        _permissionGrantRepository = GetRequiredService<IPermissionGrantRepository>();
        _guidGenerator = GetRequiredService<IGuidGenerator>();
        _principalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
    }

    [Fact]
    public async Task Default_validator_denies_explicit_cross_tenant_request_before_authorization_runs()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var typeId = Guid.NewGuid();
            await _documentTypeRepository.InsertAsync(
                new DocumentType(typeId, TenantAId, TypeCode, "Explicit Tenant Contract"), autoSave: true);

            var document = new Document(
                Guid.NewGuid(),
                TenantAId,
                fileOrigin: new FileOrigin(
                    blobName: $"blobs/{Guid.NewGuid():N}.pdf",
                    uploadedByUserName: "svc",
                    contentType: "application/pdf",
                    contentHash: $"{Guid.NewGuid():N}{Guid.NewGuid():N}",
                    fileSize: 1024,
                    originalFileName: "explicit-tenant.pdf"));
            typeof(Document).GetProperty(nameof(Document.DocumentTypeId))!.SetValue(document, typeId);
            await _documentRepository.InsertAsync(document, autoSave: true);

            // Granted in BOTH tenants — see the class-level field comment for why Tenant B must also
            // grant this principal for the test to actually discriminate the fix from the #519 bug it
            // guards against. If the admission gate below did not deny first, the switch to Tenant B plus
            // this very grant would let DocumentAppService.GetListAsync's own CheckPolicyAsync pass.
            await _permissionGrantRepository.InsertAsync(
                new PermissionGrant(
                    _guidGenerator.Create(),
                    VaultExtractPermissions.Documents.Default,
                    "U",
                    PrincipalId.ToString(),
                    TenantAId),
                autoSave: true);
            await _permissionGrantRepository.InsertAsync(
                new PermissionGrant(
                    _guidGenerator.Create(),
                    VaultExtractPermissions.Documents.Default,
                    "U",
                    PrincipalId.ToString(),
                    TenantBId),
                autoSave: true);
        });

        using (_principalAccessor.Change(ServiceAccountPrincipal(PrincipalId)))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() =>
                WithUnitOfWorkAsync(() => DocumentSearchTool.SearchAsync(
                    _documentAppService,
                    documentTypeCode: TypeCode,
                    tenantId: TenantBId.ToString(),
                    serviceProvider: ServiceProvider)));
        }
    }

    // Same claim shape as McpPermissionResolution_Tests.ServiceAccountPrincipal: only AbpClaimTypes.UserId
    // plus a non-empty authentication type, matching what any authenticated MCP caller carries.
    private static ClaimsPrincipal ServiceAccountPrincipal(Guid userId)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(AbpClaimTypes.UserId, userId.ToString()) },
            authenticationType: "IntegrationTest");
        return new ClaimsPrincipal(identity);
    }
}
