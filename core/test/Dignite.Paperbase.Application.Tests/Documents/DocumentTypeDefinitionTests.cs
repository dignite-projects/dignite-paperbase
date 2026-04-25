using System;
using Dignite.Paperbase.Abstractions.Documents;
using Shouldly;
using Xunit;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// DocumentTypeDefinition 的格式契约测试：TypeCode 必须遵循
/// "&lt;owner-module&gt;.&lt;sub-type&gt;" 命名约定。
/// </summary>
public class DocumentTypeDefinitionTests
{
    [Fact]
    public void Constructor_Should_Accept_Valid_TypeCode()
    {
        var def = new DocumentTypeDefinition("contract.general", "合同");

        def.TypeCode.ShouldBe("contract.general");
        def.DisplayName.ShouldBe("合同");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_Should_Reject_Null_Or_Whitespace_TypeCode(string? typeCode)
    {
        Should.Throw<ArgumentException>(
            () => new DocumentTypeDefinition(typeCode!, "合同"));
    }

    [Theory]
    [InlineData("contract")]            // 没有点
    [InlineData(".general")]            // 前缀为空
    [InlineData("contract.")]           // 子类型为空
    public void Constructor_Should_Reject_TypeCode_Without_OwnerModule_Prefix(string typeCode)
    {
        var ex = Should.Throw<ArgumentException>(
            () => new DocumentTypeDefinition(typeCode, "合同"));

        ex.Message.ShouldContain("<owner-module>.<sub-type>");
    }

    [Fact]
    public void Constructor_Should_Reject_Empty_DisplayName()
    {
        Should.Throw<ArgumentException>(
            () => new DocumentTypeDefinition("contract.general", ""));
    }

    [Fact]
    public void ConfidenceThreshold_Default_Should_Match_ClassificationDefaults()
    {
        var def = new DocumentTypeDefinition("contract.general", "合同");

        def.ConfidenceThreshold.ShouldBe(ClassificationDefaults.DefaultConfidenceThreshold);
    }
}
