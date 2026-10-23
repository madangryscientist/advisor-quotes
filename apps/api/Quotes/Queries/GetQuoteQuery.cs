namespace Api.Quotes.Queries;

/// <summary>One quote with its status history. A required Guid, so a missing or malformed <c>quoteId</c> is a 400 before the handler runs.</summary>
public sealed record GetQuoteQuery(Guid QuoteId);
