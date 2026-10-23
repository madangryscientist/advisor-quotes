using Api.Quotes.Domain;
namespace Api.Quotes.Commands;

/// <summary>The result of every status command: the quote and where it now stands.</summary>
public sealed record QuoteStatusResult(Guid QuoteId, QuoteStatus Status);
