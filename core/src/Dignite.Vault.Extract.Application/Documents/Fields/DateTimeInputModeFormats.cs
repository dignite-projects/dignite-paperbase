using Dignite.Abp.FlexFields.Date;

namespace Dignite.Vault.Extract.Documents.Fields;

/// <summary>
/// The canonical wire format for each <see cref="DateTimeInputMode"/> — the single place that maps a
/// DateTime field's configured mode onto one of <see cref="FieldValueFormats"/>' frozen shapes.
/// <para>
/// It exists because <c>Date</c>, <c>DateTime</c> and <c>Month</c> are one field type told apart by
/// configuration, so every path that renders or parses a value has to ask the same question: the egress
/// (<see cref="FlexFieldValueJsonWriter"/>), the export cell renderer, and the value reader. Those three
/// each carried their own <c>InputMode == DateTime ? … : …</c> ternary, which silently treated the third
/// mode as <c>Date</c> — a Month field asked the model for a full date, rejected the <c>yyyy-MM</c> value
/// the operator's own month picker produced, and could never be saved. One mapping, so adding a fourth
/// mode is one edit rather than a hunt for ternaries that quietly have no branch for it.
/// </para>
/// </summary>
public static class DateTimeInputModeFormats
{
    /// <summary>The format <paramref name="inputMode"/>'s values are emitted in and parsed from.</summary>
    public static string Format(DateTimeInputMode inputMode) => inputMode switch
    {
        DateTimeInputMode.DateTime => FieldValueFormats.DateTime,
        DateTimeInputMode.Month => FieldValueFormats.Month,
        _ => FieldValueFormats.Date
    };
}
