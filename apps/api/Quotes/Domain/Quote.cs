namespace Api.Quotes.Domain;

/// <summary>
/// An advisor's quote for a client. Status only moves through <see cref="ChangeStatus"/>, which enforces
/// <see cref="QuoteStatusRules"/> and keeps <see cref="History"/> complete. The client, product and price
/// only change through <see cref="TryRevise"/>, and only while the quote is a Draft.
/// </summary>
public sealed class Quote
{
    private readonly List<QuoteStatusChange> _history = [];

    private Quote()
    {
    }

    public Guid Id { get; private init; }

    /// <summary>
    /// The human-readable reference generated when the quote is created, <c>Q-yyyyMMdd-XXXX</c>
    /// (creation date, then the last four hex digits of the random part of <see cref="Id"/>).
    /// </summary>
    public required string ExternalReference { get; init; }

    public required string AdviserId { get; init; }

    public string ClientName { get; private set; } = null!;

    public DateOnly ClientDateOfBirth { get; private set; }

    public bool ClientIsSmoker { get; private set; }

    public string ProductCode { get; private set; } = null!;

    public decimal CoverAmount { get; private set; }

    public int TermYears { get; private set; }

    public decimal Premium { get; private set; }

    public string Currency { get; private set; } = null!;

    /// <summary>The version of the rate tables that priced <see cref="Premium"/>, so the number stays explainable later.</summary>
    public string PricingVersion { get; private set; } = null!;

    public string? Notes { get; private set; }

    public QuoteStatus Status { get; private set; }

    /// <summary>The <c>Idempotency-Key</c> the quote was created with, unique per adviser. Null for quotes created without one.</summary>
    public string? IdempotencyKey { get; private init; }

    /// <summary>SHA-256 of the create command that carried <see cref="IdempotencyKey"/>, so a reused key with a different command is caught.</summary>
    public string? RequestHash { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<QuoteStatusChange> History => _history;

    /// <summary>
    /// Creates a quote in <see cref="QuoteStatus.Draft"/> and records that as the first history entry.
    /// <paramref name="terms"/> is what the quote is for; <paramref name="idempotencyKey"/> and <paramref name="requestHash"/> identify the create request, for replay.
    /// </summary>
    public static Quote Create(string adviserId, QuoteTerms terms, TimeProvider time, string? idempotencyKey = null, string? requestHash = null)
    {
        var now = time.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        var quote = new Quote
        {
            Id = id,
            ExternalReference = $"Q-{now:yyyyMMdd}-{id.ToString("N")[^4..].ToUpperInvariant()}",
            AdviserId = adviserId,
            ClientName = terms.ClientName,
            ClientDateOfBirth = terms.ClientDateOfBirth,
            ClientIsSmoker = terms.ClientIsSmoker,
            ProductCode = terms.ProductCode,
            CoverAmount = terms.CoverAmount,
            TermYears = terms.TermYears,
            Premium = terms.Premium,
            Currency = terms.Currency,
            PricingVersion = terms.PricingVersion,
            Notes = terms.Notes,
            Status = QuoteStatus.Draft,
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            CreatedAt = now,
            UpdatedAt = now,
        };
        quote._history.Add(new QuoteStatusChange(null, QuoteStatus.Draft, null, adviserId, now));
        return quote;
    }

    /// <summary>
    /// Replaces the client, product, notes and price of a Draft. Writes no history (history is status changes only).
    /// Returns false, leaving the quote untouched, when it is not a Draft.
    /// </summary>
    public bool TryRevise(QuoteTerms terms, TimeProvider time)
    {
        if (Status != QuoteStatus.Draft)
        {
            return false;
        }

        ClientName = terms.ClientName;
        ClientDateOfBirth = terms.ClientDateOfBirth;
        ClientIsSmoker = terms.ClientIsSmoker;
        ProductCode = terms.ProductCode;
        CoverAmount = terms.CoverAmount;
        TermYears = terms.TermYears;
        Premium = terms.Premium;
        Currency = terms.Currency;
        PricingVersion = terms.PricingVersion;
        Notes = terms.Notes;
        UpdatedAt = time.GetUtcNow();
        return true;
    }

    /// <summary>Draft → SentToClient.</summary>
    public StatusChangeOutcome SendToClient(string changedBy, string? reason, TimeProvider time) =>
        ChangeStatus(QuoteStatus.SentToClient, changedBy, reason, time);

    /// <summary>SentToClient → Accepted.</summary>
    public StatusChangeOutcome Accept(string changedBy, string? reason, TimeProvider time) =>
        ChangeStatus(QuoteStatus.Accepted, changedBy, reason, time);

    /// <summary>Draft or SentToClient → Declined. A decline must say why.</summary>
    public StatusChangeOutcome Decline(string changedBy, string? reason, TimeProvider time) =>
        string.IsNullOrWhiteSpace(reason) ? new ReasonRequired() : ChangeStatus(QuoteStatus.Declined, changedBy, reason, time);

    /// <summary>Moves the quote to <paramref name="to"/> if the rules allow it, appending a history entry.</summary>
    public StatusChangeOutcome ChangeStatus(QuoteStatus to, string changedBy, string? reason, TimeProvider time)
    {
        if (!QuoteStatusRules.CanMove(Status, to))
        {
            return new IllegalTransition(Status, to);
        }

        var now = time.GetUtcNow();
        var change = new QuoteStatusChange(Status, to, reason, changedBy, now);
        _history.Add(change);
        Status = to;
        UpdatedAt = now;
        return new StatusChanged(change);
    }
}
