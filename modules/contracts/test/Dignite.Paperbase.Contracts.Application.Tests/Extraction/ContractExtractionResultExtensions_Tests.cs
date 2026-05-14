using System;
using Shouldly;
using Xunit;

namespace Dignite.Paperbase.Contracts.Extraction;

/// <summary>
/// LLM-boundary adapter tests for <see cref="ContractExtractionResultExtensions.ToContractFields"/>:
/// date string parsing, confidence normalization, and Currency JPY fallback. Pure
/// function — no DI.
/// </summary>
public class ContractExtractionResultExtensions_Tests
{
    [Fact]
    public void ToContractFields_Should_Parse_Dates_And_Normalize_Confidence()
    {
        var fields = new ContractExtractionResult
        {
            Title = "業務委託契約書",
            SignedDate = "2026-04-01",
            ExpirationDate = "2027-03-31",
            TotalAmount = 1200000m,
            Currency = "JPY",
            ExtractionConfidence = 0.82
        }.ToContractFields();

        fields.Title.ShouldBe("業務委託契約書");
        fields.SignedDate.ShouldBe(new DateTime(2026, 4, 1));
        fields.ExpirationDate.ShouldBe(new DateTime(2027, 3, 31));
        fields.TotalAmount.ShouldBe(1200000m);
        fields.ExtractionConfidence.ShouldBe(0.82);
    }

    [Fact]
    public void ToContractFields_Should_Drop_OutOfRange_Confidence()
    {
        new ContractExtractionResult { ExtractionConfidence = -0.1 }.ToContractFields()
            .ExtractionConfidence.ShouldBeNull();
        new ContractExtractionResult { ExtractionConfidence = 1.1 }.ToContractFields()
            .ExtractionConfidence.ShouldBeNull();
    }

    [Fact]
    public void ToContractFields_Should_Default_Empty_Currency_To_JPY()
    {
        new ContractExtractionResult { Currency = "" }.ToContractFields().Currency.ShouldBe("JPY");
        new ContractExtractionResult { Currency = null }.ToContractFields().Currency.ShouldBe("JPY");
        new ContractExtractionResult { Currency = "USD" }.ToContractFields().Currency.ShouldBe("USD");
    }
}
