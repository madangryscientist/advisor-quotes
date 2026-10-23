using Api.Advisers.Services;
using Api.Controllers;
using Api.Cqrs;
using Asp.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests.Cqrs;

public sealed record EchoQuery(string Text);

public sealed record FailCommand(FailureKind Kind);

public sealed record WhoAmIQuery;

public sealed class EchoHandler : IQueryHandler<EchoQuery, string>
{
    public Task<Result<string>> Handle(EchoQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<Result<string>>(query.Text);
}

public sealed class FailHandler : ICommandHandler<FailCommand, string>
{
    public Task<Result<string>> Handle(FailCommand command, CancellationToken cancellationToken) =>
        Task.FromResult<Result<string>>(command.Kind switch
        {
            FailureKind.Validation => Failure.Validation("Bad input.", new Dictionary<string, string[]> { ["amount"] = ["Must be positive."] }),
            FailureKind.Unauthorized => Failure.Unauthorized("Sign in."),
            FailureKind.Forbidden => Failure.Forbidden("Not yours."),
            FailureKind.NotFound => Failure.NotFound("No such quote."),
            FailureKind.Conflict => Failure.Conflict("Already accepted."),
            _ => Failure.IdempotencyMismatch("Same key, different request."),
        });
}

public sealed class WhoAmIHandler(ICurrentAdviser adviser) : IQueryHandler<WhoAmIQuery, string>
{
    public Task<Result<string>> Handle(WhoAmIQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<Result<string>>(adviser.Id);
}

[Route(ApiRoutes.Queries)]
public sealed class TestQueriesController(
    CurrentAdviser adviser,
    IQueryHandler<EchoQuery, string> echo,
    IQueryHandler<WhoAmIQuery, string> whoAmI) : ApiControllerBase(adviser)
{
    [HttpGet("echo")]
    public Task<ActionResult<string>> Echo([FromQuery] EchoQuery query, CancellationToken cancellationToken) =>
        RunQuery(echo, query, cancellationToken);

    [HttpGet("who-am-i")]
    [RequireAdviser]
    public Task<ActionResult<string>> WhoAmI([FromQuery] WhoAmIQuery query, CancellationToken cancellationToken) =>
        RunQuery(whoAmI, query, cancellationToken);
}

[Route(ApiRoutes.Commands)]
public sealed class TestCommandsController(CurrentAdviser adviser, ICommandHandler<FailCommand, string> fail) : ApiControllerBase(adviser)
{
    [HttpPost("fail")]
    public Task<ActionResult<string>> Fail(FailCommand command, CancellationToken cancellationToken) =>
        RunCommand(fail, command, cancellationToken);
}

/// <summary>A minimal host that registers only the test controllers and handlers, so the plumbing is tested apart from the real API.</summary>
public static class CqrsHost
{
    public static async Task<HttpClient> Start(Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        // Carry the caller's Activity into the request, so a test can find its own spans by trace id.
        builder.WebHost.UseTestServer(options => options.PreserveExecutionContext = true);
        // The real API returns no bare strings, but these test handlers do; without this they would be written as text/plain, not JSON.
        builder.Services.AddControllers(options => options.OutputFormatters.RemoveType<StringOutputFormatter>()).AddApplicationPart(typeof(CqrsHost).Assembly);
        builder.Services.AddApiVersioning(options => options.ApiVersionReader = new UrlSegmentApiVersionReader()).AddMvc();
        builder.Services.AddProblemDetails();
        builder.Services.AddScoped<IQueryHandler<EchoQuery, string>, EchoHandler>();
        builder.Services.AddScoped<IQueryHandler<WhoAmIQuery, string>, WhoAmIHandler>();
        builder.Services.AddScoped<ICommandHandler<FailCommand, string>, FailHandler>();
        builder.Services.AddScoped<CurrentAdviser>();
        builder.Services.AddScoped<ICurrentAdviser>(sp => sp.GetRequiredService<CurrentAdviser>());
        builder.Services.AddSingleton<IAdviserDirectory, FakeAdviserDirectory>();
        configure?.Invoke(builder.Services);
        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        return app.GetTestClient();
    }
}
