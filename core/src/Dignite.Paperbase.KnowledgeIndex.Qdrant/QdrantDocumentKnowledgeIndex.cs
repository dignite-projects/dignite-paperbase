using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Qdrant.Client.Grpc;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.KnowledgeIndex.Qdrant;

[ExposeServices(typeof(IDocumentKnowledgeIndex))]
public class QdrantDocumentKnowledgeIndex : IDocumentKnowledgeIndex, ITransientDependency
{
    private readonly IQdrantClientGateway _gateway;
    private readonly QdrantFilterBuilder _filterBuilder;
    private readonly QdrantPointIdGenerator _pointIdGenerator;
    private readonly QdrantKnowledgeIndexOptions _options;

    public QdrantDocumentKnowledgeIndex(
        IQdrantClientGateway gateway,
        QdrantFilterBuilder filterBuilder,
        QdrantPointIdGenerator pointIdGenerator,
        IOptions<QdrantKnowledgeIndexOptions> options)
    {
        _gateway = gateway;
        _filterBuilder = filterBuilder;
        _pointIdGenerator = pointIdGenerator;
        _options = options.Value;
    }

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

        var points = update.Chunks.Select(chunk => CreatePoint(update, chunk)).ToList();
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
        if (request.QueryVector.IsEmpty)
        {
            return [];
        }

        IReadOnlyList<ScoredPoint> points;
        var filter = _filterBuilder.BuildSearchFilter(request);
        var limit = (ulong)Math.Max(0, request.TopK);
        var hybrid = _options.EnableHybridSearch && !string.IsNullOrWhiteSpace(request.QueryText);

        if (hybrid)
        {
            var sparseVector = SparseBm25Encoder.Encode(request.QueryText!);
            points = await _gateway.QueryHybridAsync(
                _options.CollectionName,
                request.QueryVector.ToArray(),
                sparseVector,
                filter,
                limit,
                scoreThreshold: null,
                cancellationToken);
        }
        else
        {
            var scoreThreshold = request.MinScore.HasValue ? (float)request.MinScore.Value : (float?)null;
            points = await _gateway.QueryAsync(
                _options.CollectionName,
                request.QueryVector.ToArray(),
                filter,
                limit,
                scoreThreshold,
                cancellationToken);
        }

        return points.Select(p => MapToResult(p, hybrid)).ToList();
    }

    protected virtual PointStruct CreatePoint(DocumentVectorIndexUpdate update, DocumentVectorRecord record)
    {
        Vectors vectors = _options.EnableHybridSearch
            ? BuildHybridVectors(record)
            : record.Vector.ToArray();

        var point = new PointStruct
        {
            Id = _pointIdGenerator.Create(update.TenantId, update.DocumentId, record.ChunkIndex),
            Vectors = vectors,
            Payload =
            {
                [QdrantPayloadFields.TenantId] = QdrantPayloadEncoder.EncodeTenantId(update.TenantId),
                [QdrantPayloadFields.DocumentId] = QdrantPayloadEncoder.EncodeDocumentId(update.DocumentId),
                [QdrantPayloadFields.ChunkIndex] = (long)record.ChunkIndex,
                [QdrantPayloadFields.Text] = record.Text
            }
        };

        if (!string.IsNullOrWhiteSpace(update.DocumentTypeCode))
        {
            point.Payload[QdrantPayloadFields.DocumentTypeCode] = update.DocumentTypeCode;
        }

        if (record.PageNumber.HasValue)
        {
            point.Payload[QdrantPayloadFields.PageNumber] = (long)record.PageNumber.Value;
        }

        return point;
    }

    protected virtual Vectors BuildHybridVectors(DocumentVectorRecord record)
    {
        var (bm25Values, bm25Indices) = SparseBm25Encoder.Encode(record.Text ?? string.Empty);

        var dense = new DenseVector();
        dense.Data.AddRange(record.Vector.ToArray());

        var sparse = new SparseVector();
        sparse.Values.AddRange(bm25Values);
        sparse.Indices.AddRange(bm25Indices);

        var namedVectors = new NamedVectors();
        namedVectors.Vectors.Add("", new Vector { Dense = dense });
        namedVectors.Vectors.Add(QdrantPayloadFields.Bm25VectorName, new Vector { Sparse = sparse });

        return new Vectors { Vectors_ = namedVectors };
    }

    protected virtual VectorSearchResult MapToResult(ScoredPoint point, bool hybrid)
    {
        return new VectorSearchResult
        {
            RecordId = Guid.Parse(point.Id.Uuid),
            DocumentId = Guid.Parse(GetStringPayload(point, QdrantPayloadFields.DocumentId)),
            DocumentTypeCode = TryGetStringPayload(point, QdrantPayloadFields.DocumentTypeCode),
            ChunkIndex = checked((int)GetIntegerPayload(point, QdrantPayloadFields.ChunkIndex)),
            Text = GetStringPayload(point, QdrantPayloadFields.Text),
            // Hybrid (RRF) scores are not normalized to [0,1] and cannot be compared
            // against MinScore thresholds — surface them as null so callers don't apply
            // a normalized-cosine threshold on the wrong scale.
            Score = hybrid ? null : Math.Clamp(point.Score, 0.0, 1.0),
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
