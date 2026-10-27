using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using Sentry;
using Sentry.AspNetCore;
using Sentry.Extensibility;
using Sentry.Protocol.Envelopes;

namespace Api.Tests;

public class SentryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Secret = "s3cret-value";

    private sealed class RecordingTransport : ITransport
    {
        public List<string> Envelopes { get; } = [];

        public async Task SendEnvelopeAsync(Envelope envelope, CancellationToken cancellationToken = default)
        {
            using var stream = new MemoryStream();
            await envelope.SerializeAsync(stream, null, cancellationToken);
            lock (Envelopes)
            {
                Envelopes.Add(Encoding.UTF8.GetString(stream.ToArray()));
            }
        }
    }

    private WebApplicationFactory<Program> WithSentry(RecordingTransport transport, bool smoke = true) =>
        factory.WithWebHostBuilder(builder =>
        {
            // UseSetting, not ConfigureAppConfiguration: Program.cs decides on the DSN while it builds, before the latter applies.
            builder.UseSetting("Sentry:Dsn", "https://key@o1.ingest.us.sentry.io/2");
            builder.UseSetting("Sentry:Release", "abc1234");
            builder.UseSetting("Sentry:Environment", "demo");
            builder.UseSetting("SENTRY_SMOKE", smoke ? "1" : null);
            builder.ConfigureServices(services => services.Configure<SentryAspNetCoreOptions>(options => options.Transport = transport));
        });

    [Fact]
    public void WithoutADsnTheSdkIsNeverInitialised()
    {
        using var host = factory.WithWebHostBuilder(_ => { });

        _ = host.Server;

        Assert.Null(host.Services.GetService<IHub>());
    }

    [Fact]
    public async Task WithoutTheSmokeSettingThePathIsNotFoundAndNothingIsSent()
    {
        var transport = new RecordingTransport();
        using var host = WithSentry(transport, smoke: false);
        using var client = host.CreateClient();

        var response = await client.PostAsync(new Uri("/smoke/sentry", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(transport.Envelopes);
    }

    [Fact]
    public async Task AnUnhandledErrorIsSentWithTheReleaseAndEnvironmentAndNothingFromTheRequest()
    {
        var transport = new RecordingTransport();
        using var host = WithSentry(transport);
        using var client = host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/smoke/sentry?token={Secret}")
        {
            Content = new StringContent($$"""{"clientName":"{{Secret}}"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Authorization", $"Bearer {Secret}");
        request.Headers.Add("Cookie", $"session={Secret}");

        var response = await client.SendAsync(request);
        await SentrySdk.FlushAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var sent = string.Concat(transport.Envelopes);
        Assert.Contains("Sentry smoke test (api)", sent, StringComparison.Ordinal);
        Assert.Contains("abc1234", sent, StringComparison.Ordinal);
        Assert.Contains("demo", sent, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, sent, StringComparison.Ordinal);
    }

    [Fact]
    public void ScrubRemovesCookiesHeadersBodyQueryStringAndUser()
    {
        var sentryEvent = new SentryEvent
        {
            Request = new SentryRequest
            {
                Url = $"http://localhost/v1/commands/save-quote?token={Secret}",
                Cookies = $"session={Secret}",
                QueryString = $"token={Secret}",
                Data = Secret,
                Headers = { ["Authorization"] = $"Bearer {Secret}", ["Cookie"] = $"session={Secret}" },
            },
            User = new SentryUser { Id = "ADV-001", IpAddress = "10.0.0.1" },
        };

        var scrubbed = Api.Telemetry.SentryRegistration.Scrub(sentryEvent);

        Assert.Null(scrubbed.Request.Cookies);
        Assert.Empty(scrubbed.Request.Headers);
        Assert.Null(scrubbed.Request.Data);
        Assert.Null(scrubbed.Request.QueryString);
        Assert.Equal("http://localhost/v1/commands/save-quote", scrubbed.Request.Url);
        Assert.Null(scrubbed.User.Id);
        Assert.Null(scrubbed.User.IpAddress);
    }
}
