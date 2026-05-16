using System.Globalization;

namespace VisitLab.Cli.Infrastructure;

internal static class LogcatTime
{
    public static string FormatSince(DateTimeOffset value) => value.ToLocalTime().ToString("MM-dd HH':'mm':'ss.fff", CultureInfo.InvariantCulture);
}