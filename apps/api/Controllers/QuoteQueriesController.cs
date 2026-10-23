using Api.Advisers.Services;
using Api.Cqrs;
using Api.Quotes.Domain;
using Api.Quotes.Queries;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Api.Controllers;

[RequireAdviser]
[ProblemResponse(StatusCodes.Status401Unauthorized)]
[ProblemResponse(StatusCodes.Status403Forbidden)]
[Route(ApiRoutes.Queries)]
public sealed class QuoteQueriesController(
    CurrentAdviser adviser,
    IQueryHandler<GetQuoteQuery, QuoteDetails> getQuote,
    IQueryHandler<ListQuotesQuery, IReadOnlyList<QuoteSummary>> listQuotes) : ApiControllerBase(adviser)
{
    [HttpGet("get-quote", Name = "getQuote")]
    [EndpointSummary("Get one quote with its status history")]
    [ProducesResponseType<QuoteDetails>(StatusCodes.Status200OK, "application/json")]
    [ProblemResponse(StatusCodes.Status404NotFound)]
    public Task<ActionResult<QuoteDetails>> GetQuote([FromQuery(Name = "QuoteId"), BindRequired] Guid quoteId, CancellationToken cancellationToken) =>
        RunQuery(getQuote, new GetQuoteQuery(quoteId), cancellationToken);

    [HttpGet("list-quotes", Name = "listQuotes")]
    [EndpointSummary("List the current adviser's quotes")]
    [ProducesResponseType<IReadOnlyList<QuoteSummary>>(StatusCodes.Status200OK, "application/json")]
    public Task<ActionResult<IReadOnlyList<QuoteSummary>>> ListQuotes([FromQuery(Name = "Status")] QuoteStatus? status, CancellationToken cancellationToken) =>
        RunQuery(listQuotes, new ListQuotesQuery(status), cancellationToken);
}
