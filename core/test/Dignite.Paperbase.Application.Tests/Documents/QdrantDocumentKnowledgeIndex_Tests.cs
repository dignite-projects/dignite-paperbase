using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Paperbase.Rag;
using Dignite.Paperbase.Rag.Qdrant;
using Microsoft.Extensions.Options;
using Qdrant.Client.Grpc;
using Shouldly;
using Xunit;

namespace Dignite.Paperbase.Documents;

public class QdrantDocumentKnowledgeIndex_Tests
{
    private const string CollectionName = "paperbase_test";

    private readonly FakeQdrantClientGateway _gateway = new();
    private readonly QdrantDocumentKnowledgeIndex _index;

    public QdrantDocumentKnowledgeIndex_Tests()
    {
        _index = new QdrantDocumentKnowledgeIndex(
            _gateway,
            new QdrantFilterBuilder(),
            new QdrantPointIdGenerator(),
            Options.Create(new QdrantRagOptions { CollectionName = CollectionName }));
    }

    [Fact]
    public async Task SearchAsync_Adds_Tenant_Filter_And_Does_Not_Leak_Other_Tenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await _index.SearchAsync(new VectorSearchRequest
        {
            TenantId = tenantA,
            QueryVector = new float[] { 0.1f, 0.2f },
            Mode = VectorSearchMode.Vector
        });

        _gateway.LastQueryFilter.ShouldNotBeNull();
        _gateway.LastQueryFilter!.Must.Count(c => c.Field.Key == QdrantPayloadFields.TenantId).ShouldBe(1);
        FilterHasKeyword(
            _gateway.LastQueryFilter,
            QdrantPayloadFields.TenantId,
            EncodeTenantId(tenantA)).ShouldBeTrue();
        FilterHasKeyword(
            _gateway.LastQueryFilter,
            QdrantPayloadFields.TenantId,
            EncodeTenantId(tenantB)).ShouldBeFalse();
    }

    [Fact]
    public async Task SearchAsync_Adds_DocumentId_Filter()
    {
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        await _index.SearchAsync(new VectorSearchRequest
        {
            TenantId = tenantId,
            DocumentId = documentId,
            QueryVector = new float[] { 0.1f, 0.2f },
            Mode = VectorSearchMode.Vector
        });

        FilterHasKeyword(
            _gateway.LastQueryFilter!,
            QdrantPayloadFields.DocumentId,
            EncodeDocumentId(documentId)).ShouldBeTrue();
    }

    [Fact]
    public async Task SearchAsync_Adds_DocumentTypeCode_Filter()
    {
        await _index.SearchAsync(new VectorSearchRequest
        {
            TenantId = Guid.NewGuid(),
            DocumentTypeCode = "contract.general",
            QueryVector = new float[] { 0.1f, 0.2f },
            Mode = VectorSearchMode.Vector
        });

        FilterHasKeyword(
            _gateway.LastQueryFilter!,
            QdrantPayloadFields.DocumentTypeCode,
            "contract.general").ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteByDocumentIdAsync_Deletes_By_Tenant_And_Document()
    {
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        await _index.DeleteByDocumentIdAsync(documentId, tenantId);

        _gateway.LastDeleteFilter.ShouldNotBeNull();
        FilterHasKeyword(
            _gateway.LastDeleteFilter!,
            QdrantPayloadFields.TenantId,
            EncodeTenantId(tenantId)).ShouldBeTrue();
        FilterHasKeyword(
            _gateway.LastDeleteFilter!,
            QdrantPayloadFields.DocumentId,
            EncodeDocumentId(documentId)).ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteByDocumentIdAsync_Uses_Host_Tenant_Payload_For_Null_Tenant()
    {
        var documentId = Guid.NewGuid();

        await _index.DeleteByDocumentIdAsync(documentId, tenantId: null);

        FilterHasKeyword(
            _gateway.LastDeleteFilter!,
            QdrantPayloadFields.TenantId,
            HostTenantId).ShouldBeTrue();
    }

    [Fact]
    public async Task UpsertDocumentAsync_Uses_Stable_PointId_For_Retry()
    {
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        await _index.UpsertDocumentAsync(CreateUpdate(tenantId, documentId, Guid.NewGuid()));
        var firstPointId = _gateway.UpsertedPoints.Single().Id.Uuid;

        await _index.UpsertDocumentAsync(CreateUpdate(tenantId, documentId, Guid.NewGuid()));
        var secondPointId = _gateway.UpsertedPoints.Single().Id.Uuid;

        secondPointId.ShouldBe(firstPointId);
    }

    [Fact]
    public async Task UpsertDocumentAsync_Deletes_Stale_Chunks_For_Same_Document()
    {
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        await _index.UpsertDocumentAsync(CreateUpdate(tenantId, documentId, Guid.NewGuid()));

        _gateway.LastDeleteFilter.ShouldNotBeNull();
        FilterHasKeyword(
            _gateway.LastDeleteFilter!,
            QdrantPayloadFields.TenantId,
            EncodeTenantId(tenantId)).ShouldBeTrue();
        FilterHasKeyword(
            _gateway.LastDeleteFilter!,
            QdrantPayloadFields.DocumentId,
            EncodeDocumentId(documentId)).ShouldBeTrue();
        _gateway.LastDeleteFilter!.Must.Any(c => c.Field.Key == QdrantPayloadFields.ChunkIndex).ShouldBeTrue();
    }

    [Fact]
    public async Task Keyword_And_Hybrid_Search_Are_Not_Supported_By_Provider()
    {
        await Should.ThrowAsync<NotSupportedException>(() => _index.SearchAsync(new VectorSearchRequest
        {
            TenantId = Guid.NewGuid(),
            QueryVector = new float[] { 0.1f, 0.2f },
            Mode = VectorSearchMode.Keyword
        }));

        await Should.ThrowAsync<NotSupportedException>(() => _index.SearchAsync(new VectorSearchRequest
        {
            TenantId = Guid.NewGuid(),
            QueryVector = new float[] { 0.1f, 0.2f },
            Mode = VectorSearchMode.Hybrid
        }));
    }

    [Fact]
    public async Task SearchSimilarDocumentsAsync_Is_Not_Supported_In_First_Phase()
    {
        await Should.ThrowAsync<NotSupportedException>(() => _index.SearchSimilarDocumentsAsync(
            Guid.NewGuid(),
            tenantId: null,
            topK: 5));
    }

    private static DocumentVectorIndexUpdate CreateUpdate(Guid? tenantId, Guid documentId, Guid chunkRecordId)
    {
        return new DocumentVectorIndexUpdate
        {
            TenantId = tenantId,
            DocumentId = documentId,
            Chunks =
            [
                new DocumentVectorRecord
                {
                    Id = chunkRecordId,
                    TenantId = tenantId,
                    DocumentId = documentId,
                    DocumentTypeCode = "contract.general",
                    ChunkIndex = 0,
                    Text = "chunk",
                    Vector = new float[] { 0.1f, 0.2f }
                }
            ]
        };
    }

    private static bool FilterHasKeyword(Filter filter, string key, string value)
    {
        return filter.Must.Any(c =>
            c.Field.Key == key &&
            c.Field.Match.Keyword == value);
    }

    private const string HostTenantId = "__host__";

    private static string EncodeTenantId(Guid? tenantId)
        => tenantId?.ToString("D") ?? HostTenantId;

    private static string EncodeDocumentId(Guid documentId)
        => documentId.ToString("D");

    private sealed class FakeQdrantClientGateway : IQdrantClientGateway
    {
        public Filter? LastQueryFilter { get; private set; }

        public Filter? LastDeleteFilter { get; private set; }

        public IReadOnlyList<PointStruct> UpsertedPoints { get; private set; } = [];

        public Task EnsureCollectionAsync(QdrantRagOptions options, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpsertAsync(
            string collectionName,
            IReadOnlyList<PointStruct> points,
            CancellationToken cancellationToken = default)
        {
            collectionName.ShouldBe(CollectionName);
            UpsertedPoints = points;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ScoredPoint>> QueryAsync(
            string collectionName,
            float[] vector,
            Filter filter,
            ulong limit,
            float? scoreThreshold,
            CancellationToken cancellationToken = default)
        {
            collectionName.ShouldBe(CollectionName);
            LastQueryFilter = filter;
            return Task.FromResult<IReadOnlyList<ScoredPoint>>([]);
        }

        public Task DeleteAsync(
            string collectionName,
            Filter filter,
            CancellationToken cancellationToken = default)
        {
            collectionName.ShouldBe(CollectionName);
            LastDeleteFilter = filter;
            return Task.CompletedTask;
        }
    }
}
