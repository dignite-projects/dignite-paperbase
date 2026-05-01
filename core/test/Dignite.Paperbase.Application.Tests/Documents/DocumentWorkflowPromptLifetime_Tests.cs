using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Documents;
using Dignite.Paperbase.Ai;
using Dignite.Paperbase.Chat.Search;
using Dignite.Paperbase.Documents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// 验证 Workflow 在每次 RunAsync 调用时都重新从 IPromptProvider 取得提示词，
/// 而非在构造函数中固定 — 保证 IPromptProvider 的动态变更（语言切换、租户定制）
/// 在下一次调用时立即生效，无需重启宿主。
/// </summary>
public class DocumentWorkflowPromptLifetime_Tests
{
    // ── Classification ────────────────────────────────────────────────────────

    [Fact]
    public async Task ClassificationWorkflow_GetClassificationPrompt_CalledOnEachRunAsync()
    {
        var inner = BuildChatClientReturning(
            """{"typeCode":null,"confidence":0,"reason":"none","candidates":[]}""");

        var promptProvider = Substitute.For<IPromptProvider>();
        promptProvider.GetClassificationPrompt(Arg.Any<string>())
            .Returns(new PromptTemplate("Prompt-A"), new PromptTemplate("Prompt-B"));

        var workflow = new DocumentClassificationWorkflow(
            inner, Options.Create(new PaperbaseAIOptions()), promptProvider);

        var types = new List<DocumentTypeDefinition>
            { new DocumentTypeDefinition("contract.general", "合同") };

        await workflow.RunAsync(types, "text one");
        await workflow.RunAsync(types, "text two");

        // Prompt is fetched from provider on every call, not once at construction.
        promptProvider.Received(2).GetClassificationPrompt(Arg.Any<string>());
    }

    [Fact]
    public async Task ClassificationWorkflow_SystemInstructions_ReflectFreshPromptOnEachCall()
    {
        var capturedSystemMessages = new List<string>();
        var inner = BuildChatClientCapturing(
            capturedSystemMessages,
            """{"typeCode":null,"confidence":0,"reason":"none","candidates":[]}""");

        var promptProvider = Substitute.For<IPromptProvider>();
        promptProvider.GetClassificationPrompt(Arg.Any<string>())
            .Returns(new PromptTemplate("Prompt-A"), new PromptTemplate("Prompt-B"));

        var workflow = new DocumentClassificationWorkflow(
            inner, Options.Create(new PaperbaseAIOptions()), promptProvider);

        var types = new List<DocumentTypeDefinition>
            { new DocumentTypeDefinition("contract.general", "合同") };

        await workflow.RunAsync(types, "text");
        await workflow.RunAsync(types, "text");

        capturedSystemMessages.Count.ShouldBe(2);
        capturedSystemMessages[0].ShouldContain("Prompt-A");
        capturedSystemMessages[1].ShouldContain("Prompt-B");
    }

    // ── Rerank ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RerankWorkflow_GetRerankPrompt_CalledOnEachRunAsync()
    {
        var inner = BuildChatClientReturning(
            """{"items":[{"id":1,"score":1.0},{"id":0,"score":0.2}]}""");

        var promptProvider = Substitute.For<IPromptProvider>();
        promptProvider.GetRerankPrompt(Arg.Any<string>())
            .Returns(new PromptTemplate("Prompt-A"), new PromptTemplate("Prompt-B"));

        var workflow = new DocumentRerankWorkflow(
            inner, Options.Create(new PaperbaseAIOptions()), promptProvider);

        var candidates = BuildRerankCandidates();

        await workflow.RerankAsync("question one", candidates, topK: 1);
        await workflow.RerankAsync("question two", candidates, topK: 1);

        promptProvider.Received(2).GetRerankPrompt(Arg.Any<string>());
    }

    [Fact]
    public async Task RerankWorkflow_SystemInstructions_ReflectFreshPromptOnEachCall()
    {
        var capturedSystemMessages = new List<string>();
        var inner = BuildChatClientCapturing(
            capturedSystemMessages,
            """{"items":[{"id":1,"score":1.0},{"id":0,"score":0.2}]}""");

        var promptProvider = Substitute.For<IPromptProvider>();
        promptProvider.GetRerankPrompt(Arg.Any<string>())
            .Returns(new PromptTemplate("Prompt-A"), new PromptTemplate("Prompt-B"));

        var workflow = new DocumentRerankWorkflow(
            inner, Options.Create(new PaperbaseAIOptions()), promptProvider);

        var candidates = BuildRerankCandidates();

        await workflow.RerankAsync("question", candidates, topK: 1);
        await workflow.RerankAsync("question", candidates, topK: 1);

        capturedSystemMessages.Count.ShouldBe(2);
        capturedSystemMessages[0].ShouldContain("Prompt-A");
        capturedSystemMessages[1].ShouldContain("Prompt-B");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static List<RerankCandidate> BuildRerankCandidates()
        =>
        [
            new("first passage", 0.8),
            new("second passage", 0.7)
        ];

    private static IChatClient BuildChatClientReturning(string responseText)
    {
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(
                new ChatResponse([new ChatMessage(ChatRole.Assistant, responseText)])));
        return inner;
    }

    private static IChatClient BuildChatClientCapturing(
        List<string> capturedSystemMessages,
        string responseText)
    {
        var inner = Substitute.For<IChatClient>();
        inner.GetResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var msgs = call.Arg<IEnumerable<ChatMessage>>().ToList();
                var opts = call.Arg<ChatOptions?>();

                // ChatClientAgent passes system instructions via ChatOptions.Instructions
                // (Microsoft.Extensions.AI ≥ 10.5) rather than as a ChatRole.System message.
                // Collect both sources so the assertion stays correct across versions.
                var allText = string.Join(" ",
                    msgs.Select(m => m.Text ?? "").Append(opts?.Instructions ?? ""));

                capturedSystemMessages.Add(allText);
                return Task.FromResult(
                    new ChatResponse([new ChatMessage(ChatRole.Assistant, responseText)]));
            });
        return inner;
    }
}
