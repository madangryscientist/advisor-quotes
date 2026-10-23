using Api.Advisers.Services;
using Api.Cqrs;
using Api.Persistence;
using Api.Quotes.Queries;
using Microsoft.EntityFrameworkCore;

namespace Api.Quotes.Handlers;

public sealed class GetQuoteHandler(QuotesDbContext db, ICurrentAdviser adviser) : IQueryHandler<GetQuoteQuery, QuoteDetails>
{
    public async Task<Result<QuoteDetails>> Handle(GetQuoteQuery query, CancellationToken cancellationToken)
    {
        // Another adviser's quote is reported as missing, the same as the SaveQuote command does. The history is an owned
        // collection, so it comes with the quote in one query, but in no promised order: EF adds no ORDER BY for it.
        var quote = await db.Quotes.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == query.QuoteId && q.AdviserId == adviser.Id, cancellationToken);
        if (quote is null)
        {
            return Failure.NotFound("No such quote.");
        }

        return new QuoteDetails
        {
            QuoteId = quote.Id,
            ExternalReference = quote.ExternalReference,
            AdviserId = quote.AdviserId,
            ClientName = quote.ClientName,
            ClientDateOfBirth = quote.ClientDateOfBirth,
            ClientIsSmoker = quote.ClientIsSmoker,
            ProductCode = quote.ProductCode,
            CoverAmount = quote.CoverAmount,
            TermYears = quote.TermYears,
            Premium = quote.Premium,
            Currency = quote.Currency,
            PricingVersion = quote.PricingVersion,
            Notes = quote.Notes,
            Status = quote.Status.ToString(),
            CreatedAt = quote.CreatedAt,
            UpdatedAt = quote.UpdatedAt,
            History = quote.History
                .OrderBy(h => h.ChangedAt)
                .Select(h => new QuoteStatusChangeDetails(h.From?.ToString(), h.To.ToString(), h.Reason, h.ChangedBy, h.ChangedAt))
                .ToList(),
        };
    }
}
