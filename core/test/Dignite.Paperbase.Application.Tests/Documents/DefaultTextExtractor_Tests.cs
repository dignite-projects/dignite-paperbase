using System.IO;
using System.Text;
using System.Threading.Tasks;
using Dignite.Paperbase.Abstractions.TextExtraction;
using Dignite.Paperbase.Ocr;
using Dignite.Paperbase.TextExtraction;
using Dignite.Paperbase.TextExtraction.ElBrunoMarkItDown;
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
    private readonly IOcrProvider _ocrProvider;

    public DefaultTextExtractor_Tests()
    {
        _extractor = GetRequiredService<ITextExtractor>();
        _ocrProvider = GetRequiredService<IOcrProvider>();
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

        // OCR Provider 直接负责输出 Markdown（即便是扁平段落），DefaultTextExtractor 透传字段。
        result.Markdown.ShouldBe("fake ocr markdown");
        result.Confidence.ShouldBe(0.95);
        result.UsedOcr.ShouldBeTrue();

        // auto 直接进 general 全量识别，无事前 probe。
        await _ocrProvider.Received(1).RecognizeAsync(
            Arg.Any<Stream>(),
            Arg.Is<OcrOptions>(o => o.OcrProfileCode == OcrProfileCodes.General));
    }

    [Fact]
    public async Task Should_Use_Markdown_Provider_For_Txt_Files()
    {
        var content = "Hello World";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var ctx = new TextExtractionContext
        {
            ContentType = "text/plain",
            FileExtension = ".txt"
        };

        var result = await _extractor.ExtractAsync(stream, ctx);

        result.Markdown.ShouldContain("Hello World");
        result.UsedOcr.ShouldBeFalse();
        // 数字版抽取无 OCR 概念——Confidence 为 null，不应兜底成 1.0。
        result.Confidence.ShouldBeNull();
    }

    [Fact]
    public async Task Should_Preserve_Markdown_For_Md_Files()
    {
        var content = "# Title\n\nSome content.";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var ctx = new TextExtractionContext
        {
            ContentType = "text/markdown",
            FileExtension = ".md"
        };

        var result = await _extractor.ExtractAsync(stream, ctx);

        result.UsedOcr.ShouldBeFalse();
        // Markdown 字段保留了原始结构（含 # 标题）
        result.Markdown.ShouldNotBeNullOrEmpty();
        result.Markdown.ShouldContain("# Title");
        result.Markdown.ShouldContain("Some content");
    }

    [Fact]
    public async Task Should_Fallback_To_Ocr_For_Scanned_Pdf()
    {
        // 非真实 PDF 字节 → ElBruno 转换失败 → 回退 OCR
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a real pdf"));
        var ctx = new TextExtractionContext
        {
            ContentType = "application/pdf",
            FileExtension = ".pdf"
        };

        var result = await _extractor.ExtractAsync(stream, ctx);

        result.UsedOcr.ShouldBeTrue();
        result.Markdown.ShouldBe("fake ocr markdown");
    }

    [Fact]
    public async Task Should_Retry_Once_With_Targeted_Profile_When_Quality_Is_Low()
    {
        // 首次 general 识别得到低置信表格结果 → 诊断 table-structure → 定向重试 table-heavy → 取更优结果。
        _ocrProvider.RecognizeAsync(Arg.Any<Stream>(), Arg.Any<OcrOptions>())
            .Returns(
                new OcrResult
                {
                    Markdown = "| A | B |\n|---|---|\n| bad | table |",
                    Confidence = 0.40,
                    PageCount = 1
                },
                new OcrResult
                {
                    Markdown = "| A | B |\n|---|---|\n| good | table |",
                    Confidence = 0.93,
                    PageCount = 1
                });

        var stream = new MemoryStream(new byte[] { 0xFF, 0xD8 });
        var ctx = new TextExtractionContext
        {
            ContentType = "image/jpeg",
            FileExtension = ".jpg"
        };

        var result = await _extractor.ExtractAsync(stream, ctx);

        result.Markdown.ShouldContain("good");

        await _ocrProvider.Received(1).RecognizeAsync(
            Arg.Any<Stream>(),
            Arg.Is<OcrOptions>(o => o.OcrProfileCode == OcrProfileCodes.General));
        await _ocrProvider.Received(1).RecognizeAsync(
            Arg.Any<Stream>(),
            Arg.Is<OcrOptions>(o => o.OcrProfileCode == OcrProfileCodes.TableHeavy));
    }

    [Fact]
    public async Task Should_Not_Select_Empty_Retry_Result_Over_Meaningful_Text()
    {
        _ocrProvider.RecognizeAsync(Arg.Any<Stream>(), Arg.Any<OcrOptions>())
            .Returns(
                new OcrResult
                {
                    Markdown = "| A | B |\n|---|---|\n| kept | table |",
                    Confidence = 0.50,
                    PageCount = 1
                },
                new OcrResult
                {
                    Markdown = string.Empty,
                    Confidence = 0.80,
                    PageCount = 1
                });

        var stream = new MemoryStream(new byte[] { 0xFF, 0xD8 });
        var ctx = new TextExtractionContext
        {
            ContentType = "image/jpeg",
            FileExtension = ".jpg"
        };

        var result = await _extractor.ExtractAsync(stream, ctx);

        // 重试结果为空 → 不优于首次有意义文本 → 保留首次 general 结果。
        result.Markdown.ShouldContain("kept");

        await _ocrProvider.Received(1).RecognizeAsync(
            Arg.Any<Stream>(),
            Arg.Is<OcrOptions>(o => o.OcrProfileCode == OcrProfileCodes.General));
        await _ocrProvider.Received(1).RecognizeAsync(
            Arg.Any<Stream>(),
            Arg.Is<OcrOptions>(o => o.OcrProfileCode == OcrProfileCodes.TableHeavy));
    }

    [Fact]
    public async Task Should_Use_Explicit_Profile_Without_Auto_Resolution()
    {
        var stream = new MemoryStream(new byte[] { 0xFF, 0xD8 });
        var ctx = new TextExtractionContext
        {
            ContentType = "image/jpeg",
            FileExtension = ".jpg",
            OcrProfileCode = OcrProfileCodes.HighAccuracy
        };

        var result = await _extractor.ExtractAsync(stream, ctx);

        result.UsedOcr.ShouldBeTrue();
        // 显式 profile 直接生效，不经 auto→general。
        await _ocrProvider.Received(1).RecognizeAsync(
            Arg.Any<Stream>(),
            Arg.Is<OcrOptions>(o => o.OcrProfileCode == OcrProfileCodes.HighAccuracy));
        await _ocrProvider.DidNotReceive().RecognizeAsync(
            Arg.Any<Stream>(),
            Arg.Is<OcrOptions>(o => o.OcrProfileCode == OcrProfileCodes.General));
    }

    [DependsOn(
        typeof(PaperbaseTextExtractionModule),
        typeof(PaperbaseTextExtractionElBrunoMarkItDownModule))]
    public class TextExtractionTestModule : AbpModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            // 取消 probe 后 OCR provider 只需实现 IOcrProvider；orchestrator 信任 provider，
            // 结构信号事后从全量结果派生。
            var fakeOcr = Substitute.For<IOcrProvider>();
            fakeOcr.RecognizeAsync(Arg.Any<Stream>(), Arg.Any<OcrOptions>())
                .Returns(new OcrResult
                {
                    Markdown = "fake ocr markdown",
                    Confidence = 0.95,
                    PageCount = 1
                });

            context.Services.AddSingleton<IOcrProvider>(fakeOcr);
        }
    }
}
