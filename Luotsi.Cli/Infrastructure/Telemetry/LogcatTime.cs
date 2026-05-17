using System.Globalization;

namespace Luotsi.Cli.Infrastructure.Telemetry;

internal static class LogcatTime
{
    public static string FormatSince(DateTimeOffset value) => value.ToLocalTime().ToString("MM-dd HH':'mm':'ss.fff", CultureInfo.InvariantCulture);
}