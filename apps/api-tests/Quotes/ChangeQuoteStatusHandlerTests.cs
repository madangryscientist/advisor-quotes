using Api.Advisers.Services;
using Api.Cqrs;
using Api.Quotes.Domain;
using Api.Quotes.Commands;
using Api.Persistence;
using Api.Quotes.Handlers;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Quotes;

public sealed class ChangeQuoteStatusHandlerTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Adviser(string id) : ICurrentAdviser
    {
        public string Id => id;
    }

    private sealed class ConflictingDbContext(DbContextOptions<QuotesDbContext> options) : QuotesDbContext(options)
    {
        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
            throw new DbUpdateConcurrencyException();
    }

    private readonly DbContextOptions<QuotesDbContext> _options = TestDb.Options();
    private readonly QuotesDbContext _db;
    private readonly Clock _clock = new(T0.AddHours(1));

    public ChangeQuoteStatusHandlerTests() => _db = new QuotesDbContext(_options);

    public void Dispose() => _db.Dispose();

    private Quote AddQuote(QuoteStatus status = QuoteStatus.Draft, string adviserId = "ADV-001")
    {
        var quote = Quote.Create(adviserId, new QuoteTerms("Thandi Nkosi", new DateOnly(1985, 6, 15), false, "LIFE", 1_000_000m, 20, 2090.00m, "ZAR", "2026.1", null), new Clock(T0));
        if (status is QuoteStatus.SentToClient or QuoteStatus.Accepted)
        {
            quote.SendToClient(adviserId, null, new Clock(T0));
        }

        if (status == QuoteStatus.Accepted)
        {
            quote.Accept(adviserId, null, new Clock(T0));
        }

        if (status == QuoteStatus.Declined)
        {
            quote.Decline(adviserId, "no", new Clock(T0));
        }

        _db.Quotes.Add(quote);
        _db.SaveChanges();
        return quote;
    }

    private Task<Result<QuoteStatusResult>> Send(Guid id, string adviserId = "ADV-001", QuotesDbContext? db = null) =>
        new SendQuoteToClientHandler(db ?? _db, new Adviser(adviserId), _clock).Handle(new SendQuoteToClientCommand(id, "emailed"), CancellationToken.None);

    private Task<Result<QuoteStatusResult>> Accept(Guid id, string adviserId = "ADV-001") =>
        new AcceptQuoteHandler(_db, new Adviser(adviserId), _clock).Handle(new AcceptQuoteCommand(id, null), CancellationToken.None);

    private Task<Result<QuoteStatusResult>> Decline(Guid id, string? reason = "client went elsewhere", string adviserId = "ADV-001") =>
        new DeclineQuoteHandler(_db, new Adviser(adviserId), _clock).Handle(new DeclineQuoteCommand(id, reason), CancellationToken.None);

    [Fact]
    public async Task SendMovesADraftAndRecordsWhoAndWhy()
    {
        var quote = AddQuote();

        var result = await Send(quote.Id, "ADV-001");

        Assert.Equal(new QuoteStatusResult(quote.Id, QuoteStatus.SentToClient), result.Value);
        Assert.Equal(new QuoteStatusChange(QuoteStatus.Draft, QuoteStatus.SentToClient, "emailed", "ADV-001", _clock.GetUtcNow()), quote.History[^1]);
    }

    [Fact]
    public async Task AcceptMovesASentQuoteAndRecordsWho()
    {
        var quote = AddQuote(QuoteStatus.SentToClient);

        var result = await Accept(quote.Id);

        Assert.Equal(new QuoteStatusResult(quote.Id, QuoteStatus.Accepted), result.Value);
        Assert.Equal(new QuoteStatusChange(QuoteStatus.SentToClient, QuoteStatus.Accepted, null, "ADV-001", _clock.GetUtcNow()), quote.History[^1]);
    }

    [Theory]
    [InlineData(QuoteStatus.Draft)]
    [InlineData(QuoteStatus.SentToClient)]
    public async Task DeclineMovesADraftOrASentQuoteAndRecordsWhoAndWhy(QuoteStatus from)
    {
        var quote = AddQuote(from);

        var result = await Decline(quote.Id);

        Assert.Equal(new QuoteStatusResult(quote.Id, QuoteStatus.Declined), result.Value);
        Assert.Equal(new QuoteStatusChange(from, QuoteStatus.Declined, "client went elsewhere", "ADV-001", _clock.GetUtcNow()), quote.History[^1]);
    }

    [Fact]
    public async Task DeclineWithoutAReasonIsAFieldErrorAndChangesNothing()
    {
        var quote = AddQuote();

        foreach (var reason in new string?[] { null, "", "   " })
        {
            var result = await Decline(quote.Id, reason);

            Assert.Equal(FailureKind.Validation, result.Failure?.Kind);
            Assert.Contains("reason", result.Failure!.Errors!.Keys);
        }

        Assert.Equal(QuoteStatus.Draft, quote.Status);
        Assert.Single(quote.History);
    }

    [Fact]
    public async Task IllegalMovesAreConflicts()
    {
        var draft = AddQuote();
        var accepted = AddQuote(QuoteStatus.Accepted);
        var declined = AddQuote(QuoteStatus.Declined);

        Assert.Equal(FailureKind.Conflict, (await Accept(draft.Id)).Failure?.Kind);
        Assert.Equal(FailureKind.Conflict, (await Send(accepted.Id)).Failure?.Kind);
        Assert.Equal(FailureKind.Conflict, (await Decline(accepted.Id)).Failure?.Kind);
        Assert.Equal(FailureKind.Conflict, (await Send(declined.Id)).Failure?.Kind);
        Assert.Equal(QuoteStatus.Draft, draft.Status);
        Assert.Equal(QuoteStatus.Accepted, accepted.Status);
    }

    [Fact]
    public async Task AnUnknownQuoteIsNotFound()
    {
        var unknown = Guid.CreateVersion7();

        Assert.Equal(FailureKind.NotFound, (await Send(unknown)).Failure?.Kind);
        Assert.Equal(FailureKind.NotFound, (await Accept(unknown)).Failure?.Kind);
        Assert.Equal(FailureKind.NotFound, (await Decline(unknown)).Failure?.Kind);
    }

    [Fact]
    public async Task AnotherAdvisersQuoteIsNotFoundAndUntouched()
    {
        var quote = AddQuote(adviserId: "ADV-002");

        Assert.Equal(FailureKind.NotFound, (await Send(quote.Id, "ADV-001")).Failure?.Kind);
        Assert.Equal(FailureKind.NotFound, (await Accept(quote.Id, "ADV-001")).Failure?.Kind);
        Assert.Equal(FailureKind.NotFound, (await Decline(quote.Id, adviserId: "ADV-001")).Failure?.Kind);
        Assert.Equal(QuoteStatus.Draft, quote.Status);
        Assert.Single(quote.History);
    }

    [Fact]
    public async Task AConcurrentChangeIsAConflict()
    {
        var quote = AddQuote();
        await using var conflicting = new ConflictingDbContext(_options);

        var result = await Send(quote.Id, db: conflicting);

        Assert.Equal(FailureKind.Conflict, result.Failure?.Kind);
    }
}
