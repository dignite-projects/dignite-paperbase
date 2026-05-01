using Dignite.Paperbase.Documents.Pipelines.Classification;
using Microsoft.Extensions.AI;
using Shouldly;
using Xunit;

namespace Dignite.Paperbase.Documents;

public class DocumentClassificationJsonModeTests
{
    [Fact]
    public void BuildRunOptions_StrictMode_Sets_Json_ResponseFormat()
    {
        var opts = DocumentClassificationWorkflow.BuildRunOptions(useStrictJsonMode: true);

        opts.ShouldNotBeNull();
        opts.ChatOptions.ShouldNotBeNull();
        opts.ChatOptions.ResponseFormat.ShouldBe(ChatResponseFormat.Json);
    }

    [Fact]
    public void BuildRunOptions_FallbackMode_Returns_Null()
    {
        DocumentClassificationWorkflow.BuildRunOptions(useStrictJsonMode: false).ShouldBeNull();
    }
}
