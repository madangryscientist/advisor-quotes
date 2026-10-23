using System.Globalization;

namespace Pricing.Tests;

public class PremiumCalculatorTests
{
    private static readonly DateOnly AsOf = new(2026, 10, 21);

    // Age 41 on AsOf: the 40–49 band.
    private static readonly DateOnly DateOfBirth41 = new(1985, 6, 15);

    private static PricingRequest Request(
        DateOnly? dateOfBirth = null,
        bool isSmoker = false,
        string productCode = "LIFE",
        decimal coverAmount = 1_000_000m,
        int termYears = 20) =>
        new(dateOfBirth ?? DateOfBirth41, AsOf, isSmoker, productCode, coverAmount, termYears);

    private static PremiumQuote Priced(PricingOutcome outcome) => Assert.IsType<PremiumQuote>(outcome);

    private static PricingError Rejected(PricingOutcome outcome) => Assert.IsType<PricingRejection>(outcome).Error;

    [Fact]
    public void LifeWorkedExample()
    {
        // R1,000,000 ÷ 1,000 = 1,000 units × R1.90 (age 40–49) × 1.00 (non-smoker) × 1.10 (20 years) × 1.00 (LIFE) = R2,090.00
        var quote = Priced(PremiumCalculator.Calculate(Request()));

        Assert.Equal(2090.00m, quote.MonthlyPremium);
    }

    [Fact]
    public void DisabilityWorkedExample()
    {
        // 1,000 × R1.90 × 1.00 × 1.10 × 1.40 (DISABILITY) = R2,926.00
        var quote = Priced(PremiumCalculator.Calculate(Request(productCode: "DISABILITY")));

        Assert.Equal(2926.00m, quote.MonthlyPremium);
    }

    [Fact]
    public void QuoteCarriesCurrencyAndPricingVersion()
    {
        var quote = Priced(PremiumCalculator.Calculate(Request()));

        Assert.Equal("ZAR", quote.Currency);
        Assert.Equal("2026.1", quote.PricingVersion);
    }

    [Theory]
    [InlineData(18, "8.00")]
    [InlineData(29, "8.00")]
    [InlineData(30, "11.00")]
    [InlineData(39, "11.00")]
    [InlineData(40, "19.00")]
    [InlineData(49, "19.00")]
    [InlineData(50, "32.00")]
    [InlineData(59, "32.00")]
    [InlineData(60, "54.00")]
    [InlineData(65, "54.00")]
    public void AgeBandBoundariesUseTheBandRate(int age, string expectedPremium)
    {
        // R10,000 of cover (10 × the per-R1,000 rate), 10 years, non-smoker, LIFE.
        var request = Request(dateOfBirth: AsOf.AddYears(-age), coverAmount: 10_000m, termYears: 10);

        var quote = Priced(PremiumCalculator.Calculate(request));

        Assert.Equal(decimal.Parse(expectedPremium, CultureInfo.InvariantCulture), quote.MonthlyPremium);
    }

    [Fact]
    public void AgeIsCompletedYearsSoTheBirthdayItselfMovesTheBand()
    {
        // The day before the 30th birthday is still 29 (R0.80 band); the birthday itself makes 30 (R1.10 band).
        var dayBefore = AsOf.AddYears(-30).AddDays(1);
        var birthday = AsOf.AddYears(-30);

        Assert.Equal(8.00m, Priced(PremiumCalculator.Calculate(Request(dateOfBirth: dayBefore, coverAmount: 10_000m, termYears: 10))).MonthlyPremium);
        Assert.Equal(11.00m, Priced(PremiumCalculator.Calculate(Request(dateOfBirth: birthday, coverAmount: 10_000m, termYears: 10))).MonthlyPremium);
    }

    [Fact]
    public void SmokerLoadingMultipliesTheNonSmokerPremiumByOneAndAHalf()
    {
        var nonSmoker = Priced(PremiumCalculator.Calculate(Request(isSmoker: false)));
        var smoker = Priced(PremiumCalculator.Calculate(Request(isSmoker: true)));

        Assert.Equal(2090.00m, nonSmoker.MonthlyPremium);
        Assert.Equal(3135.00m, smoker.MonthlyPremium);
    }

    [Fact]
    public void TermFactorStepsAt10And20Years()
    {
        // 1,000 × R1.90 for LIFE, non-smoker: factor 1.00 at 10 years, 1.10 at 11, 1.25 at 21.
        Assert.Equal(1900.00m, Priced(PremiumCalculator.Calculate(Request(termYears: 10))).MonthlyPremium);
        Assert.Equal(2090.00m, Priced(PremiumCalculator.Calculate(Request(termYears: 11))).MonthlyPremium);
        Assert.Equal(2090.00m, Priced(PremiumCalculator.Calculate(Request(termYears: 20))).MonthlyPremium);
        Assert.Equal(2375.00m, Priced(PremiumCalculator.Calculate(Request(termYears: 21))).MonthlyPremium);
    }

    [Fact]
    public void FinalPremiumRoundsHalfAwayFromZeroTo2Dp()
    {
        // R10,000 → 10 × R1.90 × 1.50 (smoker) × 1.25 (25 years) × 1.00 (LIFE) = R35.625 exactly.
        // Half away from zero gives R35.63; banker's rounding would give R35.62.
        var request = Request(isSmoker: true, coverAmount: 10_000m, termYears: 25);

        var quote = Priced(PremiumCalculator.Calculate(request));

        Assert.Equal(35.63m, quote.MonthlyPremium);
    }

    [Fact]
    public void RejectsDateOfBirthAfterAsOfAsAgeOutOfRange()
    {
        var request = Request(dateOfBirth: AsOf.AddDays(1));

        Assert.Equal(PricingError.AgeOutOfRange, Rejected(PremiumCalculator.Calculate(request)));
    }

    [Fact]
    public void RejectsAgeBelow18()
    {
        var request = Request(dateOfBirth: AsOf.AddYears(-17));

        Assert.Equal(PricingError.AgeOutOfRange, Rejected(PremiumCalculator.Calculate(request)));
    }

    [Fact]
    public void RejectsAgeAbove65()
    {
        var request = Request(dateOfBirth: AsOf.AddYears(-66));

        Assert.Equal(PricingError.AgeOutOfRange, Rejected(PremiumCalculator.Calculate(request)));
    }

    [Fact]
    public void RejectsCoverBelowMinimum()
    {
        var request = Request(coverAmount: 9_000m);

        Assert.Equal(PricingError.CoverOutOfRange, Rejected(PremiumCalculator.Calculate(request)));
    }

    [Fact]
    public void RejectsCoverAboveMaximum()
    {
        var request = Request(coverAmount: 5_001_000m);

        Assert.Equal(PricingError.CoverOutOfRange, Rejected(PremiumCalculator.Calculate(request)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void RejectsTermOutside1To30Years(int termYears)
    {
        var request = Request(termYears: termYears);

        Assert.Equal(PricingError.TermOutOfRange, Rejected(PremiumCalculator.Calculate(request)));
    }

    [Fact]
    public void RejectsUnknownProduct()
    {
        var request = Request(productCode: "FUNERAL");

        Assert.Equal(PricingError.UnknownProduct, Rejected(PremiumCalculator.Calculate(request)));
    }

    [Fact]
    public void RejectsNullProductWithoutThrowing()
    {
        var request = Request(productCode: null!);

        Assert.Equal(PricingError.UnknownProduct, Rejected(PremiumCalculator.Calculate(request)));
    }
}
