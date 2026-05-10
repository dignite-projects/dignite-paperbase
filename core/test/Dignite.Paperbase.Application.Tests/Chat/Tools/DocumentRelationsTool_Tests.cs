using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Chat;
using Dignite.Paperbase.Chat.Tools;
using Dignite.Paperbase.Documents;
using Microsoft.Extensions.AI;
using Shouldly;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace Dignite.Paperbase.Chat;

/// <summary>
/// Issue #101 — guards for the <c>get_document_relations</c> AIFunction. Verifies the
/// fail-closed contract from <c>.claude/rules/doc-chat-anti-patterns.md</c> reverse
/// example C: explicit tenant predicate (don't rely on ambient DataFilter), bidirectional
/// lookup, ordering (manual first, then AI-suggested by confidence), and the result-set
/// upper bound that protects the LLM context from a relation explosion.
/// </summary>
public class DocumentRelationsTool_Tests
    : PaperbaseApplicationTestBase<DocumentChatAppServiceTestModule>
{
    private readonly DocumentRelationsTool _tool;
    private readonly IDocumentChatToolFactory _toolFactory;
    private readonly IDocumentRelationRepository _relationRepository;
    private readonly ICurrentTenant _currentTenant;

    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public DocumentRelationsTool_Tests()
    {
        _tool = GetRequiredService<DocumentRelationsTool>();
        _toolFactory = GetRequiredService<IDocumentChatToolFactory>();
        _relationRepository = GetRequiredService<IDocumentRelationRepository>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    [Fact]
    public async Task Returns_Empty_Payload_When_Anchor_Has_No_Relations()
    {
        var anchor = Guid.NewGuid();
        var ctx = BuildContext(TenantA);

        var payload = await InvokeAsync(ctx, anchor);

        payload.GetProperty("anchorDocumentId").GetGuid().ShouldBe(anchor);
        payload.GetProperty("count").GetInt32().ShouldBe(0);
        payload.GetProperty("relations").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Returns_Bidirectional_Relations_For_The_Anchor()
    {
        // Anchor X has both an outgoing edge (X → Y) and an incoming edge (Z → X).
        // The model must see both — both edges represent something the user might
        // care about ("what does X link to" vs "what links to X").
        var anchor = Guid.NewGuid();
        var outgoingTarget = Guid.NewGuid();
        var incomingSource = Guid.NewGuid();

        await SeedRelationsAsync(
            CreateRelation(TenantA, source: anchor, target: outgoingTarget, kind: RelationSource.Manual),
            CreateRelation(TenantA, source: incomingSource, target: anchor, kind: RelationSource.Manual));

        var payload = await InvokeAsync(BuildContext(TenantA), anchor);

        payload.GetProperty("count").GetInt32().ShouldBe(2);
        var relatedIds = payload.GetProperty("relations").EnumerateArray()
            .Select(r => r.GetProperty("relatedDocumentId").GetGuid())
            .ToHashSet();
        relatedIds.ShouldContain(outgoingTarget);
        relatedIds.ShouldContain(incomingSource);
    }

    [Fact]
    public async Task RelatedDocumentId_Is_The_Other_Side_Of_The_Edge()
    {
        // Tests the convenience field BuildAnchorContextAsync's prompt advice depends on:
        // the model should not have to reason about edge direction, and the payload
        // surfaces relatedDocumentId = whichever side is NOT the anchor.
        var anchor = Guid.NewGuid();
        var counterpart = Guid.NewGuid();

        await SeedRelationsAsync(
            CreateRelation(TenantA, source: counterpart, target: anchor, kind: RelationSource.Manual));

        var payload = await InvokeAsync(BuildContext(TenantA), anchor);

        var relation = payload.GetProperty("relations")[0];
        relation.GetProperty("sourceDocumentId").GetGuid().ShouldBe(counterpart);
        relation.GetProperty("targetDocumentId").GetGuid().ShouldBe(anchor);
        relation.GetProperty("relatedDocumentId").GetGuid().ShouldBe(counterpart);
    }

    [Fact]
    public async Task Manual_Relations_Come_Before_AiSuggested_Ordered_By_Confidence_Desc()
    {
        var anchor = Guid.NewGuid();
        await SeedRelationsAsync(
            CreateRelation(TenantA, source: anchor, target: Guid.NewGuid(),
                kind: RelationSource.AiSuggested, confidence: 0.6),
            CreateRelation(TenantA, source: anchor, target: Guid.NewGuid(),
                kind: RelationSource.Manual),
            CreateRelation(TenantA, source: anchor, target: Guid.NewGuid(),
                kind: RelationSource.AiSuggested, confidence: 0.95));

        var payload = await InvokeAsync(BuildContext(TenantA), anchor);

        var relations = payload.GetProperty("relations").EnumerateArray().ToList();
        relations.Count.ShouldBe(3);
        relations[0].GetProperty("source").GetString().ShouldBe("Manual");
        relations[1].GetProperty("source").GetString().ShouldBe("AiSuggested");
        relations[1].GetProperty("confidence").GetDouble().ShouldBe(0.95);
        relations[2].GetProperty("source").GetString().ShouldBe("AiSuggested");
        relations[2].GetProperty("confidence").GetDouble().ShouldBe(0.6);
    }

    [Fact]
    public async Task Tenant_Predicate_Drops_Relations_Belonging_To_Other_Tenants()
    {
        // Seed an edge under TenantB; querying as TenantA must NOT return it. This
        // is the explicit-tenant-predicate check from reverse example C #2 — even if
        // the ambient DataFilter is bypassed in some future code path, the tool's
        // closure-captured _tenantId is the binding boundary.
        var anchor = Guid.NewGuid();
        var leakedTarget = Guid.NewGuid();

        await SeedRelationsAsync(
            CreateRelation(TenantB, source: anchor, target: leakedTarget, kind: RelationSource.Manual));

        var payload = await InvokeAsync(BuildContext(TenantA), anchor);

        payload.GetProperty("count").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Caps_Result_Set_At_The_Documented_Maximum_Of_Twenty()
    {
        // A pathological case: many relations for one anchor. The cap exists to keep
        // a single tool call from blowing up the LLM context window.
        const int seedCount = 35;
        var anchor = Guid.NewGuid();

        var relations = Enumerable.Range(0, seedCount)
            .Select(_ => CreateRelation(TenantA, source: anchor, target: Guid.NewGuid(),
                kind: RelationSource.Manual))
            .ToArray();
        await SeedRelationsAsync(relations);

        var payload = await InvokeAsync(BuildContext(TenantA), anchor);

        payload.GetProperty("count").GetInt32().ShouldBe(20);
        payload.GetProperty("relations").GetArrayLength().ShouldBe(20);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<JsonElement> InvokeAsync(DocumentChatToolContext ctx, Guid documentId)
    {
        var function = _tool.CreateAIFunction(ctx, _toolFactory);
        var args = new AIFunctionArguments
        {
            ["documentId"] = documentId
        };

        // Production callers (FunctionInvokingChatClient) invoke the AIFunction inside
        // (a) the chat turn's active UoW (EF DbContext) and (b) the same ABP tenant
        // scope as the conversation. Tests must mirror both — without the tenant scope
        // the ambient ABP IMultiTenant filter would still hide our seeded rows even
        // though the tool's explicit predicate already covers the safety property.
        var raw = await WithUnitOfWorkAsync(async () =>
        {
            using (_currentTenant.Change(ctx.TenantId))
            {
                return await function.InvokeAsync(args);
            }
        });
        var json = raw?.ToString() ?? "{}";
        return JsonDocument.Parse(json).RootElement;
    }

    private static DocumentChatToolContext BuildContext(Guid tenantId) => new()
    {
        TenantId = tenantId,
        ConversationId = Guid.NewGuid(),
        UserId = Guid.NewGuid()
    };

    private static DocumentRelation CreateRelation(
        Guid tenantId,
        Guid source,
        Guid target,
        RelationSource kind,
        double? confidence = null)
        => new(
            id: Guid.NewGuid(),
            tenantId: tenantId,
            sourceDocumentId: source,
            targetDocumentId: target,
            description: $"test relation {source}->{target}",
            source: kind,
            confidence: kind == RelationSource.AiSuggested ? (confidence ?? 0.5) : null);

    private async Task SeedRelationsAsync(params DocumentRelation[] relations)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            foreach (var r in relations)
            {
                await _relationRepository.InsertAsync(r, autoSave: true);
            }
        });
    }
}
