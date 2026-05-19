using System;
using Dignite.Paperbase.Abstractions.Documents;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// FieldDefinition 实体层不变量测试。重点：Name 白名单校验——
/// FieldDefinition.Name 会进 LLM prompt 的 JSON schema 描述（FieldExtractionWorkflow），
/// 必须阻断换行 / 引号 / 控制字符等 prompt injection 载体。
/// </summary>
public class FieldDefinitionTests
{
    [Theory]
    [InlineData("amount")]
    [InlineData("Contract_Number")]
    [InlineData("party-name")]
    [InlineData("a")]
    [InlineData("A1_b-2")]
    public void Should_Accept_Valid_Name(string name)
    {
        var def = CreateDefinition(name);
        def.Name.ShouldBe(name);
    }

    [Theory]
    [InlineData("with space")]
    [InlineData("name\"quoted")]
    [InlineData("with\nnewline")]
    [InlineData("中文")]
    [InlineData("name.dot")]
    [InlineData("name/slash")]
    [InlineData("name;sql")]
    public void Should_Reject_Name_With_Invalid_Chars(string name)
    {
        var ex = Should.Throw<BusinessException>(() => CreateDefinition(name));
        ex.Code.ShouldBe(PaperbaseErrorCodes.InvalidFieldDefinitionName);
    }

    [Fact]
    public void Should_Reject_Name_Exceeding_Max_Length()
    {
        var tooLong = new string('a', FieldDefinitionConsts.MaxNameLength + 1);
        // 长度超限走 Check.NotNullOrWhiteSpace 的 maxLength 校验，抛 ArgumentException 而非 BusinessException
        Should.Throw<ArgumentException>(() => CreateDefinition(tooLong));
    }

    private static FieldDefinition CreateDefinition(string name) =>
        new(
            id: Guid.NewGuid(),
            tenantId: null,
            documentTypeCode: "contract.general",
            name: name,
            prompt: "Extract the value.",
            dataType: FieldDataType.String);
}
