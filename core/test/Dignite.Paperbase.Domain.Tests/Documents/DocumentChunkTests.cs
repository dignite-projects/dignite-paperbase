using System;
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

        exception.Code.ShouldBe(PaperbaseErrorCodes.DocumentChunkTenantImmutable);
    }

    [Fact]
    public void UpdateRecord_Should_Reject_DocumentId_Change()
    {
        var chunk = CreateChunk(tenantId: Guid.NewGuid(), documentId: Guid.NewGuid());

        var exception = Should.Throw<BusinessException>(() =>
            chunk.UpdateRecord(chunk.TenantId, Guid.NewGuid(), 1, "updated", MakeVector(0.2f)));

        exception.Code.ShouldBe(PaperbaseErrorCodes.DocumentChunkDocumentImmutable);
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
