using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

    [Fact]
    public void BuildCitationDtos_Source_Name_Uses_Page_Format_When_Page_Present()
    {
        var docId = Guid.NewGuid();
        var dto = DocumentChatAppService.BuildCitationDtos(new List<VectorSearchResult>
        {
            new() { RecordId = Guid.NewGuid(), DocumentId = docId, ChunkIndex = 5, PageNumber = 12, Text = "..." }
        }).Single();

        dto.SourceName.ShouldBe($"Document {docId} (page 12)");
    }

    [Fact]
    public void BuildCitationDtos_Source_Name_Uses_Chunk_Format_When_Page_Null()
    {
        var docId = Guid.NewGuid();
        var dto = DocumentChatAppService.BuildCitationDtos(new List<VectorSearchResult>
        {
            new() { RecordId = Guid.NewGuid(), DocumentId = docId, ChunkIndex = 7, PageNumber = null, Text = "..." }
        }).Single();

        dto.SourceName.ShouldBe($"Document {docId} (chunk #7)");
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
}
