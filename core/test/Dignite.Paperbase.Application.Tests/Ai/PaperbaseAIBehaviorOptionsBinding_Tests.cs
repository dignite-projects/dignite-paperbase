using System.Collections.Generic;
using Dignite.Paperbase.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace Dignite.Paperbase.Ai;

/// <summary>
/// Test module that stacks an in-memory <c>PaperbaseAIBehavior</c> configuration
/// section on top of whatever <see cref="PaperbaseApplicationTestModule"/> already
/// provides. <see cref="PaperbaseApplicationModule.ConfigureServices"/> binds
/// <see cref="PaperbaseAIBehaviorOptions"/> to that section, so this module is the
/// vehicle that lets the test prove the binding is wired end-to-end.
/// </summary>
[DependsOn(typeof(PaperbaseApplicationTestModule))]
public class PaperbaseAIBehaviorOptionsBindingTestModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        var existing = context.Services.GetConfiguration();
        var stacked = new ConfigurationBuilder()
            .AddConfiguration(existing)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PaperbaseAIBehavior:DefaultLanguage"] = "en",
                ["PaperbaseAIBehavior:MaxDocumentTypesInClassificationPrompt"] = "25",
                ["PaperbaseAIBehavior:MaxTextLengthPerExtraction"] = "16000",
                ["PaperbaseAIBehavior:MaxTitleGenerationMarkdownLength"] = "2048",
            })
            .Build();

        context.Services.ReplaceConfiguration(stacked);
    }
}

/// <summary>
/// Acceptance test: configuration values placed under the <c>PaperbaseAIBehavior</c>
/// JSON section must reach <see cref="PaperbaseAIBehaviorOptions"/> consumers via
/// <see cref="IOptions{T}"/>.
/// </summary>
public class PaperbaseAIBehaviorOptionsBinding_Tests
    : PaperbaseApplicationTestBase<PaperbaseAIBehaviorOptionsBindingTestModule>
{
    private readonly PaperbaseAIBehaviorOptions _options;

    public PaperbaseAIBehaviorOptionsBinding_Tests()
    {
        _options = GetRequiredService<IOptions<PaperbaseAIBehaviorOptions>>().Value;
    }

    [Fact]
    public void Configuration_Values_Flow_Through_To_Options()
    {
        // Each assertion would fail with the class default if the
        // PaperbaseAIBehavior → PaperbaseAIBehaviorOptions binding ever regresses.
        _options.DefaultLanguage.ShouldBe("en");                                      // default "ja"
        _options.MaxDocumentTypesInClassificationPrompt.ShouldBe(25);                 // default 50
        _options.MaxTextLengthPerExtraction.ShouldBe(16000);                          // default 8000
        _options.MaxTitleGenerationMarkdownLength.ShouldBe(2048);                     // default 4000
    }
}
