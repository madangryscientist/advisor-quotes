using Api.Persistence;
using Api.Quotes.Domain;
using Api.Quotes.Services;
using Pricing;

namespace Api.Tests.Quotes;

public sealed class DemoDataSeederTests : IDisposable
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private readonly QuotesDbContext _db = new(TestDb.Options());

    public void Dispose() => _db.Dispose();

    private static readonly Clock Time = new(new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task SeedsEveryStatusAcrossTheThreeAdvisersThroughTheDomainAndPricing()
    {
        await new DemoDataSeeder(_db, Time).SeedAsync(CancellationToken.None);

        Assert.Equal(8, _db.Quotes.Count());
        Assert.Equal(["ADV-001", "ADV-002", "ADV-003"], _db.Quotes.Select(q => q.AdviserId).Distinct().Order());
        Assert.Equal(
            [QuoteStatus.Draft, QuoteStatus.SentToClient, QuoteStatus.Accepted, QuoteStatus.Declined],
            _db.Quotes.Select(q => q.Status).Distinct().Order());
        var declines = _db.Quotes.AsEnumerable().Where(q => q.Status == QuoteStatus.Declined).Select(q => q.History[^1]).ToList();
        Assert.Contains(declines, h => h.From == QuoteStatus.Draft);
        Assert.Contains(declines, h => h.From == QuoteStatus.SentToClient);
        Assert.All(_db.Quotes, q =>
        {
            Assert.Equal(PremiumCalculator.PricingVersion, q.PricingVersion);
            Assert.True(q.Premium > 0);
            Assert.Equal(q.Status, q.History[^1].To);
        });
    }

    [Fact]
    public async Task WritesNothingWhenQuotesAlreadyExist()
    {
        var seeder = new DemoDataSeeder(_db, Time);
        await seeder.SeedAsync(CancellationToken.None);
        var before = _db.Quotes.Select(q => q.Id).ToHashSet();

        await seeder.SeedAsync(CancellationToken.None);

        Assert.True(before.SetEquals(_db.Quotes.Select(q => q.Id)));
    }
}
