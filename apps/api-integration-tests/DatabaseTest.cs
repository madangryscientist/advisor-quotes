using Api.Persistence;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Api.IntegrationTests;

/// <summary>
/// Base for a test class that needs the database. Each test runs inside a transaction that is rolled back
/// afterwards, so it leaves no rows and needs no unique data. Every context from <see cref="NewContext"/>
/// shares that transaction, which lets a test save with one context and read back with a fresh one.
/// A test that needs two real connections (a race) cannot use this; it uses unique data and its own connection.
/// </summary>
[SuppressMessage("Design", "CA1001", Justification = "xUnit v2 disposes through IAsyncLifetime.DisposeAsync, not IDisposable.")]
public abstract class DatabaseTest : IAsyncLifetime
{
    private NpgsqlConnection _connection = null!;
    private NpgsqlTransaction _transaction = null!;

    public async Task InitializeAsync()
    {
        await IntegrationDatabase.EnsureMigratedAsync();
        _connection = new NpgsqlConnection(IntegrationDatabase.ConnectionString);
        await _connection.OpenAsync();
        _transaction = await _connection.BeginTransactionAsync();
    }

    public async Task DisposeAsync()
    {
        await _transaction.RollbackAsync();
        await _connection.DisposeAsync();
    }

    protected QuotesDbContext NewContext()
    {
        var db = IntegrationDatabase.NewContext(_connection);
        db.Database.UseTransaction(_transaction);
        return db;
    }
}
