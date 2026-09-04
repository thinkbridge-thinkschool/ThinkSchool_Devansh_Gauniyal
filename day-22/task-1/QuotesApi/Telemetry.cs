using System.Diagnostics;

namespace QuotesApi;

internal static class Telemetry
{
    public const string ServiceName = "QuotesApi";

    public static readonly ActivitySource Source = new(ServiceName);
}
