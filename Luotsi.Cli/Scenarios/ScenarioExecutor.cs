using System.Text.Json;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Serialization;
using Luotsi.Cli.Infrastructure.System;
using Luotsi.Cli.Infrastructure.Time;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Scenarios;

public interface IScenarioActionHost
{
    Task<ScreenState> GetScreenStateAsync();
    Task<ScreenElement> WaitVisibleAsync(string text, int timeoutSec);
    Task<WaitNotVisibleResult> WaitNotVisibleAsync(string text, int timeoutSec);
    Task<TapResult> TapTextAsync(string text, int timeoutSec);
    Task<TapPointResult> TapPointAsync(string? label, int? x, int? y, double? xRatio, double? yRatio, int postTapDelayMs);
    Task<DoubleTapHeaderLogoResult> DoubleTapHeaderLogoAsync();
    Task<TypeTextResult> TypeTextAsync(string text);
    Task<TypePinResult> TypePinAsync(string pin, int perDigitDelayMs);
    Task<KeyEventResult> KeyEventAsync(string code);
    Task<WaitLogResult> WaitForLogAsync(string text, int timeoutSec);
    Task<TelemetryMatchResult> WaitForStepAsync(string step, int timeoutSec);
    Task<TelemetryMatchResult> WaitForActionReadyAsync(string action, string? step, int timeoutSec);
    Task<ResetLogResult> ResetLogAsync();
    Task<AssertEventResult> AssertEventAsync(string name, IReadOnlyList<string> contains, string? detailsPattern, int timeoutSec, DateTimeOffset? since = null);
    Task<TakeScreenshotResult> TakeScreenshotAsync(string label);
    Task<CaptureArtifactsResult> CaptureArtifactsAsync(string label);
    Task<AssertTextInputReadyResult> AssertTextInputReadyAsync(bool requireKeyboard, int timeoutSec);
    Task<AssertBelowResult> AssertBelowAsync(string text, string referenceText, int maxGapPx);
    Task<AssertAlignedResult> AssertAlignedAsync(string text, string referenceText, int maxDeltaPx);
    Task<AssertAppVersionResult> AssertAppVersionAsync(string? packageName, int maxTopInsetPx, int maxRightInsetPx);
    Task<StartAppResult> StartAppAsync(string packageName, string? activity, bool wait);
    Task<StartUriResult> StartUriAsync(string uri, string? packageName, string? activity, string? action, bool wait);
    Task<AppPackageCommandResult> ForceStopAsync(string packageName);
    Task<AppPackageCommandResult> ClearAppAsync(string packageName);
    Task<ActivityWaitResult> WaitForActivityAsync(string activity, int timeoutSec);
    Task<ActivityWaitResult> WaitForNotActivityAsync(string activity, int timeoutSec);
    Task<AppInstalledResult> IsAppInstalledAsync(string packageName);
    Task<InstalledPackageListResult> ListInstalledPackagesAsync(bool thirdPartyOnly);
    Task<PermissionCommandResult> GrantPermissionAsync(string packageName, string permission);
    Task<PermissionCommandResult> RevokePermissionAsync(string packageName, string permission);
    Task<DeviceFingerprint> WriteDeviceFingerprintAsync();
    Task<FailureArtifactBundle> CaptureFailureArtifactsAsync(FailureCaptureRequest request, Exception exception);
}

/// <summary>
/// Loads and executes JSON scenario files.
/// </summary>
public sealed class ScenarioExecutor
{
    internal static readonly HashSet<string> SupportedScenarioActions =
    [
        "waitVisible",
        "waitNotVisible",
        "tapText",
        "tapPoint",
        "doubleTapHeaderLogo",
        "doubleTap",
        "typeText",
        "typePin",
        "keyevent",
        "waitLog",
        "waitStep",
        "waitActionReady",
        "resetLog",
        "assertEvent",
        "takeScreenshot",
        "captureArtifacts",
        "assertTextInputReady",
        "assertBelow",
        "assertAligned",
        "assertAppVersion",
        "startApp",
        "startUri",
        "forceStop",
        "clear",
        "clearApp",
        "waitForActivity",
        "waitForNotActivity",
        "isAppInstalled",
        "listInstalledPackages",
        "grantPermission",
        "revokePermission",
        "screenState",
        "sleep"
    ];

    private readonly IScenarioActionHost _actionHost;
    private readonly IFileSystem _fileSystem;
    private readonly TimeProvider _timeProvider;
    private readonly ScenarioActionDispatcher _actionDispatcher;
    private readonly IScenarioTemplateResolver _templateResolver;
    private readonly IScenarioEventSink _eventSink;

    public ScenarioExecutor(IScenarioActionHost actionHost, IFileSystem fileSystem, TimeProvider timeProvider, IDelay delay)
        : this(
            actionHost,
            fileSystem,
            timeProvider,
            delay,
            new ScenarioTemplateResolver(
                timeProvider ?? throw new ArgumentNullException(nameof(timeProvider)),
                new SystemEnvironmentVariables()))
    {
    }

    public ScenarioExecutor(IScenarioActionHost actionHost, IFileSystem fileSystem, TimeProvider timeProvider, IDelay delay, IEnvironmentVariables environment)
        : this(
            actionHost,
            fileSystem,
            timeProvider,
            delay,
            new ScenarioTemplateResolver(
                timeProvider ?? throw new ArgumentNullException(nameof(timeProvider)),
                environment ?? throw new ArgumentNullException(nameof(environment))))
    {
    }

    internal ScenarioExecutor(IScenarioActionHost actionHost, IFileSystem fileSystem, TimeProvider timeProvider, IDelay delay, IScenarioTemplateResolver templateResolver, IScenarioEventSink? eventSink = null)
    {
        _actionHost = actionHost ?? throw new ArgumentNullException(nameof(actionHost));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _actionDispatcher = new ScenarioActionDispatcher(
            actionHost,
            delay ?? throw new ArgumentNullException(nameof(delay)));
        _templateResolver = templateResolver ?? throw new ArgumentNullException(nameof(templateResolver));
        _eventSink = eventSink ?? NullScenarioEventSink.Instance;
    }

    /// <summary>
    /// Runs a JSON scenario playbook.
    /// </summary>
    /// <param name="file">Scenario file path.</param>
    /// <returns>Scenario result.</returns>
    public async Task<ScenarioRunResult> RunAsync(string file)
    {
        var scenarioStarted = _timeProvider.GetUtcNow();
        var scenario = await LoadValidatedScenarioAsync(file).ConfigureAwait(false);
        await EmitAsync(new ScenarioEvent("scenario_started", scenarioStarted, File: file, Scenario: scenario.Name)).ConfigureAwait(false);

        var status = "failed";
        try
        {
            await _actionHost.WriteDeviceFingerprintAsync().ConfigureAwait(false);
            var prologueMs = (_timeProvider.GetUtcNow() - scenarioStarted).TotalMilliseconds;
            var execution = await ExecuteStepsAsync(scenario, file, scenarioStarted, prologueMs).ConfigureAwait(false);
            status = "passed";

            return new ScenarioRunResult(
                scenario.Name,
                status,
                CreateScenarioRunTiming((_timeProvider.GetUtcNow() - scenarioStarted).TotalMilliseconds, prologueMs, execution.ExecutedStepMs),
                execution.Steps);
        }
        finally
        {
            await EmitAsync(new ScenarioEvent(
                "scenario_ended",
                _timeProvider.GetUtcNow(),
                status,
                File: file,
                Scenario: scenario.Name,
                DurationMs: (_timeProvider.GetUtcNow() - scenarioStarted).TotalMilliseconds)).ConfigureAwait(false);
        }
    }

    private async Task<ScenarioFile> LoadValidatedScenarioAsync(string file) =>
        ValidateScenario(ResolveTemplates(await LoadAsync(file).ConfigureAwait(false)), file);

    private async Task<object> ExecuteStepAsync(ScenarioStep step, DateTimeOffset? previousStepStartedAt) =>
        await _actionDispatcher.ExecuteAsync(step, previousStepStartedAt).ConfigureAwait(false);

    private async Task<ScenarioExecution> ExecuteStepsAsync(ScenarioFile scenario, string file, DateTimeOffset scenarioStarted, double prologueMs)
    {
        var steps = new List<ScenarioStepResult>(scenario.Steps.Count);
        var executedStepMs = 0d;
        DateTimeOffset? previousStepStartedAt = null;

        for (var index = 0; index < scenario.Steps.Count; index++)
        {
            var step = scenario.Steps[index];
            using var delayScope = DelayMetrics.BeginScope();
            var started = _timeProvider.GetUtcNow();
            await EmitStepAsync("scenario_step_started", file, scenario.Name, index, step, started).ConfigureAwait(false);

            try
            {
                var result = await ExecuteStepAsync(step, previousStepStartedAt).ConfigureAwait(false);
                var durationMs = (_timeProvider.GetUtcNow() - started).TotalMilliseconds;
                executedStepMs += durationMs;
                previousStepStartedAt = started;
                await EmitStepAsync("scenario_step_passed", file, scenario.Name, index, step, _timeProvider.GetUtcNow(), "passed", durationMs).ConfigureAwait(false);

                steps.Add(new ScenarioStepResult(
                    step.Name ?? step.Action,
                    step.Action,
                    durationMs,
                    CreateTimingData(step, durationMs, delayScope.TotalMilliseconds),
                    Result: result));
            }
            catch (Exception ex) when (step.ContinueOnError is true && ex is not UsageException)
            {
                var error = ScenarioErrorInfo.From(ex);
                var durationMs = (_timeProvider.GetUtcNow() - started).TotalMilliseconds;
                executedStepMs += durationMs;
                previousStepStartedAt = started;
                await EmitStepAsync("scenario_step_continued_on_error", file, scenario.Name, index, step, _timeProvider.GetUtcNow(), "continued_on_error", durationMs, error).ConfigureAwait(false);
                steps.Add(new ScenarioStepResult(
                    step.Name ?? step.Action,
                    step.Action,
                    durationMs,
                    CreateTimingData(step, durationMs, delayScope.TotalMilliseconds),
                    Status: "continued_on_error",
                    Error: error));
            }
            catch (UsageException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var durationMs = (_timeProvider.GetUtcNow() - started).TotalMilliseconds;
                executedStepMs += durationMs;
                var failureArtifacts = await _actionHost.CaptureFailureArtifactsAsync(
                    new FailureCaptureRequest("scenario", scenario.Name, file, index + 1, step.Name ?? step.Action, step.Action),
                    ex).ConfigureAwait(false);
                var error = ScenarioErrorInfo.From(ex);
                await EmitStepAsync("scenario_step_failed", file, scenario.Name, index, step, _timeProvider.GetUtcNow(), "failed", durationMs, error).ConfigureAwait(false);
                throw new ScenarioStepFailureException(
                    $"Scenario '{scenario.Name}' failed at step {index + 1} ({step.Name ?? step.Action}).",
                    error.Category,
                    new ScenarioRunFailureData(
                        scenario.Name,
                        file,
                        "failed",
                        CreateScenarioRunTiming((_timeProvider.GetUtcNow() - scenarioStarted).TotalMilliseconds, prologueMs, executedStepMs),
                        new ScenarioFailedStepResult(
                            index + 1,
                            step.Name ?? step.Action,
                            step.Action,
                            durationMs,
                            CreateTimingData(step, durationMs, delayScope.TotalMilliseconds)),
                        steps,
                        failureArtifacts),
                    ex);
            }
        }

        return new ScenarioExecution(executedStepMs, steps);
    }

    private async Task<ScenarioFile> LoadAsync(string file)
    {
        if (!_fileSystem.FileExists(file))
        {
            throw new UsageException($"Scenario file '{file}' does not exist.");
        }

        try
        {
            var text = await _fileSystem.ReadAllTextAsync(file).ConfigureAwait(false);
            var scenario = JsonSerializer.Deserialize<ScenarioFile>(text, AppJson.Options);
            if (scenario is null)
            {
                throw new UsageException($"Scenario file '{file}' was empty.");
            }

            return scenario;
        }
        catch (JsonException ex)
        {
            throw new UsageException($"Scenario file '{file}' is not valid JSON: {ex.Message}");
        }
    }

    private ScenarioFile ResolveTemplates(ScenarioFile scenario) =>
        _templateResolver.ResolveScenario(scenario);

    private static ScenarioFile ValidateScenario(ScenarioFile scenario, string file) =>
        ScenarioValidator.ValidateScenario(scenario, file, SupportedScenarioActions);

    private static ScenarioStepTiming CreateTimingData(ScenarioStep step, double durationMs, int harnessDelayMs)
    {
        var configuredDelayMs = GetConfiguredDelayMs(step);
        return new ScenarioStepTiming(
            durationMs,
            harnessDelayMs,
            configuredDelayMs,
            Math.Max(0, durationMs - harnessDelayMs));
    }

    private static ScenarioRunTiming CreateScenarioRunTiming(double totalMs, double prologueMs, double executedStepMs) =>
        new(totalMs, prologueMs, executedStepMs, Math.Max(0, totalMs - executedStepMs));

    private static int? GetConfiguredDelayMs(ScenarioStep step) => step.Action switch
    {
        "sleep" => Math.Max(0, step.Milliseconds ?? 1000),
        "tapPoint" => Math.Max(0, step.PostTapDelayMs ?? 300),
        "typePin" when !string.IsNullOrWhiteSpace(step.Text) => Math.Max(0, step.IntervalMs ?? 120) * step.Text.Count(char.IsDigit),
        _ => null
    };

    private Task EmitStepAsync(
        string eventName,
        string file,
        string scenario,
        int index,
        ScenarioStep step,
        DateTimeOffset timestamp,
        string? status = null,
        double? durationMs = null,
        ErrorInfo? error = null) =>
        EmitAsync(new ScenarioEvent(
            eventName,
            timestamp,
            status,
            File: file,
            Scenario: scenario,
            StepIndex: index + 1,
            Step: step.Name ?? step.Action,
            Action: step.Action,
            DurationMs: durationMs,
            Error: error));

    private Task EmitAsync(ScenarioEvent scenarioEvent) => _eventSink.EmitAsync(scenarioEvent);

    private sealed record ScenarioExecution(double ExecutedStepMs, IReadOnlyList<ScenarioStepResult> Steps);
}

public interface ICommandFailureDetails
{
    string CategoryOverride { get; }

    object? DataPayload { get; }
}
