using System;
using Dignite.Paperbase.Contracts.Contracts;
using Shouldly;
using Xunit;

namespace Dignite.Paperbase.Contracts.Contracts;

public class RegexContractExtractor_Tests
{
    [Fact]
    public void Should_Extract_Core_Fields_From_Japanese_Contract_Text()
    {
        // Arrange
        const string text = """
            業務委託契約書
            契約番号: CNT-2026-001
            甲: 株式会社ディグナイト
            乙: 株式会社サンプル
            契約期間: 2026年4月1日 から 2027年3月31日 まで
            契約金額: ¥1,200,000
            署名
            """;

        // Act
        var fields = RegexContractExtractor.Extract(text);

        // Assert
        fields.Title.ShouldBe("業務委託契約書");
        fields.ContractNumber.ShouldBe("CNT-2026-001");
        fields.PartyAName.ShouldBe("株式会社ディグナイト");
        fields.PartyBName.ShouldBe("株式会社サンプル");
        fields.CounterpartyName.ShouldBe("株式会社サンプル");
        fields.SignedDate.ShouldBe(new DateTime(2026, 4, 1));
        fields.EffectiveDate.ShouldBe(new DateTime(2026, 4, 1));
        fields.ExpirationDate.ShouldBe(new DateTime(2027, 3, 31));
        fields.TotalAmount.ShouldBe(1200000m);
        fields.Currency.ShouldBe("JPY");
        fields.NeedsReview.ShouldBeFalse();
        fields.ExtractionConfidence.ShouldBeGreaterThan(0.7);
    }

    [Fact]
    public void Should_Mark_NeedsReview_When_Too_Few_Fields_Are_Extracted()
    {
        // Act
        var fields = RegexContractExtractor.Extract("契約書");

        // Assert
        fields.Title.ShouldBe("契約書");
        fields.CounterpartyName.ShouldBeNull();
        fields.TotalAmount.ShouldBeNull();
        fields.NeedsReview.ShouldBeTrue();
    }
}
