using Api.Persistence;
using Api.Quotes.Domain;
using Microsoft.EntityFrameworkCore;
using Pricing;

namespace Api.Quotes.Services;

/// <summary>
/// Writes a handful of demo quotes when the quotes table is empty, so someone who only calls the API has something to list.
/// Dev only (<c>Database__SeedDemoData</c>). The quotes go through <see cref="Quote"/> and <see cref="PremiumCalculator"/>,
/// so premiums and histories are real; the <c>SaveQuote</c> handler and its Idempotency-Key are not involved.
/// </summary>
public sealed class DemoDataSeeder(QuotesDbContext db, TimeProvider time)
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>One demo quote and the status moves applied after it is created as a Draft. A move to Declined carries <paramref name="DeclineReason"/>.</summary>
    private sealed record Demo(
        string AdviserId, string Client, DateOnly DateOfBirth, bool Smoker, string Product, decimal Cover, int Term, string? Notes,
        QuoteStatus[] Moves, string? DeclineReason = null);

    private static readonly QuoteStatus[] Stay = [];
    private static readonly QuoteStatus[] Sent = [QuoteStatus.SentToClient];
    private static readonly QuoteStatus[] SentThenAccepted = [QuoteStatus.SentToClient, QuoteStatus.Accepted];
    private static readonly QuoteStatus[] SentThenDeclined = [QuoteStatus.SentToClient, QuoteStatus.Declined];
    private static readonly QuoteStatus[] DeclinedAsDraft = [QuoteStatus.Declined];

    private static readonly Demo[] Quotes =
    [
        new("ADV-001", "Naledi Dlamini", new(1992, 3, 14), false, "LIFE", 750_000m, 20, "Home loan cover.", Stay),
        new("ADV-001", "Johan Pretorius", new(1978, 9, 2), true, "LIFE", 1_500_000m, 15, null, Sent),
        new("ADV-001", "Fatima Patel", new(1985, 11, 30), false, "DISABILITY", 600_000m, 25, "Self-employed; income protection.", SentThenAccepted),
        new("ADV-002", "Sipho Khumalo", new(1999, 6, 21), false, "LIFE", 250_000m, 10, null, Stay),
        new("ADV-002", "Anika van Wyk", new(1970, 1, 8), true, "DISABILITY", 2_000_000m, 12, null, DeclinedAsDraft, "Client withdrew before the quote was sent."),
        new("ADV-002", "Thabo Mokoena", new(1988, 4, 17), false, "LIFE", 1_000_000m, 30, null, SentThenAccepted),
        new("ADV-003", "Lerato Nkosi", new(1995, 12, 5), false, "DISABILITY", 400_000m, 18, null, Sent),
        new("ADV-003", "Pieter Botha", new(1963, 7, 25), true, "LIFE", 500_000m, 5, null, SentThenDeclined, "Client found a cheaper policy."),
    ];

    /// <summary>Writes the demo quotes, or nothing when the table already has any.</summary>
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await db.Quotes.AnyAsync(cancellationToken))
        {
            return;
        }

        // Spread the quotes over the last few weeks, oldest first, so lists and histories do not all read "just now".
        var now = time.GetUtcNow();
        var clock = new Clock();
        for (var i = 0; i < Quotes.Length; i++)
        {
            clock.Now = now.AddDays(-(Quotes.Length - i) * 3);
            db.Quotes.Add(Build(Quotes[i], clock));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static Quote Build(Demo demo, Clock clock)
    {
        var pricing = PremiumCalculator.Calculate(new PricingRequest(
            demo.DateOfBirth, DateOnly.FromDateTime(clock.Now.UtcDateTime), demo.Smoker, demo.Product, demo.Cover, demo.Term));
        if (pricing is not PremiumQuote price)
        {
            throw new InvalidOperationException($"Demo quote for {demo.Client} cannot be priced: {((PricingRejection)pricing).Error}.");
        }

        var quote = Quote.Create(
            demo.AdviserId,
            new QuoteTerms(
                ClientName: demo.Client,
                ClientDateOfBirth: demo.DateOfBirth,
                ClientIsSmoker: demo.Smoker,
                ProductCode: demo.Product,
                CoverAmount: demo.Cover,
                TermYears: demo.Term,
                Premium: price.MonthlyPremium,
                Currency: price.Currency,
                PricingVersion: price.PricingVersion,
                Notes: demo.Notes),
            clock);

        foreach (var to in demo.Moves)
        {
            clock.Now = clock.Now.AddDays(1);
            var result = to switch
            {
                QuoteStatus.SentToClient => quote.SendToClient(demo.AdviserId, null, clock),
                QuoteStatus.Accepted => quote.Accept(demo.AdviserId, null, clock),
                QuoteStatus.Declined => quote.Decline(demo.AdviserId, demo.DeclineReason, clock),
                _ => throw new InvalidOperationException($"Demo quote for {demo.Client} cannot move to {to}."),
            };
            if (result is not StatusChanged)
            {
                throw new InvalidOperationException($"Demo quote for {demo.Client} cannot move to {to}: {result}.");
            }
        }

        return quote;
    }
}
