using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
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
    Task<ScreenshotAssertionResult> AssertScreenshotAsync(string label, int? expectedWidth, int? expectedHeight, string? expectedSha256);
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
        "assertScreenshot",
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
    private readonly TimeProvider _timeProvider;
    private readonly ScenarioActionDispatcher _actionDispatcher;
    private readonly ScenarioCatalog _scenarioCatalog;
    private readonly IScenarioEventSink _eventSink;
    private readonly ScenarioFailureArtifactCapturePolicy _failureArtifactCapturePolicy;
    private readonly IScenarioMetricsCollector _metricsCollector;

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

    internal ScenarioExecutor(
        IScenarioActionHost actionHost,
        IFileSystem fileSystem,
        TimeProvider timeProvider,
        IDelay delay,
        IScenarioTemplateResolver templateResolver,
        IScenarioEventSink? eventSink = null,
        ScenarioFailureArtifactCapturePolicy failureArtifactCapturePolicy = ScenarioFailureArtifactCapturePolicy.Failure,
        IScenarioMetricsCollector? metricsCollector = null)
    {
        _actionHost = actionHost ?? throw new ArgumentNullException(nameof(actionHost));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _actionDispatcher = new ScenarioActionDispatcher(
            actionHost,
            delay ?? throw new ArgumentNullException(nameof(delay)));
        _scenarioCatalog = new ScenarioCatalog(
            fileSystem ?? throw new ArgumentNullException(nameof(fileSystem)),
            templateResolver ?? throw new ArgumentNullException(nameof(templateResolver)));
        _eventSink = eventSink ?? NullScenarioEventSink.Instance;
        _failureArtifactCapturePolicy = failureArtifactCapturePolicy;
        _metricsCollector = metricsCollector ?? CompositeScenarioMetricsCollector.CreateDefault();
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
        var lifecycle = new ScenarioLifecycleCoordinator(_timeProvider, _eventSink);
        var context = new ScenarioLifecycleContext(
            file,
            ScenarioIdentity.Create(file, scenario.Name),
            scenario.Name,
            scenarioStarted);

        return await lifecycle.RunAsync(
            context,
            startedStatus: null,
            async lifecycleContext =>
            {
                await _actionHost.WriteDeviceFingerprintAsync().ConfigureAwait(false);
                var prologueMs = (_timeProvider.GetUtcNow() - lifecycleContext.StartedAt).TotalMilliseconds;
                var execution = await ExecuteLifecycleAsync(
                    scenario,
                    lifecycleContext.File,
                    lifecycleContext.ScenarioId,
                    lifecycleContext.StartedAt,
                    prologueMs).ConfigureAwait(false);
                var scenarioMetrics = CollectScenarioMetrics("passed", execution.Timing, execution.Steps);

                return new ScenarioLifecycleCompletion(
                    new ScenarioRunResult(
                        scenario.Name,
                        "passed",
                        execution.Timing,
                        scenarioMetrics,
                        execution.Steps,
                        null,
                        lifecycleContext.ScenarioId,
                        lifecycleContext.File,
                        scenario.Metadata));
            }).ConfigureAwait(false);
    }

    private Task<ScenarioFile> LoadValidatedScenarioAsync(string file) =>
        _scenarioCatalog.LoadValidatedAsync(file, SupportedScenarioActions);

    private async Task<object> ExecuteStepAsync(ScenarioStep step, DateTimeOffset? previousStepStartedAt) =>
        await _actionDispatcher.ExecuteAsync(step, previousStepStartedAt).ConfigureAwait(false);

    private async Task<ScenarioExecution> ExecuteLifecycleAsync(ScenarioFile scenario, string file, string scenarioId, DateTimeOffset scenarioStarted, double prologueMs)
    {
        var context = new ScenarioExecutionContext(scenario, file, scenarioId, scenarioStarted, prologueMs);
        Exception? firstFailure = null;

        try
        {
            await ExecuteStepsAsync(context, scenario.Setup ?? [], ScenarioStepPhases.Setup).ConfigureAwait(false);
            await ExecuteStepsAsync(context, scenario.Steps, ScenarioStepPhases.Main).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            firstFailure = ex;
        }

        try
        {
            await ExecuteStepsAsync(context, scenario.Teardown ?? [], ScenarioStepPhases.Teardown).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            firstFailure ??= ex;
        }

        if (firstFailure is not null)
        {
            if (firstFailure is ScenarioStepFailureException stepFailure && ScenarioFailureDetails.TryGetData(stepFailure) is { } failureData)
            {
                stepFailure.UpdateDataPayload(CreateFinalFailureData(context, failureData));
            }

            throw firstFailure;
        }

        return new ScenarioExecution(
            context.Steps,
            CreateScenarioRunTiming((_timeProvider.GetUtcNow() - scenarioStarted).TotalMilliseconds, prologueMs, context.ExecutedStepMs));
    }

    private async Task ExecuteStepsAsync(ScenarioExecutionContext context, IReadOnlyList<ScenarioStep> scenarioSteps, string phase)
    {
        for (var index = 0; index < scenarioSteps.Count; index++)
        {
            var step = scenarioSteps[index];
            using var delayScope = DelayMetrics.BeginScope();
            var started = _timeProvider.GetUtcNow();
            await EmitStepAsync("scenario_step_started", context.File, context.ScenarioId, context.Scenario.Name, phase, context.NextStepIndex, step, started).ConfigureAwait(false);

            try
            {
                var result = await ExecuteStepAsync(step, context.PreviousStepStartedAt).ConfigureAwait(false);
                var durationMs = (_timeProvider.GetUtcNow() - started).TotalMilliseconds;
                context.ExecutedStepMs += durationMs;
                context.PreviousStepStartedAt = started;
                var stepResult = CreateStepResult(step, phase, "passed", durationMs, delayScope.TotalMilliseconds, result: result);
                await EmitStepAsync("scenario_step_passed", context.File, context.ScenarioId, context.Scenario.Name, phase, context.NextStepIndex, step, _timeProvider.GetUtcNow(), "passed", stepResult.DurationMs, metrics: stepResult.Metrics).ConfigureAwait(false);

                context.Steps.Add(stepResult);
                context.NextStepIndex++;
            }
            catch (Exception ex) when (step.ContinueOnError is true && ex is not UsageException)
            {
                var error = ScenarioErrorInfo.From(ex);
                var durationMs = (_timeProvider.GetUtcNow() - started).TotalMilliseconds;
                context.ExecutedStepMs += durationMs;
                context.PreviousStepStartedAt = started;
                var stepResult = CreateStepResult(
                    step,
                    phase,
                    "continued_on_error",
                    durationMs,
                    delayScope.TotalMilliseconds,
                    stepStatus: "continued_on_error",
                    error: error);
                await EmitStepAsync("scenario_step_continued_on_error", context.File, context.ScenarioId, context.Scenario.Name, phase, context.NextStepIndex, step, _timeProvider.GetUtcNow(), stepResult.Status, stepResult.DurationMs, error, stepResult.Metrics).ConfigureAwait(false);
                context.Steps.Add(stepResult);
                context.NextStepIndex++;
            }
            catch (UsageException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var durationMs = (_timeProvider.GetUtcNow() - started).TotalMilliseconds;
                context.ExecutedStepMs += durationMs;
                var error = ScenarioErrorInfo.From(ex);
                var failedStep = CreateStepResult(
                    step,
                    phase,
                    "failed",
                    durationMs,
                    delayScope.TotalMilliseconds,
                    stepStatus: "failed",
                    error: error);
                await EmitStepAsync("scenario_step_failed", context.File, context.ScenarioId, context.Scenario.Name, phase, context.NextStepIndex, step, _timeProvider.GetUtcNow(), failedStep.Status, failedStep.DurationMs, error, failedStep.Metrics).ConfigureAwait(false);
                var failureArtifacts = await CaptureFailureArtifactsBestEffortAsync(
                    new FailureCaptureRequest("scenario", context.Scenario.Name, context.File, context.NextStepIndex, step.Name ?? step.Action, step.Action),
                    ex).ConfigureAwait(false);
                throw new ScenarioStepFailureException(
                    $"Scenario '{context.Scenario.Name}' failed during {phase} step {context.NextStepIndex} ({step.Name ?? step.Action}).",
                    error.Category,
                    CreateFailureData(context, failedStep, failureArtifacts),
                    ex);
            }
        }
    }

    private static ScenarioStepTiming CreateTimingData(ScenarioStep step, double durationMs, int harnessDelayMs) =>
        ScenarioTimingSupport.CreateStepTiming(step, durationMs, harnessDelayMs);

    private static ScenarioRunTiming CreateScenarioRunTiming(double totalMs, double prologueMs, double executedStepMs) =>
        new(totalMs, prologueMs, executedStepMs, Math.Max(0, totalMs - executedStepMs));

    private ScenarioStepResult CreateStepResult(
        ScenarioStep step,
        string phase,
        string metricStatus,
        double durationMs,
        int harnessDelayMs,
        object? result = null,
        string? stepStatus = null,
        ErrorInfo? error = null)
    {
        var timing = CreateTimingData(step, durationMs, harnessDelayMs);
        var metrics = _metricsCollector.CollectStep(new ScenarioStepMetricContext(step, phase, metricStatus, timing));
        return new ScenarioStepResult(
            step.Name ?? step.Action,
            step.Action,
            durationMs,
            timing,
            metrics,
            Result: result,
            Status: stepStatus,
            Error: error,
            Phase: phase);
    }

    private ScenarioRunFailureData CreateFailureData(
        ScenarioExecutionContext context,
        ScenarioStepResult failedStep,
        FailureArtifactBundle failureArtifacts)
    {
        var failureSteps = context.Steps.Concat([failedStep]).ToArray();
        var failureTiming = CreateScenarioRunTiming((_timeProvider.GetUtcNow() - context.ScenarioStarted).TotalMilliseconds, context.PrologueMs, context.ExecutedStepMs);
        return new ScenarioRunFailureData(
            context.Scenario.Name,
            context.File,
            "failed",
            failureTiming,
            CollectScenarioMetrics("failed", failureTiming, failureSteps),
            CreateFailedStepResult(context.NextStepIndex, failedStep),
            failureSteps,
            failureArtifacts,
            context.ScenarioId,
            context.Scenario.Metadata);
    }

    private ScenarioRunFailureData CreateFinalFailureData(
        ScenarioExecutionContext context,
        ScenarioRunFailureData failureData)
    {
        var failedStepIndex = failureData.FailedStep.Index - 1;
        var failedStep = failureData.Steps[failedStepIndex];
        var completedSteps = context.Steps.Take(failedStepIndex)
            .Concat([failedStep])
            .Concat(context.Steps.Skip(failedStepIndex))
            .ToArray();
        var failureTiming = CreateScenarioRunTiming((_timeProvider.GetUtcNow() - context.ScenarioStarted).TotalMilliseconds, context.PrologueMs, context.ExecutedStepMs);
        return failureData with
        {
            Timing = failureTiming,
            Metrics = CollectScenarioMetrics("failed", failureTiming, completedSteps),
            Steps = completedSteps
        };
    }

    private IReadOnlyDictionary<string, double> CollectScenarioMetrics(
        string status,
        ScenarioRunTiming timing,
        IReadOnlyList<ScenarioStepResult> steps) =>
        _metricsCollector.CollectScenario(new ScenarioScenarioMetricContext(status, timing, steps));

    private static ScenarioFailedStepResult CreateFailedStepResult(int stepIndex, ScenarioStepResult failedStep) =>
        new(
            stepIndex,
            failedStep.Step,
            failedStep.Action,
            failedStep.DurationMs,
            failedStep.Timing,
            failedStep.Phase);

    private Task EmitStepAsync(
        string eventName,
        string file,
        string scenarioId,
        string scenario,
        string phase,
        int index,
        ScenarioStep step,
        DateTimeOffset timestamp,
        string? status = null,
        double? durationMs = null,
        ErrorInfo? error = null,
        IReadOnlyDictionary<string, double>? metrics = null) =>
        EmitAsync(new ScenarioEvent(
            eventName,
            timestamp,
            status,
            File: file,
            ScenarioId: scenarioId,
            Scenario: scenario,
            Phase: phase,
            StepIndex: index,
            Step: step.Name ?? step.Action,
            Action: step.Action,
            DurationMs: durationMs,
            Metrics: metrics,
            Error: error));

    private Task EmitAsync(ScenarioEvent scenarioEvent) => _eventSink.EmitAsync(scenarioEvent);

    private async Task<FailureArtifactBundle> CaptureFailureArtifactsBestEffortAsync(FailureCaptureRequest request, Exception failure)
    {
        if (_failureArtifactCapturePolicy == ScenarioFailureArtifactCapturePolicy.Never)
        {
            return CreateEmptyFailureArtifactBundle(request, failure);
        }

        try
        {
            return await _actionHost.CaptureFailureArtifactsAsync(request, failure).ConfigureAwait(false);
        }
        catch (Exception captureException) when (!IsFatalException(captureException))
        {
            var bundle = CreateEmptyFailureArtifactBundle(request, failure);
            return bundle with { CaptureErrors = [new FailureCaptureError("failure_artifacts", captureException.Message)] };
        }
    }

    private static bool IsFatalException(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException;

    private FailureArtifactBundle CreateEmptyFailureArtifactBundle(FailureCaptureRequest request, Exception failure) =>
        new(
            ResultSchemas.FailureBundle,
            _timeProvider.GetUtcNow(),
            request.Scope,
            request.Name,
            request.File,
            request.StepIndex,
            request.StepName,
            request.Action,
            failure.GetType().FullName ?? failure.GetType().Name,
            failure.Message,
            [],
            []);

    private sealed record ScenarioExecution(
        IReadOnlyList<ScenarioStepResult> Steps,
        ScenarioRunTiming Timing);

    private sealed class ScenarioExecutionContext(
        ScenarioFile scenario,
        string file,
        string scenarioId,
        DateTimeOffset scenarioStarted,
        double prologueMs)
    {
        public ScenarioFile Scenario { get; } = scenario;

        public string File { get; } = file;

        public string ScenarioId { get; } = scenarioId;

        public DateTimeOffset ScenarioStarted { get; } = scenarioStarted;

        public double PrologueMs { get; } = prologueMs;

        public List<ScenarioStepResult> Steps { get; } = [];

        public double ExecutedStepMs { get; set; }

        public DateTimeOffset? PreviousStepStartedAt { get; set; }

        public int NextStepIndex { get; set; } = 1;
    }
}

public interface ICommandFailureDetails
{
    string CategoryOverride { get; }

    object? DataPayload { get; }
}
