using Api.Advisers.Services;

namespace Api.Tests;

/// <summary>The three seeded advisers, without the <c>advisers</c> table.</summary>
public sealed class FakeAdviserDirectory : IAdviserDirectory
{
    private static readonly HashSet<string> Known = ["ADV-001", "ADV-002", "ADV-003"];

    public Task<bool> ExistsAsync(string id, CancellationToken cancellationToken) => Task.FromResult(Known.Contains(id));
}
