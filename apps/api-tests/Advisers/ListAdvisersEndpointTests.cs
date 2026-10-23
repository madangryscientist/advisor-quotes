using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Api.Advisers.Queries;
using Api.Cqrs;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests.Advisers;

public class ListAdvisersEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed class FakeListAdvisersHandler : IQueryHandler<ListAdvisersQuery, IReadOnlyList<AdviserSummary>>
    {
        public Task<Result<IReadOnlyList<AdviserSummary>>> Handle(ListAdvisersQuery query, CancellationToken cancellationToken)
        {
            List<AdviserSummary> rows = [new() { AdviserId = "ADV-003", Name = "Aisha K." }, new() { AdviserId = "ADV-001", Name = "Thandi M." }];
            return Task.FromResult<Result<IReadOnlyList<AdviserSummary>>>(rows);
        }
    }

    [Fact]
    public async Task ListsAdvisersWithoutAnAdviserHeader()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IQueryHandler<ListAdvisersQuery, IReadOnlyList<AdviserSummary>>>();
            services.AddSingleton<IQueryHandler<ListAdvisersQuery, IReadOnlyList<AdviserSummary>>, FakeListAdvisersHandler>();
        }));
        using var client = host.CreateClient();

        var response = await client.GetAsync(new Uri("/v1/queries/list-advisers", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("ADV-003", rows[0].GetProperty("adviserId").GetString());
        Assert.Equal("Aisha K.", rows[0].GetProperty("name").GetString());
    }
}
