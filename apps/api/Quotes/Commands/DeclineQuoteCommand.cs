namespace Api.Quotes.Commands;

public sealed record DeclineQuoteCommand(Guid QuoteId, string? Reason);
