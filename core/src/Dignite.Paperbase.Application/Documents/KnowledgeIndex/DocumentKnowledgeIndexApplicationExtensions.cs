using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Rag;

/// <summary>
/// Application-layer convenience extensions for <see cref="IDocumentKnowledgeIndex"/>.
/// Requires <see cref="ICurrentTenant"/>, so lives in the Application layer rather than
/// the Rag abstraction layer (which has no MultiTenancy dependency).
/// </summary>
public static class DocumentKnowledgeIndexApplicationExtensions
{
    /// <summary>
    /// Execute a search pre-filled with the tenant from <paramref name="currentTenant"/>.
    /// Use this in Application / Workflow code that runs inside an ABP request context.
    /// For background jobs or CLI tools, set <see cref="VectorSearchRequest.TenantId"/>
    /// explicitly instead.
    /// </summary>
    public static Task<IReadOnlyList<VectorSearchResult>> SearchForCurrentTenantAsync(
        this IDocumentKnowledgeIndex index,
        ICurrentTenant currentTenant,
        VectorSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var requestWithTenant = request with { TenantId = currentTenant.Id };
        return index.SearchAsync(requestWithTenant, cancellationToken);
    }
}
