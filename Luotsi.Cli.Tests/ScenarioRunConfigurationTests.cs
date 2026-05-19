using Luotsi.Cli.Cli;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Scenarios;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed class ScenarioRunConfigurationTests
{
    [Fact]
    public void Create_Parses_Run_Output_Policies()
    {
        var options = CliOptions.Parse([
            "run",
            "--events-jsonl", "/tmp/events.jsonl",
            "--report-json", "/tmp/report.json",
            "--report-junit", "/tmp/junit.xml",
            "--capture-on", "never",
            "--attach-artifacts", "always"]);

        var configuration = ScenarioRunConfiguration.Create(options);

        Assert.Equal("/tmp/events.jsonl", configuration.EventsJsonlPath);
        Assert.Equal("/tmp/report.json", configuration.JsonReportPath);
        Assert.Equal("/tmp/junit.xml", configuration.JUnitReportPath);
        Assert.Equal(ScenarioFailureArtifactCapturePolicy.Never, configuration.FailureArtifactCapturePolicy);
        Assert.Equal(ScenarioArtifactAttachmentPolicy.Always, configuration.ArtifactAttachmentPolicy);
    }

    [Fact]
    public void Create_Invalid_AttachArtifacts_Throws_UsageException()
    {
        var options = CliOptions.Parse(["run", "--attach-artifacts", "sometimes"]);

        var error = Assert.Throws<UsageException>(() => ScenarioRunConfiguration.Create(options));

        Assert.Contains("--attach-artifacts", error.Message, StringComparison.Ordinal);
    }
}