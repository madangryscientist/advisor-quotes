using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Api.Cqrs;
using Api.Quotes.Queries;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests.Quotes;

public class GetQuoteEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Guid KnownId = Guid.Parse("0197f3a0-0000-7000-8000-000000000001");

    private sealed class FakeGetQuoteHandler : IQueryHandler<GetQuoteQuery, QuoteDetails>
    {
        public Task<Result<QuoteDetails>> Handle(GetQuoteQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<Result<QuoteDetails>>(query.QuoteId == KnownId
                ? new QuoteDetails
                {
                    QuoteId = KnownId,
                    ExternalReference = "Q-20261021-0001",
                    AdviserId = "ADV-001",
                    ClientName = "Thandi Nkosi",
                    ClientDateOfBirth = new DateOnly(1985, 6, 15),
                    ProductCode = "LIFE",
                    CoverAmount = 1_000_000m,
                    TermYears = 20,
                    Premium = 2090.00m,
                    Currency = "ZAR",
                    PricingVersion = "2026.1",
                    Status = "SentToClient",
                    History =
                    [
                        new QuoteStatusChangeDetails(null, "Draft", null, "ADV-001", new DateTimeOffset(2026, 10, 21, 9, 0, 0, TimeSpan.Zero)),
                        new QuoteStatusChangeDetails("Draft", "SentToClient", "sent by email", "ADV-001", new DateTimeOffset(2026, 10, 21, 10, 0, 0, TimeSpan.Zero)),
                    ],
                }
                : Failure.NotFound("No such quote."));
    }

    private async Task<HttpResponseMessage> Get(string query)
    {
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IQueryHandler<GetQuoteQuery, QuoteDetails>>();
            services.AddSingleton<IQueryHandler<GetQuoteQuery, QuoteDetails>, FakeGetQuoteHandler>();
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Adviser-Id", "ADV-001");
        return await client.GetAsync(new Uri($"/v1/queries/get-quote{query}", UriKind.Relative));
    }

    [Fact]
    public async Task AFoundQuoteIsOkWithItsHistory()
    {
        var response = await Get($"?quoteId={KnownId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(KnownId, body.GetProperty("quoteId").GetGuid());
        Assert.Equal("1985-06-15", body.GetProperty("clientDateOfBirth").GetString());
        Assert.Equal("SentToClient", body.GetProperty("status").GetString());
        Assert.Equal(["Draft", "SentToClient"], body.GetProperty("history").EnumerateArray().Select(h => h.GetProperty("to").GetString()));
    }

    [Fact]
    public async Task AnUnknownQuoteIsNotFound()
    {
        var response = await Get($"?quoteId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
