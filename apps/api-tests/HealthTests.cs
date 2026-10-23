using System.Net;

namespace Api.Tests;

public class HealthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task HealthReturnsOk()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadyReturnsOkWhenDatabaseIsReachable()
    {
        factory.DatabaseHealth.Reachable = true;
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadyReturnsServiceUnavailableWhenDatabaseIsUnreachable()
    {
        factory.DatabaseHealth.Reachable = false;
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
