using System.Globalization;

namespace Dignite.Paperbase.Contracts.Contracts;

/// <summary>
/// 合同字段提取的结构化输出契约 + Agent Instructions。
/// 由 ChatClientAgent 通过 ResponseFormat = JsonSchema&lt;ContractExtractionResult&gt; 直接反序列化。
/// </summary>
public class ContractExtractionResult
{
    /// <summary>契約タイトル（例: 業務委託基本契約書）</summary>
    public string? Title { get; set; }

    /// <summary>契約番号（例: 2024-001）</summary>
    public string? ContractNumber { get; set; }

    /// <summary>甲（委託者）の名称（例: 株式会社ABC）</summary>
    public string? PartyAName { get; set; }

    /// <summary>乙（受託者）の名称（例: 株式会社XYZ）</summary>
    public string? PartyBName { get; set; }

    /// <summary>相手方名（甲乙不明の場合の補完）</summary>
    public string? CounterpartyName { get; set; }

    /// <summary>契約締結日（ISO 8601: yyyy-MM-dd）</summary>
    public string? SignedDate { get; set; }

    /// <summary>契約開始日（ISO 8601: yyyy-MM-dd）</summary>
    public string? EffectiveDate { get; set; }

    /// <summary>契約終了日（ISO 8601: yyyy-MM-dd）</summary>
    public string? ExpirationDate { get; set; }

    /// <summary>契約金額（数値のみ、単位・カンマ不要）</summary>
    public decimal? TotalAmount { get; set; }

    /// <summary>通貨コード（例: JPY）</summary>
    public string? Currency { get; set; }

    /// <summary>自動更新の有無</summary>
    public bool? AutoRenewal { get; set; }

    /// <summary>解除通知期間（日数、整数）</summary>
    public int? TerminationNoticeDays { get; set; }

    /// <summary>準拠法（例: 日本法）</summary>
    public string? GoverningLaw { get; set; }

    /// <summary>契約概要（一文程度）</summary>
    public string? Summary { get; set; }

    /// <summary>抽出結果全体の信頼度（0.0-1.0）。判断できない場合は null。</summary>
    public double? ExtractionConfidence { get; set; }
}

public static class ContractAgentInstructions
{
    public const string SystemPrompt =
        "あなたは契約書の情報抽出専門家です。" +
        "提供された契約書テキストから所定のフィールドを抽出し、JSON で回答してください。" +
        "日付は ISO 8601 形式（yyyy-MM-dd）。金額は数値のみ（単位・カンマ不要）。" +
        "ExtractionConfidence は抽出結果全体の信頼度を 0.0 から 1.0 で設定し、判断できない場合は null を設定してください。" +
        "値が不明な場合は null を設定してください。推測せず、テキストに明記されている値のみ抽出してください。";
}

/// <summary>
/// LLM-boundary adapter: converts the LLM's <see cref="ContractExtractionResult"/>
/// shape into a domain-shaped <see cref="ContractFields"/>. Date-string parsing and
/// confidence normalization are LLM concerns and live here, not in the aggregate.
/// </summary>
public static class ContractExtractionResultExtensions
{
    public static ContractFields ToContractFields(this ContractExtractionResult result)
    {
        return new ContractFields
        {
            Title = result.Title,
            ContractNumber = result.ContractNumber,
            PartyAName = result.PartyAName,
            PartyBName = result.PartyBName,
            CounterpartyName = result.CounterpartyName,
            SignedDate = ParseDate(result.SignedDate),
            EffectiveDate = ParseDate(result.EffectiveDate),
            ExpirationDate = ParseDate(result.ExpirationDate),
            TotalAmount = result.TotalAmount,
            Currency = string.IsNullOrEmpty(result.Currency) ? "JPY" : result.Currency,
            AutoRenewal = result.AutoRenewal,
            TerminationNoticeDays = result.TerminationNoticeDays,
            GoverningLaw = result.GoverningLaw,
            Summary = result.Summary,
            ExtractionConfidence = NormalizeConfidence(result.ExtractionConfidence)
        };
    }

    private static double? NormalizeConfidence(double? value)
    {
        if (!value.HasValue || value.Value < 0 || value.Value > 1)
        {
            return null;
        }

        return value.Value;
    }

    private static System.DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return System.DateTime.TryParseExact(
                value, "yyyy-MM-dd", CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)
            ? d
            : null;
    }
}
