using System;
using System.Collections.Generic;
using System.Linq;
using Qdrant.Client.Grpc;
using Volo.Abp.DependencyInjection;
using static Qdrant.Client.Grpc.Conditions;

namespace Dignite.Paperbase.Rag.Qdrant;

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

        if (request.DocumentId.HasValue)
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
