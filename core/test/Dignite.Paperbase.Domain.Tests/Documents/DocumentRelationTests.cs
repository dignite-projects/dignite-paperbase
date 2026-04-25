using System;
using Dignite.Paperbase.Domain.Documents;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Dignite.Paperbase.Documents;

public class DocumentRelationTests
{
    [Fact]
    public void Confirm_Should_Convert_To_Manual_And_Clear_Confidence()
    {
        var relation = new DocumentRelation(
            Guid.NewGuid(),
            tenantId: null,
            sourceDocumentId: Guid.NewGuid(),
            targetDocumentId: Guid.NewGuid(),
            relationType: "references",
            source: RelationSource.AiSuggested,
            confidence: 0.9);

        relation.Confirm();

        relation.Source.ShouldBe(RelationSource.Manual);
        relation.Confidence.ShouldBeNull();
    }

    [Fact]
    public void Constructor_Should_Clear_Confidence_For_Manual_Relation()
    {
        var relation = new DocumentRelation(
            Guid.NewGuid(),
            tenantId: null,
            sourceDocumentId: Guid.NewGuid(),
            targetDocumentId: Guid.NewGuid(),
            relationType: "references",
            source: RelationSource.Manual,
            confidence: 0.9);

        relation.Confidence.ShouldBeNull();
    }

    [Fact]
    public void Constructor_Should_Reject_Empty_Document_Id()
    {
        var exception = Should.Throw<BusinessException>(() => new DocumentRelation(
            Guid.NewGuid(),
            tenantId: null,
            sourceDocumentId: Guid.Empty,
            targetDocumentId: Guid.NewGuid(),
            relationType: "references",
            source: RelationSource.AiSuggested,
            confidence: 0.9));

        exception.Code.ShouldBe(PaperbaseErrorCodes.DocumentRelationDocumentIdRequired);
    }

    [Fact]
    public void Constructor_Should_Reject_Self_Relation()
    {
        var documentId = Guid.NewGuid();

        var exception = Should.Throw<BusinessException>(() => new DocumentRelation(
            Guid.NewGuid(),
            tenantId: null,
            sourceDocumentId: documentId,
            targetDocumentId: documentId,
            relationType: "references",
            source: RelationSource.AiSuggested,
            confidence: 0.9));

        exception.Code.ShouldBe(PaperbaseErrorCodes.DocumentRelationCannotTargetSelf);
    }

    [Fact]
    public void Constructor_Should_Reject_Out_Of_Range_Confidence()
    {
        var exception = Should.Throw<BusinessException>(() => new DocumentRelation(
            Guid.NewGuid(),
            tenantId: null,
            sourceDocumentId: Guid.NewGuid(),
            targetDocumentId: Guid.NewGuid(),
            relationType: "references",
            source: RelationSource.AiSuggested,
            confidence: 1.1));

        exception.Code.ShouldBe(PaperbaseErrorCodes.DocumentRelationConfidenceOutOfRange);
    }

    [Fact]
    public void Constructor_Should_Reject_Blank_Relation_Type()
    {
        Should.Throw<ArgumentException>(() => new DocumentRelation(
            Guid.NewGuid(),
            tenantId: null,
            sourceDocumentId: Guid.NewGuid(),
            targetDocumentId: Guid.NewGuid(),
            relationType: " ",
            source: RelationSource.AiSuggested,
            confidence: 0.9));
    }
}
