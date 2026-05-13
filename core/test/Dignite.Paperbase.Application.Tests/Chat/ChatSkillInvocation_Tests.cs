using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Chat.Telemetry;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Volo.Abp.Auditing;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Claims;
using Xunit;
using MEAI = Microsoft.Extensions.AI;

namespace Dignite.Paperbase.Chat;

/// <summary>
/// Integration-level guards for the MAF Agent Skills audit pipeline introduced in
/// Issue #149. Companion to <see cref="ChatToolInvocation_Tests"/> (which already
/// covers the direct <c>search_paperbase_documents</c> path); these tests prove that
/// when the LLM invokes a skill via MAF's <c>run_skill_script</c> meta-tool:
///
/// <list type="number">
///   <item>The skill body actually executes (parameter binding works end-to-end).</item>
///   <item>The invocation produces a <see cref="ChatToolAuditEntry"/> with a derived
///         <c>skill:&lt;skill-name&gt;/&lt;script-name&gt;</c> ToolName — proving the
///         <see cref="AuditingSkillsContextProvider"/> + <c>AuditedChatFunction</c>
///         pipeline are wired into <c>BuildAgentSkillsProvider</c>.</item>
///   <item>The turn classifies as <see cref="GroundingSource.Structured"/> (not
///         <see cref="GroundingSource.None"/> / <c>IsDegraded</c>) — the
///         Codex-finding-3 regression that motivated the audit decorator.</item>
/// </list>
/// </summary>
public class ChatSkillInvocation_Tests
    : PaperbaseApplicationTestBase<ChatSkillInvocationTestModule>
{
    private readonly IChatAppService _appService;
    private readonly StubAuditSkillScriptedClient _scripted;
    private readonly ICurrentPrincipalAccessor _principalAccessor;
    private readonly IAuditingManager _auditingManager;
    private readonly StubAuditTestSkill _stubSkill;

    private static readonly Guid OwnerUserId = Guid.Parse("00000000-0000-0000-0000-000000000010");

    public ChatSkillInvocation_Tests()
    {
        _appService = GetRequiredService<IChatAppService>();
        _scripted = GetRequiredService<StubAuditSkillScriptedClient>();
        _principalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
        _auditingManager = GetRequiredService<IAuditingManager>();
        _stubSkill = GetRequiredService<StubAuditTestSkill>();
    }

    [Fact]
    public async Task SendMessageAsync_Records_Skill_Script_Invocation_With_Derived_Name_In_Audit_Log()
    {
        // Issue #149 (Codex adversarial-review finding 3): the run_skill_script meta-tool
        // had to be wrapped by AuditingSkillsContextProvider so per-skill invocations
        // produce audit entries with names like "skill:<skill>/<script>" — not the generic
        // "run_skill_script" that loses per-skill granularity. This test exercises the full
        // path from SendMessageAsync through PrepareAgentSetupAsync → BuildAgentSkillsProvider
        // → AuditingSkillsContextProvider → AuditedChatFunction.DeriveSkillAwareToolName.
        var conversationId = await CreateConversationAsync();

        using var auditScope = _auditingManager.BeginScope();
        await WithUnitOfWorkAsync(async () =>
        {
            using (ChangeUser(OwnerUserId))
            {
                await _appService.SendMessageAsync(conversationId, new SendChatMessageInput
                {
                    Message = "Trigger the stub skill",
                    ClientTurnId = Guid.NewGuid()
                });
            }
        });

        _stubSkill.InvokeCallCount.ShouldBe(1);

        var auditLog = _auditingManager.Current!.Log;
        auditLog.ExtraProperties.ShouldContainKey(ChatTelemetryRecorder.AuditToolCallsPropertyName);

        var toolCalls = auditLog.ExtraProperties[ChatTelemetryRecorder.AuditToolCallsPropertyName]
            .ShouldBeOfType<List<ChatToolAuditEntry>>();
        toolCalls.ShouldContain(
            t => t.ToolName == "skill:test-stub-skill/invoke" && t.Outcome == ChatTelemetryOutcome.Success,
            "audited tool calls must include the derived skill:test-stub-skill/invoke entry");
    }

    [Fact]
    public async Task SendMessageAsync_Classifies_Skill_Only_Turn_As_Structured_Grounding()
    {
        // The mis-classification this guards against (Codex finding 3): without the audit
        // wrapping + ClassifyGrounding recognising "skill:*" as Structured, a turn that
        // answered a metadata question entirely through skills would be recorded as
        // GroundingSource.None and IsDegraded=true — the "honest signal" would lie.
        var conversationId = await CreateConversationAsync();

        using var auditScope = _auditingManager.BeginScope();
        ChatTurnResultDto result = null!;
        await WithUnitOfWorkAsync(async () =>
        {
            using (ChangeUser(OwnerUserId))
            {
                result = await _appService.SendMessageAsync(conversationId, new SendChatMessageInput
                {
                    Message = "Trigger the stub skill",
                    ClientTurnId = Guid.NewGuid()
                });
            }
        });

        result.GroundingSource.ShouldBe(GroundingSource.Structured);
        result.IsDegraded.ShouldBeFalse();

        var turn = _auditingManager.Current!.Log
            .ExtraProperties[ChatTelemetryRecorder.AuditTurnPropertyName]
            .ShouldBeOfType<ChatTurnAuditEntry>();
        turn.GroundingSource.ShouldBe(GroundingSource.Structured);
        turn.ToolCallSummary.ShouldNotBeNull();
        turn.ToolCallSummary!.ShouldContainKey("skill:test-stub-skill/invoke");
    }

    private async Task<Guid> CreateConversationAsync()
        => await WithUnitOfWorkAsync(async () =>
        {
            using (ChangeUser(OwnerUserId))
            {
                var dto = await _appService.CreateConversationAsync(new CreateChatConversationInput
                {
                    Title = "Skill invocation audit test"
                });
                return dto.Id;
            }
        });

    private IDisposable ChangeUser(Guid userId)
    {
        var claims = new List<Claim> { new(AbpClaimTypes.UserId, userId.ToString()) };
        return _principalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")));
    }
}

/// <summary>
/// Tiny class-based skill used by <see cref="ChatSkillInvocation_Tests"/>. Registered
/// manually (not via <c>[ExposeServices]</c> + auto-DI) so the production skill
/// inventory of other test modules stays untouched. Counts invocations so tests can
/// assert the skill actually ran, not just that the audit entry happened to appear.
/// </summary>
public sealed class StubAuditTestSkill : AgentClassSkill<StubAuditTestSkill>
{
    public int InvokeCallCount { get; private set; }

    public override AgentSkillFrontmatter Frontmatter { get; } = new(
        "test-stub-skill",
        "Test stub skill — exists only inside ChatSkillInvocation_Tests to drive the audit pipeline.");

    protected override string Instructions => "Test stub.";

    [AgentSkillScript("invoke")]
    [Description("Echoes a static OK payload.")]
    public string Invoke()
    {
        InvokeCallCount++;
        return """{"ok":true}""";
    }
}

[DependsOn(typeof(ChatAppServiceTestModule))]
public class ChatSkillInvocationTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Register a single stub skill (manually, not via [ExposeServices]) so the
        // production DI surface other test modules rely on stays unchanged.
        context.Services.AddSingleton<StubAuditTestSkill>();
        context.Services.AddSingleton<AgentSkill>(sp => sp.GetRequiredService<StubAuditTestSkill>());

        // Scripted chat client emits a run_skill_script call so MAF's
        // FunctionInvokingChatClient dispatches into the AgentSkillsProvider's
        // meta-tool → AuditingSkillsContextProvider's audited wrapper → the stub's
        // Invoke method.
        context.Services.AddSingleton<StubAuditSkillScriptedClient>();
        context.Services.AddSingleton<IChatClient>(sp =>
            new FunctionInvokingChatClient(
                sp.GetRequiredService<StubAuditSkillScriptedClient>(),
                NullLoggerFactory.Instance,
                sp)
            {
                MaximumIterationsPerRequest = 5
            });
    }
}

/// <summary>
/// Two-turn scripted chat client: turn 1 emits <c>run_skill_script("test-stub-skill",
/// "invoke", {})</c>; turn 2 emits the final assistant text. Mirrors the pattern of
/// <c>ScriptedToolCallingChatClient</c> in <see cref="ChatToolInvocation_Tests"/>
/// but targets MAF's skill meta-tool instead of <c>search_paperbase_documents</c>.
/// </summary>
public sealed class StubAuditSkillScriptedClient : IChatClient
{
    public int Calls { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<MEAI.ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        if (Calls == 1)
        {
            return Task.FromResult(new ChatResponse(new MEAI.ChatMessage(
                ChatRole.Assistant,
                new List<AIContent>
                {
                    new FunctionCallContent(
                        "call-stub-1",
                        "run_skill_script",
                        new Dictionary<string, object?>
                        {
                            ["skillName"] = "test-stub-skill",
                            ["scriptName"] = "invoke",
                            ["arguments"] = new Dictionary<string, object?>()
                        })
                })));
        }

        return Task.FromResult(new ChatResponse(
            new MEAI.ChatMessage(ChatRole.Assistant, "Stub skill ran successfully.")));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<MEAI.ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
