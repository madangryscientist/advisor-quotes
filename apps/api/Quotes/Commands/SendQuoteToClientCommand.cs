namespace Api.Quotes.Commands;

public sealed record SendQuoteToClientCommand(Guid QuoteId, string? Reason);
