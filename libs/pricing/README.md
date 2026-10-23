# Pricing

A pure premium calculator for the Advisor Quote Service. It takes a request and returns a monthly premium in ZAR or a typed rejection. It has no I/O, no clock and no ASP.NET or EF dependency.

```csharp
var outcome = PremiumCalculator.Calculate(new PricingRequest(
    DateOfBirth: new DateOnly(1985, 6, 15),
    AsOf: new DateOnly(2026, 10, 21),
    IsSmoker: false,
    ProductCode: "LIFE",
    CoverAmount: 1_000_000m,
    TermYears: 20));

// outcome is PremiumQuote { MonthlyPremium: 2090.00, Currency: "ZAR", PricingVersion: "2026.1" }
// or PricingRejection { Error: AgeOutOfRange | CoverOutOfRange | TermOutOfRange | UnknownProduct }
```

## Formula

```
premium = (cover ÷ 1,000) × ageBaseRate × smokerLoading × termFactor × productMultiplier
```

| Factor | Values |
|---|---|
| Age (completed years at `AsOf`, 18–65) → base rate per R1,000 | 18–29: 0.80 · 30–39: 1.10 · 40–49: 1.90 · 50–59: 3.20 · 60–65: 5.40 |
| Smoker loading | smoker ×1.50 · non-smoker ×1.00 |
| Term | 1–10 years ×1.00 · 11–20 years ×1.10 · 21–30 years ×1.25 |
| Product | `LIFE` ×1.00 · `DISABILITY` ×1.40 |

Cover is R10,000 to R5,000,000. Term is 1 to 30 years. Arithmetic is exact `decimal`; only the result is rounded, to 2 dp, half away from zero.

## Rejections

| `PricingError` | Cause |
|---|---|
| `AgeOutOfRange` | completed age outside 18–65 (also a date of birth after the as-of date) |
| `CoverOutOfRange` | cover outside R10,000–R5,000,000 |
| `TermOutOfRange` | term outside 1–30 years |
| `UnknownProduct` | product code not in the table |

## Versioning

`PricingVersion` is `2026.1`. Bump it whenever a rate, band, or factor changes, so a stored quote can be explained by the tables that produced it.

## Tests

`libs/pricing-tests` holds the xUnit tests: a worked example per product, age-band boundaries, the birthday edge, smoker loading, term steps, rounding, and each rejection. Run them with `bunx nx test Pricing.Tests`.
