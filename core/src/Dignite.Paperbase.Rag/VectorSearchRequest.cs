using System;

namespace Dignite.Paperbase.Rag;

/// <summary>
/// Parameters for a vector / keyword / hybrid search operation.
/// TenantId is explicit so providers do not rely on ABP ambient context,
/// making them safe to use from Hangfire jobs and CLI tools.
/// </summary>
public sealed record VectorSearchRequest
{
    /// <summary>
    /// Tenant to search within. Must be set by callers; use
    /// <see cref="DocumentVectorStoreExtensions.SearchForCurrentTenantAsync"/> to fill it
    /// from ABP's ICurrentTenant in application code.
    /// </summary>
    public Guid? TenantId { get; init; }

    /// <summary>Query embedding vector. Required when Mode is Vector or Hybrid.</summary>
    public ReadOnlyMemory<float> QueryVector { get; init; }

    /// <summary>Query text. Required when Mode is Keyword or Hybrid.</summary>
    public string? QueryText { get; init; }

    /// <summary>Maximum number of results to return.</summary>
    public int TopK { get; init; } = 5;

    /// <summary>Restrict results to a specific document. Null means all documents.</summary>
    public Guid? DocumentId { get; init; }

    /// <summary>Restrict results to a specific document type. Null means all types.</summary>
    public string? DocumentTypeCode { get; init; }

    /// <summary>
    /// Minimum acceptable score in [0, 1]. Applied only when the provider
    /// reports NormalizesScore = true.
    /// </summary>
    public double? MinScore { get; init; }

    /// <summary>Search strategy to use.</summary>
    public VectorSearchMode Mode { get; init; } = VectorSearchMode.Vector;
}
