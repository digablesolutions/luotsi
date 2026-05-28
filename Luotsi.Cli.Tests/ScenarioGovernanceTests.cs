using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed class ScenarioGovernanceTests
{
    [Fact]
    public void ScenarioRunResult_Positional_Optional_Arguments_Remain_Compatible()
    {
        var artifactCommands = new[]
        {
            new ScenarioArtifactCommandHint("open_artifacts", "Open the artifact browser for this run.", "luotsi artifacts open /tmp/run")
        };

        var result = new ScenarioRunResult(
            "login smoke",
            "passed",
            new ScenarioRunTiming(10, 0, 10, 0),
            ScenarioMetrics.Empty,
            [],
            null,
            null,
            null,
            null,
            null,
            "line",
            artifactCommands);

        Assert.Equal("line", result.ProgressMode);
        Assert.Same(artifactCommands, result.ArtifactCommands);
        Assert.Null(result.Governance);
    }

    [Fact]
    public void ScenarioRunBatchResult_Positional_Optional_Arguments_Remain_Compatible()
    {
        var artifactCommands = new[]
        {
            new ScenarioArtifactCommandHint("open_artifacts", "Open the artifact browser for this run.", "luotsi artifacts open /tmp/run")
        };

        var result = new ScenarioRunBatchResult(
            "/tmp/scenarios",
            "passed",
            1,
            1,
            1,
            1,
            0,
            0,
            null,
            null,
            [],
            ScenarioShardStrategies.Index,
            ScenarioMetrics.Empty,
            null,
            "line",
            artifactCommands);

        Assert.Equal("line", result.ProgressMode);
        Assert.Same(artifactCommands, result.ArtifactCommands);
        Assert.Null(result.Governance);
    }

    [Fact]
    public void FromFailureData_InvalidFailedStepIndex_FallsBack_To_UnknownFailure()
    {
        var failureData = new ScenarioRunFailureData(
            "login smoke",
            "/tmp/scenarios/login.json",
            "failed",
            new ScenarioRunTiming(1500, 100, 1300, 100),
            ScenarioMetrics.Empty,
            new ScenarioFailedStepResult(
                2,
                "wait login button",
                "waitVisible",
                750,
                new ScenarioStepTiming(750, 250, null, 500)),
            [],
            new FailureArtifactBundle(
                ResultSchemas.FailureBundle,
                DateTimeOffset.Parse("2026-05-18T10:00:03Z", null, System.Globalization.DateTimeStyles.RoundtripKind),
                "scenario",
                "login smoke",
                "/tmp/scenarios/login.json",
                1,
                "wait login button",
                "waitVisible",
                "System.InvalidOperationException",
                "button not visible",
                [],
                []));

        var verdict = ScenarioGovernanceClassifier.FromFailureData(failureData);

        Assert.Equal("unknown_failure", verdict.Kind);
        Assert.Contains("step 2", verdict.Summary, StringComparison.Ordinal);
    }
}
