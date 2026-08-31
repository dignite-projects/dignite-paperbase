using System;
using System.Collections.Generic;
using System.Linq;
using Dignite.Abp.FlexFields;
using Dignite.Vault.Extract.FlexFields.Tags;
using Shouldly;
using Xunit;

namespace Dignite.Vault.Extract.FlexFields;

/// <summary>
/// <see cref="TagsFieldType"/>: the open-vocabulary multi-value type that replaces v2's
/// <c>FieldDefinition.AllowMultiple</c> flag (#559 resolution 2 / 4).
/// </summary>
public class TagsFieldType_Tests : VaultExtractFlexFieldsTestBase
{
    private readonly IFieldTypeResolver _resolver;

    public TagsFieldType_Tests()
    {
        _resolver = GetRequiredService<IFieldTypeResolver>();
    }

    private static FlexFieldValue Value(
        object? value,
        bool required = false,
        bool searchable = true,
        FieldConfigurationDictionary? configuration = null)
    {
        var data = new FlexFieldData(
            Guid.NewGuid(),
            name: "parties",
            displayName: "Parties",
            fieldTypeName: TagsFieldType.ControlName,
            configuration: configuration);

        return new FlexFieldValue(data, required, searchable, value);
    }

    private IReadOnlyList<System.ComponentModel.DataAnnotations.ValidationResult> Validate(FlexFieldValue field)
    {
        return _resolver.Get(TagsFieldType.ControlName).Validate(new FieldValidationArgs(field));
    }

    /// <summary>
    /// The registration key is stored inside every field definition bound to this type, so it is data,
    /// not a name. Asserted for the same reason the kernel asserts its own six built-ins.
    /// </summary>
    [Fact]
    public void Registration_key_is_frozen()
    {
        TagsFieldType.ControlName.ShouldBe("Tags");
        _resolver.Get("Tags").ShouldBeOfType<TagsFieldType>();
    }

    /// <summary>
    /// Resolving through <see cref="IFieldTypeResolver"/> at all is the proof that FieldTypeBase's
    /// ITransientDependency self-registration reaches a downstream's own field types with no options
    /// class or registry to add to.
    /// </summary>
    [Fact]
    public void Registers_itself_without_any_explicit_wiring()
    {
        _resolver.GetAll().ShouldContain(fieldType => fieldType is TagsFieldType);
    }

    [Fact]
    public void Is_indexable_as_string()
    {
        _resolver.Get(TagsFieldType.ControlName).IndexValueType.ShouldBe(FlexFieldValueType.String);
    }

    [Fact]
    public void Empty_value_is_valid_when_not_required()
    {
        Validate(Value(null)).ShouldBeEmpty();
        Validate(Value(new List<string>())).ShouldBeEmpty();
    }

    [Fact]
    public void Empty_value_is_rejected_when_required()
    {
        Validate(Value(null, required: true)).ShouldHaveSingleItem();
        Validate(Value(new List<string>(), required: true)).ShouldHaveSingleItem();
    }

    [Fact]
    public void Arbitrary_values_are_accepted()
    {
        // The whole point of this type versus Select: no configured option list to be a member of.
        Validate(Value(new List<string> { "Acme Corp", "上海某某有限公司", "Jane Doe" })).ShouldBeEmpty();
    }

    /// <summary>
    /// The count cap is a security guardrail on an LLM write path, not a UI nicety - a malicious
    /// document can induce a huge array. Both halves are asserted: at the limit passes, one over fails.
    /// </summary>
    [Fact]
    public void Value_count_is_capped()
    {
        var configuration = new TagsConfiguration { MaxCount = 3 }.ConfigurationDictionary;

        Validate(Value(new List<string> { "a", "b", "c" }, configuration: configuration))
            .ShouldBeEmpty();

        Validate(Value(new List<string> { "a", "b", "c", "d" }, configuration: configuration))
            .ShouldHaveSingleItem();
    }

    [Fact]
    public void Value_length_is_capped()
    {
        var configuration = new TagsConfiguration { MaxLength = 5 }.ConfigurationDictionary;

        Validate(Value(new List<string> { "12345" }, configuration: configuration))
            .ShouldBeEmpty();

        Validate(Value(new List<string> { "12345", "123456" }, configuration: configuration))
            .ShouldHaveSingleItem();
    }

    /// <summary>
    /// Defaults carry v2's <c>DocumentExtractedFieldConsts</c> limits forward: a field migrated from an
    /// AllowMultiple text field must not silently gain or lose headroom.
    /// </summary>
    [Fact]
    public void Defaults_match_the_v2_multi_value_limits()
    {
        var configuration = new TagsConfiguration();

        configuration.MaxCount.ShouldBe(100);
        configuration.MaxLength.ShouldBe(256);
    }

    /// <summary>
    /// The per-value default must stay within the query index's own string slot, or a value could be
    /// stored but never found by the index it is searched through.
    /// </summary>
    [Fact]
    public void Default_value_length_fits_the_index_string_slot()
    {
        new TagsConfiguration().MaxLength.ShouldBeLessThanOrEqualTo(FlexFieldConsts.MaxStringValueLength);
    }

    [Fact]
    public void Each_value_is_decomposed_into_its_own_searchable_value()
    {
        var fieldType = _resolver.Get(TagsFieldType.ControlName);

        var values = fieldType
            .GetSearchableValues(Value(new List<string> { "Acme Corp", "Jane Doe" }))
            .ToList();

        values.ShouldBe(new object[] { "Acme Corp", "Jane Doe" });
    }

    [Fact]
    public void Nothing_is_searchable_when_the_usage_is_not_searchable()
    {
        var fieldType = _resolver.Get(TagsFieldType.ControlName);

        fieldType
            .GetSearchableValues(Value(new List<string> { "Acme Corp" }, searchable: false))
            .ShouldBeEmpty();
    }

    /// <summary>
    /// A value bag round-trips through JSON, so a list commonly comes back as a
    /// <see cref="System.Text.Json.JsonElement"/> rather than a <c>List&lt;string&gt;</c>. Both must
    /// decompose identically - a field type that only works on the in-memory shape works only until the
    /// entity is reloaded.
    /// </summary>
    [Fact]
    public void Values_survive_a_json_round_trip()
    {
        var fieldType = _resolver.Get(TagsFieldType.ControlName);
        var element = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            """["Acme Corp","Jane Doe"]""");

        var values = fieldType.GetSearchableValues(Value(element)).ToList();

        values.ShouldBe(new object[] { "Acme Corp", "Jane Doe" });
    }
}
