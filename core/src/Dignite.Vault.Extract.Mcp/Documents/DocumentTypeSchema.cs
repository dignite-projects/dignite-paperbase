using System.Collections.Generic;

namespace Dignite.Vault.Extract.Mcp.Documents;

/// <summary>
/// Document type field schema: the LLM-facing read projection for the MCP document-type resource
/// (ambient or explicitly tenant-scoped). It lets downstream AI clients discover which fields a type
/// has and what they hold, so they can populate the search tool's <c>fieldFilters</c> /
/// <c>includeFields</c> with correct field names. <see cref="DisplayName"/> is admin-configured
/// user-derived text and is already wrapped with <c>PromptBoundary.WrapField</c> to prevent indirect
/// prompt injection. <see cref="TypeCode"/> and field <c>Name</c> / <c>FieldType</c> are
/// system-controlled values (allow-list / registration key), so they are emitted raw.
/// </summary>
public sealed record DocumentTypeSchema
{
    public required string TypeCode { get; init; }

    /// <summary>Resource URI for this schema. Uses an explicit tenant scope when the caller supplied tenantId.</summary>
    public required string Uri { get; init; }

    /// <summary>Type display name, already wrapped with PromptBoundary.</summary>
    public string? DisplayName { get; init; }

    public required IReadOnlyList<DocumentTypeFieldSchema> Fields { get; init; }
}

/// <summary>
/// Schema projection for a single field. <see cref="Name"/> is the identifier used in
/// <c>fieldFilters</c> / <c>includeFields</c>. <see cref="DisplayName"/> is already wrapped with
/// PromptBoundary. The extraction instruction is intentionally omitted: it is useless for query /
/// projection orchestration and would waste LLM context while widening the injection surface.
/// </summary>
public sealed record DocumentTypeFieldSchema
{
    public required string Name { get; init; }

    /// <summary>
    /// Field type registration key: <c>Text</c>, <c>Number</c>, <c>Boolean</c>, <c>DateTime</c>,
    /// <c>Select</c>, <c>CKEditor</c> (long text) or <c>Tags</c> (multi-valued). Determines the available
    /// query operators — Text / Boolean / Select / Tags support equality only, while Number and DateTime
    /// support ranges.
    /// <para>
    /// Renamed from <c>dataType</c> in v3 (#559) rather than reused: the value set changed shape at the
    /// same time (v2's <c>Date</c> and <c>DateTime</c> became one <c>DateTime</c> type told apart by
    /// configuration, and <c>LongText</c> became <c>CKEditor</c>), so a client still reading the old key
    /// fails loudly instead of quietly matching new values against the old enum names.
    /// </para>
    /// </summary>
    public required string FieldType { get; init; }

    /// <summary>
    /// Whether the field holds several values. When true, the field is a <b>JSON array</b>
    /// (<c>string[]</c>) in search-result <c>extractedFields</c> rather than a scalar, so clients can
    /// parse it correctly. Equality filtering still matches one value and returns documents containing
    /// that value.
    /// <para>
    /// Kept as its own flag rather than left for the client to infer from <see cref="FieldType"/>: this
    /// is the one thing about a field a client must get right to parse a response at all, and it should
    /// not depend on the client knowing which registration keys happen to be multi-valued.
    /// </para>
    /// </summary>
    public bool IsMultiValue { get; init; }

    /// <summary>
    /// Whether this field can appear in <c>fieldFilters</c> at all. False for a field whose type is not
    /// indexable (long text) or whose admin turned searchability off — filtering on one is rejected, so
    /// saying so up front is cheaper than letting the model discover it through an error.
    /// </summary>
    public bool IsFilterable { get; init; }

    /// <summary>Field display name, already wrapped with PromptBoundary.</summary>
    public string? DisplayName { get; init; }

    public bool IsRequired { get; init; }
}
