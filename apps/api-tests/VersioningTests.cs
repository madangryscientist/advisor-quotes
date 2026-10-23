using System.Net;
using System.Net.Http.Json;

namespace Api.Tests;

public class VersioningTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task AVersionedRouteReportsTheSupportedVersions()
    {
        using var client = factory.CreateClient();

        // No X-Adviser-Id, so the handler never runs and no database is needed; the version header is sent regardless.
        var response = await client.PostAsJsonAsync(new Uri("/v1/commands/save-quote", UriKind.Relative), new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("1.0", Assert.Single(response.Headers.GetValues("api-supported-versions")));
    }

    [Fact]
    public async Task AnUnsupportedVersionIsNotFound()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/v2/queries/list-advisers", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnUnversionedRouteIsNotFound()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/queries/list-advisers", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
