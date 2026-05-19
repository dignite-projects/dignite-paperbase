using System.Collections.Generic;
using System.Text.Json;
using Dignite.Paperbase.Documents;
using Shouldly;
using Volo.Abp.Data;
using Xunit;

namespace Dignite.Paperbase.Application.Tests.Documents;

/// <summary>
/// 验证 <see cref="Dignite.Paperbase.DocumentPipelineRunToDocumentPipelineRunDtoMapper"/>
/// 把 <c>ExtraProperties["Candidates"]</c> 上的两种形态都正确投影成强类型
/// <see cref="DocumentPipelineRunDto.Candidates"/>：
///   (a) 同一 UoW 内尚未持久化往返时 — 写入的原始 <see cref="PipelineRunCandidate"/> 列表；
///   (b) EF Core / ABP 持久化读回时 — <see cref="JsonElement"/>。
///
/// 这条 mapping 是 Angular / .NET HttpApi.Client 拿到强类型 candidates 的核心保障，
/// 一旦 mapper 内 ExtraProperties → Candidates 的 wrapper 被去掉，前端会回退到字符串 key cast，
/// drift 风险立刻回来。
/// </summary>
public class DocumentPipelineRunDtoMapper_Tests
{
    private readonly DocumentPipelineRunToDocumentPipelineRunDtoMapper _mapper = new();

    [Fact]
    public void Maps_Candidates_From_Raw_List_In_ExtraProperties()
    {
        var source = CreateClassificationRun();
        source.SetProperty(
            PipelineRunExtraPropertyNames.ClassificationCandidates,
            new List<PipelineRunCandidate>
            {
                new("contract.general", 0.64),
                new("invoice.standard", 0.31)
            });

        var dto = _mapper.Map(source);

        dto.Candidates.ShouldNotBeNull();
        dto.Candidates.Count.ShouldBe(2);
        dto.Candidates[0].TypeCode.ShouldBe("contract.general");
        dto.Candidates[0].ConfidenceScore.ShouldBe(0.64);
        dto.Candidates[1].TypeCode.ShouldBe("invoice.standard");
        dto.Candidates[1].ConfidenceScore.ShouldBe(0.31);
    }

    [Fact]
    public void Maps_Candidates_From_JsonElement_As_Persisted_Reads_Return()
    {
        var source = CreateClassificationRun();
        var json = JsonSerializer.SerializeToElement(new[]
        {
            new PipelineRunCandidate("contract.general", 0.64),
            new PipelineRunCandidate("invoice.standard", 0.31)
        });
        source.SetProperty(PipelineRunExtraPropertyNames.ClassificationCandidates, json);

        var dto = _mapper.Map(source);

        dto.Candidates.ShouldNotBeNull();
        dto.Candidates.Count.ShouldBe(2);
        dto.Candidates[0].TypeCode.ShouldBe("contract.general");
        dto.Candidates[0].ConfidenceScore.ShouldBe(0.64);
    }

    [Fact]
    public void Candidates_Is_Null_When_ExtraProperties_Has_No_Key()
    {
        var source = CreateClassificationRun();

        var dto = _mapper.Map(source);

        dto.Candidates.ShouldBeNull();
    }

    [Fact]
    public void Candidates_Is_Null_When_Stored_Value_Is_Not_An_Array()
    {
        var source = CreateClassificationRun();
        var notAnArray = JsonSerializer.SerializeToElement(new { unexpected = "shape" });
        source.SetProperty(PipelineRunExtraPropertyNames.ClassificationCandidates, notAnArray);

        var dto = _mapper.Map(source);

        dto.Candidates.ShouldBeNull();
    }

    /// <summary>
    /// 模拟 HttpApi.Client 在 .NET 客户端的反序列化路径：
    /// 服务端 STJ 序列化 DTO -> JSON -> 客户端 STJ 反序列化回 DTO -> Candidates 仍是强类型。
    /// Candidates 必须是 <c>{ get; set; }</c> 才能被 STJ 直接 set；改成 get-only computed
    /// property 时这条测试会立刻 fail（参 review 反例）。
    /// </summary>
    [Fact]
    public void Candidates_Survives_StjRoundtrip_For_DotNetHttpApiClient()
    {
        var source = CreateClassificationRun();
        source.SetProperty(
            PipelineRunExtraPropertyNames.ClassificationCandidates,
            new List<PipelineRunCandidate> { new("contract.general", 0.64) });
        var serverDto = _mapper.Map(source);

        var json = JsonSerializer.Serialize(serverDto);
        var roundtripped = JsonSerializer.Deserialize<DocumentPipelineRunDto>(json);

        roundtripped.ShouldNotBeNull();
        roundtripped.Candidates.ShouldNotBeNull();
        roundtripped.Candidates.Count.ShouldBe(1);
        roundtripped.Candidates[0].TypeCode.ShouldBe("contract.general");
        roundtripped.Candidates[0].ConfidenceScore.ShouldBe(0.64);
    }

    // 显式 subclass 访问 protected 无参构造器，避免 reflection 黑魔法。
    // 这里**不**调 ABP 的工厂路径（PipelineRunManager.QueueAsync 需要完整 DI + UoW），
    // 因为本测试只覆盖 mapper 的字段反序列化，与聚合根工厂语义无关。
    private static DocumentPipelineRun CreateClassificationRun() => new TestDocumentPipelineRun();

    private sealed class TestDocumentPipelineRun : DocumentPipelineRun;
}
