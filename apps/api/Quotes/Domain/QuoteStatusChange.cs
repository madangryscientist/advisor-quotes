namespace Api.Quotes.Domain;

/// <summary>One entry in a quote's status history.</summary>
/// <param name="From">The status before the change. Null for the entry written when the quote was created.</param>
/// <param name="To">The status after the change.</param>
/// <param name="Reason">Why, when the caller said.</param>
/// <param name="ChangedBy">Who made the change (the adviser id; there is no auth in scope).</param>
/// <param name="ChangedAt">When, from the injected <see cref="TimeProvider"/>.</param>
public sealed record QuoteStatusChange(
    QuoteStatus? From,
    QuoteStatus To,
    string? Reason,
    string ChangedBy,
    DateTimeOffset ChangedAt);
