using Api.Tests.Cqrs;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Api.Tests.Advisers;

public class CurrentAdviserTests
{
    private static async Task<HttpResponseMessage> WhoAmI(string? adviserId)
    {
        using var client = await CqrsHost.Start();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/v1/queries/who-am-i", UriKind.Relative));
        if (adviserId is not null)
        {
            request.Headers.Add("X-Adviser-Id", adviserId);
        }

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task AKnownAdviserReachesTheHandler()
    {
        var response = await WhoAmI("ADV-002");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ADV-002", await response.Content.ReadFromJsonAsync<string>());
    }

    [Fact]
    public async Task AMissingHeaderIsUnauthorized() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await WhoAmI(null)).StatusCode);

    [Fact]
    public async Task AnUnknownAdviserIsForbidden()
    {
        var response = await WhoAmI("ADV-999");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unknown adviser", problem.GetProperty("detail").GetString());
    }
}
