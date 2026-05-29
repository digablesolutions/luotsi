using Luotsi.Cli.Errors;
using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.Cli.View;

internal static class ViewCommandOptionsFactory
{
    public static ViewOptions Build(CliOptions options, string adbExecutable, bool allowJoinShare, TimeSpan? commandTimeout, string commandName = "view")
    {
        var joinShareEndpoint = options.Get("join-share");
        if (!allowJoinShare && !string.IsNullOrWhiteSpace(joinShareEndpoint))
        {
            throw new UsageException($"{commandName} does not support --join-share.");
        }

        var device = options.Get("device");
        if (!allowJoinShare || string.IsNullOrWhiteSpace(joinShareEndpoint))
        {
            if (string.IsNullOrWhiteSpace(device))
            {
                throw new UsageException(allowJoinShare
                    ? $"{commandName} requires --device <adb serial> or --join-share <host:port>."
                    : $"{commandName} requires --device <adb serial>.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(device))
        {
            throw new UsageException($"{commandName} requires either --device or --join-share, not both.");
        }

        var preset = ViewPresetCatalog.Resolve(options.HasFlag("defaults") ? ViewPresetCatalog.Safe : options.Get("preset"));
        var scaleMode = ResolveScaleMode(options.Get("scale-mode"), commandName);
        var statsIntervalMs = GetIntOrDefault(options, "stats-interval-ms", preset.StatsIntervalMs);
        var rendererStatsIntervalMs = GetIntOrDefault(options, "renderer-stats-interval-ms", preset.RendererStatsIntervalMs);
        if (statsIntervalMs < 0)
        {
            throw new UsageException($"{commandName} requires --stats-interval-ms zero or greater.");
        }

        if (rendererStatsIntervalMs < 0)
        {
            throw new UsageException($"{commandName} requires --renderer-stats-interval-ms zero or greater.");
        }

        var captureBackend = ResolveCaptureBackend(options.Get("capture-backend"), commandName);
        var consoleOutput = ResolveConsoleOutput(options.Get("output") ?? options.Get("o"), options.HasFlag("json"), options.HasFlag("quiet"), commandName);

        return new ViewOptions(
            device ?? joinShareEndpoint ?? string.Empty,
            adbExecutable,
            options.Get("codec") ?? CliDefaults.DefaultViewCodec,
            options.Get("decoder") ?? CliDefaults.DefaultViewDecoder,
            options.HasFlag("headless"),
            options.Get("record"),
            GetIntOrDefault(options, "max-size", preset.MaxSize),
            GetIntOrDefault(options, "max-fps", preset.MaxFps),
            options.Get("video-bit-rate") ?? preset.VideoBitRate,
            options.HasFlag("overlay-screen-state"),
            options.HasFlag("overlay-telemetry"),
            statsIntervalMs,
            rendererStatsIntervalMs,
            preset.Name,
            options.HasFlag("read-only") || !string.IsNullOrWhiteSpace(joinShareEndpoint),
            options.Get("share-bind"),
            joinShareEndpoint,
            options.HasFlag("always-on-top"),
            scaleMode,
            captureBackend,
            commandTimeout,
            consoleOutput);
    }

    private static int GetIntOrDefault(CliOptions options, string key, int defaultValue) =>
        options.Get(key) is null ? defaultValue : options.Int(key, defaultValue);

    private static string ResolveScaleMode(string? value, string commandName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "fit";
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "fit" => "fit",
            "fill" => "fill",
            _ => throw new UsageException($"{commandName} requires --scale-mode to be either fit or fill.")
        };
    }

    private static string ResolveCaptureBackend(string? value, string commandName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ViewCaptureBackends.Auto;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            ViewCaptureBackends.Auto => ViewCaptureBackends.Auto,
            ViewCaptureBackends.Screenrecord => ViewCaptureBackends.Screenrecord,
            ViewCaptureBackends.MediaProjection => ViewCaptureBackends.MediaProjection,
            _ => throw new UsageException($"{commandName} requires --capture-backend to be auto, screenrecord, or mediaprojection.")
        };
    }

    private static string ResolveConsoleOutput(string? value, bool json, bool quiet, string commandName)
    {
        if (quiet)
        {
            return ViewConsoleOutputModes.Quiet;
        }

        if (json)
        {
            return ViewConsoleOutputModes.Jsonl;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return ViewConsoleOutputModes.Human;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            ViewConsoleOutputModes.Human => ViewConsoleOutputModes.Human,
            ViewConsoleOutputModes.Json => ViewConsoleOutputModes.Jsonl,
            ViewConsoleOutputModes.Jsonl => ViewConsoleOutputModes.Jsonl,
            _ => throw new UsageException($"{commandName} requires --output to be human, json, or jsonl.")
        };
    }
}
