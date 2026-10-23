namespace Api.Quotes.Domain;

/// <summary>What a quote is for: the client, the product and the price. Used to create a quote and to revise a Draft.</summary>
public sealed record QuoteTerms(
    string ClientName,
    DateOnly ClientDateOfBirth,
    bool ClientIsSmoker,
    string ProductCode,
    decimal CoverAmount,
    int TermYears,
    decimal Premium,
    string Currency,
    string PricingVersion,
    string? Notes);
