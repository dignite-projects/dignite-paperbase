using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.MultiTenancy;

namespace Dignite.Paperbase.Rag;

/// <summary>
/// Convenience extensions for <see cref="IDocumentVectorStore"/>.
/// Lives in the Rag layer so all callers (Application, Workflows, future Agent adapters)
/// share one implementation instead of each writing their own tenant-filling boilerplate.
/// </summary>
public static class DocumentVectorStoreExtensions
{
    /// <summary>
    /// Execute a search pre-filled with the tenant from <paramref name="currentTenant"/>.
    /// Use this in Application / Workflow code that runs inside an ABP request context.
    /// For background jobs or CLI tools, set <see cref="VectorSearchRequest.TenantId"/>
    /// explicitly instead.
    /// </summary>
    public static Task<IReadOnlyList<VectorSearchResult>> SearchForCurrentTenantAsync(
        this IDocumentVectorStore store,
        ICurrentTenant currentTenant,
        VectorSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var requestWithTenant = request with { TenantId = currentTenant.Id };
        return store.SearchAsync(requestWithTenant, cancellationToken);
    }
}
