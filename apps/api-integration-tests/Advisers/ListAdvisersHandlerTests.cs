using Api.Advisers.Queries;
using Api.Advisers.Handlers;

namespace Api.IntegrationTests.Advisers;

/// <summary>Runs the real SQL against the seeded <c>advisers</c> table.</summary>
public sealed class ListAdvisersHandlerTests : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        if (IntegrationDatabase.ConnectionString is not null)
        {
            await IntegrationDatabase.EnsureMigratedAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [FactRequiresDatabase]
    public async Task ReturnsTheThreeSeededAdvisersByName()
    {
        await using var db = IntegrationDatabase.NewContext();

        var result = await new ListAdvisersHandler(db).Handle(new ListAdvisersQuery(), CancellationToken.None);

        var advisers = Assert.IsAssignableFrom<IReadOnlyList<AdviserSummary>>(result.Value);
        Assert.Equal(
            [("ADV-003", "Aisha K."), ("ADV-002", "Pieter V."), ("ADV-001", "Thandi M.")],
            advisers.Select(a => (a.AdviserId, a.Name)));
    }
}
