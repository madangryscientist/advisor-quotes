using System.Diagnostics;
using Api.Cqrs;
using Npgsql;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Api.Telemetry;

public static class TracingRegistration
{
    private const string TraceIdHeader = "X-Trace-Id";
    private const string HttpRouteTag = "http.route";

    /// <summary>
    /// Traces ASP.NET Core requests, Npgsql commands, and every command and query handler (source <c>Quotes</c>). Spans and log records are exported over OTLP only when
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set; the exporter reads the endpoint itself. Without it the spans are
    /// still made (so <see cref="UseTraceIdHeader"/> has a trace id) but go nowhere.
    /// </summary>
    public static IServiceCollection AddTracing(this IServiceCollection services, IConfiguration configuration)
    {
        var exportSpans = !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        var serviceName = configuration["OTEL_SERVICE_NAME"] ?? "advisor-quotes-api";
        var telemetry = services.AddOpenTelemetry();
        telemetry.WithTracing(tracing =>
        {
            tracing
                .ConfigureResource(resource => resource.AddService(serviceName))
                .AddSource(QuoteTelemetry.SourceName)
                .AddAspNetCoreInstrumentation(options => options.EnrichWithHttpResponse = NameRouteWithItsVersion)
                .AddNpgsql();
            if (exportSpans)
            {
                tracing.AddOtlpExporter();
            }
        });
        if (exportSpans)
        {
            // The same endpoint takes the log records, which carry the trace and span id, so the dashboard links a log line to its trace.
            // IncludeFormattedMessage: without it the dashboard shows the message template, not the rendered line.
            telemetry.WithLogging(
                logging => logging.ConfigureResource(resource => resource.AddService(serviceName)).AddOtlpExporter(),
                options => options.IncludeFormattedMessage = true);
        }

        return services;
    }

    /// <summary>
    /// The instrumentation names a request span <c>POST v{version:apiVersion}/commands/save-quote</c>, after the route
    /// template. It runs this after naming it, so the span name and <c>http.route</c> read the version the request
    /// used: <c>POST v1/commands/save-quote</c>. A version is one of a few values, so the route still groups.
    /// </summary>
    internal static void NameRouteWithItsVersion(Activity activity, HttpResponse response)
    {
        if (response.HttpContext.GetRouteValue("version") is not string version)
        {
            return;
        }

        activity.DisplayName = activity.DisplayName.Replace(ApiRoutes.Prefix, $"v{version}", StringComparison.Ordinal);
        if (activity.GetTagItem(HttpRouteTag) is string route)
        {
            activity.SetTag(HttpRouteTag, route.Replace(ApiRoutes.Prefix, $"v{version}", StringComparison.Ordinal));
        }
    }

    /// <summary>Puts the current trace id on every response as <c>X-Trace-Id</c>, so a failing call can be found in the trace backend.</summary>
    public static IApplicationBuilder UseTraceIdHeader(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                if (Activity.Current is { } activity)
                {
                    context.Response.Headers[TraceIdHeader] = activity.TraceId.ToString();
                }

                return Task.CompletedTask;
            });
            return next(context);
        });
}
