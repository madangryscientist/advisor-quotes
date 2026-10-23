using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Cqrs;

namespace Api.Tests.Cqrs;

public class CqrsTests
{
    [Fact]
    public async Task QuerySuccessIsOkWithTheValue()
    {
        using var client = await CqrsHost.Start();

        var response = await client.GetAsync(new Uri("/v1/queries/echo?text=hello", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hello", await response.Content.ReadFromJsonAsync<string>());
    }

    [Theory]
    [InlineData(FailureKind.Validation, HttpStatusCode.BadRequest)]
    [InlineData(FailureKind.Unauthorized, HttpStatusCode.Unauthorized)]
    [InlineData(FailureKind.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData(FailureKind.NotFound, HttpStatusCode.NotFound)]
    [InlineData(FailureKind.Conflict, HttpStatusCode.Conflict)]
    [InlineData(FailureKind.IdempotencyMismatch, HttpStatusCode.UnprocessableEntity)]
    public async Task EachFailureKindMapsToItsStatusAsProblemDetails(FailureKind kind, HttpStatusCode status)
    {
        using var client = await CqrsHost.Start();

        var response = await client.PostAsJsonAsync(new Uri("/v1/commands/fail", UriKind.Relative), new FailCommand(kind));

        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task ValidationFailureCarriesTheFieldErrors()
    {
        using var client = await CqrsHost.Start();

        var response = await client.PostAsJsonAsync(new Uri("/v1/commands/fail", UriKind.Relative), new FailCommand(FailureKind.Validation));

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Must be positive.", problem.GetProperty("errors").GetProperty("amount")[0].GetString());
    }
}
