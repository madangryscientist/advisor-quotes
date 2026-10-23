using Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests;

/// <summary>An EF Core in-memory <see cref="QuotesDbContext"/>, so handler and endpoint tests need no database server.</summary>
public static class TestDb
{
    /// <summary>Options for a database of its own. Contexts built from the same options share its rows.</summary>
    public static DbContextOptions<QuotesDbContext> Options() =>
        new DbContextOptionsBuilder<QuotesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
}
