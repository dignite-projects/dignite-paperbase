using System;
using System.Collections.Generic;
using Dignite.Paperbase.Chat.Search;
using Dignite.Paperbase.KnowledgeIndex;
using Shouldly;
using Xunit;

namespace Dignite.Paperbase.Chat;

/// <summary>
/// Focused tests for <see cref="DocumentSearchCapture"/> (Issue #99 — pre-condition for
/// the multi-step retrieval rollout in #100). Verifies append + dedup + the new upper
/// bound (<see cref="DocumentSearchCapture.MaxResults"/>) so the model can call the
/// search AIFunction multiple times per turn without losing citations from earlier
/// invocations or blowing up the citations payload via a retry loop.
/// </summary>
public class DocumentSearchCapture_Tests
{
    [Fact]
    public void Append_AccumulatesAcrossMultipleInvocations()
    {
        var capture = new DocumentSearchCapture();

        capture.Append(new[]
        {
            BuildResult(documentId: "11111111-1111-1111-1111-111111111111", chunkIndex: 0)
        });
        capture.Append(new[]
        {
            BuildResult(documentId: "22222222-2222-2222-2222-222222222222", chunkIndex: 0),
            BuildResult(documentId: "22222222-2222-2222-2222-222222222222", chunkIndex: 1)
        });

        capture.HasSearches.ShouldBeTrue();
        capture.Results.Count.ShouldBe(3);
        capture.WasTruncated.ShouldBeFalse();
    }

    [Fact]
    public void Append_DedupsSameChunkAcrossInvocations()
    {
        // Same (documentId, chunkIndex, pageNumber) must not be added twice — typical
        // when two queries in the same turn happen to return overlapping top-K hits.
        var capture = new DocumentSearchCapture();
        var doc = "11111111-1111-1111-1111-111111111111";

        capture.Append(new[]
        {
            BuildResult(doc, chunkIndex: 0),
            BuildResult(doc, chunkIndex: 1)
        });
        capture.Append(new[]
        {
            BuildResult(doc, chunkIndex: 0), // duplicate of the first call's hit
            BuildResult(doc, chunkIndex: 2)
        });

        capture.Results.Count.ShouldBe(3); // chunkIndex 0/1/2, no duplicate of 0
    }

    [Fact]
    public void Append_DedupsByRecordIdWhenAvailable()
    {
        var capture = new DocumentSearchCapture();
        var sharedRecordId = Guid.NewGuid();

        capture.Append(new[]
        {
            new VectorSearchResult { RecordId = sharedRecordId, DocumentId = Guid.NewGuid(), ChunkIndex = 0 }
        });
        capture.Append(new[]
        {
            // Different DocumentId / ChunkIndex but same RecordId — dedup must use the
            // record id when present (it's the strongest identity signal from the store).
            new VectorSearchResult { RecordId = sharedRecordId, DocumentId = Guid.NewGuid(), ChunkIndex = 99 }
        });

        capture.Results.Count.ShouldBe(1);
    }

    [Fact]
    public void Append_DropsHitsBeyondMaxResults_AndSetsWasTruncated()
    {
        // Pathological case: model retries search 5x and each call returns 10 distinct
        // hits → 50 cumulative. With cap=20 we keep the first 20, drop the rest, and
        // surface the WasTruncated flag for telemetry.
        var capture = new DocumentSearchCapture(maxResults: 20);

        for (var batch = 0; batch < 5; batch++)
        {
            var hits = new List<VectorSearchResult>();
            for (var i = 0; i < 10; i++)
            {
                hits.Add(BuildResult(
                    documentId: Guid.NewGuid().ToString(),
                    chunkIndex: i));
            }
            capture.Append(hits);
        }

        capture.Results.Count.ShouldBe(20);
        capture.WasTruncated.ShouldBeTrue();
    }

    [Fact]
    public void Append_PreservesNaturalOrderingOfFirstHits_WhenTruncating()
    {
        // When the cap kicks in mid-batch we bail on the rest of that batch (and all
        // subsequent batches). Citations stay in the natural order of the first hits
        // returned, which is what users see surfaced.
        var capture = new DocumentSearchCapture(maxResults: 3);

        capture.Append(new[]
        {
            BuildResult("11111111-1111-1111-1111-111111111111", chunkIndex: 0),
            BuildResult("22222222-2222-2222-2222-222222222222", chunkIndex: 0)
        });
        capture.Append(new[]
        {
            BuildResult("33333333-3333-3333-3333-333333333333", chunkIndex: 0),
            BuildResult("44444444-4444-4444-4444-444444444444", chunkIndex: 0), // exceeds cap
            BuildResult("55555555-5555-5555-5555-555555555555", chunkIndex: 0)
        });

        capture.Results.Count.ShouldBe(3);
        capture.Results[0].DocumentId.ShouldBe(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        capture.Results[2].DocumentId.ShouldBe(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        capture.WasTruncated.ShouldBeTrue();
    }

    [Fact]
    public void HasSearches_BecomesTrue_EvenForEmptyAppend()
    {
        // "Model searched but found nothing" is honestly grounded (IsDegraded should
        // remain false). The signal that disambiguates "didn't search" from "searched
        // and got zero" is HasSearches — must flip on first Append regardless of count.
        var capture = new DocumentSearchCapture();

        capture.Append(Array.Empty<VectorSearchResult>());

        capture.HasSearches.ShouldBeTrue();
        capture.Results.ShouldBeEmpty();
    }

    [Fact]
    public void MaxResults_ZeroOrNegative_DisablesCap()
    {
        // The default constructor protects production usage. Tests / future callers
        // that explicitly want unbounded behavior can opt out by passing 0.
        var capture = new DocumentSearchCapture(maxResults: 0);

        for (var i = 0; i < 200; i++)
        {
            capture.Append(new[] { BuildResult(Guid.NewGuid().ToString(), chunkIndex: 0) });
        }

        capture.Results.Count.ShouldBe(200);
        capture.WasTruncated.ShouldBeFalse();
    }

    private static VectorSearchResult BuildResult(string documentId, int chunkIndex, int? pageNumber = null)
        => new()
        {
            DocumentId = Guid.Parse(documentId),
            ChunkIndex = chunkIndex,
            PageNumber = pageNumber,
            Text = $"chunk {chunkIndex} of {documentId}"
        };
}
