using System.Text.Json;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli.Routing;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Serialization;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Scenarios;

public sealed record ScenarioDeviceHealthSnapshot(
    string Schema,
    string Serial,
    string State,
    DateTimeOffset UpdatedAt,
    int WindowDays,
    int ObservationCount,
    int InfrastructureFailureCount,
    int ConsecutiveInfrastructureFailureCount,
    int ConsecutiveHealthyRunCount,
    int RetryBudget,
    int RemainingRetryBudget,
    int PassThreshold,
    bool PassThresholdSatisfied,
    bool AutoQuarantined,
    string? LastGovernanceKind = null,
    string? QuarantineReason = null,
    string? RegistryFile = null,
    string? QuarantineFile = null);

public sealed record ScenarioCiPolicyResult(
    string Mode,
    string Outcome,
    int RecommendedExitCode,
    bool ExitCodeApplied,
    int RetryBudget,
    int RemainingRetryBudget,
    int PassThreshold,
    bool PassThresholdSatisfied,
    bool RetryRecommended,
    bool QuarantineEnforced,
    string Summary);

internal sealed record ScenarioDeviceHealthObservation(
    DateTimeOffset ObservedAt,
    string Status,
    string GovernanceKind,
    bool InfrastructureRelated,
    bool QuarantineCandidate);

internal sealed record ScenarioDeviceHealthRecord(
    string Schema,
    string Serial,
    string State,
    DateTimeOffset UpdatedAt,
    int WindowDays,
    int RetryBudget,
    int PassThreshold,
    IReadOnlyList<ScenarioDeviceHealthObservation> Observations);

internal sealed class ScenarioDeviceHealthRegistry(IFileSystem fileSystem, TimeProvider timeProvider, IEnvironmentVariables environment)
{
    private const string Schema = "luotsi-device-health.v1";
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    public async Task<ScenarioDeviceHealthSnapshot> RecordAsync(
        string serial,
        string status,
        ScenarioGovernanceVerdict governance,
        ScenarioRunConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(governance);
        ArgumentNullException.ThrowIfNull(configuration);

        var now = _timeProvider.GetUtcNow();
        var path = GetRecordPath(serial);
        var record = Read(path) ?? new ScenarioDeviceHealthRecord(
            Schema,
            serial,
            "healthy",
            now,
            configuration.DeviceHealthWindowDays,
            configuration.RetryBudget,
            configuration.PassThreshold,
            []);

        var cutoff = now.AddDays(-configuration.DeviceHealthWindowDays);
        var observations = record.Observations
            .Where(observation => observation.ObservedAt >= cutoff)
            .Append(new ScenarioDeviceHealthObservation(
                now,
                status,
                governance.Kind,
                governance.InfrastructureRelated,
                governance.QuarantineCandidate))
            .OrderBy(observation => observation.ObservedAt)
            .ToArray();

        var infrastructureFailureCount = observations.Count(static observation => observation.InfrastructureRelated);
        var consecutiveInfrastructureFailureCount = CountTrailing(observations, static observation => observation.InfrastructureRelated);
        var consecutiveHealthyRunCount = CountTrailing(observations, static observation => IsHealthyRun(observation.Status, observation.InfrastructureRelated));
        var autoQuarantined = governance.InfrastructureRelated &&
            (governance.QuarantineCandidate || infrastructureFailureCount > configuration.RetryBudget);
        var state = DetermineState(record.State, autoQuarantined, governance.InfrastructureRelated, consecutiveHealthyRunCount, configuration.PassThreshold);
        var summary = BuildQuarantineReason(serial, configuration, infrastructureFailureCount, governance);
        var updatedRecord = new ScenarioDeviceHealthRecord(
            Schema,
            serial,
            state,
            now,
            configuration.DeviceHealthWindowDays,
            configuration.RetryBudget,
            configuration.PassThreshold,
            observations);

        _fileSystem.CreateDirectory(GetRegistryRoot());
        var tempPath = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var stream = _fileSystem.OpenWrite(tempPath, overwrite: true))
            {
                await JsonSerializer.SerializeAsync(stream, updatedRecord, AppJson.Options).ConfigureAwait(false);
            }

            _fileSystem.CopyFile(tempPath, path, overwrite: true);
        }
        finally
        {
            if (_fileSystem.FileExists(tempPath))
            {
                _fileSystem.DeleteFile(tempPath);
            }
        }

        return new ScenarioDeviceHealthSnapshot(
            Schema,
            serial,
            state,
            now,
            configuration.DeviceHealthWindowDays,
            observations.Length,
            infrastructureFailureCount,
            consecutiveInfrastructureFailureCount,
            consecutiveHealthyRunCount,
            configuration.RetryBudget,
            Math.Max(0, configuration.RetryBudget - infrastructureFailureCount),
            configuration.PassThreshold,
            consecutiveHealthyRunCount >= configuration.PassThreshold,
            autoQuarantined,
            governance.Kind,
            autoQuarantined ? summary : null,
            path);
    }

    private ScenarioDeviceHealthRecord? Read(string path)
    {
        if (!_fileSystem.FileExists(path))
        {
            return null;
        }

        try
        {
            using var stream = _fileSystem.OpenRead(path);
            return JsonSerializer.Deserialize<ScenarioDeviceHealthRecord>(stream, AppJson.Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private string GetRegistryRoot() =>
        Path.Join(ArtifactWorkspacePaths.ResolveDefaultWorkspaceRoot(_fileSystem, _environment), "lab", "device-health");

    private string GetRecordPath(string serial) =>
        Path.Join(GetRegistryRoot(), Slugify(serial) + ".json");

    private static string DetermineState(
        string previousState,
        bool autoQuarantined,
        bool infrastructureRelated,
        int consecutiveHealthyRunCount,
        int passThreshold)
    {
        if (autoQuarantined)
        {
            return "quarantined";
        }

        if (infrastructureRelated)
        {
            return "suspect";
        }

        if (string.Equals(previousState, "quarantined", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(previousState, "suspect", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(previousState, "recovering", StringComparison.OrdinalIgnoreCase))
        {
            return consecutiveHealthyRunCount >= passThreshold ? "healthy" : "recovering";
        }

        return "healthy";
    }

    private static bool IsHealthyRun(string status, bool infrastructureRelated) =>
        !infrastructureRelated &&
        (string.Equals(status, "passed", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(status, "validated", StringComparison.OrdinalIgnoreCase));

    private static int CountTrailing(
        IReadOnlyList<ScenarioDeviceHealthObservation> observations,
        Func<ScenarioDeviceHealthObservation, bool> predicate)
    {
        var count = 0;
        for (var index = observations.Count - 1; index >= 0; index--)
        {
            if (!predicate(observations[index]))
            {
                break;
            }

            count++;
        }

        return count;
    }

    private static string BuildQuarantineReason(
        string serial,
        ScenarioRunConfiguration configuration,
        int infrastructureFailureCount,
        ScenarioGovernanceVerdict governance)
    {
        if (governance.QuarantineCandidate)
        {
            return $"Device '{serial}' was automatically quarantined after a direct quarantine-candidate governance verdict ({governance.Kind}).";
        }

        return $"Device '{serial}' was automatically quarantined after {infrastructureFailureCount} infrastructure-related failures within the last {configuration.DeviceHealthWindowDays} days, exceeding retry budget {configuration.RetryBudget}.";
    }

    private static string Slugify(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-');
        }

        return builder.ToString().Trim('-');
    }
}

internal static class ScenarioCiPolicyEvaluator
{
    public static ScenarioCiPolicyResult Evaluate(
        ScenarioGovernanceVerdict governance,
        ScenarioDeviceHealthSnapshot? deviceHealth,
        ScenarioRunConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(governance);
        ArgumentNullException.ThrowIfNull(configuration);

        var mode = FormatMode(configuration.CiPolicyMode);
        var exitCodeApplied = configuration.CiPolicyMode == ScenarioCiPolicyMode.Enforced;
        var outcome = ResolveOutcome(governance, deviceHealth);
        var recommendedExitCode = ResolveExitCode(outcome);
        var retryRecommended = configuration.RetryBudget > 0 &&
            (string.Equals(outcome, "retryable_lab_failure", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(outcome, "device_quarantined", StringComparison.OrdinalIgnoreCase));
        var passThresholdSatisfied = deviceHealth?.PassThresholdSatisfied ?? true;
        return new ScenarioCiPolicyResult(
            mode,
            outcome,
            recommendedExitCode,
            exitCodeApplied,
            configuration.RetryBudget,
            deviceHealth?.RemainingRetryBudget ?? configuration.RetryBudget,
            configuration.PassThreshold,
            passThresholdSatisfied,
            retryRecommended,
            deviceHealth?.AutoQuarantined ?? false,
            BuildSummary(outcome, governance, deviceHealth, configuration));
    }

    private static string ResolveOutcome(ScenarioGovernanceVerdict governance, ScenarioDeviceHealthSnapshot? deviceHealth)
    {
        if (deviceHealth?.AutoQuarantined == true)
        {
            return "device_quarantined";
        }

        return governance.Kind switch
        {
            "passed" or "validated" => "pass",
            "scenario_observable_failure" => "product_regression_candidate",
            "lab_infrastructure_failure" => "retryable_lab_failure",
            "environment_failure" => "environment_failure",
            "harness_failure" or "unknown_failure" => "harness_failure",
            "mixed_failure" => "mixed_failure",
            _ when governance.InfrastructureRelated => "retryable_lab_failure",
            _ when governance.RegressionCandidate => "product_regression_candidate",
            _ => "non_product_failure"
        };
    }

    private static int ResolveExitCode(string outcome) =>
        outcome switch
        {
            "pass" => 0,
            "product_regression_candidate" => 1,
            "environment_failure" => 3,
            "harness_failure" => 4,
            "mixed_failure" => 5,
            "retryable_lab_failure" or "device_quarantined" => 20,
            _ => 1
        };

    private static string BuildSummary(
        string outcome,
        ScenarioGovernanceVerdict governance,
        ScenarioDeviceHealthSnapshot? deviceHealth,
        ScenarioRunConfiguration configuration) =>
        outcome switch
        {
            "pass" => "The run satisfied the current governance-aware CI policy.",
            "product_regression_candidate" => "The run looks like an observable product regression candidate and should block CI.",
            "retryable_lab_failure" => $"The run failed with infrastructure-related governance. Retry budget is {configuration.RetryBudget}, and device health is {(deviceHealth?.State ?? "unknown")}.",
            "device_quarantined" => deviceHealth?.QuarantineReason
                ?? "The selected device is now quarantined and should be repaired before running CI again.",
            "environment_failure" => "The run failed because the host or scenario environment is not configured correctly.",
            "harness_failure" => "The run failed inside Luotsi or the scenario harness, so CI should not treat it as a trustworthy product verdict.",
            "mixed_failure" => $"The run contains mixed governance signals ({governance.Kind}); inspect individual failures before deciding CI disposition.",
            _ => $"The run completed with governance kind '{governance.Kind}'."
        };

    private static string FormatMode(ScenarioCiPolicyMode mode) =>
        mode.ToString().ToLowerInvariant();
}

internal sealed class ScenarioGovernancePolicyCoordinator(
    ScenarioDeviceHealthRegistry deviceHealthRegistry,
    LabQuarantineStore quarantineStore)
{
    private const string DeviceHealthArtifactFileName = "device-health.json";
    private const string CiPolicyArtifactFileName = "ci-policy.json";
    private const string PolicyIoWarningCode = "device_health_policy_io";
    private readonly ScenarioDeviceHealthRegistry _deviceHealthRegistry = deviceHealthRegistry ?? throw new ArgumentNullException(nameof(deviceHealthRegistry));
    private readonly LabQuarantineStore _quarantineStore = quarantineStore ?? throw new ArgumentNullException(nameof(quarantineStore));

    public async Task<ScenarioRunResult> ApplyAsync(ScenarioRunResult result, ScenarioRunConfiguration configuration, ArtifactSession? artifacts = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(configuration);

        ScenarioDeviceHealthSnapshot? deviceHealth;
        try
        {
            deviceHealth = await UpdateDeviceHealthAsync(result.DeviceAllocation, result.Status, result.Governance, configuration).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPolicyIoException(ex))
        {
            return AppendPolicyWarning(
                result,
                CreateStatePersistenceWarning(ex),
                deviceHealth: null,
                ciPolicy: null);
        }

        var ciPolicy = result.Governance is null || configuration.CiPolicyMode == ScenarioCiPolicyMode.Off
            ? null
            : ScenarioCiPolicyEvaluator.Evaluate(result.Governance, deviceHealth, configuration);
        try
        {
            await WriteArtifactsAsync(artifacts, deviceHealth, ciPolicy).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPolicyIoException(ex))
        {
            return AppendPolicyWarning(
                result,
                CreateArtifactPersistenceWarning(ex),
                deviceHealth,
                ciPolicy);
        }

        return result with
        {
            DeviceHealth = deviceHealth,
            CiPolicy = ciPolicy
        };
    }

    public async Task<ScenarioRunBatchResult> ApplyAsync(ScenarioRunBatchResult result, ScenarioRunConfiguration configuration, ArtifactSession? artifacts = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(configuration);

        ScenarioDeviceHealthSnapshot? deviceHealth;
        try
        {
            deviceHealth = await UpdateDeviceHealthAsync(result.DeviceAllocation, result.Status, result.Governance, configuration).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPolicyIoException(ex))
        {
            return AppendPolicyWarning(
                result,
                CreateStatePersistenceWarning(ex),
                deviceHealth: null,
                ciPolicy: null);
        }

        var ciPolicy = result.Governance is null || configuration.CiPolicyMode == ScenarioCiPolicyMode.Off
            ? null
            : ScenarioCiPolicyEvaluator.Evaluate(result.Governance, deviceHealth, configuration);
        try
        {
            await WriteArtifactsAsync(artifacts, deviceHealth, ciPolicy).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPolicyIoException(ex))
        {
            return AppendPolicyWarning(
                result,
                CreateArtifactPersistenceWarning(ex),
                deviceHealth,
                ciPolicy);
        }

        return result with
        {
            DeviceHealth = deviceHealth,
            CiPolicy = ciPolicy
        };
    }

    public async Task<Exception> ApplyAsync(Exception exception, ScenarioRunConfiguration configuration, ArtifactSession? artifacts = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(configuration);

        var failureData = ScenarioFailureDetails.TryGetData(exception);
        var governance = failureData?.Governance ?? ScenarioGovernanceClassifier.FromException(exception);
        ScenarioDeviceHealthSnapshot? deviceHealth;
        try
        {
            deviceHealth = await UpdateDeviceHealthAsync(
                    ScenarioFailureDetails.TryGetDeviceAllocation(exception),
                    failureData?.Status ?? "failed",
                    governance,
                    configuration)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPolicyIoException(ex))
        {
            AttachPolicyWarning(exception, failureData, CreateStatePersistenceWarning(ex), governance, deviceHealth: null, ciPolicy: null);
            return exception;
        }

        var ciPolicy = configuration.CiPolicyMode == ScenarioCiPolicyMode.Off
            ? null
            : ScenarioCiPolicyEvaluator.Evaluate(governance, deviceHealth, configuration);
        try
        {
            await WriteArtifactsAsync(artifacts, deviceHealth, ciPolicy).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPolicyIoException(ex))
        {
            AttachPolicyWarning(exception, failureData, CreateArtifactPersistenceWarning(ex), governance, deviceHealth, ciPolicy);
            return exception;
        }

        AttachPolicyWarning(exception, failureData, warning: null, governance, deviceHealth, ciPolicy);
        return exception;
    }

    private async Task<ScenarioDeviceHealthSnapshot?> UpdateDeviceHealthAsync(
        ScenarioDeviceAllocation? allocation,
        string status,
        ScenarioGovernanceVerdict? governance,
        ScenarioRunConfiguration configuration)
    {
        if (governance is null || string.IsNullOrWhiteSpace(allocation?.Serial))
        {
            return null;
        }

        var deviceHealth = await _deviceHealthRegistry.RecordAsync(allocation.Serial!, status, governance, configuration).ConfigureAwait(false);
        LabQuarantineResult? quarantine = null;
        if (deviceHealth.AutoQuarantined)
        {
            quarantine = await _quarantineStore.QuarantineAsync(
                allocation.Serial!,
                deviceHealth.QuarantineReason ?? "Automatically quarantined by Luotsi device health policy.",
                "luotsi-policy",
                "automatic").ConfigureAwait(false);
        }
        else if (string.Equals(deviceHealth.State, "healthy", StringComparison.OrdinalIgnoreCase))
        {
            await _quarantineStore.ReleaseAutomaticAsync(allocation.Serial!).ConfigureAwait(false);
        }

        quarantine ??= _quarantineStore.TryGetBySerial(allocation.Serial!);
        return quarantine is null
            ? deviceHealth
            : deviceHealth with { QuarantineFile = quarantine.QuarantineFile };
    }

    private static async Task WriteArtifactsAsync(
        ArtifactSession? artifacts,
        ScenarioDeviceHealthSnapshot? deviceHealth,
        ScenarioCiPolicyResult? ciPolicy)
    {
        if (artifacts is null)
        {
            return;
        }

        if (deviceHealth is not null)
        {
            await artifacts.WriteJsonAsync(DeviceHealthArtifactFileName, deviceHealth).ConfigureAwait(false);
        }

        if (ciPolicy is not null)
        {
            await artifacts.WriteJsonAsync(CiPolicyArtifactFileName, ciPolicy).ConfigureAwait(false);
        }
    }

    private static bool IsPolicyIoException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException;

    private static string CreateStatePersistenceWarning(Exception exception) =>
        $"Luotsi could not persist device health policy state, so device_health and ci_policy were omitted. {exception.Message}".Trim();

    private static string CreateArtifactPersistenceWarning(Exception exception) =>
        $"Luotsi could not write device health policy artifacts. device_health and ci_policy remain in the result, but the artifact sidecars are incomplete. {exception.Message}".Trim();

    private static ScenarioRunResult AppendPolicyWarning(
        ScenarioRunResult result,
        string warning,
        ScenarioDeviceHealthSnapshot? deviceHealth,
        ScenarioCiPolicyResult? ciPolicy) =>
        result with
        {
            MetadataWarnings = AppendWarning(result.MetadataWarnings, warning),
            DeviceHealth = deviceHealth,
            CiPolicy = ciPolicy
        };

    private static ScenarioRunBatchResult AppendPolicyWarning(
        ScenarioRunBatchResult result,
        string warning,
        ScenarioDeviceHealthSnapshot? deviceHealth,
        ScenarioCiPolicyResult? ciPolicy) =>
        result with
        {
            Scenarios = result.Scenarios
                .Select(item => item with { MetadataWarnings = AppendWarning(item.MetadataWarnings, warning) })
                .ToArray(),
            DeviceHealth = deviceHealth,
            CiPolicy = ciPolicy
        };

    private static void AttachPolicyWarning(
        Exception exception,
        ScenarioRunFailureData? failureData,
        string? warning,
        ScenarioGovernanceVerdict governance,
        ScenarioDeviceHealthSnapshot? deviceHealth,
        ScenarioCiPolicyResult? ciPolicy)
    {
        if (failureData is null)
        {
            return;
        }

        ScenarioFailureDetails.UpdateDataPayload(
            exception,
            failureData with
            {
                Governance = failureData.Governance ?? governance,
                MetadataWarnings = warning is null
                    ? failureData.MetadataWarnings
                    : AppendWarning(failureData.MetadataWarnings, warning),
                DeviceHealth = deviceHealth,
                CiPolicy = ciPolicy
            });
    }

    private static IReadOnlyList<ScenarioMetadataWarning> AppendWarning(
        IReadOnlyList<ScenarioMetadataWarning>? warnings,
        string message)
    {
        var updated = warnings?.ToList() ?? [];
        updated.Add(new ScenarioMetadataWarning(PolicyIoWarningCode, message));
        return updated;
    }
}
