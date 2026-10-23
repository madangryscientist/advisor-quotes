namespace Api.Quotes.Queries;

/// <summary>The query-side shape of a quote. Plain data read straight from SQL, with no link to the domain model.</summary>
public sealed record QuoteDetails
{
    public Guid QuoteId { get; init; }

    public string ExternalReference { get; init; } = null!;

    public string AdviserId { get; init; } = null!;

    public string ClientName { get; init; } = null!;

    public DateOnly ClientDateOfBirth { get; init; }

    public bool ClientIsSmoker { get; init; }

    public string ProductCode { get; init; } = null!;

    public decimal CoverAmount { get; init; }

    public int TermYears { get; init; }

    public decimal Premium { get; init; }

    public string Currency { get; init; } = null!;

    public string PricingVersion { get; init; } = null!;

    public string? Notes { get; init; }

    public string Status { get; init; } = null!;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Oldest first.</summary>
    public IReadOnlyList<QuoteStatusChangeDetails> History { get; init; } = [];
}
