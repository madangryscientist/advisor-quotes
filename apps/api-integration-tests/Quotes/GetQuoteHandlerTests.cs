using Api.Advisers.Services;
using Api.Cqrs;
using Api.Quotes.Domain;
using Api.Quotes.Queries;
using Api.Quotes.Handlers;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Quotes;

/// <summary>
/// Runs the real SQL. The handler reads through its own pooled connections, so it cannot see the uncommitted
/// rows of <see cref="DatabaseTest"/>: this class commits a quote under a fresh id and deletes it afterwards.
/// </summary>
public sealed class GetQuoteHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    private readonly List<Guid> _created = [];

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
        if (IntegrationDatabase.ConnectionString is not null)
        {
            await IntegrationDatabase.EnsureMigratedAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_created.Count == 0)
        {
            return;
        }

        await using var db = IntegrationDatabase.NewContext();
        await db.Quotes.Where(q => _created.Contains(q.Id)).ExecuteDeleteAsync();
    }

    private async Task<Quote> SaveQuote(DateTimeOffset at)
    {
        var quote = Quote.Create("ADV-001", new QuoteTerms("Thandi Nkosi", new DateOnly(1985, 6, 15), true, "LIFE", 1_000_000m, 20, 2090.00m, "ZAR", "2026.1", "first call"), new Clock(at));
        quote.ChangeStatus(QuoteStatus.SentToClient, "ADV-001", "sent by email", new Clock(at.AddHours(1)));
        quote.ChangeStatus(QuoteStatus.Accepted, "ADV-001", null, new Clock(at.AddHours(2)));
        await using var db = IntegrationDatabase.NewContext();
        db.Quotes.Add(quote);
        await db.SaveChangesAsync();
        _created.Add(quote.Id);
        return quote;
    }

    private static async Task<Result<QuoteDetails>> Get(Guid quoteId, string adviserId)
    {
        await using var db = IntegrationDatabase.NewContext();
        return await new GetQuoteHandler(db, new Adviser(adviserId)).Handle(new GetQuoteQuery(quoteId), CancellationToken.None);
    }

    [FactRequiresDatabase]
    public async Task ReturnsTheQuoteWithItsHistoryOldestFirst()
    {
        var saved = await SaveQuote(T0);

        var result = await Get(saved.Id, "ADV-001");

        var quote = Assert.IsType<QuoteDetails>(result.Value);
        Assert.Equal(saved.ExternalReference, quote.ExternalReference);
        Assert.Equal("Thandi Nkosi", quote.ClientName);
        Assert.Equal(new DateOnly(1985, 6, 15), quote.ClientDateOfBirth);
        Assert.True(quote.ClientIsSmoker);
        Assert.Equal(1_000_000m, quote.CoverAmount);
        Assert.Equal(2090.00m, quote.Premium);
        Assert.Equal("first call", quote.Notes);
        Assert.Equal("Accepted", quote.Status);
        Assert.Equal(T0, quote.CreatedAt);
        Assert.Equal(
            [new(null, "Draft", null, "ADV-001", T0), new("Draft", "SentToClient", "sent by email", "ADV-001", T0.AddHours(1)), new("SentToClient", "Accepted", null, "ADV-001", T0.AddHours(2))],
            quote.History);
    }

    [FactRequiresDatabase]
    public async Task TheHistoryIsOldestFirstWhateverOrderPostgresStoresItIn()
    {
        var saved = await SaveQuote(T0);
        // A no-op update writes a new version of the creation row after the others, so a read with no ORDER BY
        // returns it last. Rows written by separate requests end up out of order the same way.
        await using (var db = IntegrationDatabase.NewContext())
        {
            await db.Database.ExecuteSqlAsync(
                $"UPDATE quote_status_changes SET changed_by = changed_by WHERE quote_id = {saved.Id} AND from_status IS NULL");
        }

        var result = await Get(saved.Id, "ADV-001");

        Assert.Equal(["Draft", "SentToClient", "Accepted"], result.Value!.History.Select(h => h.To));
    }

    [FactRequiresDatabase]
    public async Task AnUnknownQuoteIsNotFound()
    {
        var result = await Get(Guid.NewGuid(), "ADV-001");

        Assert.Equal(FailureKind.NotFound, result.Failure?.Kind);
    }

    [FactRequiresDatabase]
    public async Task AnotherAdvisersQuoteIsNotFound()
    {
        var saved = await SaveQuote(T0);

        var result = await Get(saved.Id, "ADV-002");

        Assert.Equal(FailureKind.NotFound, result.Failure?.Kind);
    }
}
