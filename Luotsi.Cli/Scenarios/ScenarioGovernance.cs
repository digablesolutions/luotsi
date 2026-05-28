using Luotsi.Cli.Models;

namespace Luotsi.Cli.Scenarios;

public sealed record ScenarioGovernanceVerdict(
    string Kind,
    string Confidence,
    string Summary,
    bool RegressionCandidate,
    bool InfrastructureRelated,
    bool QuarantineCandidate,
    string? RecommendedAction = null);

internal static class ScenarioGovernanceClassifier
{
    public static ScenarioGovernanceVerdict FromScenarioResult(ScenarioRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return FromStatus(result.Status, result.DeviceAllocation);
    }

    public static ScenarioGovernanceVerdict FromFailureData(ScenarioRunFailureData data, ScenarioDeviceAllocation? allocation = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return FromError(data.Steps[data.FailedStep.Index - 1].Error, allocation, data.FailedStep);
    }

    public static ScenarioGovernanceVerdict FromBatchItem(ScenarioBatchItemResult item, ScenarioDeviceAllocation? allocation = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Data is not null)
        {
            return FromFailureData(item.Data, allocation);
        }

        if (item.Error is not null)
        {
            return FromError(item.Error, allocation);
        }

        return FromStatus(item.Status, allocation);
    }

    public static ScenarioGovernanceVerdict FromBatch(ScenarioRunBatchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Scenarios.Count == 0 || result.FailedCount == 0)
        {
            return FromStatus(result.Status, result.DeviceAllocation);
        }

        var failedVerdicts = result.Scenarios
            .Where(static scenario => string.Equals(scenario.Status, "failed", StringComparison.OrdinalIgnoreCase))
            .Select(static scenario => scenario.Governance)
            .Where(static verdict => verdict is not null)
            .Cast<ScenarioGovernanceVerdict>()
            .ToArray();
        if (failedVerdicts.Length == 0)
        {
            return CreateUnknownFailure("The run failed without a classified scenario-level governance verdict.");
        }

        var kinds = failedVerdicts
            .Select(static verdict => verdict.Kind)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (kinds.Length == 1)
        {
            return failedVerdicts[0];
        }

        return new ScenarioGovernanceVerdict(
            "mixed_failure",
            "medium",
            $"The run failed with mixed governance signals: {string.Join(", ", kinds.OrderBy(static kind => kind, StringComparer.OrdinalIgnoreCase))}.",
            failedVerdicts.Any(static verdict => verdict.RegressionCandidate),
            failedVerdicts.Any(static verdict => verdict.InfrastructureRelated),
            failedVerdicts.Any(static verdict => verdict.QuarantineCandidate),
            "Inspect the failed scenarios individually before deciding whether this is a product regression, lab issue, or setup problem.");
    }

    public static ScenarioGovernanceVerdict FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return FromError(
            ScenarioErrorInfo.From(exception),
            ScenarioFailureDetails.TryGetDeviceAllocation(exception),
            ScenarioFailureDetails.TryGetData(exception)?.FailedStep);
    }

    public static ScenarioGovernanceVerdict FromStatus(string status, ScenarioDeviceAllocation? allocation)
    {
        if (string.Equals(status, "passed", StringComparison.OrdinalIgnoreCase))
        {
            return new ScenarioGovernanceVerdict(
                "passed",
                "high",
                allocation?.Serial is null
                    ? "The scenario run passed."
                    : $"The scenario run passed on device '{allocation.Serial}'.",
                false,
                false,
                false,
                null);
        }

        if (string.Equals(status, "validated", StringComparison.OrdinalIgnoreCase))
        {
            return new ScenarioGovernanceVerdict(
                "validated",
                "high",
                "The scenario validated successfully without executing device actions.",
                false,
                false,
                false,
                "Run the scenario against a device when you need a production signal.");
        }

        if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return CreateUnknownFailure("The scenario run failed without a classified governance signal.");
        }

        return new ScenarioGovernanceVerdict(
            "non_failure_status",
            "medium",
            $"The scenario completed with status '{status}'.",
            false,
            false,
            false,
            null);
    }

    public static ScenarioGovernanceVerdict FromError(
        ErrorInfo? error,
        ScenarioDeviceAllocation? allocation = null,
        ScenarioFailedStepResult? failedStep = null)
    {
        if (error is null)
        {
            return CreateUnknownFailure("The scenario failed without structured error details.");
        }

        if (IsObservableFailure(error.Category))
        {
            var summary = failedStep is null
                ? "The run reached scenario assertions and failed on observable app or UI behavior."
                : $"The run reached scenario assertions and failed at step {failedStep.Index} ({failedStep.Name}).";
            return new ScenarioGovernanceVerdict(
                "scenario_observable_failure",
                "medium",
                summary,
                true,
                false,
                false,
                "Inspect the failure capsule and compare this run against the latest healthy replay.");
        }

        if (string.Equals(error.Category, "usage_error", StringComparison.OrdinalIgnoreCase))
        {
            return new ScenarioGovernanceVerdict(
                "environment_failure",
                "high",
                "The run failed because the Luotsi command or scenario input was invalid.",
                false,
                false,
                false,
                "Fix the command arguments or scenario file, then rerun.");
        }

        if (string.Equals(error.Category, "configuration_error", StringComparison.OrdinalIgnoreCase))
        {
            if (LooksLikeLabInfrastructureFailure(error, allocation))
            {
                var serialSuffix = string.IsNullOrWhiteSpace(allocation?.Serial) ? string.Empty : $" for device '{allocation.Serial}'";
                return new ScenarioGovernanceVerdict(
                    "lab_infrastructure_failure",
                    "high",
                    $"The run failed before a trustworthy product verdict because the selected device or ADB transport was not healthy{serialSuffix}.",
                    false,
                    true,
                    IsQuarantineCandidate(allocation),
                    IsQuarantineCandidate(allocation)
                        ? "Repair or quarantine the unhealthy device before rerunning."
                        : "Repair the device or ADB transport, then rerun.");
            }

            return new ScenarioGovernanceVerdict(
                "environment_failure",
                "high",
                "The run failed because the host or scenario environment was not configured correctly.",
                false,
                false,
                false,
                "Fix the missing dependency, package, path, or scenario setup before rerunning.");
        }

        if (string.Equals(error.Category, "scenario_error", StringComparison.OrdinalIgnoreCase))
        {
            if (failedStep is not null && string.Equals(failedStep.Phase, ScenarioStepPhases.Main, StringComparison.OrdinalIgnoreCase))
            {
                return new ScenarioGovernanceVerdict(
                    "scenario_observable_failure",
                    "low",
                    $"The run failed during main step {failedStep.Index} ({failedStep.Name}) after reaching the scenario body.",
                    true,
                    false,
                    false,
                    "Inspect the failure capsule and compare this run against the latest healthy replay before trusting it as a regression signal.");
            }

            return new ScenarioGovernanceVerdict(
                "harness_failure",
                "low",
                "The run failed inside Luotsi or the scenario harness without a more specific product or lab classification.",
                false,
                false,
                false,
                "Inspect the failure details, reports, and event stream before trusting this run as a product signal.");
        }

        return CreateUnknownFailure($"The run failed with unclassified category '{error.Category}'.");
    }

    private static bool IsObservableFailure(string category) =>
        string.Equals(category, "selector_or_screen_state", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(category, "screen_state_unavailable", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(category, "log_wait_timeout", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(category, "oracle_timeout", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeLabInfrastructureFailure(ErrorInfo error, ScenarioDeviceAllocation? allocation)
    {
        if (allocation?.Device is { } device && !string.Equals(device.Availability, "available", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var message = error.Message;
        return message.Contains("adb", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("device offline", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("wait-for-device", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("readiness", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Selected device", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsQuarantineCandidate(ScenarioDeviceAllocation? allocation)
    {
        var state = allocation?.Device?.State;
        return !string.IsNullOrWhiteSpace(allocation?.Serial) &&
               (string.Equals(state, "offline", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(state, "unauthorized", StringComparison.OrdinalIgnoreCase));
    }

    private static ScenarioGovernanceVerdict CreateUnknownFailure(string summary) =>
        new(
            "unknown_failure",
            "low",
            summary,
            false,
            false,
            false,
            "Inspect the failure details and reports before treating this as a trustworthy regression signal.");
}
