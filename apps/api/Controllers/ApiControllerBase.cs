using Api.Advisers.Services;
using Api.Cqrs;
using Api.Telemetry;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// What every controller shares: it is versioned, it runs a handler inside a trace span, and it turns a failed
/// <see cref="Result{T}"/> into one ProblemDetails status. A controller lists its handlers as constructor parameters.
/// </summary>
[ApiController]
[ApiVersion(ApiRoutes.Version)]
[Consumes("application/json")]
[ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
public abstract class ApiControllerBase(CurrentAdviser adviser) : ControllerBase
{
    protected async Task<ActionResult<TResult>> RunCommand<TCommand, TResult>(
        ICommandHandler<TCommand, TResult> handler, TCommand command, CancellationToken cancellationToken)
        where TCommand : notnull =>
        ToActionResult(await HandlerSpan.RunAsync("command", command, adviser, () => handler.Handle(command, cancellationToken)));

    protected async Task<ActionResult<TResult>> RunQuery<TQuery, TResult>(
        IQueryHandler<TQuery, TResult> handler, TQuery query, CancellationToken cancellationToken)
        where TQuery : notnull =>
        ToActionResult(await HandlerSpan.RunAsync("query", query, adviser, () => handler.Handle(query, cancellationToken)));

    /// <summary>A success keeps the value in <c>ActionResult&lt;T&gt;.Value</c> (still a 200), so an action can read it before it returns.</summary>
    protected ActionResult<T> ToActionResult<T>(Result<T> result)
    {
        if (result.Failure is { } failure)
        {
            return ToProblem(failure);
        }

        return result.Value!;
    }

    /// <summary>The one place a failure becomes a ProblemDetails response.</summary>
    internal ActionResult ToProblem(Failure failure) => failure.Kind switch
    {
        FailureKind.Validation => ValidationProblem(new ValidationProblemDetails(failure.Errors ?? new Dictionary<string, string[]>()) { Detail = failure.Detail }),
        FailureKind.Unauthorized => Problem(failure.Detail, statusCode: StatusCodes.Status401Unauthorized),
        FailureKind.Forbidden => Problem(failure.Detail, statusCode: StatusCodes.Status403Forbidden),
        FailureKind.NotFound => Problem(failure.Detail, statusCode: StatusCodes.Status404NotFound),
        FailureKind.Conflict => Problem(failure.Detail, statusCode: StatusCodes.Status409Conflict),
        FailureKind.IdempotencyMismatch => Problem(failure.Detail, statusCode: StatusCodes.Status422UnprocessableEntity),
        _ => throw new ArgumentOutOfRangeException(nameof(failure)),
    };
}
