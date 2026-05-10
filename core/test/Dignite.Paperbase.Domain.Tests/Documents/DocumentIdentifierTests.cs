using System;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Dignite.Paperbase.Documents;

public class DocumentIdentifierTests
{
    [Fact]
    public void Constructor_Should_Reject_Empty_Document_Id()
    {
        var exception = Should.Throw<BusinessException>(() => new DocumentIdentifier(
            Guid.NewGuid(),
            tenantId: null,
            documentId: Guid.Empty,
            identifierType: "ContractNumber",
            identifierValue: "HT-2024-001"));

        exception.Code.ShouldBe(PaperbaseErrorCodes.DocumentIdentifierDocumentIdRequired);
    }

    [Fact]
    public void Constructor_Should_Reject_Blank_Identifier_Type()
    {
        var exception = Should.Throw<BusinessException>(() => new DocumentIdentifier(
            Guid.NewGuid(),
            tenantId: null,
            documentId: Guid.NewGuid(),
            identifierType: "  ",
            identifierValue: "HT-2024-001"));

        exception.Code.ShouldBe(PaperbaseErrorCodes.DocumentIdentifierTypeRequired);
    }

    [Fact]
    public void Constructor_Should_Reject_Blank_Identifier_Value()
    {
        var exception = Should.Throw<BusinessException>(() => new DocumentIdentifier(
            Guid.NewGuid(),
            tenantId: null,
            documentId: Guid.NewGuid(),
            identifierType: "ContractNumber",
            identifierValue: " "));

        exception.Code.ShouldBe(PaperbaseErrorCodes.DocumentIdentifierValueRequired);
    }

    [Fact]
    public void Constructor_Should_Trim_Surrounding_Whitespace()
    {
        var entity = new DocumentIdentifier(
            Guid.NewGuid(),
            tenantId: null,
            documentId: Guid.NewGuid(),
            identifierType: "  ContractNumber  ",
            identifierValue: "  HT-2024-001\n");

        entity.IdentifierType.ShouldBe("ContractNumber");
        entity.IdentifierValue.ShouldBe("HT-2024-001");
    }

    [Fact]
    public void Constructor_Should_Truncate_Oversized_Type_Defensively()
    {
        var oversized = new string('x', DocumentIdentifierConsts.MaxTypeLength + 50);

        var entity = new DocumentIdentifier(
            Guid.NewGuid(),
            tenantId: null,
            documentId: Guid.NewGuid(),
            identifierType: oversized,
            identifierValue: "HT-2024-001");

        entity.IdentifierType.Length.ShouldBe(DocumentIdentifierConsts.MaxTypeLength);
    }

    [Fact]
    public void Constructor_Should_Truncate_Oversized_Value_Defensively()
    {
        var oversized = new string('y', DocumentIdentifierConsts.MaxValueLength + 100);

        var entity = new DocumentIdentifier(
            Guid.NewGuid(),
            tenantId: null,
            documentId: Guid.NewGuid(),
            identifierType: "PartyName",
            identifierValue: oversized);

        entity.IdentifierValue.Length.ShouldBe(DocumentIdentifierConsts.MaxValueLength);
    }

    [Fact]
    public void Constructor_Should_Persist_Tenant_Id()
    {
        var tenantId = Guid.NewGuid();

        var entity = new DocumentIdentifier(
            Guid.NewGuid(),
            tenantId: tenantId,
            documentId: Guid.NewGuid(),
            identifierType: "ContractNumber",
            identifierValue: "HT-2024-001");

        entity.TenantId.ShouldBe(tenantId);
    }
}
