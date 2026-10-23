using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Api.Tests.Quotes;

public class ChangeQuoteStatusEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private HttpClient Client()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Adviser-Id", "ADV-001");
        return client;
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string command, object body) =>
        client.PostAsJsonAsync(new Uri($"/v1/commands/{command}", UriKind.Relative), body);

    private static async Task<Guid> SaveDraft(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/commands/save-quote", UriKind.Relative));
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(new
        {
            clientName = "Thandi Nkosi",
            clientDateOfBirth = "1985-06-15",
            clientIsSmoker = false,
            productCode = "LIFE",
            coverAmount = 1_000_000,
            termYears = 20,
        });
        using var response = await client.SendAsync(request);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quoteId").GetGuid();
    }

    [Fact]
    public async Task SendThenAcceptReturnsTheQuoteIdAndStatus()
    {
        using var client = Client();
        var id = await SaveDraft(client);

        var sent = await Post(client, "send-quote-to-client", new { quoteId = id });
        var accepted = await Post(client, "accept-quote", new { quoteId = id, reason = "signed" });

        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        Assert.Equal("SentToClient", (await sent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var body = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, body.GetProperty("quoteId").GetGuid());
        Assert.Equal("Accepted", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task FailuresMapToTheirStatusCodes()
    {
        using var client = Client();
        var id = await SaveDraft(client);

        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "decline-quote", new { quoteId = id })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(client, "accept-quote", new { quoteId = id })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(client, "send-quote-to-client", new { quoteId = Guid.CreateVersion7() })).StatusCode);
    }
}
