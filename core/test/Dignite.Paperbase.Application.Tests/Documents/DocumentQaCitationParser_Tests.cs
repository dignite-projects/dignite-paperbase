using System.Collections.Generic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Dignite.Paperbase.Documents;

public class DocumentQaCitationParser_Tests
{
    private static readonly ExposedWorkflow Sut = new();

    [Theory]
    [InlineData("[chunk 0]", 0)]
    [InlineData("[Chunk 1]", 1)]
    [InlineData("[CHUNK 2]", 2)]
    [InlineData("[chunk  3]", 3)]
    [InlineData("【chunk 4】", 4)]
    public void ParseCitedChunkIndices_Recognises_Variant(string answerText, int expectedIndex)
    {
        Sut.ParseCitedChunkIndices(answerText).ShouldContain(expectedIndex);
    }

    [Fact]
    public void ParseCitedChunkIndices_Returns_All_Cited_Indices()
    {
        var result = Sut.ParseCitedChunkIndices("See [chunk 1] and [chunk 3] for details.");
        result.Count.ShouldBe(2);
        result.ShouldContain(1);
        result.ShouldContain(3);
    }

    [Fact]
    public void ParseCitedChunkIndices_Returns_Empty_When_No_Citations()
    {
        Sut.ParseCitedChunkIndices("No citations here.").ShouldBeEmpty();
    }

    [Fact]
    public void ParseCitedChunkIndices_Deduplicates_Repeated_Citation()
    {
        var result = Sut.ParseCitedChunkIndices("[chunk 2] and [chunk 2] again.");
        result.Count.ShouldBe(1);
        result.ShouldContain(2);
    }

    private sealed class ExposedWorkflow : AI.Workflows.DocumentQaWorkflow
    {
        public ExposedWorkflow() : base(
            Substitute.For<IChatClient>(),
            Options.Create(new AI.PaperbaseAIOptions()),
            new AI.DefaultPromptProvider())
        { }

        public new HashSet<int> ParseCitedChunkIndices(string text)
            => base.ParseCitedChunkIndices(text);
    }
}
