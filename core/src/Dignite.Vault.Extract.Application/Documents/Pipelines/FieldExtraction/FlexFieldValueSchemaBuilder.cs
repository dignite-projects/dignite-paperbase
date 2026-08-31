using System;
using System.Linq;
using System.Text.Json.Nodes;
using Dignite.Abp.FlexFields;
using Dignite.Abp.FlexFields.Boolean;
using Dignite.Abp.FlexFields.CKEditor;
using Dignite.Abp.FlexFields.Date;
using Dignite.Abp.FlexFields.Number;
using Dignite.Abp.FlexFields.Select;
using Dignite.Abp.FlexFields.Text;
using Dignite.Vault.Extract.Documents.Fields;
using Dignite.Vault.Extract.FlexFields.Tags;

namespace Dignite.Vault.Extract.Documents.Pipelines.FieldExtraction;

/// <summary>
/// Builds the JSON-schema fragment that constrains one field's extracted value, from its v3 field type
/// and configuration — the replacement for the <c>FieldDataType</c> switch in
/// <see cref="FieldExtractionWorkflow"/>.
/// <para>
/// The reason this is worth having beyond parity: a field type can now describe its own value precisely
/// enough for the model to be <i>constrained</i> rather than merely instructed. A
/// <see cref="SelectFieldType"/> field emits its configured options as a JSON-schema <c>enum</c>, so the
/// model physically cannot return a value outside the list. Under v2 the only way to express a closed
/// vocabulary was to describe it in the field's prompt and hope — and then reject the mismatch after the
/// call, having already paid for it.
/// </para>
/// <para>
/// Every schema is <c>&lt;type&gt;-or-null</c>: a field the document does not contain must have a way to
/// say so. Forcing a value would trade a missing extraction for an invented one, which is far harder to
/// notice downstream.
/// </para>
/// </summary>
public static class FlexFieldValueSchemaBuilder
{
    /// <summary>
    /// The value schema for <paramref name="field"/>.
    /// </summary>
    public static JsonObject Build(Field field)
    {
        return Build(field.FieldTypeName, field.Configuration);
    }

    public static JsonObject Build(string fieldTypeName, FieldConfigurationDictionary configuration)
    {
        if (string.Equals(fieldTypeName, TagsFieldType.ControlName, StringComparison.Ordinal))
        {
            var tags = new TagsConfiguration(configuration);
            return new JsonObject
            {
                ["type"] = JsonTypes("array", "null"),
                // maxItems mirrors the validator's hard cap rather than merely hinting at it, so an
                // untrusted document cannot induce an unbounded array that is then rejected wholesale.
                ["maxItems"] = tags.MaxCount,
                ["items"] = new JsonObject
                {
                    ["type"] = "string",
                    ["maxLength"] = tags.MaxLength
                },
                ["description"] = "A JSON array of short structured string values, or null/empty array when absent."
            };
        }

        if (string.Equals(fieldTypeName, SelectFieldType.ControlName, StringComparison.Ordinal))
        {
            return BuildSelect(configuration);
        }

        if (string.Equals(fieldTypeName, TextFieldType.ControlName, StringComparison.Ordinal))
        {
            return new JsonObject
            {
                ["type"] = JsonTypes("string", "null"),
                ["maxLength"] = new TextConfiguration(configuration).CharLimit,
                ["description"] = "A short structured string value, or null when absent."
            };
        }

        if (string.Equals(fieldTypeName, CKEditorFieldType.ControlName, StringComparison.Ordinal))
        {
            return new JsonObject
            {
                ["type"] = JsonTypes("string", "null"),
                // An anti-abuse ceiling, not a storage limit: the column is unbounded, but an untrusted
                // document must not be able to induce an enormous generation. Carried over from v2's
                // LongText, whose role this field type now fills.
                ["maxLength"] = DocumentExtractedFieldConsts.MaxLongTextValueLength,
                ["description"] = "A long-form text value (e.g. a summary or description), or null when absent."
            };
        }

        if (string.Equals(fieldTypeName, NumberFieldType.ControlName, StringComparison.Ordinal))
        {
            return new JsonObject
            {
                ["type"] = JsonTypes("number", "null"),
                ["description"] = "A JSON number, or null when absent."
            };
        }

        if (string.Equals(fieldTypeName, BooleanFieldType.ControlName, StringComparison.Ordinal))
        {
            return new JsonObject
            {
                ["type"] = JsonTypes("boolean", "null"),
                ["description"] = "A JSON boolean, or null when absent."
            };
        }

        if (string.Equals(fieldTypeName, DateTimeFieldType.ControlName, StringComparison.Ordinal))
        {
            return BuildDateTime(configuration);
        }

        // A field type with no schema here would otherwise reach the model as an unconstrained value of
        // unknown shape, and its output would fail validation after the call rather than before it. Loud
        // failure is the right trade: adding a field type is a deliberate act, and this is the one place
        // that must be updated alongside it.
        throw new NotSupportedException(
            $"No extraction schema is defined for field type '{fieldTypeName}'.");
    }

    private static JsonObject BuildSelect(FieldConfigurationDictionary configuration)
    {
        var select = new SelectConfiguration(configuration);

        var options = new JsonArray();
        foreach (var option in select.Options.Where(o => !string.IsNullOrWhiteSpace(o.Value)))
        {
            options.Add(option.Value);
        }

        // No options configured means no closed vocabulary to enforce. Emitting an empty enum would make
        // every value invalid and the field permanently unextractable, so this degrades to a plain string
        // instead - wrong configuration should not silently become "this field can never have a value".
        if (options.Count == 0)
        {
            return new JsonObject
            {
                ["type"] = JsonTypes("string", "null"),
                ["description"] = "A string value, or null when absent."
            };
        }

        if (select.Multiple)
        {
            return new JsonObject
            {
                ["type"] = JsonTypes("array", "null"),
                ["items"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = options.DeepClone()
                },
                ["description"] = "A JSON array of values drawn from the allowed list, or null/empty array when absent."
            };
        }

        return new JsonObject
        {
            // The enum carries the null option itself: a "string-or-null" type alongside a value enum is
            // read inconsistently across providers, and the whole point of this branch is that the model
            // cannot return anything else.
            ["type"] = JsonTypes("string", "null"),
            ["enum"] = WithNull(options),
            ["description"] = "One value from the allowed list, or null when absent."
        };
    }

    private static JsonObject BuildDateTime(FieldConfigurationDictionary configuration)
    {
        var dateTime = new DateTimeConfiguration(configuration);

        // Date, DateTime and Month are one field type in v3, told apart by InputMode - so the pattern the
        // model is held to comes from configuration rather than from the type. Asking a date-only field
        // for hours and minutes would invent precision the document does not have, and asking a month
        // field for a day would invent one outright: the model would have to pick a day the document never
        // stated, and FlexFieldValueReader would then reject the value for not being a month.
        return dateTime.InputMode switch
        {
            DateTimeInputMode.DateTime => new JsonObject
            {
                ["type"] = JsonTypes("string", "null"),
                ["pattern"] = @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}$",
                ["description"] = "An offset-free ISO-8601 local date-time string in YYYY-MM-DDThh:mm:ss format, or null when absent."
            },
            DateTimeInputMode.Month => new JsonObject
            {
                ["type"] = JsonTypes("string", "null"),
                ["pattern"] = @"^\d{4}-\d{2}$",
                ["description"] = "An ISO-8601 year and month in YYYY-MM format, with no day, or null when absent."
            },
            _ => new JsonObject
            {
                ["type"] = JsonTypes("string", "null"),
                ["pattern"] = @"^\d{4}-\d{2}-\d{2}$",
                ["description"] = "An ISO-8601 date string in YYYY-MM-DD format, or null when absent."
            }
        };
    }

    private static JsonArray WithNull(JsonArray options)
    {
        var withNull = options.DeepClone().AsArray();
        withNull.Add(null);
        return withNull;
    }

    private static JsonArray JsonTypes(params string[] types)
    {
        var array = new JsonArray();
        foreach (var type in types)
        {
            array.Add(type);
        }
        return array;
    }
}
