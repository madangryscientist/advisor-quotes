using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Telemetry;
using Api.Tests.Cqrs;
using OpenTelemetry.Trace;

namespace Api.Tests.Telemetry;

public sealed class HandlerTracingTests
{
    private const string ClientName = "Thandi Nkosi";
    private const string ClientDateOfBirth = "1985-06-15";

    private readonly List<Activity> exported = [];
    private readonly HttpClient client;

    public HandlerTracingTests()
    {
        // Its own factory: the Quotes source is process-wide, but each host's exporter list only gets what its provider sampled.
        client = new ApiFactory()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(exported))))
            .CreateClient();
        client.DefaultRequestHeaders.Add("X-Adviser-Id", "ADV-001");
    }

    [Fact]
    public async Task SaveThenAcceptIsOneTraceFromHttpThroughTheCommandToPrice()
    {
        using var save = await Post("save-quote", new
        {
            clientName = ClientName,
            clientDateOfBirth = ClientDateOfBirth,
            clientIsSmoker = false,
            productCode = "life",
            coverAmount = 1_000_000,
            termYears = 20,
        }, idempotencyKey: "trace-1");
        var quoteId = (await save.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quoteId").GetGuid().ToString();
        using var send = await Post("send-quote-to-client", new { quoteId });
        using var accept = await Post("accept-quote", new { quoteId, reason = "signed" });

        var saveTrace = await TraceOf(save);
        var saveCommand = Assert.Single(saveTrace, a => a.DisplayName == "command SaveQuote");
        var price = Assert.Single(saveTrace, a => a.DisplayName == "price");
        var saveServer = Assert.Single(saveTrace, a => a.Kind == ActivityKind.Server);
        Assert.Equal(saveServer.SpanId, saveCommand.ParentSpanId);
        // The version the request used, not the route template's placeholder.
        Assert.Equal("POST v1/commands/save-quote", saveServer.DisplayName);
        Assert.Equal("v1/commands/save-quote", Tag(saveServer, "http.route"));
        Assert.Equal(saveCommand.SpanId, price.ParentSpanId);
        Assert.Equal("create", Tag(saveCommand, "quote.save.mode"));
        Assert.Equal("ok", Tag(saveCommand, "outcome"));
        Assert.Equal("ADV-001", Tag(saveCommand, "adviser.id"));
        Assert.Equal("LIFE", Tag(saveCommand, "product.code"));
        Assert.Equal(quoteId, Tag(saveCommand, "quote.id"));
        Assert.Equal("Draft", Tag(saveCommand, "quote.status.to"));
        Assert.Equal("False", Tag(saveCommand, "idempotency.replayed"));

        var acceptTrace = await TraceOf(accept);
        var acceptCommand = Assert.Single(acceptTrace, a => a.DisplayName == "command AcceptQuote");
        Assert.Equal(Assert.Single(acceptTrace, a => a.Kind == ActivityKind.Server).SpanId, acceptCommand.ParentSpanId);
        Assert.Equal("ok", Tag(acceptCommand, "outcome"));
        Assert.Equal(quoteId, Tag(acceptCommand, "quote.id"));
        Assert.Equal("SentToClient", Tag(acceptCommand, "quote.status.from"));
        Assert.Equal("Accepted", Tag(acceptCommand, "quote.status.to"));

        // Client details travel in the body, so they must appear in no attribute of any span.
        var everything = (await TraceOf(save)).Concat(await TraceOf(send)).Concat(acceptTrace)
            .SelectMany(a => a.TagObjects.Select(t => $"{t.Key}={t.Value}"));
        Assert.DoesNotContain(everything, tag => tag.Contains(ClientName, StringComparison.Ordinal) || tag.Contains(ClientDateOfBirth, StringComparison.Ordinal) || tag.StartsWith("client", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OutcomesAndReplaysAreAttributes()
    {
        var body = new { clientName = ClientName, clientDateOfBirth = ClientDateOfBirth, clientIsSmoker = false, productCode = "LIFE", coverAmount = 1_000_000, termYears = 20 };
        using var first = await Post("save-quote", body, idempotencyKey: "replay-1");
        using var replay = await Post("save-quote", body, idempotencyKey: "replay-1");
        using var invalid = await Post("save-quote", new { clientName = "" }, idempotencyKey: "invalid-1");
        using var missing = await Post("accept-quote", new { quoteId = Guid.NewGuid() });

        Assert.Equal("True", Tag(Assert.Single(await TraceOf(replay), a => a.DisplayName == "command SaveQuote"), "idempotency.replayed"));
        Assert.Equal("validation", Tag(Assert.Single(await TraceOf(invalid), a => a.DisplayName == "command SaveQuote"), "outcome"));
        Assert.Equal("not_found", Tag(Assert.Single(await TraceOf(missing), a => a.DisplayName == "command AcceptQuote"), "outcome"));
        Assert.DoesNotContain(await TraceOf(invalid), a => a.DisplayName == "price");
    }

    private async Task<HttpResponseMessage> Post(string command, object body, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/commands/{command}", UriKind.Relative)) { Content = JsonContent.Create(body) };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request);
    }

    /// <summary>The spans of the response's trace. The server span ends after the response is sent, so wait for it to be exported.</summary>
    private Task<Activity[]> TraceOf(HttpResponseMessage response)
    {
        var traceId = response.Headers.GetValues("X-Trace-Id").Single();
        SpinWait.SpinUntil(() => exported.ToArray().Any(a => a.TraceId.ToString() == traceId && a.Kind == ActivityKind.Server), TimeSpan.FromSeconds(5));
        return Task.FromResult<Activity[]>([.. exported.ToArray().Where(a => a.TraceId.ToString() == traceId)]);
    }

    private static string? Tag(Activity span, string key) => span.GetTagItem(key)?.ToString();
}

public sealed class QueryTracingTests
{
    [Fact]
    public async Task AQueryRunsInAQuerySpan()
    {
        // The listener is process-wide and test classes run in parallel, so keep only the spans of this test's own trace.
        using var parent = new Activity("test").Start();
        var started = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == QuoteTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = span =>
            {
                if (span.TraceId == parent.TraceId)
                {
                    started.Add(span);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        using var client = await CqrsHost.Start();

        using var response = await client.GetAsync(new Uri("/v1/queries/echo?text=hi", UriKind.Relative));

        var span = Assert.Single(started, a => a.DisplayName == "query Echo");
        Assert.Equal("ok", span.GetTagItem("outcome"));
    }
}
