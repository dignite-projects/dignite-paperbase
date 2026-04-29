using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Qdrant.Client.Grpc;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Rag.Qdrant;

[ExposeServices(typeof(IDocumentKnowledgeIndex))]
public class QdrantDocumentKnowledgeIndex : IDocumentKnowledgeIndex, ITransientDependency
{
    private readonly IQdrantClientGateway _gateway;
    private readonly QdrantFilterBuilder _filterBuilder;
    private readonly QdrantPointIdGenerator _pointIdGenerator;
    private readonly QdrantRagOptions _options;

    public QdrantDocumentKnowledgeIndex(
        IQdrantClientGateway gateway,
        QdrantFilterBuilder filterBuilder,
        QdrantPointIdGenerator pointIdGenerator,
        IOptions<QdrantRagOptions> options)
    {
        _gateway = gateway;
        _filterBuilder = filterBuilder;
        _pointIdGenerator = pointIdGenerator;
        _options = options.Value;
    }

    public virtual DocumentKnowledgeIndexCapabilities Capabilities { get; } = new()
    {
        SupportsVectorSearch = true,
        SupportsKeywordSearch = false,
        SupportsHybridSearch = false,
        SupportsStructuredFilter = true,
        SupportsDeleteByDocumentId = true,
        NormalizesScore = true,
        SupportsSearchSimilarDocuments = false
    };

    /// <summary>
    /// Qdrant writes are non-transactional. This method is idempotent by using stable
    /// point ids (tenant + document + chunk index), upserting current chunks first,
    /// then deleting stale chunks for the same document.
    /// </summary>
    public virtual async Task UpsertDocumentAsync(
        DocumentVectorIndexUpdate update,
        CancellationToken cancellationToken = default)
    {
        if (update.Chunks.Count == 0)
        {
            await DeleteByDocumentIdAsync(update.DocumentId, update.TenantId, cancellationToken);
            return;
        }

        var points = update.Chunks.Select(CreatePoint).ToList();
        await _gateway.UpsertAsync(_options.CollectionName, points, cancellationToken);

        var retainedChunkIndexes = update.Chunks.Select(c => c.ChunkIndex).Distinct().ToList();
        await _gateway.DeleteAsync(
            _options.CollectionName,
            _filterBuilder.BuildStaleChunksFilter(update.TenantId, update.DocumentId, retainedChunkIndexes),
            cancellationToken);
    }

    public virtual Task DeleteByDocumentIdAsync(
        Guid documentId,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        return _gateway.DeleteAsync(
            _options.CollectionName,
            _filterBuilder.BuildDocumentFilter(tenantId, documentId),
            cancellationToken);
    }

    public virtual async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        VectorSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Mode != VectorSearchMode.Vector)
        {
            throw new NotSupportedException("Qdrant RAG provider currently supports only Vector search mode.");
        }

        if (request.QueryVector.IsEmpty)
        {
            return [];
        }

        var points = await _gateway.QueryAsync(
            _options.CollectionName,
            request.QueryVector.ToArray(),
            _filterBuilder.BuildSearchFilter(request),
            (ulong)Math.Max(0, request.TopK),
            request.MinScore.HasValue ? (float)request.MinScore.Value : null,
            cancellationToken);

        return points.Select(MapToResult).ToList();
    }

    public virtual Task<IReadOnlyList<DocumentSimilarityResult>> SearchSimilarDocumentsAsync(
        Guid documentId,
        Guid? tenantId,
        int topK,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Qdrant RAG provider does not support similar-document search in this phase.");
    }

    protected virtual PointStruct CreatePoint(DocumentVectorRecord record)
    {
        var point = new PointStruct
        {
            Id = _pointIdGenerator.Create(record.TenantId, record.DocumentId, record.ChunkIndex),
            Vectors = record.Vector.ToArray(),
            Payload =
            {
                [QdrantPayloadFields.TenantId] = QdrantPayloadEncoder.EncodeTenantId(record.TenantId),
                [QdrantPayloadFields.DocumentId] = QdrantPayloadEncoder.EncodeDocumentId(record.DocumentId),
                [QdrantPayloadFields.ChunkIndex] = (long)record.ChunkIndex,
                [QdrantPayloadFields.Text] = record.Text
            }
        };

        if (!string.IsNullOrWhiteSpace(record.DocumentTypeCode))
        {
            point.Payload[QdrantPayloadFields.DocumentTypeCode] = record.DocumentTypeCode;
        }

        if (!string.IsNullOrWhiteSpace(record.Title))
        {
            point.Payload[QdrantPayloadFields.Title] = record.Title;
        }

        if (record.PageNumber.HasValue)
        {
            point.Payload[QdrantPayloadFields.PageNumber] = (long)record.PageNumber.Value;
        }

        return point;
    }

    protected virtual VectorSearchResult MapToResult(ScoredPoint point)
    {
        return new VectorSearchResult
        {
            RecordId = Guid.Parse(point.Id.Uuid),
            DocumentId = Guid.Parse(GetStringPayload(point, QdrantPayloadFields.DocumentId)),
            DocumentTypeCode = TryGetStringPayload(point, QdrantPayloadFields.DocumentTypeCode),
            ChunkIndex = checked((int)GetIntegerPayload(point, QdrantPayloadFields.ChunkIndex)),
            Text = GetStringPayload(point, QdrantPayloadFields.Text),
            Score = Math.Clamp(point.Score, 0.0, 1.0),
            Title = TryGetStringPayload(point, QdrantPayloadFields.Title),
            PageNumber = TryGetIntegerPayload(point, QdrantPayloadFields.PageNumber) is { } page
                ? checked((int)page)
                : null
        };
    }

    protected virtual string GetStringPayload(ScoredPoint point, string key)
        => TryGetStringPayload(point, key)
           ?? throw new InvalidOperationException($"Qdrant point payload is missing '{key}'.");

    protected virtual string? TryGetStringPayload(ScoredPoint point, string key)
        => point.Payload.TryGetValue(key, out var value) && value.KindCase == Value.KindOneofCase.StringValue
            ? value.StringValue
            : null;

    protected virtual long GetIntegerPayload(ScoredPoint point, string key)
        => TryGetIntegerPayload(point, key)
           ?? throw new InvalidOperationException($"Qdrant point payload is missing '{key}'.");

    protected virtual long? TryGetIntegerPayload(ScoredPoint point, string key)
        => point.Payload.TryGetValue(key, out var value) && value.KindCase == Value.KindOneofCase.IntegerValue
            ? value.IntegerValue
            : null;
}
