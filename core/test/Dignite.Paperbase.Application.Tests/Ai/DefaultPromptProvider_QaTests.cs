using Dignite.Paperbase.Ai;
using Shouldly;
using Xunit;

namespace Dignite.Paperbase.Ai;

/// <summary>
/// Acceptance test for Issue #85 (Slice 3): the QA system prompt must direct the
/// model to call <c>search_paperbase_documents</c>. Under the single MAF tool-calling
/// chat path, no document content is auto-injected — if the prompt does not actively
/// instruct tool use, the model will reply "no content available" without ever
/// invoking the search tool, surfacing as <c>IsDegraded = true</c> on every turn.
///
/// This test catches the regression at the prompt level rather than relying on a
/// model-roundtrip integration test (which would require simulating tool-call flow
/// through <c>FunctionInvokingChatClient</c>).
/// </summary>
public class DefaultPromptProvider_QaTests
{
    private readonly DefaultPromptProvider _provider = new();

    [Fact]
    public void QaPrompt_References_Search_Tool_By_Name()
    {
        // The exact tool name DocumentChatAppService registers must appear verbatim
        // so the model can match instructions to the available tool.
        var prompt = _provider.GetQaPrompt("ja").SystemInstructions;
        prompt.ShouldContain("search_paperbase_documents");
    }

    [Fact]
    public void QaPrompt_Directs_Model_To_Always_Call_Search_For_Document_Questions()
    {
        // The directive must be unambiguous — without it, a model receiving an
        // empty context (no auto-injection in the new path) will follow the older
        // "no content provided, give up" pattern and never invoke the tool.
        var prompt = _provider.GetQaPrompt("ja").SystemInstructions;
        prompt.ShouldContain("always call");
        prompt.ShouldContain("at least once");
    }

    [Fact]
    public void QaPrompt_Mentions_Contributor_Tool_Chaining()
    {
        // Business-module tool contributors (e.g. ContractChatToolContributor) attach
        // structured-query tools alongside search_paperbase_documents. The prompt
        // must guide the model to chain them so a structured tool's IDs feed back
        // into a focused RAG pass.
        var prompt = _provider.GetQaPrompt("ja").SystemInstructions;
        prompt.ShouldContain("chain");
    }

    [Fact]
    public void QaPrompt_Preserves_Citation_Format_Contract()
    {
        // Existing post-processing parses [chunk N] citations; the prompt must keep
        // requiring exactly that shape so the citation extraction does not regress.
        var prompt = _provider.GetQaPrompt("ja").SystemInstructions;
        prompt.ShouldContain("[chunk N]");
        prompt.ShouldContain("halfwidth");
    }
}
