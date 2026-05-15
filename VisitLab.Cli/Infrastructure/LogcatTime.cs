using System.Globalization;

namespace VisitLab.Cli;

internal static class LogcatTime
{
    public static string FormatSince(DateTimeOffset value) => value.ToLocalTime().ToString("MM-dd HH':'mm':'ss.fff", CultureInfo.InvariantCulture);
}