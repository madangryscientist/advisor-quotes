using System.Diagnostics;
using Api.Advisers.Services;
using Api.Cqrs;
using Api.Quotes.Commands;
using Api.Quotes.Queries;

namespace Api.Telemetry;

/// <summary>
/// The span around a handler and the one place that decides which attributes it carries. Only ids and codes: never a client's
/// name or date of birth. A new command or query that should show its quote id or status adds a case to <see cref="TagRequest"/>
/// or <see cref="TagValue"/>. The status a status command moves from is set by <c>QuoteStatusChanger</c>.
/// </summary>
public static class HandlerSpan
{
    public static async Task<Result<T>> RunAsync<T>(string kind, object request, CurrentAdviser adviser, Func<Task<Result<T>>> handle)
    {
        using var span = QuoteTelemetry.Source.StartActivity($"{kind} {request.GetType().Name.Replace(kind, "", StringComparison.OrdinalIgnoreCase)}");
        span?.SetTag("adviser.id", adviser.ResolvedId);
        TagRequest(span, request);
        try
        {
            var result = await handle();
            span?.SetTag("outcome", Outcome(result.Failure));
            TagValue(span, result.Value);
            return result;
        }
        catch (Exception)
        {
            span?.SetTag("outcome", "error");
            span?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
    }

    private static string Outcome(Failure? failure) => failure?.Kind switch
    {
        null => "ok",
        FailureKind.Validation => "validation",
        FailureKind.Unauthorized => "unauthorized",
        FailureKind.Forbidden => "forbidden",
        FailureKind.NotFound => "not_found",
        FailureKind.Conflict => "conflict",
        FailureKind.IdempotencyMismatch => "idempotency_mismatch",
        _ => "error",
    };

    private static void TagRequest(Activity? span, object request)
    {
        Guid? quoteId = request switch
        {
            SaveQuoteCommand save => save.QuoteId,
            SendQuoteToClientCommand send => send.QuoteId,
            AcceptQuoteCommand accept => accept.QuoteId,
            DeclineQuoteCommand decline => decline.QuoteId,
            GetQuoteQuery get => get.QuoteId,
            _ => null,
        };
        span?.SetTag("quote.id", quoteId);
        if (request is SaveQuoteCommand command)
        {
            span?.SetTag("quote.save.mode", command.QuoteId is null ? "create" : "update");
            span?.SetTag("product.code", command.ProductCode?.ToUpperInvariant());
        }
    }

    private static void TagValue(Activity? span, object? value)
    {
        switch (value)
        {
            case SaveQuoteResult save:
                span?.SetTag("quote.id", save.QuoteId);
                span?.SetTag("quote.status.to", save.Status.ToString());
                span?.SetTag("idempotency.replayed", save.Replayed);
                break;
            case QuoteStatusResult status:
                span?.SetTag("quote.status.to", status.Status.ToString());
                break;
        }
    }
}
