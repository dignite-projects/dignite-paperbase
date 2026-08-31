using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.CKEditor;
using Dignite.Abp.FlexFields.Date;
using Dignite.Abp.FlexFields.Text;
using Dignite.Vault.Extract.FlexFields.Tags;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Vault.Extract.Ai;
using Dignite.Vault.Extract.Documents.Fields;
using Dignite.Vault.Extract.Documents.Pipelines.FieldExtraction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Dignite.Vault.Extract.Documents;

/// <summary>
/// Contract + normalization semantics for <see cref="FieldExtractionWorkflow"/> (#204 typed values, #527 §1/§3
/// value+warning envelope). IChatClient is replaced with NSubstitute — no real LLM. The workflow now returns a
/// <see cref="FieldExtractionWorkflowResult"/> (<c>{ values, validationWarnings }</c>): values keep the strict
/// typed-validation behavior (matching types kept, mismatches nulled, now under the <c>values</c> key), and warnings are
/// defensively normalized (undeclared / blank / malformed discarded, deduped per field, truncated, capped) without ever
/// dropping a valid value. These tests exercise the server-side parser, not the JSON schema (the mock returns raw JSON).
/// </summary>
public class FieldExtractionWorkflow_Tests
{
    private static FieldExtractionWorkflow CreateWorkflow(string jsonResponse)
    {
        var chatClient = Substitute.For<IChatClient>();
        chatClient.GetResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Any<ChatOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, jsonResponse)])));

        return new FieldExtractionWorkflow(
            chatClient,
            NullLogger<FieldExtractionWorkflow>.Instance,
            new FieldSchemaPromptBudgetGuard(Options.Create(new VaultExtractBehaviorOptions())),
            TestFieldTypeRegistry.Default);
    }

    private static FieldExtractionDescriptor Field(
        string name, string fieldTypeName, FieldConfigurationDictionary? configuration = null)
        => new(System.Guid.NewGuid(), name, $"Extract {name}.", fieldTypeName,
            configuration ?? new FieldConfigurationDictionary(), IsRequired: false);

    /// <summary>Open-vocabulary multi-value — what v2 expressed as a Text field with AllowMultiple.</summary>
    private static FieldExtractionDescriptor MultiField(string name)
        => Field(name, TagsFieldType.ControlName);

    // Date and DateTime share one field type in v3 and are told apart by InputMode, so the two v2 data
    // types become two configurations of the same descriptor.
    private static FieldExtractionDescriptor DateField(string name)
        => Field(name, DateTimeFieldType.ControlName,
            new DateTimeConfiguration { InputMode = DateTimeInputMode.Date }.ConfigurationDictionary);

    private static FieldExtractionDescriptor DateTimeField(string name)
        => Field(name, DateTimeFieldType.ControlName,
            new DateTimeConfiguration { InputMode = DateTimeInputMode.DateTime }.ConfigurationDictionary);

    private static (FieldExtractionWorkflow Workflow, Func<ChatOptions?> CapturedOptions) CreateWorkflowCapturingOptions(
        string jsonResponse, ChatFinishReason? finishReason = null)
    {
        var chatClient = Substitute.For<IChatClient>();
        ChatOptions? captured = null;
        var response = new ChatResponse([new ChatMessage(ChatRole.Assistant, jsonResponse)]);
        if (finishReason.HasValue)
        {
            response.FinishReason = finishReason.Value;
        }

        chatClient.GetResponseAsync(
                Arg.Any<IEnumerable<ChatMessage>>(),
                Arg.Do<ChatOptions?>(o => captured = o),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        var workflow = new FieldExtractionWorkflow(
            chatClient,
            NullLogger<FieldExtractionWorkflow>.Instance,
            new FieldSchemaPromptBudgetGuard(Options.Create(new VaultExtractBehaviorOptions())),
            TestFieldTypeRegistry.Default);

        return (workflow, () => captured);
    }

    // ─── values: typed validation (unchanged behavior, now under the `values` key) ───

    [Fact]
    public async Task Keeps_values_matching_declared_type()
    {
        var json = """
        {
          "values": {
            "amount": 1500.50,
            "count": 3,
            "active": true,
            "signed_on": "2024-01-15",
            "party": "Acme Corp"
          },
          "validationWarnings": []
        }
        """;
        var workflow = CreateWorkflow(json);

        var result = await workflow.ExtractAsync(
            new[]
            {
                Field("amount", "Number"),
                Field("count", "Number"),
                Field("active", "Boolean"),
                DateField("signed_on"),
                Field("party", "Text"),
            },
            "# doc");

        result.Values["amount"]!.Value.GetDecimal().ShouldBe(1500.50m);
        result.Values["count"]!.Value.GetInt64().ShouldBe(3);
        result.Values["active"]!.Value.GetBoolean().ShouldBeTrue();
        result.Values["signed_on"]!.Value.GetString().ShouldBe("2024-01-15");
        result.Values["party"]!.Value.GetString().ShouldBe("Acme Corp");
        result.ValidationWarnings.ShouldBeEmpty();
    }

    /// <summary>
    /// The workflow passes values through as the model returned them and does <b>not</b> type-check.
    /// <para>
    /// v2 validated here and converted in the service, so the same rules lived in two places that agreed
    /// only because both switched on the same enum. v3 has one gate — <c>FlexFieldValueReader</c>, which the
    /// service must call anyway — and the assertions that used to live in this test now live in
    /// <c>FlexFieldValueReader_Tests</c>, against the field type rather than a data-type enum.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Passes_values_through_without_type_checking_them()
    {
        var json = """
        {
          "values": {
            "amount": "about 100k",
            "party": 123
          },
          "validationWarnings": []
        }
        """;
        var workflow = CreateWorkflow(json);

        var result = await workflow.ExtractAsync(
            new[] { Field("amount", "Number"), Field("party", "Text") },
            "# doc");

        // Shapes that do not match their field type survive this far; the reader is what rejects them.
        result.Values["amount"]!.Value.GetString().ShouldBe("about 100k");
        result.Values["party"]!.Value.GetInt32().ShouldBe(123);
    }

    [Fact]
    public async Task DateTime_must_be_offset_free_wall_clock()
    {
        var json = """
        {
          "values": {
            "offset_free": "2024-01-01T10:00:00",
            "with_offset": "2024-01-01T10:00:00+08:00",
            "utc_z": "2024-01-01T10:00:00Z"
          },
          "validationWarnings": []
        }
        """;
        var workflow = CreateWorkflow(json);

        var result = await workflow.ExtractAsync(
            new[]
            {
                DateTimeField("offset_free"),
                DateTimeField("with_offset"),
                DateTimeField("utc_z"),
            },
            "# doc");

        // All three come back untouched: rejecting the offset-bearing ones is the reader's job now, and
        // FlexFieldValueReader_Tests covers it. What this still pins is that the workflow does not mangle
        // the string on the way through.
        result.Values["offset_free"]!.Value.GetString().ShouldBe("2024-01-01T10:00:00");
        result.Values["with_offset"]!.Value.GetString().ShouldBe("2024-01-01T10:00:00+08:00");
        result.Values["utc_z"]!.Value.GetString().ShouldBe("2024-01-01T10:00:00Z");
    }

    [Fact]
    public async Task Missing_or_explicit_null_becomes_null()
    {
        var workflow = CreateWorkflow(
            """{ "values": { "present": "x", "explicit_null": null }, "validationWarnings": [] }""");

        var result = await workflow.ExtractAsync(
            new[]
            {
                Field("present", "Text"),
                Field("explicit_null", "Text"),
                Field("absent", "Text"),
            },
            "# doc");

        result.Values["present"]!.Value.GetString().ShouldBe("x");
        result.Values["explicit_null"].ShouldBeNull();
        result.Values["absent"].ShouldBeNull();
    }

    [Fact]
    public async Task Non_json_output_nulls_all_fields_and_has_no_warnings()
    {
        var workflow = CreateWorkflow("sorry, I can't do that");

        var result = await workflow.ExtractAsync(
            new[] { Field("amount", "Number") },
            "# doc");

        result.Values["amount"].ShouldBeNull();
        result.ValidationWarnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task Missing_values_key_nulls_all_fields()
    {
        // Schema drift: no `values` key -> every field is null (graceful degradation, like the old flat-shape miss).
        var workflow = CreateWorkflow("""{ "validationWarnings": [] }""");

        var result = await workflow.ExtractAsync(
            new[] { Field("amount", "Number"), Field("party", "Text") },
            "# doc");

        result.Values["amount"].ShouldBeNull();
        result.Values["party"].ShouldBeNull();
    }

    /// <summary>
    /// The workflow hands multi-valued JSON through untouched — array shape, element types and the count
    /// cap are all <see cref="FlexFieldValueReader"/>'s to enforce, and it runs on both the extraction and
    /// the operator-edit path. Under v2 this method validated as well, so the same rules lived in two
    /// places that agreed only because both switched on the same enum. What must survive here is that the
    /// raw <c>JsonElement</c> reaches the service undamaged: re-encoding it would cost decimal precision
    /// for nothing, and dropping it would hide a rejection the service reports per field.
    /// <para>
    /// The rejections themselves are pinned in <c>FlexFieldValueReader_Tests</c>
    /// (<c>Tags_rejects_a_scalar</c> / <c>Tags_rejects_a_non_string_element</c> /
    /// <c>Tags_over_the_count_cap_is_rejected_whole</c>).
    /// </para>
    /// </summary>
    [Fact]
    public async Task Multi_value_json_reaches_the_service_verbatim()
    {
        var json = """
        {
          "values": {
            "tags": ["urgent", "legal", "2026"],
            "scalar_tags": "urgent"
          },
          "validationWarnings": []
        }
        """;
        var workflow = CreateWorkflow(json);

        var result = await workflow.ExtractAsync(
            new[] { MultiField("tags"), MultiField("scalar_tags"), MultiField("absent_tags") },
            "# doc");

        result.Values["tags"]!.Value.ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Array);
        result.Values["tags"]!.Value.EnumerateArray().Select(e => e.GetString())
            .ShouldBe(new[] { "urgent", "legal", "2026" });

        // Off-shape, but still passed on: the workflow is not the gate, and swallowing it here would turn a
        // reported per-field rejection into a silently missing field.
        result.Values["scalar_tags"]!.Value.ValueKind.ShouldBe(System.Text.Json.JsonValueKind.String);

        // A field the model omitted is the one case the workflow does decide: absent means null.
        result.Values["absent_tags"].ShouldBeNull();
    }

    [Fact]
    public async Task Empty_field_list_short_circuits_without_calling_llm()
    {
        var workflow = CreateWorkflow("{}");

        var result = await workflow.ExtractAsync(Array.Empty<FieldExtractionDescriptor>(), "# doc");

        result.Values.ShouldBeEmpty();
        result.ValidationWarnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task Over_budget_schema_assertion_blocks_the_llm_call()
    {
        var chatClient = Substitute.For<IChatClient>();
        var workflow = new FieldExtractionWorkflow(
            chatClient,
            NullLogger<FieldExtractionWorkflow>.Instance,
            new FieldSchemaPromptBudgetGuard(Options.Create(new VaultExtractBehaviorOptions
            {
                MaxFieldSchemaPromptLength = 4
            })),
            TestFieldTypeRegistry.Default);
        var fields = new[]
        {
            new FieldExtractionDescriptor(
                Guid.NewGuid(), "body", "12345", TextFieldType.ControlName, new FieldConfigurationDictionary(), IsRequired: false)
        };

        await Should.ThrowAsync<InvalidOperationException>(() => workflow.ExtractAsync(fields, "# doc"));

        await chatClient.DidNotReceive().GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(),
            Arg.Any<ChatOptions?>(),
            Arg.Any<CancellationToken>());
    }

    // ─── validationWarnings: server-side normalization (#527 §3) ───

    [Fact]
    public async Task Warning_is_returned_and_the_field_value_is_kept()
    {
        // The core #527 contract: a warned field KEEPS its value (never nulled), and the warning is returned separately.
        var json = """
        {
          "values": { "transactions": "| Date | Balance |" },
          "validationWarnings": [
            { "fieldName": "transactions", "message": "Row 4 balance does not reconcile." }
          ]
        }
        """;
        var workflow = CreateWorkflow(json);

        var result = await workflow.ExtractAsync(new[] { Field("transactions", "Text") }, "# doc");

        result.Values["transactions"].ShouldNotBeNull();   // value preserved despite the warning
        result.ValidationWarnings.Count.ShouldBe(1);
        result.ValidationWarnings[0].FieldName.ShouldBe("transactions");
        result.ValidationWarnings[0].Message.ShouldBe("Row 4 balance does not reconcile.");
    }

    [Fact]
    public async Task Warning_for_undeclared_field_is_discarded()
    {
        var json = """
        {
          "values": { "amount": 100 },
          "validationWarnings": [
            { "fieldName": "amount", "message": "problem" },
            { "fieldName": "not_a_field", "message": "should be dropped" }
          ]
        }
        """;
        var workflow = CreateWorkflow(json);

        var result = await workflow.ExtractAsync(new[] { Field("amount", "Number") }, "# doc");

        result.ValidationWarnings.Select(w => w.FieldName).ShouldBe(new[] { "amount" });
    }

    [Fact]
    public async Task Blank_message_is_discarded()
    {
        var workflow = CreateWorkflow(
            """{ "values": { "amount": 100 }, "validationWarnings": [ { "fieldName": "amount", "message": "   " } ] }""");

        var result = await workflow.ExtractAsync(new[] { Field("amount", "Number") }, "# doc");

        result.ValidationWarnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task Duplicate_warnings_for_one_field_are_merged_to_one()
    {
        var json = """
        {
          "values": { "amount": 100 },
          "validationWarnings": [
            { "fieldName": "amount", "message": "first" },
            { "fieldName": "amount", "message": "second" }
          ]
        }
        """;
        var workflow = CreateWorkflow(json);

        var result = await workflow.ExtractAsync(new[] { Field("amount", "Number") }, "# doc");

        result.ValidationWarnings.Count.ShouldBe(1);
        result.ValidationWarnings[0].Message.ShouldBe("first");   // first wins
    }

    [Fact]
    public async Task Overlong_message_is_truncated_at_char_boundary()
    {
        var longMessage = new string('x', DocumentFieldValidationWarningConsts.MaxMessageLength + 50);
        var workflow = CreateWorkflow(
            $$"""{ "values": { "amount": 100 }, "validationWarnings": [ { "fieldName": "amount", "message": "{{longMessage}}" } ] }""");

        var result = await workflow.ExtractAsync(new[] { Field("amount", "Number") }, "# doc");

        result.ValidationWarnings.Single().Message.Length
            .ShouldBe(DocumentFieldValidationWarningConsts.MaxMessageLength);
    }

    [Fact]
    public async Task Excess_warnings_are_capped()
    {
        // More distinct warned fields than the cap -> only MaxWarningsPerExtraction warnings are kept.
        var fields = Enumerable.Range(0, DocumentFieldValidationWarningConsts.MaxWarningsPerExtraction + 5)
            .Select(i => Field($"f{i}", "Text")).ToArray();
        var values = string.Join(",", fields.Select(f => $$""" "{{f.Name}}": "v" """));
        var warnings = string.Join(",", fields.Select(f => $$"""{ "fieldName": "{{f.Name}}", "message": "bad" }"""));
        var workflow = CreateWorkflow($$"""{ "values": { {{values}} }, "validationWarnings": [ {{warnings}} ] }""");

        var result = await workflow.ExtractAsync(fields, "# doc");

        result.ValidationWarnings.Count.ShouldBe(DocumentFieldValidationWarningConsts.MaxWarningsPerExtraction);
    }

    [Fact]
    public async Task Malformed_warning_entries_do_not_drop_valid_values_or_the_valid_warning()
    {
        // A non-object entry, a missing-message entry, and a wrong-typed fieldName are all discarded; the valid value and
        // the valid warning survive — a malformed warning never corrupts the values half (#527 §3).
        var json = """
        {
          "values": { "amount": 100 },
          "validationWarnings": [
            "not-an-object",
            { "fieldName": "amount" },
            { "fieldName": 42, "message": "x" },
            { "fieldName": "amount", "message": "valid" }
          ]
        }
        """;
        var workflow = CreateWorkflow(json);

        var result = await workflow.ExtractAsync(new[] { Field("amount", "Number") }, "# doc");

        result.Values["amount"]!.Value.GetInt64().ShouldBe(100);
        result.ValidationWarnings.Count.ShouldBe(1);
        result.ValidationWarnings[0].Message.ShouldBe("valid");
    }

    [Fact]
    public async Task Missing_validationWarnings_key_yields_empty()
    {
        var workflow = CreateWorkflow("""{ "values": { "amount": 100 } }""");

        var result = await workflow.ExtractAsync(new[] { Field("amount", "Number") }, "# doc");

        result.Values["amount"].ShouldNotBeNull();
        result.ValidationWarnings.ShouldBeEmpty();
    }

    // ─── MaxOutputTokens: reproduces a real silent-truncation incident on a many-row AllowMultiple field ───
    // (SiliconFlow's own default output cap, observed at ~4096 tokens, applies whenever the caller sends none;
    // the JSON gets cut off mid-generation and fails to parse, silently nulling every field on the call).

    [Fact]
    public async Task MaxOutputTokens_covers_an_AllowMultiple_fields_own_worst_case()
    {
        var (workflow, capturedOptions) = CreateWorkflowCapturingOptions(
            """{ "values": { "tags": [] }, "validationWarnings": [] }""");

        await workflow.ExtractAsync(new[] { MultiField("tags") }, "# doc");

        // A hard floor, not an exact figure: MaxOutputTokens must at least cover every array slot at its max
        // length, or a full multi-value field can never finish generating before the request's own ceiling
        // truncates it — the same failure this fix targets, just self-inflicted instead of provider-inflicted.
        capturedOptions()!.MaxOutputTokens.ShouldNotBeNull();
        capturedOptions()!.MaxOutputTokens!.Value.ShouldBeGreaterThan(
            DocumentExtractedFieldConsts.MaxMultiValueCount * DocumentExtractedFieldConsts.MaxTextValueLength);
    }

    [Fact]
    public async Task MaxOutputTokens_scales_up_when_an_AllowMultiple_field_is_requested()
    {
        var (scalarWorkflow, scalarOptions) = CreateWorkflowCapturingOptions(
            """{ "values": { "amount": 1 }, "validationWarnings": [] }""");
        await scalarWorkflow.ExtractAsync(new[] { Field("amount", "Number") }, "# doc");

        var (multiWorkflow, multiOptions) = CreateWorkflowCapturingOptions(
            """{ "values": { "tags": [] }, "validationWarnings": [] }""");
        await multiWorkflow.ExtractAsync(new[] { MultiField("tags") }, "# doc");

        multiOptions()!.MaxOutputTokens!.Value.ShouldBeGreaterThan(scalarOptions()!.MaxOutputTokens!.Value);
    }

    [Fact]
    public async Task Response_cut_off_at_the_token_limit_degrades_without_throwing()
    {
        // finish_reason=length with an incomplete (unterminated) JSON body — the exact shape observed against a
        // real multi-page document once its AllowMultiple field grew past the provider's silent default cap.
        // ExtractAsync must not throw; it degrades through the existing non-JSON-output fallback (all-null).
        var (workflow, _) = CreateWorkflowCapturingOptions(
            """{ "values": { "amount": 1""",
            ChatFinishReason.Length);

        var result = await workflow.ExtractAsync(new[] { Field("amount", "Number") }, "# doc");

        result.Values["amount"].ShouldBeNull();
    }
}
