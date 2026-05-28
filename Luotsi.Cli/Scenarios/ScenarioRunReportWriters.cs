using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Scenarios;

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
                CreateGovernanceProperties(report.Governance),
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
        var governanceProperties = CreateGovernanceProperties(scenario.Governance);
        if (governanceProperties is not null)
        {
            element.Add(governanceProperties);
        }

        if (string.Equals(scenario.Status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            element.Add(new XElement(
                "failure",
                new XAttribute("type", scenario.Error?.Category ?? "scenario_error"),
                new XAttribute("message", scenario.Error?.Message ?? $"Scenario '{scenario.Scenario}' failed."),
                scenario.FailedStep is null
                    ? scenario.Error?.Message
                    : $"Step {scenario.FailedStep.Index} ({scenario.FailedStep.Name}) failed during {scenario.FailedStep.Action}. {scenario.Error?.Message}".Trim()));
        }

        if (scenario.Artifacts.Count > 0 || scenario.Metrics.Count > 0)
        {
            element.Add(new XElement(
                "system-out",
                string.Join(
                    Environment.NewLine,
                    scenario.Metrics
                        .OrderBy(static metric => metric.Key, StringComparer.Ordinal)
                        .Select(static metric => $"metric: {metric.Key}={metric.Value.ToString("0.###", CultureInfo.InvariantCulture)}")
                        .Concat(scenario.Artifacts.Select(static artifact =>
                        $"{artifact.Kind}: {artifact.FileName}" +
                        (artifact.StepIndex is null ? string.Empty : $" (step {artifact.StepIndex}: {artifact.StepName})"))))));
        }

        return element;
    }

    private static string Seconds(double milliseconds) =>
        Math.Max(0, milliseconds / 1000d).ToString("0.###", CultureInfo.InvariantCulture);

    private static XElement? CreateGovernanceProperties(ScenarioGovernanceVerdict? governance)
    {
        if (governance is null)
        {
            return null;
        }

        return new XElement(
            "properties",
            new XElement("property", new XAttribute("name", "luotsi.governance.kind"), new XAttribute("value", governance.Kind)),
            new XElement("property", new XAttribute("name", "luotsi.governance.confidence"), new XAttribute("value", governance.Confidence)),
            new XElement("property", new XAttribute("name", "luotsi.governance.summary"), new XAttribute("value", governance.Summary)),
            new XElement("property", new XAttribute("name", "luotsi.governance.regression_candidate"), new XAttribute("value", governance.RegressionCandidate.ToString().ToLowerInvariant())),
            new XElement("property", new XAttribute("name", "luotsi.governance.infrastructure_related"), new XAttribute("value", governance.InfrastructureRelated.ToString().ToLowerInvariant())),
            new XElement("property", new XAttribute("name", "luotsi.governance.quarantine_candidate"), new XAttribute("value", governance.QuarantineCandidate.ToString().ToLowerInvariant())),
            string.IsNullOrWhiteSpace(governance.RecommendedAction)
                ? null
                : new XElement("property", new XAttribute("name", "luotsi.governance.recommended_action"), new XAttribute("value", governance.RecommendedAction)));
    }
}

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
