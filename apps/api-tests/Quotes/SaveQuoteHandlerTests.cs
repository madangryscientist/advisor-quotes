using Api.Advisers.Services;
using Api.Cqrs;
using Api.Quotes.Domain;
using Api.Quotes.Commands;
using Api.Persistence;
using Api.Quotes.Handlers;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Quotes;

public sealed class SaveQuoteHandlerTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Adviser(string id) : ICurrentAdviser
    {
        public string Id => id;
    }

    /// <summary>
    /// Another request inserted the same key between this one's lookup and its insert: the first lookup misses,
    /// and the save hits the unique index. <paramref name="insertWinner"/> writes the other request's quote just before that.
    /// </summary>
    private sealed class RacingDbContext(DbContextOptions<QuotesDbContext> options, Func<Task> insertWinner) : QuotesDbContext(options)
    {
        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            await insertWinner();
            ChangeTracker.Clear();
            throw new DuplicateIdempotencyKeyException();
        }
    }

    private readonly DbContextOptions<QuotesDbContext> _options = TestDb.Options();
    private readonly QuotesDbContext _db;
    private readonly Clock _clock = new(T0);

    public SaveQuoteHandlerTests() => _db = new QuotesDbContext(_options);

    public void Dispose() => _db.Dispose();

    // Age 41 on 2026-10-21: 1,000 × R1.90 × 1.10 (term 20) = R2,090.00.
    private static SaveQuoteCommand Command(Guid? quoteId = null, decimal cover = 1_000_000m, string? key = "key-1") =>
        new(quoteId, "Thandi Nkosi", new DateOnly(1985, 6, 15), false, "life", cover, 20, "first call") { IdempotencyKey = key };

    private Task<Result<SaveQuoteResult>> Save(SaveQuoteCommand command, string adviserId = "ADV-001", QuotesDbContext? db = null) =>
        new SaveQuoteHandler(db ?? _db, new Adviser(adviserId), _clock).Handle(command, CancellationToken.None);

    [Fact]
    public async Task ACreateIsPricedAndDraftWithOneHistoryEntry()
    {
        var saved = (await Save(Command())).Value!;

        Assert.Equal(2090.00m, saved.Premium);
        Assert.Equal("ZAR", saved.Currency);
        Assert.Equal("2026.1", saved.PricingVersion);
        Assert.Equal(QuoteStatus.Draft, saved.Status);
        Assert.Matches(@"^Q-20261021-[0-9A-F]{4}$", saved.ExternalReference);
        var quote = await _db.Quotes.FirstAsync(q => q.Id == saved.QuoteId);
        Assert.Equal("ADV-001", quote.AdviserId);
        Assert.Equal("LIFE", quote.ProductCode);
        Assert.Single(quote.History);
    }

    [Fact]
    public async Task ARetryWithTheProductCodeInAnotherCaseReplays()
    {
        // The stored code is upper-case, so "life" and "LIFE" are the same create and the key replays it.
        var created = (await Save(Command() with { ProductCode = "LIFE" })).Value!;

        var replay = (await Save(Command())).Value!;

        Assert.Equal(created.QuoteId, replay.QuoteId);
        Assert.True(replay.Replayed);
    }

    [Fact]
    public async Task AnUpdateRepricesAndWritesNoHistory()
    {
        var created = (await Save(Command())).Value!;
        _clock.Now = T0.AddDays(1);

        var updated = (await Save(Command(created.QuoteId, cover: 500_000m))).Value!;

        Assert.Equal(created.QuoteId, updated.QuoteId);
        Assert.Equal(created.ExternalReference, updated.ExternalReference);
        Assert.Equal(1045.00m, updated.Premium);
        var quote = await _db.Quotes.FirstAsync(q => q.Id == created.QuoteId);
        Assert.Equal(500_000m, quote.CoverAmount);
        Assert.Equal(T0.AddDays(1), quote.UpdatedAt);
        Assert.Single(quote.History);
    }

    [Fact]
    public async Task AnUpdatePricesAgeAtTheCreationDate()
    {
        // Turns 30 on 2026-11-01: priced on the 18–29 band at creation, and it stays there after the birthday.
        var command = Command() with { ClientDateOfBirth = new DateOnly(1996, 11, 1) };
        var created = (await Save(command)).Value!;
        _clock.Now = new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);

        var updated = (await Save(command with { QuoteId = created.QuoteId })).Value!;

        Assert.Equal(created.Premium, updated.Premium);
    }

    [Fact]
    public async Task UpdatingANonDraftIsAConflict()
    {
        var created = (await Save(Command())).Value!;
        var quote = await _db.Quotes.FirstAsync(q => q.Id == created.QuoteId);
        quote.ChangeStatus(QuoteStatus.SentToClient, "ADV-001", null, _clock);

        // Unpriceable too: the status is checked first.
        var result = await Save(Command(created.QuoteId, cover: 1_000m));

        Assert.Equal(FailureKind.Conflict, result.Failure?.Kind);
        Assert.Equal(1_000_000m, quote.CoverAmount);
    }

    [Fact]
    public async Task AnotherAdvisersQuoteIsNotFound()
    {
        var created = (await Save(Command())).Value!;

        var result = await Save(Command(created.QuoteId), adviserId: "ADV-002");

        Assert.Equal(FailureKind.NotFound, result.Failure?.Kind);
    }

    [Fact]
    public async Task AnUnknownQuoteIsNotFound() =>
        Assert.Equal(FailureKind.NotFound, (await Save(Command(Guid.CreateVersion7()))).Failure?.Kind);

    [Fact]
    public async Task APricingRejectionIsAFieldError()
    {
        var result = await Save(Command(cover: 1_000m));

        Assert.Equal(FailureKind.Validation, result.Failure?.Kind);
        Assert.Contains("coverAmount", result.Failure!.Errors!.Keys);
    }

    [Fact]
    public async Task ACreateWithoutAKeyIsABadRequest()
    {
        var result = await Save(Command(key: null));

        Assert.Equal(FailureKind.Validation, result.Failure?.Kind);
        Assert.Equal("Idempotency-Key header is required when creating a quote.", result.Failure!.Detail);
        Assert.Empty(_db.Quotes);
    }

    [Fact]
    public async Task AReplayReturnsTheSameQuoteInItsCurrentState()
    {
        var created = (await Save(Command())).Value!;
        var quote = await _db.Quotes.FirstAsync(q => q.Id == created.QuoteId);
        quote.ChangeStatus(QuoteStatus.SentToClient, "ADV-001", null, _clock);

        var replayed = (await Save(Command())).Value!;

        Assert.False(created.Replayed);
        Assert.True(replayed.Replayed);
        Assert.Equal(created.QuoteId, replayed.QuoteId);
        Assert.Equal(QuoteStatus.SentToClient, replayed.Status);
        Assert.Single(_db.Quotes);
    }

    [Fact]
    public async Task TheSameKeyWithADifferentCommandIsAMismatch()
    {
        await Save(Command());

        var result = await Save(Command(cover: 500_000m));

        Assert.Equal(FailureKind.IdempotencyMismatch, result.Failure?.Kind);
        Assert.Single(_db.Quotes);
    }

    [Fact]
    public async Task TheSameKeyFromAnotherAdviserIsItsOwnQuote()
    {
        var first = (await Save(Command())).Value!;

        var second = (await Save(Command(), adviserId: "ADV-002")).Value!;

        Assert.NotEqual(first.QuoteId, second.QuoteId);
        Assert.False(second.Replayed);
    }

    [Fact]
    public async Task AnUpdateIgnoresTheKey()
    {
        var created = (await Save(Command())).Value!;
        await Save(Command(key: "key-2"));

        // key-2 belongs to another quote with another command, and no key at all is fine too.
        var reusedKey = await Save(Command(created.QuoteId, cover: 500_000m, key: "key-2"));
        var noKey = await Save(Command(created.QuoteId, cover: 400_000m, key: null));

        Assert.Equal(created.QuoteId, reusedKey.Value?.QuoteId);
        Assert.False(reusedKey.Value!.Replayed);
        Assert.Equal(created.QuoteId, noKey.Value?.QuoteId);
        Assert.Equal(2, _db.Quotes.Count());
    }

    [Fact]
    public async Task LosingTheInsertRaceReturnsTheWinner()
    {
        SaveQuoteResult? winner = null;
        await using var racing = new RacingDbContext(_options, async () => winner = (await Save(Command())).Value);

        var loser = (await Save(Command(), db: racing)).Value!;

        Assert.Equal(winner!.QuoteId, loser.QuoteId);
        Assert.True(loser.Replayed);
        Assert.Single(_db.Quotes);
    }
}
