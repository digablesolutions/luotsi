using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VisitLab.Cli;

/// <summary>
/// Entry point for the VisitLab command-line application.
/// </summary>
public sealed class App
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly TimeProvider _timeProvider;
    private readonly IFileSystem _fileSystem;
    private readonly IProcessRunner _processRunner;
    private readonly IDelay _delay;
    private readonly IDeviceHostFactory _deviceHostFactory;
    private readonly IConsoleIO _console;
    private readonly IEnvironmentVariables _environment;
    private readonly IUniqueIdGenerator _idGenerator;

    public App(
        TimeProvider? timeProvider = null,
        IFileSystem? fileSystem = null,
        IProcessRunner? processRunner = null,
        IDelay? delay = null,
        IAdbClientFactory? adbClientFactory = null,
        IConsoleIO? console = null,
        IEnvironmentVariables? environment = null,
        IUniqueIdGenerator? idGenerator = null,
        IDeviceHostFactory? deviceHostFactory = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _fileSystem = fileSystem ?? new PhysicalFileSystem();
        _processRunner = processRunner ?? new DefaultProcessRunner();
        _delay = delay ?? new TaskDelay(_timeProvider);
        _console = console ?? new SystemConsoleIO();
        _environment = environment ?? new SystemEnvironmentVariables();
        _idGenerator = idGenerator ?? new GuidUniqueIdGenerator();
        _deviceHostFactory = deviceHostFactory ?? new DefaultDeviceHostFactory(
            adbClientFactory ?? new DefaultAdbClientFactory(),
            _processRunner,
            _delay,
            _fileSystem,
            _timeProvider,
            _environment,
            _idGenerator);
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

        var artifacts = ArtifactSession.Create(options, _fileSystem, _timeProvider);
        var runner = _deviceHostFactory.Create(
            new DeviceHostConfiguration(
                options.Get("platform") ?? "android",
                options.Get("adb") ?? _environment.GetEnvironmentVariable("DEVICE_E2E_ADB") ?? "adb",
                options.Get("device")),
            artifacts);
        var scenarios = new ScenarioExecutor(runner, _fileSystem, _timeProvider, _delay, _environment);

        if (string.Equals(options.Command, "inspect", StringComparison.OrdinalIgnoreCase))
        {
            var inspectSession = new InspectSession(runner, _console, _timeProvider);
            return await inspectSession.RunAsync().ConfigureAwait(false);
        }

        try
        {
            object data = options.Command switch
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
                _ => throw new UsageException($"Unknown command '{options.Command}'."),
            };

            WriteEnvelope(new CommandEnvelope(true, options.Command, started, _timeProvider.GetUtcNow(), data, artifacts.ToData(), null));
            return 0;
        }
        catch (UsageException ex)
        {
            WriteEnvelope(new CommandEnvelope(false, options.Command, started, _timeProvider.GetUtcNow(), null, artifacts.ToData(), ErrorInfo.From(ex, "usage_error")));
            return 2;
        }
        catch (Exception ex)
        {
            var failure = ex as ICommandFailureDetails;
            var failureData = failure?.DataPayload;
            if (failureData is null)
            {
                failureData = await runner.CaptureFailureArtifactsAsync(new FailureCaptureRequest("command", options.Command, null, null, null, options.Command), ex).ConfigureAwait(false);
            }

            var category = failure?.CategoryOverride ?? ErrorInfo.Classify(ex.Message);
            WriteEnvelope(new CommandEnvelope(false, options.Command, started, _timeProvider.GetUtcNow(), failureData, artifacts.ToData(), ErrorInfo.From(ex, category)));
            return 1;
        }
    }

    private void WriteEnvelope(CommandEnvelope envelope)
    {
        _console.WriteLine(JsonSerializer.Serialize(envelope, JsonOptions));
    }
}