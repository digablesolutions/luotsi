using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VisitLab.Cli.Errors;
using VisitLab.Cli.Infrastructure;
using VisitLab.Cli.Models;

namespace VisitLab.Cli.Scenarios;

public interface IScenarioActionHost
{
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
    Task<DeviceFingerprint> WriteDeviceFingerprintAsync();
    Task<FailureArtifactBundle> CaptureFailureArtifactsAsync(FailureCaptureRequest request, Exception exception);
}

/// <summary>
/// Loads and executes JSON scenario files.
/// </summary>
public sealed class ScenarioExecutor(IDeviceHost actionHost, IFileSystem fileSystem, TimeProvider timeProvider, IDelay delay, IEnvironmentVariables? environment = null)
{
    private static readonly HashSet<string> SupportedScenarioActions =
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
        "screenState",
        "sleep"
    ];

    private readonly IDeviceHost _actionHost = actionHost ?? throw new ArgumentNullException(nameof(actionHost));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IDelay _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    private readonly IEnvironmentVariables _environment = environment ?? new SystemEnvironmentVariables();

    /// <summary>
    /// Runs a JSON scenario playbook.
    /// </summary>
    /// <param name="file">Scenario file path.</param>
    /// <returns>Scenario result.</returns>
    public async Task<object> RunAsync(string file)
    {
        var scenarioStarted = _timeProvider.GetUtcNow();
        var scenario = ValidateScenario(ResolveTemplates(await LoadAsync(file).ConfigureAwait(false)), file);
        var steps = new List<object>();
        await _actionHost.WriteDeviceFingerprintAsync().ConfigureAwait(false);
        var prologueMs = (_timeProvider.GetUtcNow() - scenarioStarted).TotalMilliseconds;
        var executedStepMs = 0d;
        DateTimeOffset? previousStepStartedAt = null;

        for (var index = 0; index < scenario.Steps.Count; index++)
        {
            var step = scenario.Steps[index];
            using var delayScope = DelayMetrics.BeginScope();
            var started = _timeProvider.GetUtcNow();

            try
            {
                var result = await ExecuteStepAsync(step, previousStepStartedAt).ConfigureAwait(false);
                var durationMs = (_timeProvider.GetUtcNow() - started).TotalMilliseconds;
                executedStepMs += durationMs;
                previousStepStartedAt = started;

                steps.Add(new
                {
                    step = step.Name ?? step.Action,
                    action = step.Action,
                    duration_ms = durationMs,
                    timing = CreateTimingData(step, durationMs, delayScope.TotalMilliseconds),
                    result
                });
            }
            catch (Exception ex) when (step.ContinueOnError is true && ex is not UsageException)
            {
                var category = ex is ICommandFailureDetails continuedFailure ? continuedFailure.CategoryOverride : ErrorInfo.Classify(ex.Message);
                var durationMs = (_timeProvider.GetUtcNow() - started).TotalMilliseconds;
                executedStepMs += durationMs;
                previousStepStartedAt = started;
                steps.Add(new
                {
                    step = step.Name ?? step.Action,
                    action = step.Action,
                    status = "continued_on_error",
                    duration_ms = durationMs,
                    timing = CreateTimingData(step, durationMs, delayScope.TotalMilliseconds),
                    error = ErrorInfo.From(ex, category)
                });
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
                var category = ex is ICommandFailureDetails failure ? failure.CategoryOverride : ErrorInfo.Classify(ex.Message);
                throw new ScenarioStepFailureException(
                    $"Scenario '{scenario.Name}' failed at step {index + 1} ({step.Name ?? step.Action}).",
                    category,
                    new
                    {
                        scenario = scenario.Name,
                        file,
                        status = "failed",
                        timing = CreateScenarioRunTiming((_timeProvider.GetUtcNow() - scenarioStarted).TotalMilliseconds, prologueMs, executedStepMs),
                        failed_step = new
                        {
                            index = index + 1,
                            name = step.Name ?? step.Action,
                            action = step.Action,
                            duration_ms = durationMs,
                            timing = CreateTimingData(step, durationMs, delayScope.TotalMilliseconds)
                        },
                        steps,
                        failure_artifacts = failureArtifacts
                    },
                    ex);
            }
        }

        return new
        {
            scenario = scenario.Name,
            status = "passed",
            timing = CreateScenarioRunTiming((_timeProvider.GetUtcNow() - scenarioStarted).TotalMilliseconds, prologueMs, executedStepMs),
            steps
        };
    }

    private async Task<object> ExecuteStepAsync(ScenarioStep step, DateTimeOffset? previousStepStartedAt)
    {
        return step.Action switch
        {
            "waitVisible" => await _actionHost.WaitVisibleAsync(step.Text ?? throw new UsageException("waitVisible requires text."), step.TimeoutSec ?? 15).ConfigureAwait(false),
            "waitNotVisible" => await _actionHost.WaitNotVisibleAsync(step.Text ?? throw new UsageException("waitNotVisible requires text."), step.TimeoutSec ?? 15).ConfigureAwait(false),
            "tapText" => await _actionHost.TapTextAsync(step.Text ?? throw new UsageException("tapText requires text."), step.TimeoutSec ?? 15).ConfigureAwait(false),
            "tapPoint" => await _actionHost.TapPointAsync(step.Label ?? step.Name ?? step.Text, step.X, step.Y, step.XRatio, step.YRatio, step.PostTapDelayMs ?? 300).ConfigureAwait(false),
            "doubleTapHeaderLogo" => await _actionHost.DoubleTapHeaderLogoAsync().ConfigureAwait(false),
            "doubleTap" when step.HeaderLogo is true => await _actionHost.DoubleTapHeaderLogoAsync().ConfigureAwait(false),
            "typeText" => await _actionHost.TypeTextAsync(step.Text ?? throw new UsageException("typeText requires text.")).ConfigureAwait(false),
            "typePin" => await _actionHost.TypePinAsync(step.Text ?? throw new UsageException("typePin requires text."), step.IntervalMs ?? 120).ConfigureAwait(false),
            "keyevent" => await _actionHost.KeyEventAsync(step.Code ?? throw new UsageException("keyevent requires code.")).ConfigureAwait(false),
            "waitLog" => await _actionHost.WaitForLogAsync(step.Text ?? throw new UsageException("waitLog requires text."), step.TimeoutSec ?? 15).ConfigureAwait(false),
            "waitStep" => await _actionHost.WaitForStepAsync(step.Step ?? step.Text ?? throw new UsageException("waitStep requires step."), step.TimeoutSec ?? 15).ConfigureAwait(false),
            "waitActionReady" => await _actionHost.WaitForActionReadyAsync(step.Text ?? throw new UsageException("waitActionReady requires text."), step.Step, step.TimeoutSec ?? 15).ConfigureAwait(false),
            "resetLog" => await _actionHost.ResetLogAsync().ConfigureAwait(false),
            "assertEvent" => await _actionHost.AssertEventAsync(step.Event ?? step.Text ?? throw new UsageException("assertEvent requires event or text."), step.Contains ?? Array.Empty<string>(), step.DetailsPattern, step.TimeoutSec ?? 15, step.ObserveFromPreviousStep is true ? previousStepStartedAt : null).ConfigureAwait(false),
            "takeScreenshot" => await _actionHost.TakeScreenshotAsync(step.Label ?? step.Text ?? step.Name ?? throw new UsageException("takeScreenshot requires label, text, or name.")).ConfigureAwait(false),
            "captureArtifacts" => await _actionHost.CaptureArtifactsAsync(step.Label ?? step.Text ?? step.Name ?? throw new UsageException("captureArtifacts requires label, text, or name.")).ConfigureAwait(false),
            "assertTextInputReady" => await _actionHost.AssertTextInputReadyAsync(step.RequireKeyboard ?? false, step.TimeoutSec ?? 15).ConfigureAwait(false),
            "assertBelow" => await _actionHost.AssertBelowAsync(step.Text ?? throw new UsageException("assertBelow requires text."), step.Below ?? throw new UsageException("assertBelow requires below."), step.MaxGapPx ?? 260).ConfigureAwait(false),
            "assertAligned" => await _actionHost.AssertAlignedAsync(step.Text ?? throw new UsageException("assertAligned requires text."), step.With ?? throw new UsageException("assertAligned requires with."), step.MaxDeltaPx ?? 160).ConfigureAwait(false),
            "assertAppVersion" => await _actionHost.AssertAppVersionAsync(step.Package ?? step.Text, step.MaxTopInsetPx ?? 140, step.MaxRightInsetPx ?? 300).ConfigureAwait(false),
            "screenState" => await _actionHost.GetScreenStateAsync().ConfigureAwait(false),
            "sleep" => await SleepAsync(step.Milliseconds ?? 1000).ConfigureAwait(false),
            _ => throw new UsageException($"Unknown scenario action '{step.Action}'.")
        };
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

    private ScenarioFile ResolveTemplates(ScenarioFile scenario)
    {
        var resolvedVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (scenario.Variables is not null)
        {
            foreach (var key in scenario.Variables.Keys)
            {
                ResolveVariable(key, scenario.Variables, resolvedVariables, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }
        }

        return scenario with
        {
            Name = ResolveValue(scenario.Name, scenario.Variables, resolvedVariables) ?? scenario.Name,
            Steps = scenario.Steps.Select(step => step with
            {
                Name = ResolveValue(step.Name, scenario.Variables, resolvedVariables),
                Action = ResolveValue(step.Action, scenario.Variables, resolvedVariables) ?? step.Action,
                Text = ResolveValue(step.Text, scenario.Variables, resolvedVariables),
                Code = ResolveValue(step.Code, scenario.Variables, resolvedVariables),
                Step = ResolveValue(step.Step, scenario.Variables, resolvedVariables),
                Label = ResolveValue(step.Label, scenario.Variables, resolvedVariables),
                Event = ResolveValue(step.Event, scenario.Variables, resolvedVariables),
                Contains = step.Contains?.Select(value => ResolveValue(value, scenario.Variables, resolvedVariables) ?? value).ToArray(),
                DetailsPattern = ResolveValue(step.DetailsPattern, scenario.Variables, resolvedVariables),
                Below = ResolveValue(step.Below, scenario.Variables, resolvedVariables),
                With = ResolveValue(step.With, scenario.Variables, resolvedVariables),
                Package = ResolveValue(step.Package, scenario.Variables, resolvedVariables)
            }).ToArray()
        };
    }

    private static ScenarioFile ValidateScenario(ScenarioFile scenario, string file)
    {
        if (string.IsNullOrWhiteSpace(scenario.Name))
        {
            throw new UsageException($"Scenario file '{file}' must define a non-empty name.");
        }

        if (scenario.Steps is null || scenario.Steps.Count == 0)
        {
            throw new UsageException($"Scenario file '{file}' must define at least one step.");
        }

        for (var index = 0; index < scenario.Steps.Count; index++)
        {
            ValidateStep(scenario, scenario.Steps[index], index + 1);
        }

        return scenario;
    }

    private static void ValidateStep(ScenarioFile scenario, ScenarioStep step, int index)
    {
        var stepLabel = $"Scenario '{scenario.Name}' step {index}";
        var action = step.Action.Trim();

        if (string.IsNullOrWhiteSpace(step.Action))
        {
            throw new UsageException($"{stepLabel} must define a non-empty action.");
        }

        if (!SupportedScenarioActions.Contains(action) ||
            (string.Equals(action, "doubleTap", StringComparison.OrdinalIgnoreCase) && step.HeaderLogo is not true))
        {
            throw new UsageException($"Unknown scenario action '{step.Action}'.");
        }

        ValidatePositive(step.TimeoutSec, $"{stepLabel} timeoutSec");
        ValidateNonNegative(step.Milliseconds, $"{stepLabel} milliseconds");
        ValidateNonNegative(step.PostTapDelayMs, $"{stepLabel} postTapDelayMs");
        ValidateNonNegative(step.IntervalMs, $"{stepLabel} intervalMs");
        ValidateNonNegative(step.MaxGapPx, $"{stepLabel} maxGapPx");
        ValidateNonNegative(step.MaxDeltaPx, $"{stepLabel} maxDeltaPx");
        ValidateNonNegative(step.MaxTopInsetPx, $"{stepLabel} maxTopInsetPx");
        ValidateNonNegative(step.MaxRightInsetPx, $"{stepLabel} maxRightInsetPx");

        if (step.X is < 0 || step.Y is < 0)
        {
            throw new UsageException($"{stepLabel} coordinates must be zero or greater.");
        }

        if (step.XRatio is { } xRatio && (xRatio < 0 || xRatio > 1) ||
            step.YRatio is { } yRatio && (yRatio < 0 || yRatio > 1))
        {
            throw new UsageException($"{stepLabel} xRatio/yRatio must be between 0 and 1.");
        }

        switch (action)
        {
            case "waitVisible":
            case "waitNotVisible":
            case "tapText":
            case "typeText":
            case "waitLog":
                RequireScenarioValue(step.Text, $"{stepLabel} {action} requires text.");
                break;

            case "typePin":
                RequireScenarioValue(step.Text, $"{stepLabel} typePin requires text.");
                if (step.Text!.Any(static digit => !char.IsDigit(digit)))
                {
                    throw new UsageException($"{stepLabel} typePin supports digits only.");
                }

                break;

            case "keyevent":
                RequireScenarioValue(step.Code, $"{stepLabel} keyevent requires code.");
                break;

            case "waitStep":
                if (string.IsNullOrWhiteSpace(step.Step) && string.IsNullOrWhiteSpace(step.Text))
                {
                    throw new UsageException($"{stepLabel} waitStep requires step.");
                }

                break;

            case "waitActionReady":
                RequireScenarioValue(step.Text, $"{stepLabel} waitActionReady requires text.");
                break;

            case "assertEvent":
                if (string.IsNullOrWhiteSpace(step.Event) && string.IsNullOrWhiteSpace(step.Text))
                {
                    throw new UsageException($"{stepLabel} assertEvent requires event or text.");
                }

                ValidateRegex(step.DetailsPattern, $"{stepLabel} assertEvent detailsPattern is not a valid regular expression");
                break;

            case "takeScreenshot":
                if (string.IsNullOrWhiteSpace(step.Label) && string.IsNullOrWhiteSpace(step.Text) && string.IsNullOrWhiteSpace(step.Name))
                {
                    throw new UsageException($"{stepLabel} takeScreenshot requires label, text, or name.");
                }

                break;

            case "captureArtifacts":
                if (string.IsNullOrWhiteSpace(step.Label) && string.IsNullOrWhiteSpace(step.Text) && string.IsNullOrWhiteSpace(step.Name))
                {
                    throw new UsageException($"{stepLabel} captureArtifacts requires label, text, or name.");
                }

                break;

            case "assertBelow":
                RequireScenarioValue(step.Text, $"{stepLabel} assertBelow requires text.");
                RequireScenarioValue(step.Below, $"{stepLabel} assertBelow requires below.");
                break;

            case "assertAligned":
                RequireScenarioValue(step.Text, $"{stepLabel} assertAligned requires text.");
                RequireScenarioValue(step.With, $"{stepLabel} assertAligned requires with.");
                break;
        }

        if (string.Equals(action, "tapPoint", StringComparison.OrdinalIgnoreCase))
        {
            var hasAbsolutePoint = step.X.HasValue || step.Y.HasValue;
            var hasRelativePoint = step.XRatio.HasValue || step.YRatio.HasValue;

            if (step.X.HasValue != step.Y.HasValue)
            {
                throw new UsageException($"{stepLabel} tapPoint requires both x and y when using absolute coordinates.");
            }

            if (step.XRatio.HasValue != step.YRatio.HasValue)
            {
                throw new UsageException($"{stepLabel} tapPoint requires both xRatio and yRatio when using relative coordinates.");
            }

            if (!hasAbsolutePoint && !hasRelativePoint)
            {
                throw new UsageException($"{stepLabel} tapPoint requires x/y or xRatio/yRatio.");
            }
        }

        if (string.Equals(action, "assertEvent", StringComparison.OrdinalIgnoreCase) &&
            step.ObserveFromPreviousStep is true &&
            index == 1)
        {
            throw new UsageException($"{stepLabel} assertEvent cannot observe from the previous step when it is the first step.");
        }
    }

    private static void ValidatePositive(int? value, string label)
    {
        if (value is <= 0)
        {
            throw new UsageException($"{label} must be greater than zero.");
        }
    }

    private static void ValidateNonNegative(int? value, string label)
    {
        if (value is < 0)
        {
            throw new UsageException($"{label} must be zero or greater.");
        }
    }

    private static void RequireScenarioValue(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new UsageException(message);
        }
    }

    private static void ValidateRegex(string? pattern, string messagePrefix)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return;
        }

        try
        {
            _ = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException ex)
        {
            throw new UsageException($"{messagePrefix}: {ex.Message}");
        }
    }

    private string ResolveVariable(
        string name,
        IReadOnlyDictionary<string, string> variables,
        IDictionary<string, string> resolvedVariables,
        ISet<string> stack)
    {
        if (resolvedVariables.TryGetValue(name, out var resolved))
        {
            return resolved;
        }

        if (!variables.TryGetValue(name, out var template))
        {
            throw new UsageException($"Scenario variable '{name}' is not defined.");
        }

        if (!stack.Add(name))
        {
            throw new UsageException($"Scenario variable '{name}' is part of a cycle.");
        }

        resolved = ResolveValue(template, variables, resolvedVariables, stack) ?? string.Empty;
        stack.Remove(name);
        resolvedVariables[name] = resolved;
        return resolved;
    }

    private string? ResolveValue(
        string? value,
        IReadOnlyDictionary<string, string>? variables,
        IDictionary<string, string> resolvedVariables,
        ISet<string>? stack = null)
    {
        if (value is null)
        {
            return null;
        }

        var builder = new StringBuilder();

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '$' && index + 1 < value.Length && value[index + 1] == '{')
            {
                var endIndex = FindPlaceholderEnd(value, index + 2);
                if (endIndex < 0)
                {
                    throw new UsageException($"Scenario template '{value}' has an unterminated placeholder.");
                }

                var token = value[(index + 2)..endIndex];
                builder.Append(ResolvePlaceholder(token, variables, resolvedVariables, stack ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
                index = endIndex;
                continue;
            }

            builder.Append(value[index]);
        }

        return builder.ToString();
    }

    private string ResolvePlaceholder(
        string token,
        IReadOnlyDictionary<string, string>? variables,
        IDictionary<string, string> resolvedVariables,
        ISet<string> stack)
    {
        if (token.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
        {
            var expression = token[4..];
            var splitIndex = FindTopLevelSeparator(expression, '|');
            var envName = splitIndex >= 0 ? expression[..splitIndex] : expression;
            var fallback = splitIndex >= 0 ? expression[(splitIndex + 1)..] : null;
            var envValue = _environment.GetEnvironmentVariable(envName);

            if (!string.IsNullOrWhiteSpace(envValue))
            {
                return envValue;
            }

            if (fallback is not null)
            {
                return ResolveValue(fallback, variables, resolvedVariables, stack) ?? string.Empty;
            }

            throw new UsageException($"Scenario requires environment variable '{envName}'.");
        }

        if (token.StartsWith("now:", StringComparison.OrdinalIgnoreCase))
        {
            return _timeProvider.GetUtcNow().ToLocalTime().ToString(token[4..], CultureInfo.InvariantCulture);
        }

        if (token.StartsWith("var:", StringComparison.OrdinalIgnoreCase))
        {
            if (variables is null)
            {
                throw new UsageException($"Scenario variable placeholder '{token}' has no variables block.");
            }

            return ResolveVariable(token[4..], variables, resolvedVariables, stack);
        }

        throw new UsageException($"Unsupported scenario placeholder '${{{token}}}'.");
    }

    private static int FindPlaceholderEnd(string value, int startIndex)
    {
        var depth = 1;

        for (var index = startIndex; index < value.Length; index++)
        {
            if (value[index] == '$' && index + 1 < value.Length && value[index + 1] == '{')
            {
                depth++;
                index++;
                continue;
            }

            if (value[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static int FindTopLevelSeparator(string value, char separator)
    {
        var depth = 0;

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '$' && index + 1 < value.Length && value[index + 1] == '{')
            {
                depth++;
                index++;
                continue;
            }

            if (value[index] == '}' && depth > 0)
            {
                depth--;
                continue;
            }

            if (depth == 0 && value[index] == separator)
            {
                return index;
            }
        }

        return -1;
    }

    private async Task<SleepResult> SleepAsync(int milliseconds)
    {
        if (milliseconds < 0)
        {
            throw new UsageException("sleep requires milliseconds zero or greater.");
        }

        await _delay.DelayAsync(milliseconds).ConfigureAwait(false);
        return new SleepResult(milliseconds);
    }

    private static object CreateTimingData(ScenarioStep step, double durationMs, int harnessDelayMs)
    {
        var configuredDelayMs = GetConfiguredDelayMs(step);
        return new
        {
            total_ms = durationMs,
            harness_delay_ms = harnessDelayMs,
            configured_delay_ms = configuredDelayMs,
            non_delay_ms = Math.Max(0, durationMs - harnessDelayMs)
        };
    }

    private static object CreateScenarioRunTiming(double totalMs, double prologueMs, double executedStepMs) => new
    {
        total_ms = totalMs,
        prologue_ms = prologueMs,
        steps_ms = executedStepMs,
        non_step_ms = Math.Max(0, totalMs - executedStepMs)
    };

    private static int? GetConfiguredDelayMs(ScenarioStep step) => step.Action switch
    {
        "sleep" => Math.Max(0, step.Milliseconds ?? 1000),
        "tapPoint" => Math.Max(0, step.PostTapDelayMs ?? 300),
        "typePin" when !string.IsNullOrWhiteSpace(step.Text) => Math.Max(0, step.IntervalMs ?? 120) * step.Text.Count(char.IsDigit),
        _ => null
    };
}

public interface ICommandFailureDetails
{
    string CategoryOverride { get; }

    object? DataPayload { get; }
}