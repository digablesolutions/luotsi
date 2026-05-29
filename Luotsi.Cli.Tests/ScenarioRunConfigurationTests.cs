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
        Assert.Equal(ScenarioProgressMode.Plain, configuration.ProgressMode);
    }

    [Fact]
    public void Create_Invalid_AttachArtifacts_Throws_UsageException()
    {
        var options = CliOptions.Parse(["run", "--attach-artifacts", "sometimes"]);

        var error = Assert.Throws<UsageException>(() => ScenarioRunConfiguration.Create(options));

        Assert.Contains("--attach-artifacts", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_Parses_Progress_Mode()
    {
        var options = CliOptions.Parse(["run", "--progress", "jsonl"]);

        var configuration = ScenarioRunConfiguration.Create(options);

        Assert.Equal(ScenarioProgressMode.Jsonl, configuration.ProgressMode);
    }

    [Fact]
    public void Create_Quiet_Flag_Uses_Quiet_Progress_Mode()
    {
        var options = CliOptions.Parse(["run", "--quiet"]);

        var configuration = ScenarioRunConfiguration.Create(options);

        Assert.Equal(ScenarioProgressMode.Quiet, configuration.ProgressMode);
    }

    [Fact]
    public void Create_Quiet_Flag_Allows_Explicit_Quiet_Progress_Mode()
    {
        var options = CliOptions.Parse(["run", "--quiet", "--progress", "quiet"]);

        var configuration = ScenarioRunConfiguration.Create(options);

        Assert.Equal(ScenarioProgressMode.Quiet, configuration.ProgressMode);
    }

    [Fact]
    public void Create_Quiet_Flag_Rejects_Conflicting_Progress_Mode()
    {
        var options = CliOptions.Parse(["run", "--quiet", "--progress", "line"]);

        var error = Assert.Throws<UsageException>(() => ScenarioRunConfiguration.Create(options));

        Assert.Contains("--quiet", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_Auto_Progress_Uses_Line_Mode_In_CI()
    {
        var options = CliOptions.Parse(["run"]);

        var configuration = ScenarioRunConfiguration.Create(
            options,
            new FakeEnvironmentVariables(new Dictionary<string, string> { ["CI"] = "true" }));

        Assert.Equal(ScenarioProgressMode.Line, configuration.ProgressMode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("")]
    public void Create_Auto_Progress_Uses_Plain_Mode_When_CI_Is_Disabled(string ci)
    {
        var options = CliOptions.Parse(["run"]);

        var configuration = ScenarioRunConfiguration.Create(
            options,
            new FakeEnvironmentVariables(new Dictionary<string, string> { ["CI"] = ci }));

        Assert.Equal(ScenarioProgressMode.Plain, configuration.ProgressMode);
    }

    [Fact]
    public void Create_Invalid_Progress_Throws_UsageException()
    {
        var options = CliOptions.Parse(["run", "--progress", "chatty"]);

        var error = Assert.Throws<UsageException>(() => ScenarioRunConfiguration.Create(options));

        Assert.Contains("--progress", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_TooLarge_DeviceHealthWindowDays_Throws_UsageException()
    {
        var options = CliOptions.Parse(["run", "--device-health-window-days", "1000000000"]);

        var error = Assert.Throws<UsageException>(() => ScenarioRunConfiguration.Create(options));

        Assert.Contains("--device-health-window-days", error.Message, StringComparison.Ordinal);
    }
}
