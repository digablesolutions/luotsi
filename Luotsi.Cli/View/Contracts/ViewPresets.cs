using Luotsi.Cli.Errors;

namespace Luotsi.Cli.View;

/// <summary>
/// Launch preset used to seed view session defaults.
/// </summary>
/// <param name="Name">Preset name.</param>
/// <param name="MaxSize">Default maximum mirrored size.</param>
/// <param name="MaxFps">Default maximum frame rate.</param>
/// <param name="VideoBitRate">Default stream bit rate.</param>
/// <param name="StatsIntervalMs">Default JSONL stats cadence.</param>
/// <param name="RendererStatsIntervalMs">Default renderer stats cadence.</param>
public sealed record ViewPreset(
    string Name,
    int MaxSize,
    int MaxFps,
    string VideoBitRate,
    int StatsIntervalMs,
    int RendererStatsIntervalMs);

/// <summary>
/// Built-in view launch presets.
/// </summary>
public static class ViewPresetCatalog
{
    public const string Safe = "safe";
    public const string Balanced = "balanced";
    public const string HighQuality = "high-quality";
    public const string LowLatency = "low-latency";

    private static readonly IReadOnlyDictionary<string, ViewPreset> Presets = new Dictionary<string, ViewPreset>(StringComparer.OrdinalIgnoreCase)
    {
        [Safe] = new ViewPreset(Safe, 1280, 30, "4M", 1000, 250),
        [Balanced] = new ViewPreset(Balanced, 1600, 60, "8M", 1000, 0),
        [HighQuality] = new ViewPreset(HighQuality, 1920, 60, "12M", 1000, 0),
        ["quality"] = new ViewPreset(HighQuality, 1920, 60, "12M", 1000, 0),
        [LowLatency] = new ViewPreset(LowLatency, 1280, 60, "6M", 250, 0)
    };

    /// <summary>
    /// Resolves a built-in preset.
    /// </summary>
    /// <param name="presetName">Optional preset name.</param>
    /// <returns>Resolved preset.</returns>
    public static ViewPreset Resolve(string? presetName)
    {
        var normalizedName = string.IsNullOrWhiteSpace(presetName) ? Balanced : presetName;
        if (Presets.TryGetValue(normalizedName, out var preset))
        {
            return preset;
        }

        throw new UsageException($"Unknown view preset '{presetName}'. Supported values: {Safe}, {Balanced}, {HighQuality}, {LowLatency}.");
    }
}
