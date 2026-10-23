namespace Api.Quotes.Domain;

/// <summary>Where a quote is in its life. <see cref="Accepted"/> and <see cref="Declined"/> are terminal.</summary>
public enum QuoteStatus
{
    Draft,
    SentToClient,
    Accepted,
    Declined,
}
