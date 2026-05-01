using System;
using System.Collections.Generic;

namespace Dignite.Paperbase.Rag;

/// <summary>
/// Parameters for a vector search operation.
/// TenantId is explicit so providers do not rely on ABP ambient context,
/// making them safe to use from Hangfire jobs and CLI tools.
/// </summary>
public sealed record VectorSearchRequest
{
    /// <summary>
    /// Tenant to search within. Must be set by callers.
    /// In HTTP-context code, use <c>Dignite.Paperbase.Documents.KnowledgeIndex.DocumentKnowledgeIndexApplicationExtensions.SearchForCurrentTenantAsync</c>
    /// (Application layer) to fill it from ABP's ICurrentTenant.
    /// In background jobs or CLI tools, set this field explicitly.
    /// </summary>
    public Guid? TenantId { get; init; }

    /// <summary>Query embedding vector.</summary>
    public ReadOnlyMemory<float> QueryVector { get; init; }

    /// <summary>Maximum number of results to return.</summary>
    public int TopK { get; init; } = 5;

    /// <summary>Restrict results to a specific document. Null means all documents.</summary>
    public Guid? DocumentId { get; init; }

    /// <summary>
    /// Restrict results to a set of documents.
    /// When non-null and non-empty this supersedes <see cref="DocumentId"/>.
    /// Typical use: the LLM retrieved a list of matching document IDs from a business-module
    /// tool (e.g. <c>search_contracts</c>) and wants to do a focused RAG pass over exactly
    /// those documents.
    /// </summary>
    public IReadOnlyList<Guid>? DocumentIds { get; init; }

    /// <summary>Restrict results to a specific document type. Null means all types.</summary>
    public string? DocumentTypeCode { get; init; }

    /// <summary>Minimum acceptable normalized score in [0, 1].</summary>
    public double? MinScore { get; init; }

    /// <summary>
    /// Original query text. When non-null, implementations that support hybrid search
    /// (e.g., Qdrant BM25 full-text + dense vector with RRF fusion) may combine
    /// dense-vector recall with keyword recall for improved precision.
    /// If null, pure dense-vector search is performed.
    /// </summary>
    public string? QueryText { get; init; }
}
