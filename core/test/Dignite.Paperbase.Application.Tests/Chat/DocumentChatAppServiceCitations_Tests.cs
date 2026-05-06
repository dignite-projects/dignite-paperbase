using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Dignite.Paperbase.KnowledgeIndex;
using Shouldly;
using Xunit;

namespace Dignite.Paperbase.Chat;

/// <summary>
/// Focused unit tests for <see cref="DocumentChatAppService.BuildCitationDtos"/> and
/// the underlying <see cref="DocumentChatAppService.TruncateByGrapheme"/> helper.
/// Replaces the integration-level coverage that lived in
/// <c>Citations_Reflect_Injected_Chunks</c> and <c>Snippet_Does_Not_Break_Multibyte_Characters</c>
/// before Slice 3 dropped the BeforeAIInvoke auto-injection path — under the new
/// single MAF tool-calling path the substituted IChatClient does not invoke the
/// search tool, so end-to-end citation flow can no longer be exercised through the
/// AppService surface.
/// </summary>
public class DocumentChatAppServiceCitations_Tests
{
    [Fact]
    public void BuildCitationDtos_Returns_Empty_When_Results_Are_Null()
    {
        var dtos = DocumentChatAppService.BuildCitationDtos(null);
        dtos.ShouldNotBeNull();
        dtos.ShouldBeEmpty();
    }

    [Fact]
    public void BuildCitationDtos_Maps_DocumentId_ChunkIndex_PageNumber_Snippet_For_Each_Chunk()
    {
        var docId = Guid.NewGuid();
        var results = new List<VectorSearchResult>
        {
            new() { RecordId = Guid.NewGuid(), DocumentId = docId, ChunkIndex = 0, PageNumber = 1, Text = "chunk 0 text" },
            new() { RecordId = Guid.NewGuid(), DocumentId = docId, ChunkIndex = 1, PageNumber = 2, Text = "chunk 1 text" },
            new() { RecordId = Guid.NewGuid(), DocumentId = docId, ChunkIndex = 2, PageNumber = null, Text = "chunk 2 text" }
        };

        var dtos = DocumentChatAppService.BuildCitationDtos(results);

        dtos.Count.ShouldBe(3);
        for (var i = 0; i < 3; i++)
        {
            dtos[i].DocumentId.ShouldBe(docId);
            dtos[i].ChunkIndex.ShouldBe(i);
            dtos[i].PageNumber.ShouldBe(results[i].PageNumber);
            dtos[i].Snippet.ShouldBe(results[i].Text);
        }
    }

    [Theory]
    [InlineData(12)]    // page present — page number is ignored
    [InlineData(null)]  // page absent
    public void BuildCitationDtos_Source_Name_Uses_Chunk_Format_Regardless_Of_Page(int? pageNumber)
    {
        var docId = Guid.NewGuid();
        var dto = DocumentChatAppService.BuildCitationDtos(new List<VectorSearchResult>
        {
            new() { RecordId = Guid.NewGuid(), DocumentId = docId, ChunkIndex = 5, PageNumber = pageNumber, Text = "..." }
        }).Single();

        dto.SourceName.ShouldBe($"Document {docId} (chunk #5)");
    }

    [Fact]
    public void BuildCitationDtos_Truncates_Snippet_To_SnippetMaxGraphemes()
    {
        // Build a multibyte+emoji string longer than the boundary; assert the snippet
        // length is bounded by SnippetMaxGraphemes (200) measured in grapheme clusters,
        // not chars or bytes — so emojis are not split mid-codepoint and CJK chars stay
        // intact.
        var longText = string.Concat(Enumerable.Repeat("日本語テスト🚀", 30)); // ~240 graphemes

        var dto = DocumentChatAppService.BuildCitationDtos(new List<VectorSearchResult>
        {
            new() { RecordId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), ChunkIndex = 0, Text = longText }
        }).Single();

        var graphemeCount = new StringInfo(dto.Snippet).LengthInTextElements;
        graphemeCount.ShouldBeLessThanOrEqualTo(DocumentChatAppService.SnippetMaxGraphemes);

        // JSON round-trip must succeed without throwing on a half-emoji.
        var json = System.Text.Json.JsonSerializer.Serialize(dto);
        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<ChatCitationDto>(json);
        roundTripped!.Snippet.ShouldBe(dto.Snippet);
    }

    [Fact]
    public void TruncateByGrapheme_Returns_Empty_For_Null_Or_Empty()
    {
        DocumentChatAppService.TruncateByGrapheme(null!, 10).ShouldBe(string.Empty);
        DocumentChatAppService.TruncateByGrapheme(string.Empty, 10).ShouldBe(string.Empty);
    }

    [Fact]
    public void TruncateByGrapheme_Returns_Whole_Text_When_Within_Limit()
    {
        DocumentChatAppService.TruncateByGrapheme("hello", 100).ShouldBe("hello");
    }

    [Fact]
    public void TruncateByGrapheme_Counts_Emoji_As_Single_Grapheme()
    {
        // "🚀" is two UTF-16 code units (surrogate pair) but one grapheme cluster.
        // A naive `Substring(text, 0, n)` would split the pair; this helper must not.
        var text = "abc🚀def🚀ghi"; // 11 graphemes
        var truncated = DocumentChatAppService.TruncateByGrapheme(text, 5);
        new StringInfo(truncated).LengthInTextElements.ShouldBe(5);
        truncated.ShouldBe("abc🚀d");
    }

    [Fact]
    public void CitationJsonOptions_Serializes_With_CamelCase_Property_Names()
    {
        // The Angular client reads ChatMessageDto.citationsJson as a raw string and
        // parses it with default JS conventions (camelCase). If the persisted JSON
        // were PascalCase, citation.documentId / pageNumber / chunkIndex / snippet /
        // sourceName would all be undefined on the client.
        var dto = new ChatCitationDto
        {
            DocumentId = Guid.NewGuid(),
            PageNumber = 4,
            ChunkIndex = 12,
            Snippet = "snippet text",
            SourceName = "Document X"
        };

        var json = JsonSerializer.Serialize(dto, DocumentChatAppService.CitationJsonOptions);

        // The wire format must be camelCase — the client depends on it.
        json.ShouldContain("\"documentId\"", Case.Sensitive);
        json.ShouldContain("\"pageNumber\"", Case.Sensitive);
        json.ShouldContain("\"chunkIndex\"", Case.Sensitive);
        json.ShouldContain("\"snippet\"", Case.Sensitive);
        json.ShouldContain("\"sourceName\"", Case.Sensitive);
        json.ShouldNotContain("\"DocumentId\"", Case.Sensitive);
        json.ShouldNotContain("\"PageNumber\"", Case.Sensitive);
    }

    [Fact]
    public void CitationJsonOptions_Deserializes_Old_PascalCase_Rows()
    {
        // PropertyNameCaseInsensitive=true keeps deserialization compatible with rows
        // written before the camelCase fix landed. Without this, every assistant
        // message persisted under the old code path would silently drop its citation
        // metadata after upgrade.
        var legacyJson = """
            [{"DocumentId":"11111111-1111-1111-1111-111111111111","PageNumber":7,"ChunkIndex":3,"Snippet":"old","SourceName":"Doc"}]
            """;

        var citations = JsonSerializer.Deserialize<List<ChatCitationDto>>(legacyJson, DocumentChatAppService.CitationJsonOptions);

        citations.ShouldNotBeNull();
        citations!.Count.ShouldBe(1);
        citations[0].DocumentId.ShouldBe(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        citations[0].PageNumber.ShouldBe(7);
        citations[0].ChunkIndex.ShouldBe(3);
        citations[0].Snippet.ShouldBe("old");
        citations[0].SourceName.ShouldBe("Doc");
    }
}
