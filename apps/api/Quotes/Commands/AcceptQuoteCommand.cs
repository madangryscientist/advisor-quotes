namespace Api.Quotes.Commands;

public sealed record AcceptQuoteCommand(Guid QuoteId, string? Reason);
