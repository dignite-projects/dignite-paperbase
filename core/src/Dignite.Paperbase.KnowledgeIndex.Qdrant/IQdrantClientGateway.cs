using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Qdrant.Client.Grpc;

namespace Dignite.Paperbase.KnowledgeIndex.Qdrant;

public interface IQdrantClientGateway
{
    Task EnsureCollectionAsync(QdrantKnowledgeIndexOptions options, CancellationToken cancellationToken = default);

    Task UpsertAsync(
        string collectionName,
        IReadOnlyList<PointStruct> points,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScoredPoint>> QueryAsync(
        string collectionName,
        float[] vector,
        Filter filter,
        ulong limit,
        float? scoreThreshold,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScoredPoint>> QueryHybridAsync(
        string collectionName,
        float[] denseVector,
        (float[] Values, uint[] Indices) sparseVector,
        Filter filter,
        ulong limit,
        float? scoreThreshold,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string collectionName,
        Filter filter,
        CancellationToken cancellationToken = default);
}
