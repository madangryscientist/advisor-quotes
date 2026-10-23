using System.Net;
using Api.Advisers.Queries;
using Api.Cqrs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests;

public class ExceptionHandlingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed class ThrowingListAdvisersHandler : IQueryHandler<ListAdvisersQuery, IReadOnlyList<AdviserSummary>>
    {
        public Task<Result<IReadOnlyList<AdviserSummary>>> Handle(ListAdvisersQuery query, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task AnUnhandledExceptionIsProblemDetailsWithATraceIdInDevelopment()
    {
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IQueryHandler<ListAdvisersQuery, IReadOnlyList<AdviserSummary>>>();
                services.AddSingleton<IQueryHandler<ListAdvisersQuery, IReadOnlyList<AdviserSummary>>, ThrowingListAdvisersHandler>();
            });
        });
        using var client = host.CreateClient();

        var response = await client.GetAsync(new Uri("/v1/queries/list-advisers", UriKind.Relative));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.Contains("X-Trace-Id"));
    }
}
