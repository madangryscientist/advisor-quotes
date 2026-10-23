namespace Api.Quotes.Domain;

/// <summary>The result of <see cref="Quote.ChangeStatus"/>: <see cref="StatusChanged"/>, <see cref="IllegalTransition"/> or <see cref="ReasonRequired"/>.</summary>
public abstract record StatusChangeOutcome;

/// <summary>The move was legal and has been applied. <paramref name="Change"/> is the history entry it appended.</summary>
public sealed record StatusChanged(QuoteStatusChange Change) : StatusChangeOutcome;

/// <summary>The move is not allowed by <see cref="QuoteStatusRules"/>. The quote is untouched.</summary>
public sealed record IllegalTransition(QuoteStatus From, QuoteStatus To) : StatusChangeOutcome;

/// <summary>The move needs a reason and none was given (declining). The quote is untouched.</summary>
public sealed record ReasonRequired : StatusChangeOutcome;
