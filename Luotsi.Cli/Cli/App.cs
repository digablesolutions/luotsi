using System.Text.Json;
using System.Text.Json.Serialization;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure;
using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;
using Luotsi.Cli.View;

namespace Luotsi.Cli.Cli;

/// <summary>
/// Entry point for the Luotsi command-line application.
/// </summary>
public sealed class App
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly TimeProvider _timeProvider;
    private readonly IFileSystem _fileSystem;
    private readonly IDelay _delay;
    private readonly IDeviceHostFactory _deviceHostFactory;
    private readonly IConsoleIo _console;
    private readonly IEnvironmentVariables _environment;
    private readonly IViewSessionFactory _viewSessionFactory;
    private readonly IViewDoctorFactory _viewDoctorFactory;

    public App(
        TimeProvider? timeProvider = null,
        IFileSystem? fileSystem = null,
        IProcessRunner? processRunner = null,
        IDelay? delay = null,
        IAdbClientFactory? adbClientFactory = null,
        IConsoleIo? console = null,
        IEnvironmentVariables? environment = null,
        IUniqueIdGenerator? idGenerator = null,
        IDeviceHostFactory? deviceHostFactory = null,
        IViewSessionFactory? viewSessionFactory = null,
        IViewDoctorFactory? viewDoctorFactory = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _fileSystem = fileSystem ?? new PhysicalFileSystem();
        var processRunner1 = processRunner ?? new DefaultProcessRunner();
        _delay = delay ?? new TaskDelay(_timeProvider);
        _console = console ?? new SystemConsoleIo();
        _environment = environment ?? new SystemEnvironmentVariables();
        var idGenerator1 = idGenerator ?? new GuidUniqueIdGenerator();
        _deviceHostFactory = deviceHostFactory ?? new DefaultDeviceHostFactory(
            adbClientFactory ?? new DefaultAdbClientFactory(),
            processRunner1,
            _delay,
            _fileSystem,
            _timeProvider,
            _environment,
            idGenerator1);
        _viewSessionFactory = viewSessionFactory ?? new DefaultViewSessionFactory(
            _console,
            _timeProvider,
            adbClientFactory ?? new DefaultAdbClientFactory(),
            processRunner1,
            _environment,
            _fileSystem,
            idGenerator1);
        _viewDoctorFactory = viewDoctorFactory ?? new DefaultViewDoctorFactory(
            _environment,
            _fileSystem,
            processRunner1);
    }

    /// <summary>
    /// Runs the command-line application.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>The process exit code.</returns>
    public async Task<int> RunAsync(string[] args)
    {
        var started = _timeProvider.GetUtcNow();
        var options = CliOptions.Parse(args);
        if (options.Command is null || options.HasFlag("help") || options.HasFlag("h"))
        {
            _console.WriteErrorLine(Help.Text);
            return options.HasFlag("help") || options.HasFlag("h") ? 0 : 2;
        }

        ArtifactSession? artifacts = null;
        IDeviceHost? runner = null;
        var adbExecutable = options.Get("adb") ?? _environment.GetEnvironmentVariable(CliDefaults.AdbExecutableEnvironmentVariable) ?? CliDefaults.DefaultAdbExecutable;

        ArtifactData CreateArtifactData() => artifacts?.ToData() ?? new ArtifactData(
            options.Get("artifacts") ?? string.Empty,
            options.Get("poll-artifacts") ?? CliDefaults.DefaultPollArtifactsPolicy);

        try
        {
            artifacts = ArtifactSession.Create(options, _fileSystem, _timeProvider);

            if (string.Equals(options.Command, "inspect", StringComparison.OrdinalIgnoreCase))
            {
                runner = _deviceHostFactory.Create(
                    new DeviceHostConfiguration(
                        options.Get("platform") ?? CliDefaults.DefaultPlatform,
                        adbExecutable,
                        options.Get("device")),
                    artifacts);
                var inspectSession = new InspectSession(runner, _console, _timeProvider);
                return await inspectSession.RunAsync().ConfigureAwait(false);
            }

            if (string.Equals(options.Command, "view", StringComparison.OrdinalIgnoreCase))
            {
                var viewOptions = BuildViewOptions(options, adbExecutable, allowJoinShare: true);
                runner = string.IsNullOrWhiteSpace(viewOptions.JoinShareEndpoint)
                    ? _deviceHostFactory.Create(
                        new DeviceHostConfiguration(
                            options.Get("platform") ?? CliDefaults.DefaultPlatform,
                            adbExecutable,
                            options.Get("device")),
                        artifacts)
                    : new UnsupportedDeviceHost();
                var viewSession = _viewSessionFactory.Create(runner, artifacts);
                return await viewSession.RunAsync(viewOptions).ConfigureAwait(false);
            }

            if (string.Equals(options.Command, "view-doctor", StringComparison.OrdinalIgnoreCase))
            {
                runner = _deviceHostFactory.Create(
                    new DeviceHostConfiguration(
                        options.Get("platform") ?? CliDefaults.DefaultPlatform,
                        adbExecutable,
                        options.Get("device")),
                    artifacts);
                var viewDoctor = _viewDoctorFactory.Create(runner);
                var report = await viewDoctor.DiagnoseAsync(BuildViewOptions(options, adbExecutable, allowJoinShare: false)).ConfigureAwait(false);
                WriteEnvelope(new CommandEnvelope(true, options.Command, started, _timeProvider.GetUtcNow(), report, artifacts.ToData(), null));
                return 0;
            }

            runner = _deviceHostFactory.Create(
                new DeviceHostConfiguration(
                    options.Get("platform") ?? CliDefaults.DefaultPlatform,
                    adbExecutable,
                    options.Get("device")),
                artifacts);
            var scenarios = new ScenarioExecutor(runner, _fileSystem, _timeProvider, _delay, _environment);

            var data = options.Command switch
            {
                "devices" => await runner.GetDevicesAsync().ConfigureAwait(false),
                "preflight" => await runner.PreflightAsync(options.Get("package")).ConfigureAwait(false),
                "screen-state" => await runner.GetScreenStateAsync().ConfigureAwait(false),
                "telemetry-tail" => await runner.TelemetryTailAsync(options.Int("tail", CliDefaults.DefaultLogTail)).ConfigureAwait(false),
                "telemetry-watch" => await runner.TelemetryWatchAsync(options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
                "wait-step" => await runner.WaitForStepAsync(options.Require("step"), options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
                "wait-action-ready" => await runner.WaitForActionReadyAsync(options.Require("action"), options.Get("step"), options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
                "tap" => await runner.TapAsync(options.Require("x"), options.Require("y")).ConfigureAwait(false),
                "tap-text" => await runner.TapTextAsync(options.Require("text"), options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
                "wait-visible" => await runner.WaitVisibleAsync(options.Require("text"), options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
                "type-text" => await runner.TypeTextAsync(options.Require("text")).ConfigureAwait(false),
                "keyevent" => await runner.KeyEventAsync(options.Require("code")).ConfigureAwait(false),
                "logcat" => await runner.LogcatAsync(options.Int("tail", CliDefaults.DefaultLogTail)).ConfigureAwait(false),
                "wait-log" => await runner.WaitForLogAsync(options.Require("contains"), options.Int("timeout-sec", CliDefaults.DefaultTimeoutSeconds)).ConfigureAwait(false),
                "record" => await runner.RecordAsync(options.Require("output"), options.Int("time-limit-sec", CliDefaults.DefaultRecordTimeLimitSeconds)).ConfigureAwait(false),
                "run" => await scenarios.RunAsync(options.Require("file")).ConfigureAwait(false),
                _ => throw new UsageException($"Unknown command '{options.Command}'.")
            };

            WriteEnvelope(new CommandEnvelope(true, options.Command, started, _timeProvider.GetUtcNow(), data, artifacts.ToData(), null));
            return 0;
        }
        catch (UsageException ex)
        {
            WriteEnvelope(new CommandEnvelope(false, options.Command, started, _timeProvider.GetUtcNow(), null, CreateArtifactData(), ErrorInfo.From(ex, "usage_error")));
            return 2;
        }
        catch (Exception ex)
        {
            var failure = ex as ICommandFailureDetails;
            var failureData = failure?.DataPayload;
            if (failureData is null && runner is not null)
            {
                failureData = await runner.CaptureFailureArtifactsAsync(new FailureCaptureRequest("command", options.Command, null, null, null, options.Command), ex).ConfigureAwait(false);
            }

            var category = failure?.CategoryOverride ?? ErrorInfo.Classify(ex.Message);
            WriteEnvelope(new CommandEnvelope(false, options.Command, started, _timeProvider.GetUtcNow(), failureData, CreateArtifactData(), ErrorInfo.From(ex, category)));
            return 1;
        }
    }

    private static ViewOptions BuildViewOptions(CliOptions options, string adbExecutable, bool allowJoinShare)
    {
        if (options.HasFlag("defaults") && options.Get("preset") is not null)
        {
            throw new UsageException("view requires either --defaults or --preset, not both.");
        }

        var joinShareEndpoint = options.Get("join-share");
        if (!allowJoinShare && !string.IsNullOrWhiteSpace(joinShareEndpoint))
        {
            throw new UsageException("view-doctor does not support --join-share.");
        }

        var device = options.Get("device");
        if (!allowJoinShare || string.IsNullOrWhiteSpace(joinShareEndpoint))
        {
            device = options.Require("device");
        }
        else if (!string.IsNullOrWhiteSpace(device))
        {
            throw new UsageException("view requires either --device or --join-share, not both.");
        }

        var preset = ViewPresetCatalog.Resolve(options.HasFlag("defaults") ? ViewPresetCatalog.Safe : options.Get("preset"));
        var statsIntervalMs = GetIntOrDefault(options, "stats-interval-ms", preset.StatsIntervalMs);
        var rendererStatsIntervalMs = GetIntOrDefault(options, "renderer-stats-interval-ms", preset.RendererStatsIntervalMs);
        if (statsIntervalMs < 0)
        {
            throw new UsageException("view requires --stats-interval-ms zero or greater.");
        }

        if (rendererStatsIntervalMs < 0)
        {
            throw new UsageException("view requires --renderer-stats-interval-ms zero or greater.");
        }

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
            joinShareEndpoint);

        static int GetIntOrDefault(CliOptions options, string key, int defaultValue) =>
            options.Get(key) is null ? defaultValue : options.Int(key, defaultValue);
    }

    private void WriteEnvelope(CommandEnvelope envelope) => _console.WriteLine(JsonSerializer.Serialize(envelope, JsonOptions));
}