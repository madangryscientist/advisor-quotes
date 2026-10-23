using Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Api.IntegrationTests;

/// <summary>
/// The Postgres these tests run against: the <c>quotes_it</c> database named by
/// <c>ConnectionStrings__QuotesIntegration</c>. Unset, there is no database and every test skips.
/// </summary>
public static class IntegrationDatabase
{
    public const string ConnectionStringVariable = "ConnectionStrings__QuotesIntegration";

    public const string SkipReason =
        $"{ConnectionStringVariable} is not set. Start Postgres (docker compose up -d), point the variable at the quotes_it database, and rerun.";

    public static string? ConnectionString { get; } =
        Environment.GetEnvironmentVariable(ConnectionStringVariable) is { Length: > 0 } value ? value : null;

    // Migrations run once per test run, whichever test class asks first; the rest wait on the same task.
    private static readonly Lazy<Task> Migrated = new(MigrateAsync);

    public static Task EnsureMigratedAsync() => Migrated.Value;

    public static QuotesDbContext NewContext(NpgsqlConnection? connection = null)
    {
        var options = new DbContextOptionsBuilder<QuotesDbContext>();
        if (connection is null)
        {
            options.UseNpgsql(ConnectionString!);
        }
        else
        {
            options.UseNpgsql(connection);
        }

        return new QuotesDbContext(options.UseSnakeCaseNamingConvention().Options);
    }

    private static async Task MigrateAsync()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }
}
