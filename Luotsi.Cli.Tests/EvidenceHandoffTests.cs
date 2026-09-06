using System.Text.Json;
using Luotsi.Cli.Cli;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed partial class AppTests
{
    [Theory]
    [InlineData("session-timeline.jsonl")]
    [InlineData("failure-capsule.json")]
    public async Task RunAsync_ReplayPacket_Check_Rejects_Empty_Referenced_Evidence(string evidenceName)
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var processRunner = new FakeProcessRunner();
        var root = SeedReplayCapsuleArtifacts(fileSystem);
        var app = new App(new AppDependencies { Console = console, FileSystem = fileSystem, ProcessRunner = processRunner });
        Assert.Equal(0, await app.RunAsync(["replay", "packet", "--artifacts", root]));
        console.OutputLines.Clear();
        Assert.Equal(0, await app.RunAsync(["replay", "packet", "--artifacts", root, "--check"]));
        var originalSummary = await fileSystem.ReadAllTextAsync(Path.Join(root, "run-summary.json"));
        fileSystem.AddFile(Path.Join(root, evidenceName), "");
        console.OutputLines.Clear();

        Assert.Equal(2, await app.RunAsync(["replay", "packet", "--artifacts", root, "--check"]));
        using var envelope = console.ParseSingleOutputAsJson();
        Assert.Contains("empty evidence file", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains(evidenceName, envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(originalSummary, await fileSystem.ReadAllTextAsync(Path.Join(root, "run-summary.json")));
        Assert.Empty(processRunner.Calls);
    }

    [Theory]
    [InlineData("/tmp/received")]
    [InlineData("/tmp/received with spaces")]
    public async Task RunAsync_ArtifactsIntake_Offers_Regeneration_Before_Relocated_Packet_Check(string restoredRoot)
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        var processRunner = new FakeProcessRunner();
        var root = SeedReplayCapsuleArtifacts(fileSystem);
        var app = new App(new AppDependencies { Console = console, FileSystem = fileSystem, ProcessRunner = processRunner });
        Assert.Equal(0, await app.RunAsync(["replay", "packet", "--artifacts", root]));
        var originalSummary = await fileSystem.ReadAllTextAsync(Path.Join(root, "run-summary.json"));
        const string zip = "/tmp/handoff.zip";
        console.OutputLines.Clear();
        Assert.Equal(0, await app.RunAsync(["artifacts", "pack", root, "--output", zip, "--redact", "lab-safe"]));
        using var packed = console.ParseSingleOutputAsJson();
        var digest = packed.RootElement.GetProperty("data").GetProperty("sha256").GetString()!;
        AssertPreparationBeforeCheck(packed.RootElement.GetProperty("data").GetProperty("manifest").GetProperty("recommended_commands"));
        var originalZip = await fileSystem.ReadAllBytesAsync(zip);
        console.OutputLines.Clear();
        Assert.Equal(0, await app.RunAsync(["artifacts", "verify", zip, "--output", restoredRoot, "--sha256", digest, "--require-lab-safe"]));
        using var verified = console.ParseSingleOutputAsJson();
        AssertPreparationBeforeCheck(verified.RootElement.GetProperty("data").GetProperty("recommended_commands"));
        console.OutputLines.Clear();
        Assert.Equal(0, await app.RunAsync(["artifacts", "intake", zip, "--output", restoredRoot, "--sha256", digest, "--require-lab-safe", "--write-readme"]));
        using var intake = console.ParseSingleOutputAsJson();
        var hints = intake.RootElement.GetProperty("data").GetProperty("recommended_commands").EnumerateArray().ToArray();
        var prepareIndex = Array.FindIndex(hints, x => x.GetProperty("kind").GetString() == "replay_packet");
        var checkIndex = Array.FindIndex(hints, x => x.GetProperty("kind").GetString() == "replay_packet_check");
        Assert.True(prepareIndex >= 0 && checkIndex > prepareIndex);
        var quotedRoot = restoredRoot.Contains(' ') ? $"\"{restoredRoot}\"" : restoredRoot;
        Assert.Equal($"luotsi replay packet --artifacts {quotedRoot}", hints[prepareIndex].GetProperty("command").GetString());
        Assert.Contains("local navigation", hints[prepareIndex].GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Contains("original ZIP", await fileSystem.ReadAllTextAsync(Path.Join(restoredRoot, "artifact-intake.md")), StringComparison.Ordinal);

        // Explicit caller steps, never execution of commands read from the package.
        console.OutputLines.Clear();
        Assert.Equal(2, await app.RunAsync(["replay", "packet", "--artifacts", restoredRoot, "--check"]));
        console.OutputLines.Clear();
        Assert.Equal(0, await app.RunAsync(["replay", "packet", "--artifacts", restoredRoot]));
        console.OutputLines.Clear();
        Assert.Equal(0, await app.RunAsync(["replay", "packet", "--artifacts", restoredRoot, "--check"]));
        Assert.Equal(originalZip, await fileSystem.ReadAllBytesAsync(zip));
        Assert.Equal(originalSummary, await fileSystem.ReadAllTextAsync(Path.Join(root, "run-summary.json")));
        Assert.Empty(processRunner.Calls);
    }

    private static void AssertPreparationBeforeCheck(JsonElement commands)
    {
        var hints = commands.EnumerateArray().ToArray();
        var prepareIndex = Array.FindIndex(hints, x => x.GetProperty("kind").GetString() == "replay_packet");
        var checkIndex = Array.FindIndex(hints, x => x.GetProperty("kind").GetString() == "replay_packet_check");
        Assert.True(prepareIndex >= 0 && checkIndex > prepareIndex);
    }

    [Fact]
    public async Task RunAsync_ReplayPacket_Check_Allows_No_Evidence_Sessions()
    {
        var console = new FakeConsole();
        var fileSystem = new FakeFileSystem();
        const string root = "/tmp/empty-run";
        fileSystem.CreateDirectory(root);
        var app = new App(new AppDependencies { Console = console, FileSystem = fileSystem });
        Assert.Equal(0, await app.RunAsync(["replay", "packet", "--artifacts", root]));
        console.OutputLines.Clear();
        Assert.Equal(0, await app.RunAsync(["replay", "packet", "--artifacts", root, "--check"]));
    }
}
