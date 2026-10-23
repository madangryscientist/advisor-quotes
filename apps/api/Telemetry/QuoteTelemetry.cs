using System.Diagnostics;

namespace Api.Telemetry;

public static class QuoteTelemetry
{
    public const string SourceName = "Quotes";

    public static readonly ActivitySource Source = new(SourceName);
}
