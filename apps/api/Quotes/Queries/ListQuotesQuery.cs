using Api.Quotes.Domain;
namespace Api.Quotes.Queries;

/// <summary>
/// The current adviser's quotes, newest first. <paramref name="Status"/> narrows them to one status; an unknown name is a 400 at binding.
/// There is no adviser parameter: the adviser is the caller.
/// </summary>
public sealed record ListQuotesQuery(QuoteStatus? Status);
