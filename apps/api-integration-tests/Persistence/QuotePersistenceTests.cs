using Api.Quotes.Domain;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Persistence;

public class QuotePersistenceTests : DatabaseTest
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [FactRequiresDatabase]
    public async Task ASavedQuoteIsReadBackWithItsHistory()
    {
        var quote = Quote.Create("ADV-001", new QuoteTerms("Thandi Nkosi", new DateOnly(1985, 6, 15), false, "LIFE", 1_000_000m, 20, 2090.00m, "ZAR", "2026.1", "first call"), new Clock(T0));
        quote.ChangeStatus(QuoteStatus.SentToClient, "ADV-001", "sent by email", new Clock(T0.AddHours(1)));

        await using (var write = NewContext())
        {
            write.Quotes.Add(quote);
            await write.SaveChangesAsync();
        }

        await using var read = NewContext();
        var loaded = await read.Quotes.FirstOrDefaultAsync(q => q.Id == quote.Id);

        Assert.NotNull(loaded);
        Assert.Equal(quote.ExternalReference, loaded.ExternalReference);
        Assert.Equal("ADV-001", loaded.AdviserId);
        Assert.Equal("Thandi Nkosi", loaded.ClientName);
        Assert.Equal(2090.00m, loaded.Premium);
        Assert.Equal(QuoteStatus.SentToClient, loaded.Status);
        Assert.Equal(quote.History, loaded.History);
    }
}
