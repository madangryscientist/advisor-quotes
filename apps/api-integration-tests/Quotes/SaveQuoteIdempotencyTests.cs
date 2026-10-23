using Api.Advisers.Services;
using Api.Quotes.Commands;
using Api.Quotes.Handlers;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Quotes;

/// <summary>
/// Two creates with the same <c>Idempotency-Key</c> at once, each on its own connection, leave one row: whichever
/// loses the insert hits the unique index and returns the winner. Committed under a fresh key and deleted afterwards,
/// because <see cref="DatabaseTest"/>'s one shared transaction cannot race.
/// </summary>
public sealed class SaveQuoteIdempotencyTests : IAsyncLifetime
{
    private readonly string _key = $"it-{Guid.NewGuid()}";

    private sealed class Adviser(string id) : ICurrentAdviser
    {
        public string Id => id;
    }

    public Task InitializeAsync() =>
        IntegrationDatabase.ConnectionString is null ? Task.CompletedTask : IntegrationDatabase.EnsureMigratedAsync();

    public async Task DisposeAsync()
    {
        if (IntegrationDatabase.ConnectionString is null)
        {
            return;
        }

        await using var db = IntegrationDatabase.NewContext();
        await db.Quotes.Where(q => q.IdempotencyKey == _key).ExecuteDeleteAsync();
    }

    [FactRequiresDatabase]
    public async Task TwoParallelCreatesWithTheSameKeyMakeOneRow()
    {
        var command = new SaveQuoteCommand(null, "Thandi Nkosi", new DateOnly(1985, 6, 15), false, "LIFE", 1_000_000m, 20, null) { IdempotencyKey = _key };

        async Task<SaveQuoteResult> Create()
        {
            await using var db = IntegrationDatabase.NewContext();
            var result = await new SaveQuoteHandler(db, new Adviser("ADV-001"), TimeProvider.System)
                .Handle(command, CancellationToken.None);
            return result.Value ?? throw new InvalidOperationException(result.Failure?.Detail);
        }

        var results = await Task.WhenAll(Create(), Create());

        Assert.Equal(results[0].QuoteId, results[1].QuoteId);
        await using var read = IntegrationDatabase.NewContext();
        Assert.Equal(1, await read.Quotes.CountAsync(q => q.AdviserId == "ADV-001" && q.IdempotencyKey == _key));
    }
}
