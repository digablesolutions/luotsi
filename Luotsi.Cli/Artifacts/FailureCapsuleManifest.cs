using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;

namespace Luotsi.Cli.Artifacts;

internal static class FailureCapsuleArtifactNames
{
    public const string FileName = "failure-capsule.json";
}

internal sealed record FailureCapsuleManifest(
    string Schema,
    DateTimeOffset GeneratedAt,
    string Path,
    string Status,
    string? ReplayMetadataPath,
    string? ReplayTimelinePath,
    FailureCapsuleReportLinks Reports,
    IReadOnlyList<FailureCapsuleScenario> Scenarios,
    IReadOnlyList<FailureCapsuleArtifactLink> Screenshots,
    IReadOnlyList<FailureCapsuleArtifactLink> Logcat,
    IReadOnlyList<FailureCapsuleArtifactLink> Hierarchies,
    IReadOnlyList<FailureCapsuleArtifactLink> ScreenStates,
    IReadOnlyList<FailureCapsuleFailureBundle> FailureBundles);

internal sealed record FailureCapsuleReportLinks(
    string? JsonPath,
    string? JunitPath);

internal sealed record FailureCapsuleScenario(
    string Scenario,
    string? ScenarioId,
    string Status,
    string? File,
    FailureCapsuleFailedStep? FailedStep,
    IReadOnlyList<FailureCapsuleArtifactLink> Artifacts,
    ErrorInfo? Error);

internal sealed record FailureCapsuleFailedStep(
    int Index,
    string Name,
    string Action,
    string Phase);

internal sealed record FailureCapsuleArtifactLink(
    string Kind,
    string Path,
    int? StepIndex,
    string? StepName);

internal sealed record FailureCapsuleFailureBundle(
    string Path,
    string? Scenario,
    string? ScenarioId,
    string? File,
    FailureCapsuleFailedStep? FailedStep,
    IReadOnlyList<FailureCapsuleArtifactLink> Artifacts,
    ErrorInfo? Error);

internal sealed class ScenarioFailureCapsuleWriter(IFileSystem fileSystem, ArtifactSession artifacts)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly ArtifactSession _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));

    public Task WriteAsync(ScenarioRunReport report, string? jsonReportPath, string? junitReportPath)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (!IsFailed(report.Status))
        {
            return Task.CompletedTask;
        }

        var scenarios = report.Scenarios
            .Where(static scenario => IsFailed(scenario.Status))
            .Select(CreateScenario)
            .ToArray();
        var failureBundles = CreateFailureBundles(scenarios);
        var manifest = new FailureCapsuleManifest(
            ResultSchemas.FailureCapsule,
            report.EndedAt,
            NormalizePath(report.Path) ?? report.Path,
            report.Status,
            TryArtifactLink(SessionReplayArtifacts.MetadataFileName),
            TryArtifactLink(SessionReplayArtifacts.TimelineFileName),
            new FailureCapsuleReportLinks(NormalizePath(jsonReportPath), NormalizePath(junitReportPath)),
            scenarios,
            FilterArtifacts(scenarios, "screenshot"),
            FilterArtifacts(scenarios, "logcat"),
            FilterArtifacts(scenarios, "hierarchy"),
            FilterArtifacts(scenarios, "screen_state"),
            failureBundles);

        return _artifacts.WriteJsonAsync(FailureCapsuleArtifactNames.FileName, manifest);
    }

    private FailureCapsuleScenario CreateScenario(ScenarioReportScenario scenario)
    {
        var failedStep = scenario.FailedStep is null
            ? null
            : new FailureCapsuleFailedStep(
                scenario.FailedStep.Index,
                scenario.FailedStep.Name,
                scenario.FailedStep.Action,
                scenario.FailedStep.Phase);

        return new FailureCapsuleScenario(
            scenario.Scenario,
            scenario.ScenarioId,
            scenario.Status,
            NormalizePath(scenario.File),
            failedStep,
            scenario.Artifacts.Select(CreateArtifactLink).ToArray(),
            scenario.Error);
    }

    private static FailureCapsuleFailureBundle[] CreateFailureBundles(IReadOnlyList<FailureCapsuleScenario> scenarios) =>
        scenarios
            .SelectMany(static scenario => scenario.Artifacts
                .Where(static artifact => string.Equals(artifact.Kind, "metadata", StringComparison.OrdinalIgnoreCase))
                .Select(metadata => new FailureCapsuleFailureBundle(
                    metadata.Path,
                    scenario.Scenario,
                    scenario.ScenarioId,
                    scenario.File,
                    scenario.FailedStep,
                    scenario.Artifacts
                        .Where(artifact => !string.Equals(artifact.Kind, "metadata", StringComparison.OrdinalIgnoreCase))
                        .Where(artifact => metadata.StepIndex is null || artifact.StepIndex == metadata.StepIndex)
                        .ToArray(),
                    scenario.Error)))
            .ToArray();

    private static FailureCapsuleArtifactLink[] FilterArtifacts(IReadOnlyList<FailureCapsuleScenario> scenarios, string kind) =>
        scenarios
            .SelectMany(static scenario => scenario.Artifacts)
            .Where(artifact => string.Equals(artifact.Kind, kind, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToArray();

    private FailureCapsuleArtifactLink CreateArtifactLink(ScenarioReportArtifact artifact) =>
        new(
            artifact.Kind,
            NormalizePath(artifact.FileName) ?? artifact.FileName,
            artifact.StepIndex,
            artifact.StepName);

    private string? TryArtifactLink(string fileName)
    {
        var fullPath = Path.Join(_artifacts.Root, fileName);
        return _fileSystem.FileExists(fullPath) ? NormalizePath(fileName) : null;
    }

    private string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!Path.IsPathRooted(path))
        {
            return path.Replace('\\', '/');
        }

        var relativePath = Path.GetRelativePath(_artifacts.Root, path);
        if (!relativePath.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relativePath))
        {
            return relativePath.Replace('\\', '/');
        }

        return path.Replace('\\', '/');
    }

    private static bool IsFailed(string status) =>
        string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase);
}