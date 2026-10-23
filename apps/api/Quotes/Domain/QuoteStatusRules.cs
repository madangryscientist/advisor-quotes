namespace Api.Quotes.Domain;

/// <summary>
/// The only place that says which status moves are legal:
/// Draft → SentToClient → Accepted | Declined, and Draft → Declined. Accepted and Declined go nowhere.
/// </summary>
public static class QuoteStatusRules
{
    private static readonly Dictionary<QuoteStatus, QuoteStatus[]> Allowed =
        new()
        {
            [QuoteStatus.Draft] = [QuoteStatus.SentToClient, QuoteStatus.Declined],
            [QuoteStatus.SentToClient] = [QuoteStatus.Accepted, QuoteStatus.Declined],
            [QuoteStatus.Accepted] = [],
            [QuoteStatus.Declined] = [],
        };

    public static bool CanMove(QuoteStatus from, QuoteStatus to) => Allowed[from].Contains(to);
}
