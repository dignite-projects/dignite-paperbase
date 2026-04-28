using System;
using System.Collections.Generic;
using System.Linq;
using Dignite.Paperbase.Rag;
using Shouldly;
using Xunit;

namespace Dignite.Paperbase.Documents;

/// <summary>
/// Pure unit tests for <see cref="RrfFusion"/>. Slice 7 hybrid search relies on
/// RRF to merge dense + sparse rankings into a single mode-agnostic list. These
/// tests pin the contract: rank-only fusion, [0, 1] normalisation, TopK trim,
/// and graceful behaviour for empty / single-list inputs.
/// </summary>
public class RrfFusionTests
{
    [Fact]
    public void Empty_Lists_Return_Empty()
    {
        var merged = RrfFusion.Merge(
            primary: Array.Empty<VectorSearchResult>(),
            secondary: Array.Empty<VectorSearchResult>(),
            topK: 5);

        merged.ShouldBeEmpty();
    }

    [Fact]
    public void Single_List_Passes_Through_In_Same_Order()
    {
        // Only one path produced results — fusion must preserve the original ranking.
        // (Common case: vector recall returns hits but keyword finds nothing, or vice versa.)
        var primary = new[]
        {
            Result(id: "a", text: "alpha"),
            Result(id: "b", text: "beta"),
            Result(id: "c", text: "gamma")
        };

        var merged = RrfFusion.Merge(primary, Array.Empty<VectorSearchResult>(), topK: 5);

        merged.Select(r => r.Text).ShouldBe(["alpha", "beta", "gamma"]);
    }

    [Fact]
    public void Item_Appearing_In_Both_Lists_Outranks_Items_In_Only_One()
    {
        // The whole point of hybrid: a candidate that BOTH dense and sparse paths
        // consider relevant should beat a candidate either path alone considers
        // top. Even if "shared" is rank #2 in each list, RRF accumulates two
        // contributions vs the others' single contribution.
        var sharedId = Guid.NewGuid();
        var primaryOnlyId = Guid.NewGuid();
        var secondaryOnlyId = Guid.NewGuid();

        var primary = new[]
        {
            Result(primaryOnlyId, "primary-top"),
            Result(sharedId, "shared")
        };
        var secondary = new[]
        {
            Result(secondaryOnlyId, "secondary-top"),
            Result(sharedId, "shared")
        };

        var merged = RrfFusion.Merge(primary, secondary, topK: 5);

        merged[0].RecordId.ShouldBe(sharedId);
    }

    [Fact]
    public void Top1_In_Both_Lists_Beats_Top1_In_One_List()
    {
        // Sanity check on the fusion math: an item ranked #1 in both lists
        // should dominate items ranked #1 in only one list.
        var doubleHitId = Guid.NewGuid();
        var primaryOnlyId = Guid.NewGuid();
        var secondaryOnlyId = Guid.NewGuid();

        var primary = new[] { Result(doubleHitId, "double"), Result(primaryOnlyId, "p-only") };
        var secondary = new[] { Result(doubleHitId, "double"), Result(secondaryOnlyId, "s-only") };

        var merged = RrfFusion.Merge(primary, secondary, topK: 3);

        merged[0].RecordId.ShouldBe(doubleHitId);
    }

    [Fact]
    public void Score_Is_Normalized_To_Zero_One_Range()
    {
        // External contract: VectorSearchResult.Score ∈ [0, 1] regardless of mode.
        // Application-layer MinScore filtering relies on this invariant.
        var primary = new[]
        {
            Result("a", "alpha"),
            Result("b", "beta"),
            Result("c", "gamma")
        };
        var secondary = new[]
        {
            Result("b", "beta"),
            Result("d", "delta")
        };

        var merged = RrfFusion.Merge(primary, secondary, topK: 10);

        merged.ShouldAllBe(r => r.Score >= 0 && r.Score <= 1);
        // The top item should score exactly 1.0 after min-max normalisation.
        merged.Max(r => r.Score).ShouldBe(1.0);
    }

    [Fact]
    public void Single_Result_Is_Normalized_To_One()
    {
        // Degenerate min-max (range = 0): we conventionally hand back 1.0
        // rather than 0/0 NaN. A single result is by definition the most relevant
        // thing the system could find for this query.
        var merged = RrfFusion.Merge(
            primary: [Result("a", "only")],
            secondary: Array.Empty<VectorSearchResult>(),
            topK: 5);

        merged.Count.ShouldBe(1);
        merged[0].Score.ShouldBe(1.0);
    }

    [Fact]
    public void TopK_Trims_The_Result()
    {
        var primary = Enumerable.Range(0, 10)
            .Select(i => Result($"p{i}", $"p{i}"))
            .ToArray();
        var secondary = Enumerable.Range(0, 10)
            .Select(i => Result($"s{i}", $"s{i}"))
            .ToArray();

        var merged = RrfFusion.Merge(primary, secondary, topK: 3);

        merged.Count.ShouldBe(3);
    }

    [Fact]
    public void TopK_Zero_Throws()
    {
        // Defensive: callers should always pass a positive TopK. Silently
        // returning empty would mask integration bugs.
        Should.Throw<ArgumentOutOfRangeException>(() =>
            RrfFusion.Merge(
                primary: [Result("a", "x")],
                secondary: Array.Empty<VectorSearchResult>(),
                topK: 0));
    }

    [Fact]
    public void K_Zero_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            RrfFusion.Merge(
                primary: [Result("a", "x")],
                secondary: Array.Empty<VectorSearchResult>(),
                topK: 5,
                k: 0));
    }

    [Fact]
    public void Result_Preserves_Citation_Fields_From_Source()
    {
        // Title / PageNumber / DocumentTypeCode must survive RRF — these are the
        // citation fields the design explicitly calls out as "explainable".
        var id = Guid.NewGuid();
        var primary = new[]
        {
            new VectorSearchResult
            {
                RecordId = id,
                DocumentId = Guid.NewGuid(),
                ChunkIndex = 7,
                Text = "body",
                Score = 0.9,
                Title = "§3.1",
                PageNumber = 4,
                DocumentTypeCode = "contract.general"
            }
        };

        var merged = RrfFusion.Merge(primary, Array.Empty<VectorSearchResult>(), topK: 1);

        merged[0].Title.ShouldBe("§3.1");
        merged[0].PageNumber.ShouldBe(4);
        merged[0].DocumentTypeCode.ShouldBe("contract.general");
        merged[0].ChunkIndex.ShouldBe(7);
        merged[0].Text.ShouldBe("body");
    }

    private static VectorSearchResult Result(string id, string text)
        => Result(DeterministicGuid(id), text);

    private static VectorSearchResult Result(Guid id, string text) => new()
    {
        RecordId = id,
        DocumentId = id,
        ChunkIndex = 0,
        Text = text,
        Score = 0
    };

    /// <summary>Stable GUID derived from a string label so test inputs are
    /// readable and matches across primary/secondary lists work by string.</summary>
    private static Guid DeterministicGuid(string label)
    {
        var bytes = new byte[16];
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(label));
        Array.Copy(hash, bytes, 16);
        return new Guid(bytes);
    }
}
