using System;
using System.Collections.Generic;
using System.Reflection;
using Shouldly;
using Xunit;

namespace Dignite.Vault.Extract.Documents;

/// <summary>
/// Pins the "no confirmed type implies no type-bound field values" invariant (see
/// <c>RequestClassificationReview</c>'s own doc comment) for the v3 <see cref="Document.FlexFields"/> bag.
/// Both transitions already cleared the v2 <c>ExtractedFieldValues</c> collection; <c>FlexFields</c> was
/// never wired in when v3 replaced v2 as the live storage - a self-inflicted instance of the same
/// "accepted at the door, broken at every use" bug class already documented for the <c>Tree</c> field type.
/// </summary>
public class FlexFieldsClearedOnTypeRetraction_Tests
{
    [Fact]
    public void RequestClassificationReview_Clears_FlexFields()
    {
        var doc = NewDocumentWithFlexFields();

        InvokeInternal(doc, "RequestClassificationReview");

        doc.FlexFields.ShouldBeEmpty();
    }

    [Fact]
    public void MarkAsContainer_Clears_FlexFields()
    {
        var doc = NewDocumentWithFlexFields();

        InvokeInternal(doc, "MarkAsContainer");

        doc.FlexFields.ShouldBeEmpty();
    }

    private static Document NewDocumentWithFlexFields()
    {
        var doc = new Document(
            Guid.NewGuid(), null,
            new FileOrigin(
                blobName: $"blobs/{Guid.NewGuid():N}.pdf",
                uploadedByUserName: "test-user",
                contentType: "application/pdf",
                contentHash: $"{Guid.NewGuid():N}{Guid.NewGuid():N}",
                fileSize: 1024,
                originalFileName: "test.pdf"));

        doc.SetFlexFields(new Dictionary<string, object?> { ["invoice_no"] = "INV-001" });
        doc.FlexFields.ShouldNotBeEmpty(); // sanity: there must be something for the transition to clear

        return doc;
    }

    private static void InvokeInternal(Document doc, string methodName)
        => typeof(Document)
            .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(doc, null);
}
