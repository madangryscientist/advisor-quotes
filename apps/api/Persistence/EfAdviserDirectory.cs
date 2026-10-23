using Api.Advisers.Services;
using Microsoft.EntityFrameworkCore;

namespace Api.Persistence;

public sealed class EfAdviserDirectory(QuotesDbContext db) : IAdviserDirectory
{
    public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken) =>
        db.Advisers.AnyAsync(a => a.Id == id, cancellationToken);
}
