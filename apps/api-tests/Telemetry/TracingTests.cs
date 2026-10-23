using System.Diagnostics;
using System.Net.Http.Json;
using System.Net;
using Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace Api.Tests.Telemetry;

public sealed class TracingTests : IClassFixture<ApiFactory>
{
    private readonly List<Activity> exported = [];
    private readonly HttpClient client;

    private readonly ApiFactory factory;

    public TracingTests(ApiFactory factory)
    {
        this.factory = factory;
        client = factory
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(exported))))
            .CreateClient();
    }

    [Fact]
    public async Task OneRequestYieldsOneServerSpan()
    {
        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        // ASP.NET Core's activity source is process-wide, so other test hosts' spans land here too: pick ours by trace id.
        // The server span ends after the response is sent, so the client can be back before it is exported.
        var traceId = response.Headers.GetValues("X-Trace-Id").Single();
        SpinWait.SpinUntil(() => exported.ToArray().Any(a => a.TraceId.ToString() == traceId), TimeSpan.FromSeconds(5));
        var span = Assert.Single(exported.ToArray(), a => a.TraceId.ToString() == traceId);
        Assert.Equal(ActivityKind.Server, span.Kind);
        Assert.Equal("GET /health", span.DisplayName);
    }

    [Fact]
    public async Task EveryResponseCarriesTheTraceId()
    {
        using var ok = await client.GetAsync(new Uri("/health", UriKind.Relative));
        using var missing = await client.GetAsync(new Uri("/no-such-route", UriKind.Relative));

        foreach (var response in new[] { ok, missing })
        {
            Assert.Matches("^[0-9a-f]{32}$", response.Headers.GetValues("X-Trace-Id").Single());
        }
    }

    [Fact]
    public async Task AnUnhandledExceptionStillCarriesTheTraceId()
    {
        using var failing = factory
            .WithWebHostBuilder(builder =>
            {
                // Development has its own exception page, which would hide a missing handler.
                builder.UseEnvironment("Production");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<QuotesDbContext>();
                    services.AddScoped<QuotesDbContext>(sp => new ThrowingDbContext(sp.GetRequiredService<DbContextOptions<QuotesDbContext>>()));
                });
            })
            .CreateClient();
        failing.DefaultRequestHeaders.Add("X-Adviser-Id", "ADV-001");
        failing.DefaultRequestHeaders.Add("Idempotency-Key", "trace-test");

        using var response = await failing.PostAsJsonAsync(new Uri("/v1/commands/save-quote", UriKind.Relative), new
        {
            clientName = "Thandi Nkosi",
            clientDateOfBirth = "1985-06-15",
            clientIsSmoker = false,
            productCode = "LIFE",
            coverAmount = 1_000_000,
            termYears = 20,
        });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Matches("^[0-9a-f]{32}$", response.Headers.GetValues("X-Trace-Id").Single());
    }

    private sealed class ThrowingDbContext(DbContextOptions<QuotesDbContext> options) : QuotesDbContext(options)
    {
        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");
    }
}
