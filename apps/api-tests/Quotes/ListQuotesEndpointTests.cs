using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Api.Cqrs;
using Api.Quotes.Domain;
using Api.Quotes.Queries;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests.Quotes;

public class ListQuotesEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed class FakeListQuotesHandler : IQueryHandler<ListQuotesQuery, IReadOnlyList<QuoteSummary>>
    {
        public static ListQuotesQuery? Last { get; private set; }

        public Task<Result<IReadOnlyList<QuoteSummary>>> Handle(ListQuotesQuery query, CancellationToken cancellationToken)
        {
            Last = query;
            List<QuoteSummary> rows =
            [
                new()
                {
                    QuoteId = Guid.Parse("0197f3a0-0000-7000-8000-000000000001"),
                    ExternalReference = "Q-20261021-0001",
                    ClientName = "Thandi Nkosi",
                    ProductCode = "LIFE",
                    CoverAmount = 1_000_000m,
                    TermYears = 20,
                    Premium = 2090.00m,
                    Currency = "ZAR",
                    Status = "Draft",
                },
            ];
            return Task.FromResult<Result<IReadOnlyList<QuoteSummary>>>(rows);
        }
    }

    private async Task<HttpResponseMessage> Get(string query)
    {
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IQueryHandler<ListQuotesQuery, IReadOnlyList<QuoteSummary>>>();
            services.AddSingleton<IQueryHandler<ListQuotesQuery, IReadOnlyList<QuoteSummary>>, FakeListQuotesHandler>();
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Adviser-Id", "ADV-001");
        return await client.GetAsync(new Uri($"/v1/queries/list-quotes{query}", UriKind.Relative));
    }

    [Fact]
    public async Task ListsSummariesWithoutHistory()
    {
        var response = await Get("");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        var row = Assert.Single(rows);
        Assert.Equal("Q-20261021-0001", row.GetProperty("externalReference").GetString());
        Assert.False(row.TryGetProperty("history", out _));
        Assert.Null(FakeListQuotesHandler.Last?.Status);
    }

    [Fact]
    public async Task BindsTheStatusFilter()
    {
        var response = await Get("?status=SentToClient");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(QuoteStatus.SentToClient, FakeListQuotesHandler.Last?.Status);
    }

    [Fact]
    public async Task AnUnknownStatusIsABadRequest()
    {
        var response = await Get("?status=Bogus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
