namespace Api.Quotes.Queries;

/// <summary>One row of the list. No history; that is <see cref="QuoteDetails"/>.</summary>
public sealed record QuoteSummary
{
    public Guid QuoteId { get; init; }

    public string ExternalReference { get; init; } = null!;

    public string ClientName { get; init; } = null!;

    public string ProductCode { get; init; } = null!;

    public decimal CoverAmount { get; init; }

    public int TermYears { get; init; }

    public decimal Premium { get; init; }

    public string Currency { get; init; } = null!;

    public string Status { get; init; } = null!;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
