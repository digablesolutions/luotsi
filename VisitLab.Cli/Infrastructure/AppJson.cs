using System.Text.Json;

namespace VisitLab.Cli.Infrastructure;

/// <summary>
/// Application JSON settings.
/// </summary>
public static class AppJson
{
    /// <summary>
    /// Shared serializer options.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}