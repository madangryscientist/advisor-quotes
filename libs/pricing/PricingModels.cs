namespace Pricing;

/// <summary>
/// Inputs to <see cref="PremiumCalculator.Calculate"/>. Dates are calendar dates; no time zone is involved.
/// </summary>
/// <param name="DateOfBirth">The life insured's date of birth.</param>
/// <param name="AsOf">The date the premium is priced for. Age is completed years at this date.</param>
/// <param name="IsSmoker">Whether the life insured smokes.</param>
/// <param name="ProductCode">One of the codes in <see cref="PremiumCalculator"/> (for example <c>LIFE</c>, <c>DISABILITY</c>).</param>
/// <param name="CoverAmount">Cover in ZAR. Must be between R10,000 and R5,000,000.</param>
/// <param name="TermYears">Policy term in whole years, from 1 to 30.</param>
public sealed record PricingRequest(
    DateOnly DateOfBirth,
    DateOnly AsOf,
    bool IsSmoker,
    string ProductCode,
    decimal CoverAmount,
    int TermYears);

/// <summary>The result of pricing a request: either a <see cref="PremiumQuote"/> or a <see cref="PricingRejection"/>.</summary>
public abstract record PricingOutcome;

/// <summary>A priced monthly premium.</summary>
/// <param name="MonthlyPremium">Monthly premium, rounded to 2 decimal places.</param>
/// <param name="Currency">Always <c>ZAR</c>.</param>
/// <param name="PricingVersion">Version of the rate tables that produced this premium, so stored quotes stay explainable.</param>
public sealed record PremiumQuote(decimal MonthlyPremium, string Currency, string PricingVersion) : PricingOutcome;

/// <summary>A request that failed validation. Nothing was priced.</summary>
/// <param name="Error">The reason the request was rejected.</param>
public sealed record PricingRejection(PricingError Error) : PricingOutcome;

/// <summary>Why a <see cref="PricingRequest"/> was rejected.</summary>
public enum PricingError
{
    /// <summary>The completed age at the as-of date is outside 18 to 65. A date of birth after the as-of date lands here too.</summary>
    AgeOutOfRange,

    /// <summary>The cover amount is outside R10,000 to R5,000,000.</summary>
    CoverOutOfRange,

    /// <summary>The term is outside 1 to 30 years.</summary>
    TermOutOfRange,

    /// <summary>The product code is not in the rate table.</summary>
    UnknownProduct,
}
