using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Api.Tests;

/// <summary>A switchable stand-in for the database health check, so readiness can be faked healthy or unhealthy without a database.</summary>
public sealed class FakeDatabaseHealthCheck : IHealthCheck
{
    public bool Reachable { get; set; } = true;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(Reachable ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("The database is unreachable."));
}
