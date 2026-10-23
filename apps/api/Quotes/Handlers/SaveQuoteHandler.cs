using Api.Advisers.Services;
using Api.Cqrs;
using Api.Persistence;
using Api.Quotes.Domain;
using Api.Quotes.Commands;
using Api.Telemetry;
using Microsoft.EntityFrameworkCore;
using Pricing;

namespace Api.Quotes.Handlers;

public sealed class SaveQuoteHandler(QuotesDbContext db, ICurrentAdviser adviser, TimeProvider time) : ICommandHandler<SaveQuoteCommand, SaveQuoteResult>
{
    public async Task<Result<SaveQuoteResult>> Handle(SaveQuoteCommand command, CancellationToken cancellationToken)
    {
        if (Invalid(command) is { } invalid)
        {
            return invalid;
        }

        // An update by id is idempotent already, so only a create looks at the key.
        var requestHash = command.QuoteId is null ? command.RequestHash() : null;
        if (requestHash is not null && await ReplayAsync(command.IdempotencyKey!, requestHash, cancellationToken) is { } replay)
        {
            return replay;
        }

        var existing = command.QuoteId is { } id ? await db.Quotes.FirstOrDefaultAsync(q => q.Id == id, cancellationToken) : null;
        if (NotEditable(command.QuoteId, existing) is { } notEditable)
        {
            return notEditable;
        }

        // Age is taken at the quote's creation date, so revising a Draft never moves the client into another age band.
        var createdOn = DateOnly.FromDateTime((existing?.CreatedAt ?? time.GetUtcNow()).UtcDateTime);
        var priced = Price(command, createdOn);
        if (priced.Failure is { } unpriced)
        {
            return unpriced;
        }

        var price = priced.Value!;
        var terms = new QuoteTerms(
            ClientName: command.ClientName!,
            ClientDateOfBirth: command.ClientDateOfBirth!.Value,
            ClientIsSmoker: command.ClientIsSmoker!.Value,
            ProductCode: command.ProductCode!.ToUpperInvariant(),
            CoverAmount: command.CoverAmount!.Value,
            TermYears: command.TermYears!.Value,
            Premium: price.MonthlyPremium,
            Currency: price.Currency,
            PricingVersion: price.PricingVersion,
            Notes: command.Notes);
        var quote = existing is null ? Create(terms, command.IdempotencyKey, requestHash) : Revise(existing, terms);
        return await SaveAsync(quote, command, requestHash, cancellationToken);
    }

    /// <summary>Shape checks, and the Idempotency-Key rules for a create. The ranges (age, cover, term, product) are Pricing's.</summary>
    private static Failure? Invalid(SaveQuoteCommand command) => command switch
    {
        { QuoteId: null, IdempotencyKey: var key } when string.IsNullOrWhiteSpace(key) =>
            Failure.Validation("Idempotency-Key header is required when creating a quote."),
        { QuoteId: null, IdempotencyKey.Length: > SaveQuoteCommand.IdempotencyKeyMaxLength } =>
            Failure.Validation($"Idempotency-Key must be at most {SaveQuoteCommand.IdempotencyKeyMaxLength} characters."),
        _ when command.Validate() is { Count: > 0 } errors => Failure.Validation("The quote is not valid.", errors),
        _ => null,
    };

    /// <summary>An edit needs a quote this adviser owns that is still a Draft; another adviser's quote is reported as missing.</summary>
    private Failure? NotEditable(Guid? quoteId, Quote? existing) => (quoteId, existing) switch
    {
        (not null, null) => Failure.NotFound("No such quote."),
        (not null, { AdviserId: var owner }) when owner != adviser.Id => Failure.NotFound("No such quote."),
        (_, { Status: not QuoteStatus.Draft } quote) => Failure.Conflict($"Only a Draft can be edited; this quote is {quote.Status}."),
        _ => null,
    };

    private static Result<PremiumQuote> Price(SaveQuoteCommand command, DateOnly createdOn)
    {
        PricingOutcome pricing;
        using (QuoteTelemetry.Source.StartActivity("price"))
        {
            pricing = PremiumCalculator.Calculate(new PricingRequest(
                command.ClientDateOfBirth!.Value, createdOn, command.ClientIsSmoker!.Value, command.ProductCode!, command.CoverAmount!.Value, command.TermYears!.Value));
        }

        return pricing switch
        {
            PremiumQuote price => price,
            PricingRejection rejection => Failure.Validation("The quote cannot be priced.", ToFieldError(rejection.Error)),
            _ => throw new InvalidOperationException($"Unknown pricing outcome {pricing.GetType().Name}."),
        };
    }

    private Quote Create(QuoteTerms terms, string? idempotencyKey, string? requestHash)
    {
        var quote = Quote.Create(adviser.Id, terms, time, idempotencyKey, requestHash);
        db.Quotes.Add(quote);
        return quote;
    }

    private Quote Revise(Quote quote, QuoteTerms terms)
    {
        quote.TryRevise(terms, time);
        return quote;
    }

    private async Task<Result<SaveQuoteResult>> SaveAsync(Quote quote, SaveQuoteCommand command, string? requestHash, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure.Conflict("The quote was changed by someone else. Reload it and try again.");
        }
        catch (DuplicateIdempotencyKeyException)
        {
            // A concurrent request with the same key inserted first; the unique index kept it to one row.
            return await ReplayAsync(command.IdempotencyKey!, requestHash!, cancellationToken)
                ?? Failure.Conflict("Another request with this Idempotency-Key is in progress. Try again.");
        }

        return SaveQuoteResult.From(quote);
    }

    /// <summary>The earlier create with this key, if there is one: its current state, or a mismatch when the command differs.</summary>
    private async Task<Result<SaveQuoteResult>?> ReplayAsync(string idempotencyKey, string requestHash, CancellationToken cancellationToken)
    {
        var earlier = await db.Quotes.FirstOrDefaultAsync(q => q.AdviserId == adviser.Id && q.IdempotencyKey == idempotencyKey, cancellationToken);
        if (earlier is null)
        {
            return null;
        }

        return earlier.RequestHash == requestHash
            ? SaveQuoteResult.From(earlier, replayed: true)
            : Failure.IdempotencyMismatch("This Idempotency-Key was already used for a different quote.");
    }

    private static Dictionary<string, string[]> ToFieldError(PricingError error) => error switch
    {
        PricingError.AgeOutOfRange => new() { ["clientDateOfBirth"] = ["The client must be 18 to 65 when the quote is created."] },
        PricingError.CoverOutOfRange => new() { ["coverAmount"] = ["Cover must be between R10,000 and R5,000,000."] },
        PricingError.TermOutOfRange => new() { ["termYears"] = ["Term must be 1 to 30 years."] },
        PricingError.UnknownProduct => new() { ["productCode"] = ["Unknown product."] },
        _ => throw new ArgumentOutOfRangeException(nameof(error)),
    };
}
