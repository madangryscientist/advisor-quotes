using System.Diagnostics;
using Api.Advisers.Services;
using Api.Cqrs;
using Api.Persistence;
using Api.Quotes.Domain;
using Api.Quotes.Commands;
using Microsoft.EntityFrameworkCore;

namespace Api.Quotes.Services;

/// <summary>
/// Loads the adviser's quote and applies one domain move. The rules live in <see cref="Quote"/>; this only maps
/// "no such quote", "not allowed from here" and "changed by someone else" to failures.
/// </summary>
internal static class QuoteStatusChanger
{
    public static async Task<Result<QuoteStatusResult>> ChangeAsync(
        QuotesDbContext db, ICurrentAdviser adviser, Guid quoteId, Func<Quote, StatusChangeOutcome> move, CancellationToken cancellationToken)
    {
        var quote = await db.Quotes.FirstOrDefaultAsync(q => q.Id == quoteId, cancellationToken);
        if (quote is null || quote.AdviserId != adviser.Id)
        {
            return Failure.NotFound("No such quote.");
        }

        // The one tracing line outside the controller's span: only here is the status from before the move known.
        Activity.Current?.SetTag("quote.status.from", quote.Status.ToString());
        switch (move(quote))
        {
            case IllegalTransition illegal:
                return Failure.Conflict($"A quote cannot move from {illegal.From} to {illegal.To}.");
            case ReasonRequired:
                return Failure.Validation("The quote cannot be declined.", new Dictionary<string, string[]> { ["reason"] = ["A reason is required to decline a quote."] });
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure.Conflict("The quote was changed by someone else. Reload it and try again.");
        }

        return new QuoteStatusResult(quote.Id, quote.Status);
    }
}
