using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure;
using Luotsi.Cli.View;

namespace Luotsi.Cli.Cli;

public sealed record ViewProfile(
    string? Device = null,
    string? Adb = null,
    string? Codec = null,
    string? Decoder = null,
    string? Preset = null,
    bool? Headless = null,
    string? Record = null,
    int? MaxSize = null,
    int? MaxFps = null,
    string? VideoBitRate = null,
    bool? OverlayScreenState = null,
    bool? OverlayTelemetry = null,
    int? StatsIntervalMs = null,
    int? RendererStatsIntervalMs = null,
    bool? ReadOnly = null,
    string? ShareBind = null,
    string? JoinShare = null,
    string? Artifacts = null,
    string? PollArtifacts = null)
{
    public IReadOnlyDictionary<string, string?> ToOptionDefaults()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Add(values, "device", Device);
        Add(values, "adb", Adb);
        Add(values, "codec", Codec);
        Add(values, "decoder", Decoder);
        Add(values, "preset", Preset);
        Add(values, "headless", Headless);
        Add(values, "record", Record);
        Add(values, "max-size", MaxSize);
        Add(values, "max-fps", MaxFps);
        Add(values, "video-bit-rate", VideoBitRate);
        Add(values, "overlay-screen-state", OverlayScreenState);
        Add(values, "overlay-telemetry", OverlayTelemetry);
        Add(values, "stats-interval-ms", StatsIntervalMs);
        Add(values, "renderer-stats-interval-ms", RendererStatsIntervalMs);
        Add(values, "read-only", ReadOnly);
        Add(values, "share-bind", ShareBind);
        Add(values, "join-share", JoinShare);
        Add(values, "artifacts", Artifacts);
        Add(values, "poll-artifacts", PollArtifacts);
        return values;
    }

    public static ViewProfile FromResolvedOptions(CliOptions options, ViewOptions viewOptions) => new(
        string.IsNullOrWhiteSpace(viewOptions.JoinShareEndpoint) ? viewOptions.DeviceSelector : null,
        viewOptions.AdbExecutable,
        viewOptions.Codec,
        viewOptions.Decoder,
        viewOptions.PresetName,
        viewOptions.Headless,
        viewOptions.RecordPath,
        viewOptions.MaxSize,
        viewOptions.MaxFps,
        viewOptions.VideoBitRate,
        viewOptions.OverlayScreenState,
        viewOptions.OverlayTelemetry,
        viewOptions.StatsIntervalMs,
        viewOptions.RendererStatsIntervalMs,
        viewOptions.ReadOnly,
        viewOptions.ShareBindEndpoint,
        viewOptions.JoinShareEndpoint,
        options.Get("artifacts"),
        options.Get("poll-artifacts") ?? CliDefaults.DefaultPollArtifactsPolicy);

    private static void Add(Dictionary<string, string?> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[key] = value;
        }
    }

    private static void Add(Dictionary<string, string?> values, string key, int? value)
    {
        if (value.HasValue)
        {
            values[key] = value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static void Add(Dictionary<string, string?> values, string key, bool? value)
    {
        if (value is true)
        {
            values[key] = "true";
        }
    }
}

public interface IViewProfileStore
{
    Task<ViewProfile?> LoadAsync(string name, CancellationToken cancellationToken = default);
    Task SaveAsync(string name, ViewProfile profile, CancellationToken cancellationToken = default);
}

public sealed class JsonViewProfileStore(IFileSystem fileSystem, IEnvironmentVariables environment) : IViewProfileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public async Task<ViewProfile?> LoadAsync(string name, CancellationToken cancellationToken = default)
    {
        var path = GetProfilePath(name);
        if (!_fileSystem.FileExists(path))
        {
            return null;
        }

        var json = await _fileSystem.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<ViewProfile>(json, Options);
    }

    public async Task SaveAsync(string name, ViewProfile profile, CancellationToken cancellationToken = default)
    {
        var path = GetProfilePath(name);
        _fileSystem.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        await _fileSystem.WriteAllTextAsync(path, JsonSerializer.Serialize(profile, Options), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    private string GetProfilePath(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new UsageException("Profile name is required.");
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new UsageException($"Profile name '{name}' contains invalid file name characters.");
        }

        return Path.Combine(GetProfileRoot(), $"{name}.json");
    }

    private string GetProfileRoot()
    {
        var configuredRoot = _environment.GetEnvironmentVariable("LUOTSI_PROFILE_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            return configuredRoot;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return string.IsNullOrWhiteSpace(appData)
            ? Path.Combine(_fileSystem.GetTempPath(), "luotsi", "profiles")
            : Path.Combine(appData, "Luotsi", "profiles");
    }
}
