using Luotsi.Cli.Cli;
using Luotsi.Cli.Cli.Composition;
using Luotsi.Cli.Cli.Envelope;
using Luotsi.Cli.Cli.Provenance;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed class AppCommandShellTests
{
    [Fact]
    public void WriteSuccess_Writes_Command_Envelope_With_Snake_Case_Fields()
    {
        var console = new FakeConsole();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var writer = new AppCommandEnvelopeWriter(console, timeProvider, CreateProvenance());

        writer.WriteSuccess("devices", DateTimeOffset.Parse("2026-05-18T09:59:59Z", null, System.Globalization.DateTimeStyles.RoundtripKind), new DeviceListResult([]), new ArtifactData("/tmp/artifacts", "final"));

        using var envelope = console.ParseSingleOutputAsJson();
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(ResultSchemas.CommandEnvelope, envelope.RootElement.GetProperty("schema").GetString());
        Assert.Equal("/tmp/artifacts", envelope.RootElement.GetProperty("artifacts").GetProperty("artifact_root").GetString());
        Assert.Equal("luotsi", envelope.RootElement.GetProperty("provenance").GetProperty("tool").GetString());
        Assert.True(envelope.RootElement.GetProperty("provenance").TryGetProperty("framework", out _));
        Assert.True(envelope.RootElement.TryGetProperty("started_at", out _));
        Assert.True(envelope.RootElement.TryGetProperty("ended_at", out _));
    }

    [Fact]
    public void BuildProvenanceProvider_Uses_Ci_Environment()
    {
        var provider = new BuildProvenanceProvider(new FakeEnvironmentVariables(new Dictionary<string, string>
        {
            ["GITHUB_ACTIONS"] = "true",
            ["GITHUB_SHA"] = "abc123",
            ["GITHUB_REF_NAME"] = "main",
            ["GITHUB_REPOSITORY"] = "digablesolutions/luotsi",
            ["GITHUB_RUN_ID"] = "456"
        }));

        var provenance = provider.Create();

        Assert.Equal("luotsi", provenance.Tool);
        Assert.Equal("abc123", provenance.CommitSha);
        Assert.Equal("main", provenance.Branch);
        Assert.Equal("digablesolutions/luotsi", provenance.Repository);
        Assert.Equal("github-actions", provenance.CiProvider);
        Assert.Equal("456", provenance.CiRunId);
    }

    [Fact]
    public async Task WriteFailureAsync_Captures_Runner_Artifacts_When_Exception_Has_No_Payload()
    {
        var console = new FakeConsole();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var responder = new AppCommandFailureResponder(new AppCommandEnvelopeWriter(console, timeProvider, CreateProvenance()));
        var runner = new FakeDeviceHost();
        var context = new AppExecutionContext(timeProvider.GetUtcNow(), CliOptions.Parse(["wait-visible", "--artifacts", "/tmp/artifacts"]))
        {
            Runner = runner
        };

        var exitCode = await responder.WriteFailureAsync("wait-visible", timeProvider.GetUtcNow(), context, new InvalidOperationException("Timed out waiting for target"));

        using var envelope = console.ParseSingleOutputAsJson();
        Assert.Equal(1, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("selector_or_screen_state", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Equal(ResultSchemas.FailureBundle, envelope.RootElement.GetProperty("data").GetProperty("schema").GetString());
        Assert.Equal("command", envelope.RootElement.GetProperty("data").GetProperty("scope").GetString());
        Assert.Equal("wait-visible", envelope.RootElement.GetProperty("data").GetProperty("name").GetString());
        Assert.Equal("Timed out waiting for target", envelope.RootElement.GetProperty("data").GetProperty("error_message").GetString());
    }

    [Fact]
    public void Resolve_Returns_Failure_For_Batch_Result_With_Failures()
    {
        var result = new ScenarioRunBatchResult("/tmp/scenarios", "failed", 1, 1, 1, 0, 1, 0, null, null, []);
        var resolver = new AppCommandExitCodeResolver();

        var exitCode = resolver.Resolve(result);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void WriteUsageError_Returns_Usage_Exit_Code()
    {
        var console = new FakeConsole();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var responder = new AppCommandFailureResponder(new AppCommandEnvelopeWriter(console, timeProvider, CreateProvenance()));

        var exitCode = responder.WriteUsageError("tap", timeProvider.GetUtcNow(), new ArtifactData("/tmp/artifacts", "final"), new UsageException("bad args"));

        using var envelope = console.ParseSingleOutputAsJson();
        Assert.Equal(2, exitCode);
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
    }

    private static BuildProvenance CreateProvenance() =>
        new BuildProvenanceProvider(new FakeEnvironmentVariables(new Dictionary<string, string>())).Create();
}
