using System;
using Dignite.Paperbase.Rag;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Application.Documents.Rag;

public class DocumentKnowledgeIndexSearchModeResolver : ITransientDependency
{
    public virtual void EnsureSearchCapabilities(DocumentKnowledgeIndexCapabilities capabilities)
    {
        if (!capabilities.SupportsStructuredFilter)
        {
            throw new InvalidOperationException(
                "The configured document knowledge index does not support structured filters. " +
                "Paperbase requires tenant/document/type filters to avoid leaking search results across scopes.");
        }
    }

    public virtual VectorSearchMode ResolveSearchMode(
        VectorSearchMode requestedMode,
        DocumentKnowledgeIndexCapabilities capabilities)
    {
        return requestedMode switch
        {
            VectorSearchMode.Vector when capabilities.SupportsVectorSearch => VectorSearchMode.Vector,
            VectorSearchMode.Keyword when capabilities.SupportsKeywordSearch => VectorSearchMode.Keyword,
            VectorSearchMode.Hybrid when capabilities.SupportsHybridSearch => VectorSearchMode.Hybrid,
            VectorSearchMode.Hybrid when capabilities.SupportsVectorSearch => VectorSearchMode.Vector,
            VectorSearchMode.Keyword when capabilities.SupportsVectorSearch => VectorSearchMode.Vector,
            _ => throw new InvalidOperationException(
                $"The configured document knowledge index does not support requested search mode '{requestedMode}' " +
                "and cannot fall back to vector search.")
        };
    }

    public virtual bool RequiresVector(VectorSearchMode mode)
    {
        return mode is VectorSearchMode.Vector or VectorSearchMode.Hybrid;
    }
}
