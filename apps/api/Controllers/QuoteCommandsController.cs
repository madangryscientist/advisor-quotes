using Api.Advisers.Services;
using Api.Cqrs;
using Api.Quotes.Commands;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[RequireAdviser]
[ProblemResponse(StatusCodes.Status401Unauthorized)]
[ProblemResponse(StatusCodes.Status403Forbidden)]
[Route(ApiRoutes.Commands)]
public sealed class QuoteCommandsController(
    CurrentAdviser adviser,
    ICommandHandler<SaveQuoteCommand, SaveQuoteResult> saveQuote,
    ICommandHandler<SendQuoteToClientCommand, QuoteStatusResult> sendQuoteToClient,
    ICommandHandler<AcceptQuoteCommand, QuoteStatusResult> acceptQuote,
    ICommandHandler<DeclineQuoteCommand, QuoteStatusResult> declineQuote) : ApiControllerBase(adviser)
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";

    [HttpPost("save-quote", Name = "saveQuote")]
    [EndpointSummary("Create a quote, or edit a draft when the command carries its id")]
    [ProducesResponseType<SaveQuoteResult>(StatusCodes.Status200OK, "application/json")]
    [Idempotent]
    [ProblemResponse(StatusCodes.Status404NotFound)]
    [ProblemResponse(StatusCodes.Status409Conflict)]
    [ProblemResponse(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SaveQuoteResult>> SaveQuote(
        SaveQuoteCommand command,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await RunCommand(saveQuote, command with { IdempotencyKey = idempotencyKey }, cancellationToken);
        if (result.Value is { Replayed: true })
        {
            Response.Headers[ReplayedHeader] = "true";
        }

        return result;
    }

    [HttpPost("send-quote-to-client", Name = "sendQuoteToClient")]
    [EndpointSummary("Send a draft quote to the client")]
    [ProducesResponseType<QuoteStatusResult>(StatusCodes.Status200OK, "application/json")]
    [ProblemResponse(StatusCodes.Status404NotFound)]
    [ProblemResponse(StatusCodes.Status409Conflict)]
    public Task<ActionResult<QuoteStatusResult>> SendQuoteToClient(SendQuoteToClientCommand command, CancellationToken cancellationToken) =>
        RunCommand(sendQuoteToClient, command, cancellationToken);

    [HttpPost("accept-quote", Name = "acceptQuote")]
    [EndpointSummary("Record that the client accepted a quote")]
    [ProducesResponseType<QuoteStatusResult>(StatusCodes.Status200OK, "application/json")]
    [ProblemResponse(StatusCodes.Status404NotFound)]
    [ProblemResponse(StatusCodes.Status409Conflict)]
    public Task<ActionResult<QuoteStatusResult>> AcceptQuote(AcceptQuoteCommand command, CancellationToken cancellationToken) =>
        RunCommand(acceptQuote, command, cancellationToken);

    [HttpPost("decline-quote", Name = "declineQuote")]
    [EndpointSummary("Record that the client declined a quote; a reason is required")]
    [ProducesResponseType<QuoteStatusResult>(StatusCodes.Status200OK, "application/json")]
    [ProblemResponse(StatusCodes.Status404NotFound)]
    [ProblemResponse(StatusCodes.Status409Conflict)]
    public Task<ActionResult<QuoteStatusResult>> DeclineQuote(DeclineQuoteCommand command, CancellationToken cancellationToken) =>
        RunCommand(declineQuote, command, cancellationToken);
}
