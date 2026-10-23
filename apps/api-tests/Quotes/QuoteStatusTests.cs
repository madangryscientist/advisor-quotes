using Api.Quotes.Domain;

namespace Api.Tests.Quotes;

public class QuoteStatusTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    private sealed class StepClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static Quote NewQuote(TimeProvider time) =>
        Quote.Create("adv-7", new QuoteTerms("Thandi Nkosi", new DateOnly(1985, 6, 15), false, "LIFE", 1_000_000m, 20, 2090.00m, "ZAR", "2026.1", "first call"), time);

    private static Quote QuoteIn(QuoteStatus status, StepClock clock)
    {
        var quote = NewQuote(clock);
        switch (status)
        {
            case QuoteStatus.SentToClient:
            case QuoteStatus.Accepted:
                Move(quote, QuoteStatus.SentToClient, clock);
                break;
            case QuoteStatus.Declined:
                Move(quote, QuoteStatus.Declined, clock);
                break;
        }

        if (status == QuoteStatus.Accepted)
        {
            Move(quote, QuoteStatus.Accepted, clock);
        }

        Assert.Equal(status, quote.Status);
        return quote;
    }

    private static void Move(Quote quote, QuoteStatus to, StepClock clock)
    {
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.IsType<StatusChanged>(quote.ChangeStatus(to, "setup", null, clock));
    }

    // Spelled out independently of QuoteStatusRules so the tests do not just restate the implementation.
    private static readonly (QuoteStatus From, QuoteStatus To)[] Legal =
    [
        (QuoteStatus.Draft, QuoteStatus.SentToClient),
        (QuoteStatus.Draft, QuoteStatus.Declined),
        (QuoteStatus.SentToClient, QuoteStatus.Accepted),
        (QuoteStatus.SentToClient, QuoteStatus.Declined),
    ];

    public static TheoryData<QuoteStatus, QuoteStatus> LegalMoves => Pairs(legal: true);

    public static TheoryData<QuoteStatus, QuoteStatus> IllegalMoves => Pairs(legal: false);

    private static TheoryData<QuoteStatus, QuoteStatus> Pairs(bool legal)
    {
        var data = new TheoryData<QuoteStatus, QuoteStatus>();
        foreach (var from in Enum.GetValues<QuoteStatus>())
        {
            foreach (var to in Enum.GetValues<QuoteStatus>())
            {
                if (Legal.Contains((from, to)) == legal)
                {
                    data.Add(from, to);
                }
            }
        }

        return data;
    }

    [Fact]
    public void CreateStartsInDraftWithOneHistoryEntry()
    {
        var clock = new StepClock(T0);

        var quote = NewQuote(clock);

        Assert.Equal(QuoteStatus.Draft, quote.Status);
        Assert.Equal(T0, quote.CreatedAt);
        Assert.Equal(T0, quote.UpdatedAt);
        var entry = Assert.Single(quote.History);
        Assert.Equal(new QuoteStatusChange(null, QuoteStatus.Draft, null, "adv-7", T0), entry);
    }

    [Theory]
    [MemberData(nameof(LegalMoves))]
    public void LegalMoveAppliesAndAppendsHistory(QuoteStatus from, QuoteStatus to)
    {
        var clock = new StepClock(T0);
        var quote = QuoteIn(from, clock);
        var before = quote.History.Count;
        clock.Advance(TimeSpan.FromHours(1));
        var at = clock.GetUtcNow();

        var result = quote.ChangeStatus(to, "adv-9", "client asked", clock);

        var expected = new QuoteStatusChange(from, to, "client asked", "adv-9", at);
        Assert.Equal(new StatusChanged(expected), result);
        Assert.Equal(to, quote.Status);
        Assert.Equal(at, quote.UpdatedAt);
        Assert.Equal(before + 1, quote.History.Count);
        Assert.Equal(expected, quote.History[^1]);
    }

    [Theory]
    [MemberData(nameof(IllegalMoves))]
    public void IllegalMoveFailsAndLeavesQuoteUntouched(QuoteStatus from, QuoteStatus to)
    {
        var clock = new StepClock(T0);
        var quote = QuoteIn(from, clock);
        var history = quote.History.ToList();
        var updatedAt = quote.UpdatedAt;
        clock.Advance(TimeSpan.FromHours(1));

        var result = quote.ChangeStatus(to, "adv-9", "nope", clock);

        Assert.Equal(new IllegalTransition(from, to), result);
        Assert.Equal(from, quote.Status);
        Assert.Equal(updatedAt, quote.UpdatedAt);
        Assert.Equal(history, quote.History);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void DeclineWithoutAReasonIsRefusedAndLeavesQuoteUntouched(string? reason)
    {
        var clock = new StepClock(T0);
        var quote = NewQuote(clock);

        var result = quote.Decline("adv-9", reason, clock);

        Assert.Equal(new ReasonRequired(), result);
        Assert.Equal(QuoteStatus.Draft, quote.Status);
        Assert.Single(quote.History);
    }
}
