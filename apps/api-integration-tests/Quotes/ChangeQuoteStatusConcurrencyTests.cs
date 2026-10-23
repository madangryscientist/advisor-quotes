using Api.Advisers.Services;
using Api.Cqrs;
using Api.Quotes.Domain;
using Api.Quotes.Commands;
using Api.Quotes.Handlers;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Quotes;

/// <summary>
/// The <c>xmin</c> concurrency token, deterministically: two contexts load the same committed quote, both change it,
/// and the second save finds the row already changed. Committed under a fresh id and deleted afterwards, because
/// inside one transaction <c>xmin</c> never moves, so <see cref="DatabaseTest"/> cannot show this.
/// </summary>
public sealed class ChangeQuoteStatusConcurrencyTests : IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    private Guid _quoteId;

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Adviser(string id) : ICurrentAdviser
    {
        public string Id => id;
    }

    public async Task InitializeAsync()
    {
        if (IntegrationDatabase.ConnectionString is null)
        {
            return;
        }

        await IntegrationDatabase.EnsureMigratedAsync();
        var quote = Quote.Create("ADV-001", new QuoteTerms("Thandi Nkosi", new DateOnly(1985, 6, 15), false, "LIFE", 1_000_000m, 20, 2090.00m, "ZAR", "2026.1", null), new Clock(T0));
        await using var db = IntegrationDatabase.NewContext();
        db.Quotes.Add(quote);
        await db.SaveChangesAsync();
        _quoteId = quote.Id;
    }

    public async Task DisposeAsync()
    {
        if (_quoteId == Guid.Empty)
        {
            return;
        }

        await using var db = IntegrationDatabase.NewContext();
        await db.Quotes.Where(q => q.Id == _quoteId).ExecuteDeleteAsync();
    }

    [FactRequiresDatabase]
    public async Task TheSecondOfTwoChangesToTheSameQuoteThrowsAndTheHandlerMapsItToAConflict()
    {
        await using var first = IntegrationDatabase.NewContext();
        await using var second = IntegrationDatabase.NewContext();
        await using var third = IntegrationDatabase.NewContext();
        var firstCopy = await first.Quotes.FirstOrDefaultAsync(q => q.Id == _quoteId);
        var secondCopy = await second.Quotes.FirstOrDefaultAsync(q => q.Id == _quoteId);
        // The handler's context returns the instance it already tracks, so it works from the stale copy too.
        await third.Quotes.FirstOrDefaultAsync(q => q.Id == _quoteId);

        firstCopy!.SendToClient("ADV-001", null, new Clock(T0.AddHours(1)));
        await first.SaveChangesAsync();

        secondCopy!.Decline("ADV-001", "changed my mind", new Clock(T0.AddHours(2)));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        var handler = new DeclineQuoteHandler(third, new Adviser("ADV-001"), new Clock(T0.AddHours(2)));
        var result = await handler.Handle(new DeclineQuoteCommand(_quoteId, "changed my mind"), CancellationToken.None);
        Assert.Equal(FailureKind.Conflict, result.Failure?.Kind);
    }
}
