using Dignite.Abp.FlexFields;

namespace Dignite.Vault.Extract.Documents.Exports;

/// <summary>
/// Export query projection: fetch only fields needed by export and <strong>exclude Markdown</strong>,
/// which can be a large OCR/body payload. When projecting to a non-entity type, EF automatically does
/// not SELECT unreferenced columns and does not track changes, avoiding loading Markdown into memory
/// for thousands of documents.
/// <para>
/// Fixed system fields (#207 / #287): <see cref="LifecycleStatus"/> /
/// <see cref="ReviewDisposition"/> / <see cref="ReviewReasons"/> / <see cref="Title"/> are emitted
/// by the export engine directly and are never configurable. <see cref="FlexFields"/> is the field
/// value bag, projected as-is with the document itself — one JSON column, no child-row join.
/// </para>
/// </summary>
internal sealed class ExportProjection
{
    public string? Title { get; init; }
    public DocumentLifecycleStatus LifecycleStatus { get; init; }
    public DocumentReviewDisposition ReviewDisposition { get; init; }

    /// <summary>Reason axis (#287). Documents with non-blocking MissingRequiredFields still enter type-bound export normally, but the export must expose the "missing required fields" quality signal because the disposition axis ReviewDisposition does not.</summary>
    public DocumentReviewReasons ReviewReasons { get; init; }

    /// <summary>
    /// The document's field value bag, projected as-is. Keyed by field name, which is what the column
    /// definitions carry, so a cell is a direct lookup rather than a bucketed scan.
    /// </summary>
    public FlexFieldDictionary FlexFields { get; init; } = new();
}
