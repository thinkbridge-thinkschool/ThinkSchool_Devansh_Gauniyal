using System.Text.RegularExpressions;

namespace FastApi.Tests;

// Parses a line like "  50%    28.00ms" from bombardier's -l "Latency Distribution"
// block and normalizes to milliseconds, regardless of which unit bombardier chose to
// print for that magnitude (µs, ms or s).
public static class LatencyPercentileParser
{
    public static double? ParsePercentileMilliseconds(string text, string percentileLabel)
    {
        var pattern = $@"(?m)^\s*{Regex.Escape(percentileLabel)}\s+([\d.]+)\s*(ms|µs|us|s)\b";
        var match = Regex.Match(text, pattern);
        if (!match.Success)
        {
            return null;
        }

        var value = double.Parse(match.Groups[1].Value);
        var unit = match.Groups[2].Value;

        return unit switch
        {
            "s" => value * 1000.0,
            "ms" => value,
            "µs" or "us" => value / 1000.0,
            _ => value
        };
    }

    // Extracts "-c 20" and "-d 10s" style flags from a bombardier command line.
    public static string? ExtractFlag(string text, string flag)
    {
        var match = Regex.Match(text, $@"{Regex.Escape(flag)}\s+(\S+)");
        return match.Success ? match.Groups[1].Value : null;
    }
}
