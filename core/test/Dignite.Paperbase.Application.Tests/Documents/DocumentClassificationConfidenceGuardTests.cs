using Dignite.Paperbase.Documents.AI.Workflows;
using Shouldly;
using Xunit;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// 回归保护：DocumentClassificationWorkflow 对 LLM 越界置信度的两层防御。
///
/// 背景：LLM 偶发返回 NaN / &lt;0 / &gt;1 的 confidence。如果原样透传到
/// Document.ApplyAutomaticClassificationResult，会触发聚合根的 Check.Range(0,1)
/// 抛 ArgumentException，导致整条 PipelineRun 翻为 Failed，违反"低置信度 → PendingReview"
/// 的产品语义。修复策略：
///   - top-level confidence 越界 → 视为"无可信结论"（typeCode=null + confidence=0），
///     由 BackgroundJob 走 LowConfidence 分支触发 PendingReview。
///   - 候选项 confidence 越界 → Clamp 到 [0,1]（候选项仅供 UI / Run 持久化，不影响聚合根不变量）。
///
/// 这两个判定函数是修复的全部"承重"逻辑；RunAsync 中环绕它们的分支语句仅做赋值，目视即可正确。
/// </summary>
public class DocumentClassificationConfidenceGuardTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void IsValidConfidence_Returns_True_For_Values_In_Range(double value)
    {
        DocumentClassificationWorkflow.IsValidConfidence(value).ShouldBeTrue();
    }

    [Theory]
    [InlineData(-0.001)]
    [InlineData(1.001)]
    [InlineData(1.5)]
    [InlineData(-100)]
    [InlineData(100)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void IsValidConfidence_Returns_False_For_Out_Of_Range_Or_Invalid(double value)
    {
        DocumentClassificationWorkflow.IsValidConfidence(value).ShouldBeFalse();
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(1.0, 1.0)]
    public void ClampConfidence_Preserves_In_Range_Values(double input, double expected)
    {
        DocumentClassificationWorkflow.ClampConfidence(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(-1.0)]
    [InlineData(-100)]
    [InlineData(double.NegativeInfinity)]
    public void ClampConfidence_Returns_Zero_For_Below_Range(double input)
    {
        DocumentClassificationWorkflow.ClampConfidence(input).ShouldBe(0d);
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(2.0)]
    [InlineData(100)]
    [InlineData(double.PositiveInfinity)]
    public void ClampConfidence_Returns_One_For_Above_Range(double input)
    {
        DocumentClassificationWorkflow.ClampConfidence(input).ShouldBe(1d);
    }

    [Fact]
    public void ClampConfidence_Returns_Zero_For_NaN()
    {
        DocumentClassificationWorkflow.ClampConfidence(double.NaN).ShouldBe(0d);
    }
}
