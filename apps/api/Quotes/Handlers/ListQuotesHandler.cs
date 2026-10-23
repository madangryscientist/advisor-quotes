using Api.Advisers.Services;
using Api.Cqrs;
using Api.Persistence;
using Api.Quotes.Queries;
using Microsoft.EntityFrameworkCore;

namespace Api.Quotes.Handlers;

public sealed class ListQuotesHandler(QuotesDbContext db, ICurrentAdviser adviser) : IQueryHandler<ListQuotesQuery, IReadOnlyList<QuoteSummary>>
{
    public const int MaxRows = 100;

    public async Task<Result<IReadOnlyList<QuoteSummary>>> Handle(ListQuotesQuery query, CancellationToken cancellationToken)
    {
        var status = query.Status;

        // Only the summary columns are selected: no history, and nothing is tracked.
        var rows = await db.Quotes.AsNoTracking()
            .Where(q => q.AdviserId == adviser.Id && (status == null || q.Status == status))
            .OrderByDescending(q => q.CreatedAt).ThenByDescending(q => q.Id)
            .Take(MaxRows)
            .Select(q => new { q.Id, q.ExternalReference, q.ClientName, q.ProductCode, q.CoverAmount, q.TermYears, q.Premium, q.Currency, q.Status, q.CreatedAt, q.UpdatedAt })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new QuoteSummary
            {
                QuoteId = r.Id,
                ExternalReference = r.ExternalReference,
                ClientName = r.ClientName,
                ProductCode = r.ProductCode,
                CoverAmount = r.CoverAmount,
                TermYears = r.TermYears,
                Premium = r.Premium,
                Currency = r.Currency,
                Status = r.Status.ToString(),
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
            })
            .ToList();
    }
}
