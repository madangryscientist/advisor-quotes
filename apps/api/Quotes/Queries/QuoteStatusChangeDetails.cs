namespace Api.Quotes.Queries;

public sealed record QuoteStatusChangeDetails(string? From, string To, string? Reason, string ChangedBy, DateTimeOffset ChangedAt);
