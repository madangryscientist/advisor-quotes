using Api.Advisers.Queries;
using Api.Cqrs;
using Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Advisers.Handlers;

public sealed class ListAdvisersHandler(QuotesDbContext db) : IQueryHandler<ListAdvisersQuery, IReadOnlyList<AdviserSummary>>
{
    public async Task<Result<IReadOnlyList<AdviserSummary>>> Handle(ListAdvisersQuery query, CancellationToken cancellationToken) =>
        await db.Advisers.AsNoTracking()
            .OrderBy(a => a.Name).ThenBy(a => a.Id)
            .Select(a => new AdviserSummary { AdviserId = a.Id, Name = a.Name })
            .ToListAsync(cancellationToken);
}
