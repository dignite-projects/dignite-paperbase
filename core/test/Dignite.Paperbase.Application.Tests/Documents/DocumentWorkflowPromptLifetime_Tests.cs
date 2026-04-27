using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.Documents;
using Dignite.Paperbase.Documents.AI;
using Dignite.Paperbase.Documents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// 验证三个 Workflow 在每次 RunAsync 调用时都重新从 IPromptProvider 取得提示词，
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

    // ── RelationInference ─────────────────────────────────────────────────────

    [Fact]
    public async Task RelationInferenceWorkflow_GetRelationInferencePrompt_CalledOnEachRunAsync()
    {
        var inner = BuildChatClientReturning("[]");

        var promptProvider = Substitute.For<IPromptProvider>();
        promptProvider.GetRelationInferencePrompt(Arg.Any<string>(), Arg.Any<double>())
            .Returns(new PromptTemplate("Prompt-A"), new PromptTemplate("Prompt-B"));

        var workflow = new DocumentRelationInferenceWorkflow(
            inner, Options.Create(new PaperbaseAIOptions()), promptProvider);

        var candidates = new List<RelationCandidate>
            { new RelationCandidate { DocumentId = Guid.NewGuid(), Summary = "s" } };

        await workflow.RunAsync(Guid.NewGuid(), "source text", candidates);
        await workflow.RunAsync(Guid.NewGuid(), "source text", candidates);

        promptProvider.Received(2).GetRelationInferencePrompt(Arg.Any<string>(), Arg.Any<double>());
    }

    [Fact]
    public async Task RelationInferenceWorkflow_SystemInstructions_ReflectFreshPromptOnEachCall()
    {
        var capturedSystemMessages = new List<string>();
        var inner = BuildChatClientCapturing(capturedSystemMessages, "[]");

        var promptProvider = Substitute.For<IPromptProvider>();
        promptProvider.GetRelationInferencePrompt(Arg.Any<string>(), Arg.Any<double>())
            .Returns(new PromptTemplate("Prompt-A"), new PromptTemplate("Prompt-B"));

        var workflow = new DocumentRelationInferenceWorkflow(
            inner, Options.Create(new PaperbaseAIOptions()), promptProvider);

        var candidates = new List<RelationCandidate>
            { new RelationCandidate { DocumentId = Guid.NewGuid(), Summary = "s" } };

        await workflow.RunAsync(Guid.NewGuid(), "source text", candidates);
        await workflow.RunAsync(Guid.NewGuid(), "source text", candidates);

        capturedSystemMessages.Count.ShouldBe(2);
        capturedSystemMessages[0].ShouldContain("Prompt-A");
        capturedSystemMessages[1].ShouldContain("Prompt-B");
    }

    // ── QA ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task QaWorkflow_GetQaPrompt_CalledOnEachRunAsync()
    {
        var inner = BuildChatClientReturning("Answer.");

        var promptProvider = Substitute.For<IPromptProvider>();
        promptProvider.GetQaPrompt(Arg.Any<string>())
            .Returns(new PromptTemplate("Prompt-A"), new PromptTemplate("Prompt-B"));

        var workflow = new DocumentQaWorkflow(
            inner, Options.Create(new PaperbaseAIOptions()), promptProvider);

        await workflow.RunFullTextAsync("question", "doc text");
        await workflow.RunFullTextAsync("question", "doc text");

        promptProvider.Received(2).GetQaPrompt(Arg.Any<string>());
    }

    [Fact]
    public async Task QaWorkflow_SystemInstructions_ReflectFreshPromptOnEachCall()
    {
        var capturedSystemMessages = new List<string>();
        var inner = BuildChatClientCapturing(capturedSystemMessages, "Answer.");

        var promptProvider = Substitute.For<IPromptProvider>();
        promptProvider.GetQaPrompt(Arg.Any<string>())
            .Returns(new PromptTemplate("Prompt-A"), new PromptTemplate("Prompt-B"));

        var workflow = new DocumentQaWorkflow(
            inner, Options.Create(new PaperbaseAIOptions()), promptProvider);

        await workflow.RunFullTextAsync("question", "doc text");
        await workflow.RunFullTextAsync("question", "doc text");

        capturedSystemMessages.Count.ShouldBe(2);
        capturedSystemMessages[0].ShouldContain("Prompt-A");
        capturedSystemMessages[1].ShouldContain("Prompt-B");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

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
