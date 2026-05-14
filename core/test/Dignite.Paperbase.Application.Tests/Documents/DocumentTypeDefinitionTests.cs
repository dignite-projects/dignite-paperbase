using System;
using Dignite.Paperbase.Abstractions.Documents;
using Shouldly;
using Volo.Abp.Localization;
using Xunit;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// DocumentTypeDefinition 的格式契约测试：TypeCode 必须遵循
/// "&lt;owner-module&gt;.&lt;sub-type&gt;" 命名约定；DisplayName 必须是 ILocalizableString。
/// </summary>
public class DocumentTypeDefinitionTests
{
    [Fact]
    public void Constructor_Should_Accept_Valid_TypeCode()
    {
        var displayName = new FixedLocalizableString("合同");
        var def = new DocumentTypeDefinition("contract.general", displayName);

        def.TypeCode.ShouldBe("contract.general");
        def.DisplayName.ShouldBeSameAs(displayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_Should_Reject_Null_Or_Whitespace_TypeCode(string? typeCode)
    {
        Should.Throw<ArgumentException>(
            () => new DocumentTypeDefinition(typeCode!, new FixedLocalizableString("合同")));
    }

    [Theory]
    [InlineData("contract")]            // 没有点
    [InlineData(".general")]            // 前缀为空
    [InlineData("contract.")]           // 子类型为空
    public void Constructor_Should_Reject_TypeCode_Without_OwnerModule_Prefix(string typeCode)
    {
        var ex = Should.Throw<ArgumentException>(
            () => new DocumentTypeDefinition(typeCode, new FixedLocalizableString("合同")));

        ex.Message.ShouldContain("<owner-module>.<sub-type>");
    }

    [Fact]
    public void Constructor_Should_Reject_Null_DisplayName()
    {
        Should.Throw<ArgumentException>(
            () => new DocumentTypeDefinition("contract.general", null!));
    }

    [Fact]
    public void ConfidenceThreshold_Default_Should_Match_ClassificationDefaults()
    {
        var def = new DocumentTypeDefinition("contract.general", new FixedLocalizableString("合同"));

        def.ConfidenceThreshold.ShouldBe(ClassificationDefaults.DefaultConfidenceThreshold);
    }
}
