using System;
using Dignite.Paperbase.Rag.Pgvector;
using Dignite.Paperbase.Rag.Pgvector.Documents;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Dignite.Paperbase.Documents;

public class DocumentChunkTests
{
    [Fact]
    public void UpdateRecord_Should_Reject_TenantId_Change()
    {
        var chunk = CreateChunk(tenantId: Guid.NewGuid(), documentId: Guid.NewGuid());

        var exception = Should.Throw<BusinessException>(() =>
            chunk.UpdateRecord(Guid.NewGuid(), chunk.DocumentId, 1, "updated", MakeVector(0.2f)));

        exception.Code.ShouldBe(PgvectorRagErrorCodes.DocumentChunkTenantImmutable);
    }

    [Fact]
    public void UpdateRecord_Should_Reject_DocumentId_Change()
    {
        var chunk = CreateChunk(tenantId: Guid.NewGuid(), documentId: Guid.NewGuid());

        var exception = Should.Throw<BusinessException>(() =>
            chunk.UpdateRecord(chunk.TenantId, Guid.NewGuid(), 1, "updated", MakeVector(0.2f)));

        exception.Code.ShouldBe(PgvectorRagErrorCodes.DocumentChunkDocumentImmutable);
    }

    [Fact]
    public void UpdateRecord_Should_Update_Mutable_Fields_When_Ownership_Matches()
    {
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var chunk = CreateChunk(tenantId, documentId);

        chunk.UpdateRecord(tenantId, documentId, 2, "updated", MakeVector(0.3f));

        chunk.TenantId.ShouldBe(tenantId);
        chunk.DocumentId.ShouldBe(documentId);
        chunk.ChunkIndex.ShouldBe(2);
        chunk.ChunkText.ShouldBe("updated");
        chunk.EmbeddingVector[0].ShouldBe(0.3f);
    }

    [Fact]
    public void Constructor_Should_Persist_Denormalized_Fields()
    {
        var chunk = new DocumentChunk(
            Guid.NewGuid(),
            tenantId: null,
            documentId: Guid.NewGuid(),
            chunkIndex: 0,
            chunkText: "section text",
            embeddingVector: MakeVector(0.1f),
            documentTypeCode: "Contract.Service",
            title: "Section 3.1 — Indemnification",
            pageNumber: 7);

        chunk.DocumentTypeCode.ShouldBe("Contract.Service");
        chunk.Title.ShouldBe("Section 3.1 — Indemnification");
        chunk.PageNumber.ShouldBe(7);
    }

    [Fact]
    public void Constructor_Should_Default_Denormalized_Fields_To_Null()
    {
        var chunk = CreateChunk(tenantId: null, documentId: Guid.NewGuid());

        chunk.DocumentTypeCode.ShouldBeNull();
        chunk.Title.ShouldBeNull();
        chunk.PageNumber.ShouldBeNull();
    }

    [Fact]
    public void Constructor_Should_Truncate_Title_Exceeding_MaxLength()
    {
        var oversizedTitle = new string('A', DocumentChunkConsts.MaxTitleLength + 50);

        var chunk = new DocumentChunk(
            Guid.NewGuid(),
            tenantId: null,
            documentId: Guid.NewGuid(),
            chunkIndex: 0,
            chunkText: "text",
            embeddingVector: MakeVector(0.1f),
            title: oversizedTitle);

        chunk.Title.ShouldNotBeNull();
        chunk.Title!.Length.ShouldBe(DocumentChunkConsts.MaxTitleLength);
    }

    [Fact]
    public void UpdateRecord_Should_Refresh_Denormalized_Fields()
    {
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var chunk = new DocumentChunk(
            Guid.NewGuid(), tenantId, documentId, 0, "v1", MakeVector(0.1f),
            documentTypeCode: "Contract.Old", title: "Old Title", pageNumber: 1);

        chunk.UpdateRecord(
            tenantId, documentId, 1, "v2", MakeVector(0.2f),
            documentTypeCode: "Contract.New", title: "New Title", pageNumber: 2);

        chunk.DocumentTypeCode.ShouldBe("Contract.New");
        chunk.Title.ShouldBe("New Title");
        chunk.PageNumber.ShouldBe(2);
    }

    private static DocumentChunk CreateChunk(Guid? tenantId, Guid documentId)
    {
        return new DocumentChunk(
            Guid.NewGuid(),
            tenantId,
            documentId,
            0,
            "initial",
            MakeVector(0.1f));
    }

    private static float[] MakeVector(float firstValue)
    {
        var vector = new float[PaperbaseDbProperties.EmbeddingVectorDimension];
        vector[0] = firstValue;
        return vector;
    }
}
