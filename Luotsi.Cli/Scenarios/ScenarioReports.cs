using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Scenarios;

internal enum ScenarioArtifactAttachmentPolicy
{
    Never,
    OnFailure,
    Always
}

internal sealed class ScenarioRunReportCoordinatorFactory(IFileSystem fileSystem, TimeProvider timeProvider)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public ScenarioRunReportCoordinator Create(CliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var writers = new List<IScenarioRunReportWriter>();
        if (!string.IsNullOrWhiteSpace(options.Get("report-json")))
        {
            writers.Add(new JsonScenarioRunReportWriter(_fileSystem, options.Require("report-json")));
        }

        if (!string.IsNullOrWhiteSpace(options.Get("report-junit")))
        {
            writers.Add(new JUnitScenarioRunReportWriter(_fileSystem, options.Require("report-junit")));
        }

        return new ScenarioRunReportCoordinator(
            _timeProvider,
            new CompositeScenarioRunReportWriter(writers),
            ParseAttachmentPolicy(options.Get("attach-artifacts")));
    }

    private static ScenarioArtifactAttachmentPolicy ParseAttachmentPolicy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ScenarioArtifactAttachmentPolicy.OnFailure;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "never" => ScenarioArtifactAttachmentPolicy.Never,
            "on-failure" or "onfailure" => ScenarioArtifactAttachmentPolicy.OnFailure,
            "always" => ScenarioArtifactAttachmentPolicy.Always,
            _ => throw new UsageException("--attach-artifacts must be one of: never, on-failure, always.")
        };
    }
}

internal sealed class ScenarioRunReportCoordinator(
    TimeProvider timeProvider,
    IScenarioRunReportWriter writer,
    ScenarioArtifactAttachmentPolicy attachmentPolicy)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IScenarioRunReportWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));

    public async Task<ScenarioRunResult> RunFileAsync(string file, Func<Task<ScenarioRunResult>> runAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentNullException.ThrowIfNull(runAsync);

        var startedAt = _timeProvider.GetUtcNow();
        try
        {
            var result = await runAsync().ConfigureAwait(false);
            await WriteAsync(ScenarioRunReport.FromSingle(file, result, startedAt, _timeProvider.GetUtcNow(), attachmentPolicy)).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            await WriteAsync(ScenarioRunReport.FromSingleFailure(file, ex, startedAt, _timeProvider.GetUtcNow(), attachmentPolicy)).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<ScenarioRunBatchResult> RunBatchAsync(ScenarioRunPlan plan, Func<Task<ScenarioRunBatchResult>> runAsync)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(runAsync);

        var startedAt = _timeProvider.GetUtcNow();
        try
        {
            var result = await runAsync().ConfigureAwait(false);
            await WriteAsync(ScenarioRunReport.FromBatch(result, startedAt, _timeProvider.GetUtcNow(), attachmentPolicy)).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            await WriteAsync(ScenarioRunReport.FromBatchFailure(plan, ex, startedAt, _timeProvider.GetUtcNow(), attachmentPolicy)).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<ScenarioRunPlan> PlanPathAsync(ScenarioQuery query, Func<Task<ScenarioRunPlan>> planAsync)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(planAsync);

        var startedAt = _timeProvider.GetUtcNow();
        try
        {
            return await planAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await WriteAsync(ScenarioRunReport.FromQueryFailure(query, ex, startedAt, _timeProvider.GetUtcNow())).ConfigureAwait(false);
            throw;
        }
    }

    private Task WriteAsync(ScenarioRunReport report) => _writer.WriteAsync(report);
}

internal interface IScenarioRunReportWriter
{
    Task WriteAsync(ScenarioRunReport report);
}

internal sealed class CompositeScenarioRunReportWriter(IReadOnlyList<IScenarioRunReportWriter> writers) : IScenarioRunReportWriter
{
    private readonly IReadOnlyList<IScenarioRunReportWriter> _writers = writers ?? throw new ArgumentNullException(nameof(writers));

    public async Task WriteAsync(ScenarioRunReport report)
    {
        foreach (var writer in _writers)
        {
            await writer.WriteAsync(report).ConfigureAwait(false);
        }
    }
}

internal sealed class JsonScenarioRunReportWriter(IFileSystem fileSystem, string path) : IScenarioRunReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly string _path = string.IsNullOrWhiteSpace(path) ? throw new ArgumentException("Report path must be non-empty.", nameof(path)) : path;

    public async Task WriteAsync(ScenarioRunReport report)
    {
        ScenarioReportFileSystem.CreateReportDirectory(_fileSystem, _path);
        await _fileSystem.WriteAllTextAsync(_path, JsonSerializer.Serialize(report, JsonOptions), Encoding.UTF8).ConfigureAwait(false);
    }
}

internal sealed class JUnitScenarioRunReportWriter(IFileSystem fileSystem, string path) : IScenarioRunReportWriter
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly string _path = string.IsNullOrWhiteSpace(path) ? throw new ArgumentException("Report path must be non-empty.", nameof(path)) : path;

    public async Task WriteAsync(ScenarioRunReport report)
    {
        ScenarioReportFileSystem.CreateReportDirectory(_fileSystem, _path);
        var xml = ToXml(report);
        await _fileSystem.WriteAllTextAsync(_path, xml.ToString(SaveOptions.DisableFormatting), Encoding.UTF8).ConfigureAwait(false);
    }

    private static XDocument ToXml(ScenarioRunReport report)
    {
        var testCases = report.Scenarios.Select(ToTestCase).ToArray();
        return new XDocument(
            new XElement(
                "testsuite",
                new XAttribute("name", report.Path),
                new XAttribute("tests", report.Scenarios.Count),
                new XAttribute("failures", report.FailedCount),
                new XAttribute("skipped", 0),
                new XAttribute("time", Seconds(report.DurationMs)),
                new XAttribute("timestamp", report.StartedAt.ToString("O", CultureInfo.InvariantCulture)),
                testCases));
    }

    private static XElement ToTestCase(ScenarioReportScenario scenario)
    {
        var element = new XElement(
            "testcase",
            new XAttribute("classname", string.IsNullOrWhiteSpace(scenario.File) ? "luotsi.scenario" : scenario.File),
            new XAttribute("name", scenario.Scenario),
            new XAttribute("id", scenario.ScenarioId ?? scenario.Scenario),
            new XAttribute("time", Seconds(scenario.DurationMs ?? 0)));

        if (scenario.Status != "passed")
        {
            element.Add(new XElement(
                "failure",
                new XAttribute("type", scenario.Error?.Category ?? "scenario_error"),
                new XAttribute("message", scenario.Error?.Message ?? $"Scenario '{scenario.Scenario}' failed."),
                scenario.FailedStep is null
                    ? scenario.Error?.Message
                    : $"Step {scenario.FailedStep.Index} ({scenario.FailedStep.Name}) failed during {scenario.FailedStep.Action}. {scenario.Error?.Message}".Trim()));
        }

        if (scenario.Artifacts.Count > 0)
        {
            element.Add(new XElement(
                "system-out",
                string.Join(
                    Environment.NewLine,
                    scenario.Artifacts.Select(static artifact =>
                        $"{artifact.Kind}: {artifact.FileName}" +
                        (artifact.StepIndex is null ? string.Empty : $" (step {artifact.StepIndex}: {artifact.StepName})")))));
        }

        return element;
    }

    private static string Seconds(double milliseconds) =>
        Math.Max(0, milliseconds / 1000d).ToString("0.###", CultureInfo.InvariantCulture);
}

internal sealed record ScenarioRunReport(
    string Schema,
    string Path,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    double DurationMs,
    int TotalCount,
    int MatchedCount,
    int SelectedCount,
    int PassedCount,
    int FailedCount,
    int ShardedOutCount,
    int? ShardCount,
    int? ShardIndex,
    string? ShardStrategy,
    IReadOnlyList<ScenarioReportScenario> Scenarios,
    ErrorInfo? Error = null)
{
    private const string ReportSchema = "luotsi-scenario-run-report.v1";

    public static ScenarioRunReport FromSingle(
        string file,
        ScenarioRunResult result,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        ScenarioArtifactAttachmentPolicy attachmentPolicy) =>
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
            result.Status == "passed" ? 1 : 0,
            result.Status == "passed" ? 0 : 1,
            0,
            null,
            null,
            null,
            [ScenarioReportScenario.FromSuccess(result, file, attachmentPolicy)]);

    public static ScenarioRunReport FromSingleFailure(
        string file,
        Exception exception,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        ScenarioArtifactAttachmentPolicy attachmentPolicy)
    {
        var failureData = (exception as ICommandFailureDetails)?.DataPayload as ScenarioRunFailureData;
        var scenario = failureData is null
            ? ScenarioReportScenario.FromException(file, exception)
            : ScenarioReportScenario.FromFailure(failureData, ScenarioErrorInfo.From(exception), attachmentPolicy);
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
            [scenario],
            ScenarioErrorInfo.From(exception));
    }

    public static ScenarioRunReport FromBatch(
        ScenarioRunBatchResult result,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        ScenarioArtifactAttachmentPolicy attachmentPolicy) =>
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
            result.Scenarios.Select(scenario => ScenarioReportScenario.FromBatchItem(scenario, attachmentPolicy)).ToArray());

    public static ScenarioRunReport FromBatchFailure(
        ScenarioRunPlan plan,
        Exception exception,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        ScenarioArtifactAttachmentPolicy attachmentPolicy)
    {
        var failureData = (exception as ICommandFailureDetails)?.DataPayload as ScenarioRunFailureData;
        ScenarioReportScenario[] scenarios = failureData is null
            ? [ScenarioReportScenario.FromException(plan.Query.Path, exception, "scenario run", $"{plan.Query.Path}::run")]
            : [ScenarioReportScenario.FromFailure(failureData, ScenarioErrorInfo.From(exception), attachmentPolicy)];
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
            scenarios,
            ScenarioErrorInfo.From(exception));
    }

    public static ScenarioRunReport FromQueryFailure(
        ScenarioQuery query,
        Exception exception,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt) =>
        new(
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
            [ScenarioReportScenario.FromException(query.Path, exception, "scenario discovery", $"{query.Path}::discovery")],
            ScenarioErrorInfo.From(exception));

    private static double CalculateDurationMs(DateTimeOffset startedAt, DateTimeOffset endedAt) =>
        Math.Max(0, (endedAt - startedAt).TotalMilliseconds);
}

internal sealed record ScenarioReportScenario(
    string Scenario,
    string? ScenarioId,
    string Status,
    string? File,
    double? DurationMs,
    ScenarioRunTiming? Timing,
    IReadOnlyList<ScenarioStepResult> Steps,
    ScenarioFailedStepResult? FailedStep,
    IReadOnlyList<ScenarioReportArtifact> Artifacts,
    ErrorInfo? Error)
{
    public static ScenarioReportScenario FromSuccess(ScenarioRunResult result, string? file, ScenarioArtifactAttachmentPolicy attachmentPolicy) =>
        new(
            result.Scenario,
            result.ScenarioId ?? (file is null ? null : ScenarioIdentity.Create(file, result.Scenario)),
            result.Status,
            result.File ?? file,
            result.Timing.TotalMs,
            result.Timing,
            result.Steps,
            null,
            GetStepArtifacts(result.Steps, attachmentPolicy),
            null);

    public static ScenarioReportScenario FromFailure(ScenarioRunFailureData data, ErrorInfo error, ScenarioArtifactAttachmentPolicy attachmentPolicy) =>
        new(
            data.Scenario,
            data.ScenarioId ?? ScenarioIdentity.Create(data.File, data.Scenario),
            data.Status,
            data.File,
            data.Timing.TotalMs,
            data.Timing,
            data.Steps,
            data.FailedStep,
            GetFailureAndStepArtifacts(data.Steps, data.FailureArtifacts, attachmentPolicy),
            error);

    public static ScenarioReportScenario FromBatchItem(ScenarioBatchItemResult item, ScenarioArtifactAttachmentPolicy attachmentPolicy)
    {
        if (item.Data is not null)
        {
            return FromFailure(item.Data, item.Error ?? new ErrorInfo("Exception", "Scenario failed.", "scenario_error"), attachmentPolicy);
        }

        return new ScenarioReportScenario(
            item.Scenario,
            item.ScenarioId ?? (item.File is null ? null : ScenarioIdentity.Create(item.File, item.Scenario)),
            item.Status,
            item.File,
            item.Timing?.TotalMs,
            item.Timing,
            item.Steps ?? [],
            null,
            item.Steps is null ? [] : GetStepArtifacts(item.Steps, attachmentPolicy),
            item.Error);
    }

    public static ScenarioReportScenario FromException(string file, Exception exception, string? scenario = null, string? scenarioId = null) =>
        new(
            scenario ?? Path.GetFileNameWithoutExtension(file),
            scenarioId ?? file,
            "failed",
            file,
            null,
            null,
            [],
            null,
            [],
            ScenarioErrorInfo.From(exception));

    private static IReadOnlyList<ScenarioReportArtifact> GetFailureArtifacts(FailureArtifactBundle bundle, ScenarioArtifactAttachmentPolicy attachmentPolicy)
    {
        if (attachmentPolicy == ScenarioArtifactAttachmentPolicy.Never)
        {
            return [];
        }

        var artifacts = bundle.Artifacts
            .Select(artifact => new ScenarioReportArtifact(artifact.Kind, artifact.FileName, bundle.StepIndex, bundle.StepName))
            .ToList();
        if (!string.IsNullOrWhiteSpace(bundle.MetadataFile))
        {
            artifacts.Add(new ScenarioReportArtifact("metadata", bundle.MetadataFile, bundle.StepIndex, bundle.StepName));
        }

        return artifacts;
    }

    private static IReadOnlyList<ScenarioReportArtifact> GetFailureAndStepArtifacts(
        IReadOnlyList<ScenarioStepResult> steps,
        FailureArtifactBundle bundle,
        ScenarioArtifactAttachmentPolicy attachmentPolicy)
    {
        if (attachmentPolicy == ScenarioArtifactAttachmentPolicy.Never)
        {
            return [];
        }

        var artifacts = new List<ScenarioReportArtifact>();
        if (attachmentPolicy == ScenarioArtifactAttachmentPolicy.Always)
        {
            artifacts.AddRange(GetStepArtifacts(steps, attachmentPolicy));
        }

        artifacts.AddRange(GetFailureArtifacts(bundle, attachmentPolicy));
        return artifacts;
    }

    private static IReadOnlyList<ScenarioReportArtifact> GetStepArtifacts(IReadOnlyList<ScenarioStepResult> steps, ScenarioArtifactAttachmentPolicy attachmentPolicy)
    {
        if (attachmentPolicy != ScenarioArtifactAttachmentPolicy.Always)
        {
            return [];
        }

        return steps
            .SelectMany((step, index) => FromStepResult(step, index + 1))
            .ToArray();
    }

    private static IEnumerable<ScenarioReportArtifact> FromStepResult(ScenarioStepResult step, int index)
    {
        if (step.Result is TakeScreenshotResult screenshot)
        {
            yield return new ScenarioReportArtifact("screenshot", screenshot.File, index, step.Step);
        }

        if (step.Result is CaptureArtifactsResult artifacts)
        {
            yield return new ScenarioReportArtifact("screenshot", artifacts.Screenshot, index, step.Step);
            yield return new ScenarioReportArtifact("logcat", artifacts.Logcat, index, step.Step);
            yield return new ScenarioReportArtifact("screen_state", artifacts.ScreenState, index, step.Step);
            yield return new ScenarioReportArtifact("hierarchy", artifacts.Hierarchy, index, step.Step);
        }
    }
}

internal sealed record ScenarioReportArtifact(string Kind, string FileName, int? StepIndex, string? StepName);

internal static class ScenarioReportFileSystem
{
    public static void CreateReportDirectory(IFileSystem fileSystem, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            fileSystem.CreateDirectory(directory);
        }
    }
}
