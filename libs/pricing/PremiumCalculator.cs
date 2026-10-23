namespace Pricing;

/// <summary>
/// Prices a monthly premium from a fixed rate table. Pure: no I/O, no clock, no shared state.
/// </summary>
/// <remarks>
/// <para>
/// <c>premium = (cover ÷ 1,000) × ageBaseRate × smokerLoading × termFactor × productMultiplier</c>
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Age</b> is completed years at <c>AsOf</c>. Eligible ages are 18 to 65 inclusive.
/// <c>ageBaseRate</c> is the monthly rate per R1,000 of cover for the age band:
/// 18–29 R0.80, 30–39 R1.10, 40–49 R1.90, 50–59 R3.20, 60–65 R5.40.
/// </description></item>
/// <item><description>
/// <b>Smoker loading</b> is ×1.50 for smokers and ×1.00 for non-smokers.
/// </description></item>
/// <item><description>
/// <b>Term factor</b> is ×1.00 for 1–10 years, ×1.10 for 11–20 years and ×1.25 for 21–30 years.
/// </description></item>
/// <item><description>
/// <b>Product multiplier</b> is ×1.00 for <c>LIFE</c> and ×1.40 for <c>DISABILITY</c>.
/// </description></item>
/// </list>
/// <para>
/// Cover must be between R10,000 and R5,000,000. All arithmetic is exact <c>decimal</c>;
/// only the final result is rounded, to 2 decimal places, with <see cref="MidpointRounding.AwayFromZero"/>.
/// </para>
/// <para>
/// Invalid input returns a <see cref="PricingRejection"/>; it never throws.
/// </para>
/// </remarks>
public static class PremiumCalculator
{
    /// <summary>Version of the rate tables in this class. Bump it whenever a rate, band or factor changes.</summary>
    public const string PricingVersion = "2026.1";

    /// <summary>The only currency this calculator prices in.</summary>
    public const string Currency = "ZAR";

    private const int MinAge = 18;
    private const int MaxAge = 65;
    private const decimal MinCover = 10_000m;
    private const decimal MaxCover = 5_000_000m;
    private const decimal CoverStep = 1_000m;
    private const int MinTerm = 1;
    private const int MaxTerm = 30;
    private const decimal SmokerLoading = 1.50m;

    // Age band → monthly rate per R1,000 of cover.
    private static readonly (int MinAge, int MaxAge, decimal RatePerThousand)[] AgeBands =
    [
        (18, 29, 0.80m),
        (30, 39, 1.10m),
        (40, 49, 1.90m),
        (50, 59, 3.20m),
        (60, MaxAge, 5.40m),
    ];

    // Term up to and including MaxTerm → factor. Checked in order.
    private static readonly (int MaxTerm, decimal Factor)[] TermFactors =
    [
        (10, 1.00m),
        (20, 1.10m),
        (30, 1.25m),
    ];

    private static readonly Dictionary<string, decimal> ProductMultipliers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LIFE"] = 1.00m,
        ["DISABILITY"] = 1.40m,
    };

    /// <summary>Prices one request, or explains why it cannot be priced.</summary>
    /// <param name="request">The inputs to price.</param>
    /// <returns>A <see cref="PremiumQuote"/> on success, otherwise a <see cref="PricingRejection"/>.</returns>
    public static PricingOutcome Calculate(PricingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A date of birth after AsOf gives a negative age, so it is rejected as AgeOutOfRange.
        // Steps run in validation order; the first rejection wins and later ones are ignored.
        return new PremiumBuilder()
            .ForAge(CompletedYears(request.DateOfBirth, request.AsOf))
            .ForCover(request.CoverAmount)
            .ForTerm(request.TermYears)
            .ForProduct(request.ProductCode)
            .ForSmoker(request.IsSmoker)
            .Build();
    }

    private static int CompletedYears(DateOnly dateOfBirth, DateOnly asOf)
    {
        var years = asOf.Year - dateOfBirth.Year;
        if (asOf < dateOfBirth.AddYears(years))
        {
            years--;
        }

        return years;
    }

    /// <summary>
    /// Accumulates the premium one rating factor at a time. The first invalid input records a
    /// rejection and every later step becomes a no-op, so <see cref="Build"/> reports that rejection.
    /// </summary>
    private sealed class PremiumBuilder
    {
        private decimal _premium = 1m;
        private PricingError? _error;

        public PremiumBuilder ForAge(int age) => age switch
        {
            < MinAge or > MaxAge => Reject(PricingError.AgeOutOfRange),
            _ => Multiply(AgeBands.First(band => age >= band.MinAge && age <= band.MaxAge).RatePerThousand),
        };

        public PremiumBuilder ForCover(decimal cover) => cover switch
        {
            < MinCover or > MaxCover => Reject(PricingError.CoverOutOfRange),
            _ => Multiply(cover / CoverStep),
        };

        public PremiumBuilder ForTerm(int years) => years switch
        {
            < MinTerm or > MaxTerm => Reject(PricingError.TermOutOfRange),
            _ => Multiply(TermFactors.First(band => years <= band.MaxTerm).Factor),
        };

        public PremiumBuilder ForProduct(string? code) =>
            ProductMultipliers.TryGetValue(code ?? string.Empty, out var multiplier)
                ? Multiply(multiplier)
                : Reject(PricingError.UnknownProduct);

        public PremiumBuilder ForSmoker(bool isSmoker) => Multiply(isSmoker ? SmokerLoading : 1.00m);

        public PricingOutcome Build() => _error switch
        {
            { } error => new PricingRejection(error),
            _ => new PremiumQuote(Math.Round(_premium, 2, MidpointRounding.AwayFromZero), Currency, PricingVersion),
        };

        private PremiumBuilder Multiply(decimal factor)
        {
            _premium *= factor;
            return this;
        }

        // Keeps the first rejection; later ones are ignored.
        private PremiumBuilder Reject(PricingError error)
        {
            _error ??= error;
            return this;
        }
    }
}
