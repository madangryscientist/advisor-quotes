using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Api.Tests.Quotes;

public class SaveQuoteEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly object Draft = new
    {
        clientName = "Thandi Nkosi",
        clientDateOfBirth = "1985-06-15",
        clientIsSmoker = false,
        productCode = "LIFE",
        coverAmount = 1_000_000,
        termYears = 20,
    };

    private async Task<HttpResponseMessage> Post(object body, string? idempotencyKey = null)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Adviser-Id", "ADV-001");
        client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        return await client.PostAsJsonAsync(new Uri("/v1/commands/save-quote", UriKind.Relative), body);
    }

    [Fact]
    public async Task ACreateReturnsOkWithTheQuote()
    {
        var response = await Post(Draft);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Idempotent-Replayed"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(Guid.TryParse(body.GetProperty("quoteId").GetString(), out _));
        Assert.StartsWith("Q-", body.GetProperty("externalReference").GetString(), StringComparison.Ordinal);
        Assert.True(body.GetProperty("premium").GetDecimal() > 0);
        Assert.Equal("ZAR", body.GetProperty("currency").GetString());
        Assert.Equal("2026.1", body.GetProperty("pricingVersion").GetString());
        Assert.Equal("Draft", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task InvalidInputIsABadRequestWithFieldErrors()
    {
        var response = await Post(new { clientName = "", productCode = "LIFE", coverAmount = 1_000_000 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("clientName", out _));
        Assert.True(errors.TryGetProperty("clientDateOfBirth", out _));
        Assert.True(errors.TryGetProperty("termYears", out _));
    }

    [Fact]
    public async Task AReplayIsMarkedAndReturnsTheSameQuote()
    {
        var key = Guid.NewGuid().ToString();
        var first = await (await Post(Draft, key)).Content.ReadFromJsonAsync<JsonElement>();

        var replay = await Post(Draft, key);

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotent-Replayed").Single());
        var body = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(first.GetProperty("quoteId").GetGuid(), body.GetProperty("quoteId").GetGuid());
        Assert.False(body.TryGetProperty("replayed", out _));
    }
}
