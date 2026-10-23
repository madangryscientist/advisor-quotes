using Api.Advisers.Services;
using Api.Quotes.Domain;
using Api.Quotes.Queries;
using Api.Quotes.Handlers;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Quotes;

/// <summary>
/// Runs the real SQL. The handler reads through its own pooled connections, so it cannot see the uncommitted rows of
/// <see cref="DatabaseTest"/>: this class commits quotes and deletes them afterwards. Other tests' rows may be present,
/// so assertions look only at the quotes made here, and the cap test owns its own adviser.
/// </summary>
public sealed class ListQuotesHandlerTests : IAsyncLifetime
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

    private async Task<Quote> SaveQuotes(string adviserId, DateTimeOffset at, bool sent = false, int count = 1)
    {
        await using var db = IntegrationDatabase.NewContext();
        Quote? last = null;
        for (var i = 0; i < count; i++)
        {
            var quote = Quote.Create(adviserId, new QuoteTerms("Thandi Nkosi", new DateOnly(1985, 6, 15), true, "LIFE", 1_000_000m, 20, 2090.00m, "ZAR", "2026.1", null), new Clock(at.AddSeconds(i)));
            if (sent)
            {
                quote.ChangeStatus(QuoteStatus.SentToClient, adviserId, null, new Clock(at.AddSeconds(i)));
            }

            db.Quotes.Add(quote);
            _created.Add(quote.Id);
            last = quote;
        }

        await db.SaveChangesAsync();
        return last!;
    }

    private static async Task<IReadOnlyList<QuoteSummary>> List(string adviserId, QuoteStatus? status = null)
    {
        await using var db = IntegrationDatabase.NewContext();
        var result = await new ListQuotesHandler(db, new Adviser(adviserId)).Handle(new ListQuotesQuery(status), CancellationToken.None);
        return Assert.IsAssignableFrom<IReadOnlyList<QuoteSummary>>(result.Value);
    }

    [FactRequiresDatabase]
    public async Task OnlyTheCurrentAdvisersQuotesComeBack()
    {
        var mine = await SaveQuotes("ADV-001", T0);
        var theirs = await SaveQuotes("ADV-002", T0);

        var rows = await List("ADV-001");

        Assert.Contains(rows, r => r.QuoteId == mine.Id);
        Assert.DoesNotContain(rows, r => r.QuoteId == theirs.Id);
    }

    [FactRequiresDatabase]
    public async Task TheStatusFilterNarrowsTheList()
    {
        var draft = await SaveQuotes("ADV-001", T0);
        var sent = await SaveQuotes("ADV-001", T0, sent: true);

        var drafts = await List("ADV-001", QuoteStatus.Draft);
        var sentRows = await List("ADV-001", QuoteStatus.SentToClient);

        Assert.Contains(drafts, r => r.QuoteId == draft.Id);
        Assert.DoesNotContain(drafts, r => r.QuoteId == sent.Id);
        Assert.All(drafts, r => Assert.Equal("Draft", r.Status));
        Assert.Contains(sentRows, r => r.QuoteId == sent.Id);
        Assert.DoesNotContain(sentRows, r => r.QuoteId == draft.Id);
    }

    [FactRequiresDatabase]
    public async Task NewestComesFirst()
    {
        var older = await SaveQuotes("ADV-001", T0.AddYears(10));
        var newer = await SaveQuotes("ADV-001", T0.AddYears(10).AddHours(1));

        var ids = (await List("ADV-001")).Select(r => r.QuoteId).ToList();

        Assert.True(ids.IndexOf(newer.Id) < ids.IndexOf(older.Id));
        Assert.True(ids.IndexOf(older.Id) >= 0);
    }

    [FactRequiresDatabase]
    public async Task TheListIsCappedAtOneHundred()
    {
        await SaveQuotes("ADV-003", T0, count: ListQuotesHandler.MaxRows + 5);

        var rows = await List("ADV-003");

        // The five oldest of the batch are the ones dropped.
        Assert.Equal(ListQuotesHandler.MaxRows, rows.Count);
        Assert.Equal(T0.AddSeconds(ListQuotesHandler.MaxRows + 4), rows[0].CreatedAt);
        Assert.Equal(T0.AddSeconds(5), rows[^1].CreatedAt);
    }
}
