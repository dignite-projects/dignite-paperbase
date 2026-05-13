using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Chat.Telemetry;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Dignite.Paperbase.Chat;

/// <summary>
/// Issue #149 (Codex adversarial review finding 3): decorates an
/// <see cref="AgentSkillsProvider"/> (or any other <see cref="AIContextProvider"/> that
/// emits MAF skill-system tools) so each <see cref="AIFunction"/> it advertises is
/// wrapped in <c>AuditedChatFunction</c> — bringing skill script invocations into the
/// same audit + grounding pipeline that <c>search_paperbase_documents</c> already lives in.
///
/// <para>
/// Without this decorator, <c>run_skill_script</c> / <c>load_skill</c> /
/// <c>read_skill_resource</c> calls would not produce <see cref="ChatToolAuditEntry"/>
/// records and <see cref="ChatTelemetryRecorder.ClassifyGrounding"/> would treat a
/// structured-only turn as ungrounded — flagging it incorrectly as
/// <see cref="Telemetry.GroundingSource.None"/> / <c>IsDegraded = true</c>.
/// </para>
///
/// <para>
/// The decorator overrides <see cref="AIContextProvider.InvokingCoreAsync"/> directly
/// (rather than <c>ProvideAIContextAsync</c>) because MAF's base merging would otherwise
/// double-process the upstream context: we delegate to the inner provider's
/// <see cref="AIContextProvider.InvokingAsync"/>, which already returns a fully-merged
/// <see cref="AIContext"/>, then walk its <see cref="AIContext.Tools"/> in-place to
/// replace each <see cref="AIFunction"/> with an audited variant.
/// </para>
///
/// <para>
/// <strong>Intentional non-forwarding of post-invocation hooks.</strong> This decorator
/// only forwards <see cref="AIContextProvider.InvokingAsync"/> and
/// <see cref="AIContextProvider.StateKeys"/>. The base
/// <see cref="AIContextProvider.InvokedAsync"/> and <c>StoreAIContextAsync</c> hooks
/// are deliberately not delegated to the inner provider because <see cref="AgentSkillsProvider"/>
/// in MAF 1.5 does not override them — it has no per-invocation state to update once
/// the response comes back, and no per-message storage to persist. If a future MAF
/// version adds behaviour to those hooks (or if Paperbase swaps the inner provider for
/// something stateful), this decorator must grow forwarding overrides for them too.
/// Catch it via the <c>ChatSkillInvocation_Tests</c> guards in this assembly: those
/// tests exercise the full <c>SendMessageAsync → run_skill_script → audit log</c> path
/// and will fail if the wrapping breaks.
/// </para>
/// </summary>
internal sealed class AuditingSkillsContextProvider : AIContextProvider
{
    private readonly AIContextProvider _inner;
    private readonly ChatToolFactory _toolFactory;
    private readonly ChatToolContext _toolContext;

    public AuditingSkillsContextProvider(
        AIContextProvider inner,
        ChatToolFactory toolFactory,
        ChatToolContext toolContext)
    {
        _inner = inner;
        _toolFactory = toolFactory;
        _toolContext = toolContext;
    }

    public override IReadOnlyList<string> StateKeys => _inner.StateKeys;

    protected override async ValueTask<AIContext> InvokingCoreAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        var inner = await _inner.InvokingAsync(context, cancellationToken).ConfigureAwait(false);
        if (inner.Tools is null)
        {
            return inner;
        }

        // AIContext.Tools is IEnumerable<AITool>?; materialise once before wrapping so
        // the resulting list is enumerable repeatedly.
        var wrapped = new List<AITool>();
        var hasAny = false;
        foreach (var tool in inner.Tools)
        {
            hasAny = true;
            wrapped.Add(tool is AIFunction fn
                ? _toolFactory.WrapAudited(fn, _toolContext)
                : tool);
        }

        if (!hasAny)
        {
            return inner;
        }

        return new AIContext
        {
            Instructions = inner.Instructions,
            Messages = inner.Messages,
            Tools = wrapped
        };
    }
}
