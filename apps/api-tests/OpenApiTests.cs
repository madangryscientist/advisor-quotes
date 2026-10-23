using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Api.Tests;

public class OpenApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Uri DocumentUri = new("/openapi/v1.json", UriKind.Relative);

    [Fact]
    public async Task DocumentListsTheQuoteEndpointsWithNamesAndSummaries()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(DocumentUri);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var paths = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("paths");
        var expected = new Dictionary<string, (string Method, string OperationId)>
        {
            ["/v1/commands/save-quote"] = ("post", "saveQuote"),
            ["/v1/commands/send-quote-to-client"] = ("post", "sendQuoteToClient"),
            ["/v1/commands/accept-quote"] = ("post", "acceptQuote"),
            ["/v1/commands/decline-quote"] = ("post", "declineQuote"),
            ["/v1/queries/get-quote"] = ("get", "getQuote"),
            ["/v1/queries/list-quotes"] = ("get", "listQuotes"),
            ["/v1/queries/list-advisers"] = ("get", "listAdvisers"),
        };
        foreach (var (path, (method, operationId)) in expected)
        {
            var operation = paths.GetProperty(path).GetProperty(method);
            Assert.Equal(operationId, operation.GetProperty("operationId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("summary").GetString()));
            Assert.True(operation.GetProperty("responses").TryGetProperty("200", out var ok));
            Assert.True(ok.TryGetProperty("content", out _), $"{path} 200 declares no body type");
        }

        Assert.Contains("Idempotency-Key", HeaderNames(paths, "/v1/commands/save-quote", "post"));
        var replayed = paths.GetProperty("/v1/commands/save-quote").GetProperty("post").GetProperty("responses").GetProperty("200").GetProperty("headers");
        Assert.True(replayed.TryGetProperty("Idempotent-Replayed", out _));
        Assert.Contains("X-Adviser-Id", HeaderNames(paths, "/v1/queries/get-quote", "get"));
        Assert.DoesNotContain("X-Adviser-Id", HeaderNames(paths, "/v1/queries/list-advisers", "get"));
    }

    private static List<string> HeaderNames(JsonElement paths, string path, string method) =>
        paths.GetProperty(path).GetProperty(method).TryGetProperty("parameters", out var parameters)
            ? parameters.EnumerateArray()
            .Where(p => p.GetProperty("in").GetString() == "header")
            .Select(p => p.GetProperty("name").GetString()!)
            .ToList()
            : [];
}
