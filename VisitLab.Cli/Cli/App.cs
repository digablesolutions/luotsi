using System.Text.Json;
using System.Text.Json.Serialization;
using VisitLab.Cli.Artifacts;
using VisitLab.Cli.Errors;
using VisitLab.Cli.Infrastructure;
using VisitLab.Cli.Models;
using VisitLab.Cli.Scenarios;
using VisitLab.Cli.View;

namespace VisitLab.Cli.Cli;

/// <summary>
/// Entry point for the VisitLab command-line application.
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
        IViewSessionFactory? viewSessionFactory = null)
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
        var adbExecutable = options.Get("adb") ?? _environment.GetEnvironmentVariable("DEVICE_E2E_ADB") ?? "adb";

        ArtifactData CreateArtifactData() => artifacts?.ToData() ?? new ArtifactData(
            options.Get("artifacts") ?? string.Empty,
            options.Get("poll-artifacts") ?? "final");

        try
        {
            artifacts = ArtifactSession.Create(options, _fileSystem, _timeProvider);
            runner = _deviceHostFactory.Create(
                new DeviceHostConfiguration(
                    options.Get("platform") ?? "android",
                    adbExecutable,
                    options.Get("device")),
                artifacts);
            var scenarios = new ScenarioExecutor(runner, _fileSystem, _timeProvider, _delay, _environment);

            if (string.Equals(options.Command, "inspect", StringComparison.OrdinalIgnoreCase))
            {
                var inspectSession = new InspectSession(runner, _console, _timeProvider);
                return await inspectSession.RunAsync().ConfigureAwait(false);
            }

            if (string.Equals(options.Command, "view", StringComparison.OrdinalIgnoreCase))
            {
                var viewSession = _viewSessionFactory.Create(runner, artifacts);
                return await viewSession.RunAsync(BuildViewOptions(options, adbExecutable)).ConfigureAwait(false);
            }

            var data = options.Command switch
            {
                "devices" => await runner.GetDevicesAsync().ConfigureAwait(false),
                "preflight" => await runner.PreflightAsync(options.Get("package")).ConfigureAwait(false),
                "screen-state" => await runner.GetScreenStateAsync().ConfigureAwait(false),
                "telemetry-tail" => await runner.TelemetryTailAsync(options.Int("tail", 200)).ConfigureAwait(false),
                "telemetry-watch" => await runner.TelemetryWatchAsync(options.Int("timeout-sec", 15)).ConfigureAwait(false),
                "wait-step" => await runner.WaitForStepAsync(options.Require("step"), options.Int("timeout-sec", 15)).ConfigureAwait(false),
                "wait-action-ready" => await runner.WaitForActionReadyAsync(options.Require("action"), options.Get("step"), options.Int("timeout-sec", 15)).ConfigureAwait(false),
                "tap" => await runner.TapAsync(options.Require("x"), options.Require("y")).ConfigureAwait(false),
                "tap-text" => await runner.TapTextAsync(options.Require("text"), options.Int("timeout-sec", 15)).ConfigureAwait(false),
                "wait-visible" => await runner.WaitVisibleAsync(options.Require("text"), options.Int("timeout-sec", 15)).ConfigureAwait(false),
                "type-text" => await runner.TypeTextAsync(options.Require("text")).ConfigureAwait(false),
                "keyevent" => await runner.KeyEventAsync(options.Require("code")).ConfigureAwait(false),
                "logcat" => await runner.LogcatAsync(options.Int("tail", 200)).ConfigureAwait(false),
                "wait-log" => await runner.WaitForLogAsync(options.Require("contains"), options.Int("timeout-sec", 15)).ConfigureAwait(false),
                "record" => await runner.RecordAsync(options.Require("output"), options.Int("time-limit-sec", 30)).ConfigureAwait(false),
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

    private static ViewOptions BuildViewOptions(CliOptions options, string adbExecutable) =>
        new(
            options.Require("device"),
            adbExecutable,
            options.Get("codec") ?? "h264",
            options.Get("decoder") ?? "ffmpeg",
            options.HasFlag("headless"),
            options.Get("record"),
            options.Int("max-size", 1600),
            options.Int("max-fps", 60),
            options.Get("video-bit-rate") ?? "8M",
            options.HasFlag("overlay-screen-state"),
            options.HasFlag("overlay-telemetry"));

    private void WriteEnvelope(CommandEnvelope envelope) => _console.WriteLine(JsonSerializer.Serialize(envelope, JsonOptions));
}