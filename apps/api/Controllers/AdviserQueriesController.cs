using Api.Advisers.Queries;
using Api.Advisers.Services;
using Api.Cqrs;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>No <c>X-Adviser-Id</c> here: the UI picker needs the list before anyone is chosen.</summary>
[Route(ApiRoutes.Queries)]
public sealed class AdviserQueriesController(
    CurrentAdviser adviser,
    IQueryHandler<ListAdvisersQuery, IReadOnlyList<AdviserSummary>> listAdvisers) : ApiControllerBase(adviser)
{
    [HttpGet("list-advisers", Name = "listAdvisers")]
    [EndpointSummary("List the advisers the UI can act as")]
    [ProducesResponseType<IReadOnlyList<AdviserSummary>>(StatusCodes.Status200OK, "application/json")]
    public Task<ActionResult<IReadOnlyList<AdviserSummary>>> ListAdvisers(CancellationToken cancellationToken) =>
        RunQuery(listAdvisers, new ListAdvisersQuery(), cancellationToken);
}
