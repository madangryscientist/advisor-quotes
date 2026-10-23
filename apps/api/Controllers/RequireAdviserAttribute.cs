using Api.Advisers.Services;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Api.Controllers;

/// <summary>Resolves the adviser from <c>X-Adviser-Id</c> before the action runs, or answers 401 or 403 without running it.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireAdviserAttribute : ActionFilterAttribute
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var adviser = http.RequestServices.GetRequiredService<CurrentAdviser>();
        if (await adviser.ResolveAsync(http.Request.Headers[CurrentAdviser.HeaderName], http.RequestAborted) is { } denied)
        {
            context.Result = ((ApiControllerBase)context.Controller).ToProblem(denied);
            return;
        }

        await next();
    }
}
