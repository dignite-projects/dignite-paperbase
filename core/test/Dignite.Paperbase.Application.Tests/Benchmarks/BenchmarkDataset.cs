using System;
using System.Collections.Generic;
using System.Linq;

namespace Dignite.Paperbase.Documents.Benchmarks;

/// <summary>
/// Synthetic JP/EN dataset for the Slice 7 hybrid-search benchmark. The corpus
/// is hand-built (not脱敏 production data) so the harness is reproducible in
/// CI without external data. Each <see cref="BenchmarkChunk"/> has a stable id
/// and explicit business intent (contract, invoice, certificate, prose) so we
/// can label which chunks are the "correct" answer for each query.
///
/// Design constraints:
/// - Chunk surface is shaped to produce realistic ranking ambiguity: many
///   contract chunks share most of their Japanese vocabulary and only differ
///   in the contract number (<c>ABC-NNN</c>). Dense embedding-style scoring
///   based on bigram overlap will under-discriminate between them; sparse
///   token matching breaks the tie via the rare ID. This is the canonical
///   case hybrid search is supposed to fix.
/// - Semantic queries deliberately use vocabulary that overlaps with multiple
///   chunks; the correct answers are the ones whose <em>topic</em> matches,
///   not the ones with the most surface overlap.
/// </summary>
public static class BenchmarkDataset
{
    public static IReadOnlyList<BenchmarkChunk> Chunks { get; } = BuildChunks();
    public static IReadOnlyList<BenchmarkQuery> Queries { get; } = BuildQueries();

    private static IReadOnlyList<BenchmarkChunk> BuildChunks()
    {
        var chunks = new List<BenchmarkChunk>();

        // ── Contracts (10) — each shares most Japanese vocabulary, differs in ID ──
        for (var i = 1; i <= 10; i++)
        {
            var id = $"ABC-{i:D3}";
            chunks.Add(Chunk(
                $"contract.{id}",
                $"業務委託契約書 {id}。契約番号: {id}。契約期間: 2026-04-01 から 2027-03-31 まで。" +
                $"甲は乙に対して業務を委託し、乙はこれを受託する。本契約期間中、双方は秘密保持義務を負う。"));
        }

        // Contract chunks with explicit金額 (different IDs again)
        chunks.Add(Chunk(
            "contract.ABC-002.amount",
            "業務委託契約書 ABC-002。契約金額: 1,200,000円（税別）。支払条件は月末締め翌月末払い。" +
            "甲: 株式会社Alpha, 乙: 株式会社Beta。"));
        chunks.Add(Chunk(
            "contract.ABC-005.amount",
            "業務委託契約書 ABC-005。契約金額: 980,000円（税別）。支払期日: 2026-06-30。" +
            "甲: 株式会社Gamma, 乙: 株式会社Delta。"));

        // ── Invoices (10) — same template, distinguished by INV id ──
        for (var i = 1; i <= 10; i++)
        {
            var id = $"INV-2026-04-{i:D3}";
            var amount = 500_000 + i * 50_000;
            chunks.Add(Chunk(
                $"invoice.{id}",
                $"請求書 {id}。請求金額: {amount:N0}円。支払期限: 2026-05-31。" +
                $"発行者: 株式会社Echo。受取人: 株式会社Foxtrot。" +
                $"請求書番号: {id}。"));
        }

        // ── Certificates (5) — name-keyed ──
        chunks.Add(Chunk("cert.yamada",
            "在職証明書。氏名: 山田太郎。社員番号: EMP-12345。所属: 開発部。" +
            "上記の者は当社に在職していることを証明する。"));
        chunks.Add(Chunk("cert.suzuki",
            "卒業証明書。氏名: 鈴木花子。学籍番号: STU-2024-789。" +
            "上記の者は本校を 2024-03-31 に卒業したことを証明する。"));
        chunks.Add(Chunk("cert.tanaka",
            "資格証明書。氏名: 田中一郎。資格: 情報処理技術者。資格番号: CERT-AB-9876。" +
            "上記の者が当該資格を有することを証明する。"));
        chunks.Add(Chunk("cert.sato",
            "在職証明書。氏名: 佐藤次郎。社員番号: EMP-67890。所属: 営業部。" +
            "上記の者は当社に在職していることを証明する。"));
        chunks.Add(Chunk("cert.takahashi",
            "卒業証明書。氏名: 高橋美咲。学籍番号: STU-2023-456。" +
            "上記の者は本校を 2023-09-30 に卒業したことを証明する。"));

        // ── Semantic prose (5) — broader Japanese / no explicit IDs ──
        chunks.Add(Chunk("prose.privacy",
            "本サービスは、お客様のプライバシーを尊重し、個人情報を適切に管理します。" +
            "個人情報の収集、利用、開示は法令に従って行います。"));
        chunks.Add(Chunk("prose.support",
            "弊社のカスタマーサポートは、年中無休で対応しております。" +
            "お問い合わせは公式サイトのお問い合わせフォームからお願いいたします。"));
        chunks.Add(Chunk("prose.warranty",
            "製品の保証期間は購入日から一年間です。保証期間中の故障は無償で修理します。" +
            "ただし、お客様の過失による故障は保証対象外となります。"));
        chunks.Add(Chunk("prose.shipping",
            "商品の発送は注文確定後3営業日以内に行います。" +
            "離島・遠隔地の場合は別途送料が発生する場合があります。"));
        chunks.Add(Chunk("prose.returns",
            "商品の返品は到着後14日以内にご連絡ください。" +
            "未開封・未使用の商品に限り、返品を承ります。"));

        return chunks;
    }

    private static IReadOnlyList<BenchmarkQuery> BuildQueries()
    {
        var queries = new List<BenchmarkQuery>();

        // ── Precise-text queries (15) — rely on rare tokens (IDs, names) to disambiguate ──
        queries.Add(Q(QueryCategory.PreciseText, "ABC-001 の契約期間は？", "contract.ABC-001"));
        queries.Add(Q(QueryCategory.PreciseText, "ABC-005 の契約金額", "contract.ABC-005.amount"));
        queries.Add(Q(QueryCategory.PreciseText, "契約番号 ABC-007", "contract.ABC-007"));
        queries.Add(Q(QueryCategory.PreciseText, "ABC-002 の支払条件", "contract.ABC-002.amount"));
        queries.Add(Q(QueryCategory.PreciseText, "ABC-009 の業務内容", "contract.ABC-009"));

        queries.Add(Q(QueryCategory.PreciseText, "INV-2026-04-001 の請求金額", "invoice.INV-2026-04-001"));
        queries.Add(Q(QueryCategory.PreciseText, "INV-2026-04-005 の支払期限", "invoice.INV-2026-04-005"));
        queries.Add(Q(QueryCategory.PreciseText, "INV-2026-04-008 の発行者", "invoice.INV-2026-04-008"));
        queries.Add(Q(QueryCategory.PreciseText, "INV-2026-04-003 の受取人", "invoice.INV-2026-04-003"));
        queries.Add(Q(QueryCategory.PreciseText, "請求書番号 INV-2026-04-010", "invoice.INV-2026-04-010"));

        queries.Add(Q(QueryCategory.PreciseText, "山田太郎 の在職証明書", "cert.yamada"));
        queries.Add(Q(QueryCategory.PreciseText, "EMP-12345 の社員情報", "cert.yamada"));
        queries.Add(Q(QueryCategory.PreciseText, "鈴木花子 の卒業日", "cert.suzuki"));
        queries.Add(Q(QueryCategory.PreciseText, "CERT-AB-9876 の資格", "cert.tanaka"));
        queries.Add(Q(QueryCategory.PreciseText, "佐藤次郎 の社員番号", "cert.sato"));

        // ── Semantic queries (15) — broader Japanese, multiple plausible chunks ──
        queries.Add(Q(QueryCategory.Semantic, "甲乙双方の責任範囲はどうなっていますか",
            // Any of the contract chunks is a valid answer
            BuildAllContractIds()));
        queries.Add(Q(QueryCategory.Semantic, "秘密保持義務について",
            BuildAllContractIds()));
        queries.Add(Q(QueryCategory.Semantic, "業務委託の期間",
            BuildAllContractIds()));

        queries.Add(Q(QueryCategory.Semantic, "請求書の支払期限",
            BuildAllInvoiceIds()));
        queries.Add(Q(QueryCategory.Semantic, "発行者と受取人について",
            BuildAllInvoiceIds()));
        queries.Add(Q(QueryCategory.Semantic, "請求金額の確認",
            BuildAllInvoiceIds()));

        queries.Add(Q(QueryCategory.Semantic, "在職を証明する書類",
            new[] { "cert.yamada", "cert.sato" }));
        queries.Add(Q(QueryCategory.Semantic, "卒業を証明する書類",
            new[] { "cert.suzuki", "cert.takahashi" }));
        queries.Add(Q(QueryCategory.Semantic, "技術者の資格証明",
            new[] { "cert.tanaka" }));

        queries.Add(Q(QueryCategory.Semantic, "個人情報の取扱い方針",
            new[] { "prose.privacy" }));
        queries.Add(Q(QueryCategory.Semantic, "プライバシー保護について",
            new[] { "prose.privacy" }));
        queries.Add(Q(QueryCategory.Semantic, "カスタマーサポートに連絡したい",
            new[] { "prose.support" }));
        queries.Add(Q(QueryCategory.Semantic, "製品保証期間中の故障対応",
            new[] { "prose.warranty" }));
        queries.Add(Q(QueryCategory.Semantic, "発送までにかかる日数",
            new[] { "prose.shipping" }));
        queries.Add(Q(QueryCategory.Semantic, "返品ポリシーを教えて",
            new[] { "prose.returns" }));

        return queries;
    }

    private static BenchmarkChunk Chunk(string id, string text)
        => new() { Id = id, Text = text };

    private static BenchmarkQuery Q(QueryCategory category, string text, params string[] expectedChunkIds)
        => new()
        {
            Category = category,
            Text = text,
            ExpectedChunkIds = new HashSet<string>(expectedChunkIds, StringComparer.Ordinal)
        };

    private static string[] BuildAllContractIds()
        => Enumerable.Range(1, 10).Select(i => $"contract.ABC-{i:D3}")
            .Concat(["contract.ABC-002.amount", "contract.ABC-005.amount"])
            .ToArray();

    private static string[] BuildAllInvoiceIds()
        => Enumerable.Range(1, 10).Select(i => $"invoice.INV-2026-04-{i:D3}").ToArray();
}

public sealed class BenchmarkChunk
{
    public string Id { get; init; } = default!;
    public string Text { get; init; } = default!;
}

public sealed class BenchmarkQuery
{
    public QueryCategory Category { get; init; }
    public string Text { get; init; } = default!;
    public HashSet<string> ExpectedChunkIds { get; init; } = default!;
}

public enum QueryCategory
{
    /// <summary>Queries dominated by rare exact tokens — IDs, names, dates,
    /// invoice numbers. Hybrid search is expected to outperform vector here.</summary>
    PreciseText,

    /// <summary>Queries dominated by topical/semantic vocabulary — contract
    /// terms, policy language, paraphrases. Vector search alone should already
    /// do well; hybrid should not regress.</summary>
    Semantic
}
