using System.Linq;
using Dignite.Paperbase.Ai;
using Dignite.Paperbase.Documents.AI;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// TextChunker 在 Slice 4 增加了"语义边界回溯"——在 ChunkSize 附近向后查找最近的
/// 段落 / 句末标点 / 弱句末作为切点，避免在词或句中硬切。
/// 这里覆盖中/日/英三种文本以及无标点兜底场景，保证回退默认值不破坏现有数据。
/// </summary>
public class TextChunkerTests
{
    private static TextChunker BuildChunker(int chunkSize, int overlap, int tolerance)
    {
        var options = Options.Create(new PaperbaseAIOptions
        {
            ChunkSize = chunkSize,
            ChunkOverlap = overlap,
            ChunkBoundaryTolerance = tolerance,
        });
        return new TextChunker(options);
    }

    [Fact]
    public void Chunk_Empty_Or_Whitespace_Returns_Empty()
    {
        var chunker = BuildChunker(800, 100, 120);
        chunker.Chunk("").ShouldBeEmpty();
        chunker.Chunk("   \t\n").ShouldBeEmpty();
    }

    [Fact]
    public void Chunk_Short_Text_Returns_Single_Chunk()
    {
        var chunker = BuildChunker(800, 100, 120);
        var result = chunker.Chunk("Hello world.");
        result.Count.ShouldBe(1);
        result[0].ShouldBe("Hello world.");
    }

    [Fact]
    public void Chunk_Chinese_Snaps_To_Period_Within_Tolerance()
    {
        // 句号位于第 12 字符（index 11）。chunkSize=20, tolerance=10
        // → 回溯窗口 [10, 20)，覆盖到该句号；期望首块在句号处截断而非硬切到 20
        var text = "今天天气很好阳光也不错。明天也是好天气哦。";
        var chunker = BuildChunker(chunkSize: 20, overlap: 4, tolerance: 10);

        var result = chunker.Chunk(text);

        result.Count.ShouldBeGreaterThanOrEqualTo(1);
        result[0].ShouldEndWith("。");
        result[0].Length.ShouldBeLessThan(20);
    }

    [Fact]
    public void Chunk_Japanese_Snaps_To_Sentence_End()
    {
        // 日文混排：以 。 和 ！ 为切点
        var text = "今日はいい天気です。明日も晴れるでしょう！週末は雨かもしれません。";
        var chunker = BuildChunker(chunkSize: 14, overlap: 3, tolerance: 6);

        var result = chunker.Chunk(text);

        result.Count.ShouldBeGreaterThanOrEqualTo(2);
        result.Take(result.Count - 1).ShouldAllBe(c =>
            c.EndsWith("。") || c.EndsWith("！") || c.EndsWith("？"));
    }

    [Fact]
    public void Chunk_English_Snaps_On_Period_Followed_By_Whitespace()
    {
        // 英文：句号必须后接空白才算句末，避免在 3.14 这种小数处误切
        var text = "Pi is 3.14159 approximately. The next number is e. End.";
        var chunker = BuildChunker(chunkSize: 30, overlap: 5, tolerance: 10);

        var result = chunker.Chunk(text);

        // 第一块不应该在 "3." 处切，应在 "approximately." 之后
        result[0].ShouldContain("3.14159");
    }

    [Fact]
    public void Chunk_Paragraph_Break_Has_Higher_Priority_Than_Period()
    {
        var text = "段落一第一句。段落一第二句。\n\n段落二第一句。段落二第二句。";
        var chunker = BuildChunker(chunkSize: 18, overlap: 3, tolerance: 8);

        var result = chunker.Chunk(text);

        // 期望首块在 \n\n 处断开
        result[0].ShouldContain("段落一");
        result[0].ShouldNotContain("段落二");
    }

    [Fact]
    public void Chunk_No_Natural_Boundary_Falls_Back_To_ChunkSize()
    {
        // 一长串无标点字符（如 URL / Hash）：应仍然按 chunkSize 切分以保证终止
        var text = new string('a', 50);
        var chunker = BuildChunker(chunkSize: 10, overlap: 2, tolerance: 4);

        var result = chunker.Chunk(text);

        result.Count.ShouldBeGreaterThan(1);
        result[0].Length.ShouldBe(10);
        result.ShouldAllBe(c => c.Length <= 10);
    }

    [Fact]
    public void Chunk_Tolerance_Zero_Behaves_Like_Fixed_Width()
    {
        // tolerance=0 退化为原"固定字符长度"分块，保证可回滚行为
        var text = "今天天气很好阳光也不错。明天也是好天气哦。";
        var chunker = BuildChunker(chunkSize: 10, overlap: 2, tolerance: 0);

        var result = chunker.Chunk(text);

        // 每块（除最后）严格 10 字符
        for (var i = 0; i < result.Count - 1; i++)
        {
            result[i].Length.ShouldBe(10);
        }
    }

    [Fact]
    public void Chunk_Adjacent_Chunks_Have_Overlap()
    {
        var text = "第一句话内容很长。第二句话也很长。第三句话同样长。第四句话结束。";
        var chunker = BuildChunker(chunkSize: 15, overlap: 5, tolerance: 5);

        var result = chunker.Chunk(text);

        // 至少两块时，相邻块应有重叠（用前一块尾部 1 字符是否在后一块中粗略验证）
        if (result.Count >= 2)
        {
            var prev = result[0];
            var next = result[1];
            var tail = prev[^1].ToString();
            next.ShouldContain(tail);
        }
    }

    [Fact]
    public void Chunk_Loop_Always_Terminates_Even_With_Pathological_Options()
    {
        // overlap >= chunkSize 是非法配置，构造时应被夹紧；验证不会死循环
        var text = "abcdefghij" + new string('x', 100);
        var chunker = BuildChunker(chunkSize: 5, overlap: 999, tolerance: 999);

        var result = chunker.Chunk(text);

        result.Count.ShouldBeGreaterThan(0);
        result.Sum(c => c.Length).ShouldBeGreaterThanOrEqualTo(text.Length);
    }
}
