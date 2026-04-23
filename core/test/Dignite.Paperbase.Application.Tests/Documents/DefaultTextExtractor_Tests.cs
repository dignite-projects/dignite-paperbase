using System.IO;
using System.Text;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.TextExtraction;
using Dignite.Paperbase.Ocr;
using Dignite.Paperbase.TextExtraction;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Volo.Abp.Modularity;
using Volo.Abp.Testing;
using Xunit;

namespace Dignite.Paperbase.Documents;

public class DefaultTextExtractor_Tests : AbpIntegratedTest<DefaultTextExtractor_Tests.TextExtractionTestModule>
{
    private readonly ITextExtractor _extractor;

    public DefaultTextExtractor_Tests()
    {
        _extractor = GetRequiredService<ITextExtractor>();
    }

    [Fact]
    public async Task Should_Use_OcrProvider_For_Image_Files()
    {
        var stream = new MemoryStream(new byte[] { 0xFF, 0xD8 }); // fake JPEG bytes
        var ctx = new TextExtractionContext
        {
            ContentType = "image/jpeg",
            FileExtension = ".jpg"
        };

        var result = await _extractor.ExtractAsync(stream, ctx);

        result.ExtractedText.ShouldBe("fake ocr text");
        result.Confidence.ShouldBe(0.95);
    }

    [Fact]
    public async Task Should_Use_PlainText_For_Txt_Files()
    {
        var content = "Hello World";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var ctx = new TextExtractionContext
        {
            ContentType = "text/plain",
            FileExtension = ".txt"
        };

        var result = await _extractor.ExtractAsync(stream, ctx);

        result.ExtractedText.ShouldBe(content);
        result.Confidence.ShouldBe(1.0);
    }

    [Fact]
    public async Task Should_Use_PlainText_For_Markdown_Files()
    {
        var content = "# Title\n\nSome content.";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var ctx = new TextExtractionContext
        {
            ContentType = "text/markdown",
            FileExtension = ".md"
        };

        var result = await _extractor.ExtractAsync(stream, ctx);

        result.ExtractedText.ShouldBe(content);
        result.Confidence.ShouldBe(1.0);
    }

    [Fact]
    public async Task Should_Fallback_To_Ocr_For_Scanned_Pdf()
    {
        // 空 PDF（无文字层）→ NoTextLayerException → OCR fallback
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a real pdf"));
        var ctx = new TextExtractionContext
        {
            ContentType = "application/pdf",
            FileExtension = ".pdf"
        };

        var result = await _extractor.ExtractAsync(stream, ctx);

        // Broken PDF triggers NoTextLayerException → OCR path
        result.ExtractedText.ShouldBe("fake ocr text");
    }

    [DependsOn(typeof(PaperbaseTextExtractionModule))]
    public class TextExtractionTestModule : AbpModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            var fakeOcr = Substitute.For<IOcrProvider>();
            fakeOcr.RecognizeAsync(Arg.Any<Stream>(), Arg.Any<OcrOptions>())
                .Returns(new OcrResult
                {
                    RawText = "fake ocr text",
                    Confidence = 0.95,
                    PageCount = 1
                });

            context.Services.AddSingleton(fakeOcr);
        }
    }
}
