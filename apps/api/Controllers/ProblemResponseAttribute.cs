using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>Declares a failure status that is answered as <c>application/problem+json</c> with a <see cref="ProblemDetails"/> body.</summary>
public sealed class ProblemResponseAttribute(int statusCode)
    : ProducesResponseTypeAttribute(typeof(ProblemDetails), statusCode, "application/problem+json");
