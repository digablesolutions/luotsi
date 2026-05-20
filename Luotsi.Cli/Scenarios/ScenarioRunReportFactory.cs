using Luotsi.Cli.Models;

namespace Luotsi.Cli.Scenarios;

internal static class ScenarioRunReportFactory
{
    private const string ReportSchema = "luotsi-scenario-run-report.v1";

    public static ScenarioRunReport FromSingle(
        string file,
        ScenarioRunResult result,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        ScenarioArtifactAttachmentPolicy attachmentPolicy,
        BuildProvenance provenance) =>
        new(
            ReportSchema,
            file,
            result.Status,
            startedAt,
            endedAt,
            CalculateDurationMs(startedAt, endedAt),
            1,
            1,
            1,
            IsPassed(result.Status) ? 1 : 0,
            IsFailed(result.Status) ? 1 : 0,
            0,
            null,
            null,
            null,
            result.Metrics,
            result.DeviceAllocation,
            provenance,
            [CreateScenarioFromSuccess(result, file, attachmentPolicy)]);

    public static ScenarioRunReport FromSingleFailure(
        string file,
        Exception exception,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        ScenarioArtifactAttachmentPolicy attachmentPolicy,
        BuildProvenance provenance)
    {
        var error = ScenarioErrorInfo.From(exception);
        var failureData = ScenarioFailureDetails.TryGetData(exception);
        var scenario = failureData is null
            ? CreateScenarioFromException(file, exception)
            : CreateScenarioFromFailure(failureData, error, attachmentPolicy);
        return new ScenarioRunReport(
            ReportSchema,
            file,
            "failed",
            startedAt,
            endedAt,
            CalculateDurationMs(startedAt, endedAt),
            1,
            1,
            1,
            0,
            1,
            0,
            null,
            null,
            null,
            scenario.Metrics,
            null,
            provenance,
            [scenario],
            error);
    }

    public static ScenarioRunReport FromBatch(
        ScenarioRunBatchResult result,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        ScenarioArtifactAttachmentPolicy attachmentPolicy,
        BuildProvenance provenance) =>
        new(
            ReportSchema,
            result.Path,
            result.Status,
            startedAt,
            endedAt,
            CalculateDurationMs(startedAt, endedAt),
            result.TotalCount,
            result.MatchedCount,
            result.SelectedCount,
            result.PassedCount,
            result.FailedCount,
            result.ShardedOutCount,
            result.ShardCount,
            result.ShardIndex,
            result.ShardStrategy,
            result.Metrics ?? ScenarioMetrics.Empty,
            result.DeviceAllocation,
            provenance,
            result.Scenarios.Select(scenario => CreateScenarioFromBatchItem(scenario, attachmentPolicy)).ToArray());

    public static ScenarioRunReport FromBatchFailure(
        ScenarioRunPlan plan,
        Exception exception,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        ScenarioArtifactAttachmentPolicy attachmentPolicy,
        BuildProvenance provenance)
    {
        var error = ScenarioErrorInfo.From(exception);
        var failureData = ScenarioFailureDetails.TryGetData(exception);
        ScenarioReportScenario[] scenarios = failureData is null
            ? [CreateScenarioFromException(plan.Query.Path, exception, "scenario run", $"{plan.Query.Path}::run")]
            : [CreateScenarioFromFailure(failureData, error, attachmentPolicy)];
        return new ScenarioRunReport(
            ReportSchema,
            plan.Query.Path,
            "failed",
            startedAt,
            endedAt,
            CalculateDurationMs(startedAt, endedAt),
            plan.TotalCount,
            plan.MatchedCount,
            plan.SelectedCount,
            0,
            1,
            plan.ShardedOutCount,
            plan.Query.ShardCount,
            plan.Query.ShardIndex,
            plan.Query.ShardStrategy,
            failureData?.Metrics ?? ScenarioMetrics.Empty,
            null,
            provenance,
            scenarios,
            error);
    }

    public static ScenarioRunReport FromQueryFailure(
        ScenarioQuery query,
        Exception exception,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        BuildProvenance provenance)
    {
        var error = ScenarioErrorInfo.From(exception);
        return new ScenarioRunReport(
            ReportSchema,
            query.Path,
            "failed",
            startedAt,
            endedAt,
            CalculateDurationMs(startedAt, endedAt),
            0,
            0,
            0,
            0,
            1,
            0,
            query.ShardCount,
            query.ShardIndex,
            query.ShardStrategy,
            ScenarioMetrics.Empty,
            null,
            provenance,
            [CreateScenarioFromException(query.Path, exception, "scenario discovery", $"{query.Path}::discovery")],
            error);
    }

    private static ScenarioReportScenario CreateScenarioFromSuccess(
        ScenarioRunResult result,
        string? file,
        ScenarioArtifactAttachmentPolicy attachmentPolicy) =>
        new(
            result.Scenario,
            result.ScenarioId ?? (file is null ? null : ScenarioIdentity.Create(file, result.Scenario)),
            result.Status,
            result.File ?? file,
            result.Timing.TotalMs,
            result.Timing,
            result.Metrics,
            result.Steps,
            null,
            ScenarioReportArtifactProjection.FromSteps(result.Steps, attachmentPolicy),
            null);

    private static ScenarioReportScenario CreateScenarioFromFailure(
        ScenarioRunFailureData data,
        ErrorInfo error,
        ScenarioArtifactAttachmentPolicy attachmentPolicy) =>
        new(
            data.Scenario,
            data.ScenarioId ?? ScenarioIdentity.Create(data.File, data.Scenario),
            data.Status,
            data.File,
            data.Timing.TotalMs,
            data.Timing,
            data.Metrics,
            data.Steps,
            data.FailedStep,
            ScenarioReportArtifactProjection.FromFailureAndSteps(data.Steps, data.FailureArtifacts, attachmentPolicy),
            error);

    private static ScenarioReportScenario CreateScenarioFromBatchItem(
        ScenarioBatchItemResult item,
        ScenarioArtifactAttachmentPolicy attachmentPolicy)
    {
        if (item.Data is not null)
        {
            return CreateScenarioFromFailure(
                item.Data,
                item.Error ?? new ErrorInfo("Exception", "Scenario failed.", "scenario_error"),
                attachmentPolicy);
        }

        return new ScenarioReportScenario(
            item.Scenario,
            item.ScenarioId ?? (item.File is null ? null : ScenarioIdentity.Create(item.File, item.Scenario)),
            item.Status,
            item.File,
            item.Timing?.TotalMs,
            item.Timing,
            item.Metrics ?? ScenarioMetrics.Empty,
            item.Steps ?? [],
            null,
            item.Steps is null ? [] : ScenarioReportArtifactProjection.FromSteps(item.Steps, attachmentPolicy),
            item.Error);
    }

    private static ScenarioReportScenario CreateScenarioFromException(
        string file,
        Exception exception,
        string? scenario = null,
        string? scenarioId = null)
    {
        var scenarioName = scenario ?? Path.GetFileNameWithoutExtension(file);
        return new ScenarioReportScenario(
            scenarioName,
            scenarioId ?? ScenarioIdentity.Create(file, scenarioName),
            "failed",
            file,
            null,
            null,
            ScenarioMetrics.Empty,
            [],
            null,
            [],
            ScenarioErrorInfo.From(exception));
    }

    private static double CalculateDurationMs(DateTimeOffset startedAt, DateTimeOffset endedAt) =>
        Math.Max(0, (endedAt - startedAt).TotalMilliseconds);

    private static bool IsPassed(string status) =>
        string.Equals(status, "passed", StringComparison.OrdinalIgnoreCase);

    private static bool IsFailed(string status) =>
        string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase);
}
