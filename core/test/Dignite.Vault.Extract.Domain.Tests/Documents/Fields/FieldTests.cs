using System;
using Dignite.Abp.FlexFields;
using Dignite.Vault.Extract.Documents.Fields;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Dignite.Vault.Extract.Documents.Fields;

/// <summary>
/// <see cref="Field"/> — the v3 field definition (#559). The guards here are the ones the FlexFields
/// kernel does <b>not</b> provide: it validates no format on <c>IFlexField.Name</c> at all, so every
/// assertion about the name allow-list is testing something adopting the kernel would otherwise have
/// silently dropped.
/// </summary>
public class FieldTests
{
    private static Field Create(
        string name = "contract_amount",
        string displayName = "Contract Amount",
        string fieldTypeName = "Number",
        string? description = null,
        bool isSearchable = true)
    {
        return new Field(
            Guid.NewGuid(),
            tenantId: null,
            documentTypeId: Guid.NewGuid(),
            name: name,
            displayName: displayName,
            fieldTypeName: fieldTypeName,
            description: description,
            isSearchable: isSearchable);
    }

    [Fact]
    public void Creates_with_the_flex_field_contract_populated()
    {
        var field = Create(description: "The total contract value.");

        // Typed as the contract, not the class: this is what the kernel will actually see.
        IFlexField contract = field;

        contract.Name.ShouldBe("contract_amount");
        contract.DisplayName.ShouldBe("Contract Amount");
        contract.FieldTypeName.ShouldBe("Number");
        contract.Description.ShouldBe("The total contract value.");
        contract.Configuration.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("contract_amount")]
    [InlineData("contract-amount")]
    [InlineData("Amount2")]
    [InlineData("A")]
    public void Accepts_names_within_the_allow_list(string name)
    {
        Create(name: name).Name.ShouldBe(name);
    }

    /// <summary>
    /// The allow-list is a prompt-injection boundary - Name is concatenated raw into the LLM schema
    /// message - so these are the characters that must never reach prompt context, not merely untidy
    /// input.
    /// </summary>
    [Theory]
    [InlineData("contract amount")]
    [InlineData("contract\namount")]
    [InlineData("contract\"amount")]
    [InlineData("# amount")]
    [InlineData("amount`s")]
    [InlineData("金額")]
    public void Rejects_names_outside_the_allow_list(string name)
    {
        Should.Throw<BusinessException>(() => Create(name: name));
    }

    /// <summary>
    /// Blank and over-length names fail earlier, in ABP's <c>Check</c> guard, so they surface as
    /// <see cref="ArgumentException"/> rather than the allow-list's <see cref="BusinessException"/>.
    /// Asserted separately rather than folded into the theory above, because the distinction is real:
    /// this is the same split v2's <c>FieldDefinition</c> already had, and v3 keeps it so the app
    /// layer's existing handling of both does not have to change.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_name(string name)
    {
        Should.Throw<ArgumentException>(() => Create(name: name));
    }

    [Fact]
    public void Rejects_a_name_longer_than_the_limit()
    {
        Should.Throw<ArgumentException>(() => Create(name: new string('a', FieldDefinitionConsts.MaxNameLength + 1)));
    }

    /// <summary>
    /// Carried over from v2's <c>FieldDefinition</c> unchanged so a migrated display name cannot
    /// become unsavable under v3.
    /// </summary>
    [Fact]
    public void Rejects_control_characters_in_the_display_name()
    {
        Should.Throw<BusinessException>(() => Create(displayName: "Contract\nAmount"));
    }

    // ─── NormalizeDisplayName contract (#264: draft assistant prefill must pass SetDisplayName) ─────────
    // Carried over from v2's FieldDefinitionTests unchanged (#593: FieldDefinition removed, the method
    // moved onto Field, the sole live caller FieldDraftSuggestionAppService follows it).

    [Theory]
    [InlineData("Amount\nIgnore")]            // \n to space
    [InlineData("Amount\r\nIgnore")]          // \r\n to collapsed space
    [InlineData("Tab\there")]                 // tab
    [InlineData("Null\0byte")]                // \0
    [InlineData("Vertical\vTab")]
    [InlineData("Form\fFeed")]
    [InlineData("  双侧空白  ")]
    [InlineData("多   连续   空白")]
    public void NormalizeDisplayName_Output_Should_Pass_SetDisplayName(string raw)
    {
        // Contract lock: Normalize output must pass SetDisplayName through the constructor. This
        // prevents future tightening of the rejection domain from silently drifting between the two paths
        // and making drafted values fail loudly when saved (#264 review2 #3).
        var normalized = Field.NormalizeDisplayName(raw);

        var field = Create(displayName: normalized);

        field.DisplayName.ShouldBe(normalized);
        normalized.ShouldNotContain('\n');
        normalized.ShouldNotContain('\t');
        normalized.ShouldNotContain('\0');
    }

    [Fact]
    public void NormalizeDisplayName_Should_Truncate_Without_Leaving_Lone_Surrogate()
    {
        // The MaxDisplayNameLength-th code unit is exactly the high surrogate of an astral character
        // (emoji); truncation must not leave an orphan high surrogate.
        var raw = new string('a', FieldDefinitionConsts.MaxDisplayNameLength - 1) + "😀"; // 😀 = U+D83D U+DE00

        var normalized = Field.NormalizeDisplayName(raw);

        normalized.Length.ShouldBeLessThanOrEqualTo(FieldDefinitionConsts.MaxDisplayNameLength);
        // Last char is not an orphan high surrogate; either keep 😀 intact or drop it entirely.
        if (normalized.Length > 0)
        {
            char.IsHighSurrogate(normalized[^1]).ShouldBeFalse();
        }
        // Still safe to construct: no throw and serializable.
        var field = Create(displayName: normalized);
        field.DisplayName.ShouldBe(normalized);
    }

    [Fact]
    public void NormalizeDisplayName_Should_Return_Empty_For_Blank()
    {
        Field.NormalizeDisplayName(null).ShouldBe(string.Empty);
        Field.NormalizeDisplayName("   ").ShouldBe(string.Empty);
    }

    [Fact]
    public void Blank_description_collapses_to_null()
    {
        Create(description: "   ").Description.ShouldBeNull();
        Create(description: null).Description.ShouldBeNull();
    }

    /// <summary>
    /// #447 kept the extraction instruction uncapped as admin-authored configuration; moving it onto
    /// the contract's Description must not quietly reintroduce a limit.
    /// </summary>
    [Fact]
    public void Description_is_not_length_capped()
    {
        var long_description = new string('x', 20_000);

        Create(description: long_description).Description.ShouldBe(long_description);
    }

    /// <summary>
    /// v2 indexed every extracted value unconditionally, so the v3 default has to preserve that or
    /// migrated fields would silently stop being filterable.
    /// </summary>
    [Fact]
    public void Is_searchable_by_default()
    {
        new Field(
            Guid.NewGuid(),
            tenantId: null,
            documentTypeId: Guid.NewGuid(),
            name: "amount",
            displayName: "Amount",
            fieldTypeName: "Number")
            .IsSearchable.ShouldBeTrue();
    }

    [Fact]
    public void Rename_goes_through_the_same_allow_list()
    {
        var field = Create();

        Should.Throw<BusinessException>(() => field.SetName("contract amount"));

        field.SetName("total_amount");
        field.Name.ShouldBe("total_amount");
    }

    [Fact]
    public void Requires_a_document_type()
    {
        Should.Throw<ArgumentException>(() => new Field(
            Guid.NewGuid(),
            tenantId: null,
            documentTypeId: Guid.Empty,
            name: "amount",
            displayName: "Amount",
            fieldTypeName: "Number"));
    }

    [Fact]
    public void Requires_a_field_type()
    {
        Should.Throw<ArgumentException>(() => Create(fieldTypeName: "  "));
    }
}
