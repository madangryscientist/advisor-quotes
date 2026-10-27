using Sentry;
using Sentry.AspNetCore;

namespace Api.Telemetry;

public static class SentryRegistration
{
    private const string SmokeSetting = "SENTRY_SMOKE";

    /// <summary>
    /// Reports unhandled errors to Sentry, errors only (no tracing, profiling or logs, and no OpenTelemetry integration, so
    /// <see cref="TracingRegistration"/> is untouched). Off unless <c>Sentry:Dsn</c> (env <c>Sentry__Dsn</c>) is set: without it
    /// nothing is registered, the SDK is never initialised and nothing is sent. <c>Sentry:Release</c> and <c>Sentry:Environment</c>
    /// are bound by the SDK from the same section.
    /// </summary>
    public static WebApplicationBuilder AddSentryReporting(this WebApplicationBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(builder.Configuration["Sentry:Dsn"]))
        {
            return builder;
        }

        builder.WebHost.UseSentry(options =>
        {
            options.MaxBreadcrumbs = 0;
            options.SendDefaultPii = false;
            // Only what the request pipeline lets out. Without this every logged error (a failed readiness probe) is an event too.
            options.MinimumEventLevel = LogLevel.None;
            options.SetBeforeSend((sentryEvent, _) => Scrub(sentryEvent));
        });
        return builder;
    }

    /// <summary>Nothing about a request leaves with an error: no cookies, headers, body, query string or user.</summary>
    public static SentryEvent Scrub(SentryEvent sentryEvent)
    {
        sentryEvent.Request.Cookies = null;
        sentryEvent.Request.Headers.Clear();
        sentryEvent.Request.Data = null;
        sentryEvent.Request.QueryString = null;
        sentryEvent.Request.Url = sentryEvent.Request.Url?.Split('?', '#')[0];
        sentryEvent.User = new SentryUser();
        return sentryEvent;
    }

    /// <summary>
    /// The verification affordance: <c>POST /smoke/sentry</c> throws, but only while <c>SENTRY_SMOKE=1</c> is set in the
    /// environment. Off by default, so on a normal deploy the path is a 404.
    /// </summary>
    public static void MapSentrySmoke(this WebApplication app)
    {
        if (app.Configuration[SmokeSetting] == "1")
        {
            app.MapPost("/smoke/sentry", () => { throw new InvalidOperationException("Sentry smoke test (api)"); });
        }
    }
}
