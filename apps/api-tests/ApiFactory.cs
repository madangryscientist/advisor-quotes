using Api.Advisers.Services;
using Api.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Api.Tests;

/// <summary>The API with no database: quotes are stored in an in-memory EF database and the adviser directory is in memory, migrations do not run, and the database health check is faked.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public FakeDatabaseHealthCheck DatabaseHealth { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:MigrateOnStartup"] = "false",
                // Development turns the demo seed on; the tests want an empty database.
                ["Database:SeedDemoData"] = "false",
            }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<QuotesDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<QuotesDbContext>>();
            services.AddSingleton(TestDb.Options());
            services.RemoveAll<IAdviserDirectory>();
            services.AddSingleton<IAdviserDirectory, FakeAdviserDirectory>();
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                options.Registrations.Clear();
                options.Registrations.Add(new HealthCheckRegistration("database", DatabaseHealth, failureStatus: null, tags: null));
            });
        });
    }
}
