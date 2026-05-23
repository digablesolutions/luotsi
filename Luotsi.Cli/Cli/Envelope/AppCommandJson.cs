using System.Text.Json;
using System.Text.Json.Serialization;

namespace Luotsi.Cli.Cli.Envelope;

internal static class AppCommandJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}