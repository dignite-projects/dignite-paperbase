using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Volo.Abp.DependencyInjection;

namespace Dignite.Paperbase.Rag.Qdrant;

[ExposeServices(typeof(IQdrantClientGateway))]
public class QdrantClientGateway : IQdrantClientGateway, ISingletonDependency
{
    private readonly Lazy<IQdrantClient> _client;

    public QdrantClientGateway(QdrantClientFactory clientFactory)
    {
        _client = new Lazy<IQdrantClient>(clientFactory.CreateClient);
    }

    public virtual async Task EnsureCollectionAsync(
        QdrantRagOptions options,
        CancellationToken cancellationToken = default)
    {
        var client = _client.Value;
        if (!await client.CollectionExistsAsync(options.CollectionName, cancellationToken))
        {
            await client.CreateCollectionAsync(
                options.CollectionName,
                new VectorParams
                {
                    Size = (ulong)options.VectorDimension,
                    Distance = ParseDistance(options.Distance)
                },
                sparseVectorsConfig: options.EnableHybridSearch ? BuildSparseBm25Config() : null,
                cancellationToken: cancellationToken);
        }
        else
        {
            var info = await client.GetCollectionInfoAsync(options.CollectionName, cancellationToken);
            var vectorParams = info.Config.Params.VectorsConfig.Params;
            if (vectorParams.Size != (ulong)options.VectorDimension ||
                vectorParams.Distance != ParseDistance(options.Distance))
            {
                throw new InvalidOperationException(
                    $"Qdrant collection '{options.CollectionName}' vector config does not match QdrantRag options.");
            }

            if (options.EnableHybridSearch)
            {
                await EnsureSparseBm25VectorAsync(client, options.CollectionName, info, cancellationToken);
            }
        }

        await CreateKeywordPayloadIndexAsync(
            client,
            options.CollectionName,
            QdrantPayloadFields.TenantId,
            isTenant: true,
            cancellationToken);
        await CreateKeywordPayloadIndexAsync(
            client,
            options.CollectionName,
            QdrantPayloadFields.DocumentId,
            isTenant: false,
            cancellationToken);
        await CreateKeywordPayloadIndexAsync(
            client,
            options.CollectionName,
            QdrantPayloadFields.DocumentTypeCode,
            isTenant: false,
            cancellationToken);

        await CreateIntegerPayloadIndexAsync(
            client,
            options.CollectionName,
            QdrantPayloadFields.ChunkIndex,
            cancellationToken);

        await CreateFullTextPayloadIndexAsync(
            client, options.CollectionName, QdrantPayloadFields.Text, cancellationToken);
    }

    public virtual Task UpsertAsync(
        string collectionName,
        IReadOnlyList<PointStruct> points,
        CancellationToken cancellationToken = default)
    {
        return _client.Value.UpsertAsync(collectionName, points, wait: true, cancellationToken: cancellationToken);
    }

    public virtual Task<IReadOnlyList<ScoredPoint>> QueryAsync(
        string collectionName,
        float[] vector,
        Filter filter,
        ulong limit,
        float? scoreThreshold,
        CancellationToken cancellationToken = default)
    {
        return _client.Value.QueryAsync(
            collectionName: collectionName,
            query: vector,
            filter: filter,
            scoreThreshold: scoreThreshold,
            limit: limit,
            payloadSelector: true,
            cancellationToken: cancellationToken);
    }

    public virtual async Task<IReadOnlyList<ScoredPoint>> QueryHybridAsync(
        string collectionName,
        float[] denseVector,
        (float[] Values, uint[] Indices) sparseVector,
        Filter filter,
        ulong limit,
        float? scoreThreshold,
        CancellationToken cancellationToken = default)
    {
        var prefetch = new List<PrefetchQuery>
        {
            new() { Query = denseVector, Filter = filter, Limit = limit * 3 },
            new()
            {
                Query = (sparseVector.Values, sparseVector.Indices),
                Using = QdrantPayloadFields.Bm25VectorName,
                Filter = filter,
                Limit = limit * 3
            }
        };

        return await _client.Value.QueryAsync(
            collectionName: collectionName,
            query: (Query)Fusion.Rrf,
            prefetch: prefetch,
            usingVector: null,
            filter: filter,
            scoreThreshold: scoreThreshold,
            searchParams: null,
            limit: limit,
            offset: 0,
            payloadSelector: true,
            vectorsSelector: null,
            readConsistency: null,
            shardKeySelector: null,
            lookupFrom: null,
            timeout: null,
            cancellationToken: cancellationToken);
    }

    public virtual Task DeleteAsync(
        string collectionName,
        Filter filter,
        CancellationToken cancellationToken = default)
    {
        return _client.Value.DeleteAsync(collectionName, filter, wait: true, cancellationToken: cancellationToken);
    }

    protected virtual async Task CreateKeywordPayloadIndexAsync(
        IQdrantClient client,
        string collectionName,
        string fieldName,
        bool isTenant,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.CreatePayloadIndexAsync(
                collectionName: collectionName,
                fieldName: fieldName,
                schemaType: PayloadSchemaType.Keyword,
                indexParams: new PayloadIndexParams
                {
                    KeywordIndexParams = new KeywordIndexParams
                    {
                        IsTenant = isTenant
                    }
                },
                wait: true,
                cancellationToken: cancellationToken);
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.AlreadyExists or StatusCode.InvalidArgument)
        {
            // Qdrant treats duplicate index creation as an error on some versions.
            // Startup ensure remains idempotent because the desired index already exists.
        }
    }

    protected virtual async Task CreateIntegerPayloadIndexAsync(
        IQdrantClient client,
        string collectionName,
        string fieldName,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.CreatePayloadIndexAsync(
                collectionName: collectionName,
                fieldName: fieldName,
                schemaType: PayloadSchemaType.Integer,
                wait: true,
                cancellationToken: cancellationToken);
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.AlreadyExists or StatusCode.InvalidArgument)
        {
            // See CreateKeywordPayloadIndexAsync.
        }
    }

    protected virtual async Task CreateFullTextPayloadIndexAsync(
        IQdrantClient client,
        string collectionName,
        string fieldName,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.CreatePayloadIndexAsync(
                collectionName: collectionName,
                fieldName: fieldName,
                schemaType: PayloadSchemaType.Text,
                wait: true,
                cancellationToken: cancellationToken);
        }
        catch (RpcException ex) when (
            ex.StatusCode is StatusCode.AlreadyExists or StatusCode.InvalidArgument)
        { }
    }

    protected virtual async Task EnsureSparseBm25VectorAsync(
        IQdrantClient client,
        string collectionName,
        CollectionInfo info,
        CancellationToken cancellationToken)
    {
        if (info.Config.Params.SparseVectorsConfig.Map.ContainsKey(QdrantPayloadFields.Bm25VectorName))
            return;

        await client.UpdateCollectionAsync(
            collectionName,
            sparseVectorsConfig: BuildSparseBm25Config(),
            cancellationToken: cancellationToken);
    }

    protected virtual SparseVectorConfig BuildSparseBm25Config()
    {
        var config = new SparseVectorConfig();
        config.Map.Add(QdrantPayloadFields.Bm25VectorName, new SparseVectorParams
        {
            Modifier = Modifier.Idf
        });
        return config;
    }

    protected virtual Distance ParseDistance(string distance)
    {
        return distance.Trim().ToLowerInvariant() switch
        {
            "cosine" => Distance.Cosine,
            _ => throw new InvalidOperationException("QdrantRag:Distance currently supports only 'Cosine'.")
        };
    }
}
