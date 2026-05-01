using System;
using System.Collections.Generic;
using System.Linq;
using Qdrant.Client.Grpc;
using Volo.Abp.DependencyInjection;
using static Qdrant.Client.Grpc.Conditions;

namespace Dignite.Paperbase.KnowledgeIndex.Qdrant;

public class QdrantFilterBuilder : ITransientDependency
{
    public virtual Filter BuildTenantFilter(Guid? tenantId)
    {
        return new Filter
        {
            Must =
            {
                MatchKeyword(QdrantPayloadFields.TenantId, QdrantPayloadEncoder.EncodeTenantId(tenantId))
            }
        };
    }

    public virtual Filter BuildDocumentFilter(Guid? tenantId, Guid documentId)
    {
        var filter = BuildTenantFilter(tenantId);
        filter.Must.Add(MatchKeyword(QdrantPayloadFields.DocumentId, QdrantPayloadEncoder.EncodeDocumentId(documentId)));
        return filter;
    }

    public virtual Filter BuildSearchFilter(VectorSearchRequest request)
    {
        var filter = BuildTenantFilter(request.TenantId);

        // DocumentIds (multi) supersedes DocumentId (single).
        if (request.DocumentIds?.Count > 0)
        {
            if (request.DocumentIds.Count == 1)
            {
                filter.Must.Add(MatchKeyword(
                    QdrantPayloadFields.DocumentId,
                    QdrantPayloadEncoder.EncodeDocumentId(request.DocumentIds[0])));
            }
            else
            {
                // Wrap multiple document IDs in a Should (OR) sub-filter nested inside Must.
                var shouldFilter = new Filter();
                foreach (var docId in request.DocumentIds)
                {
                    shouldFilter.Should.Add(MatchKeyword(
                        QdrantPayloadFields.DocumentId,
                        QdrantPayloadEncoder.EncodeDocumentId(docId)));
                }
                filter.Must.Add(new Condition { Filter = shouldFilter });
            }
        }
        else if (request.DocumentId.HasValue)
        {
            filter.Must.Add(MatchKeyword(
                QdrantPayloadFields.DocumentId,
                QdrantPayloadEncoder.EncodeDocumentId(request.DocumentId.Value)));
        }

        if (!string.IsNullOrWhiteSpace(request.DocumentTypeCode))
        {
            filter.Must.Add(MatchKeyword(QdrantPayloadFields.DocumentTypeCode, request.DocumentTypeCode));
        }

        return filter;
    }

    public virtual Filter BuildStaleChunksFilter(
        Guid? tenantId,
        Guid documentId,
        IReadOnlyCollection<int> retainedChunkIndexes)
    {
        var filter = BuildDocumentFilter(tenantId, documentId);

        if (retainedChunkIndexes.Count > 0)
        {
            filter.Must.Add(MatchExcept(
                QdrantPayloadFields.ChunkIndex,
                retainedChunkIndexes.Select(i => (long)i).ToList()));
        }

        return filter;
    }
}
