using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Processes;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed partial class AppTests
{
    [Fact]
    public async Task ProcessRunner_Captures_Stdout_And_Exit_Code()
    {
        var (fileName, args) = OperatingSystem.IsWindows()
            ? ("powershell.exe", ["-NoLogo", "-NoProfile", "-Command", "[Console]::Out.Write('ok')"])
            : ("/bin/sh", new[] { "-c", "printf 'ok'" });
        var result = await new DefaultProcessRunner().RunAsync(fileName, args);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ok", result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
    }

    [Fact]
    public async Task ArtifactSession_Rejects_Rooted_Or_Nested_Artifact_Names()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var session = ArtifactSession.Create(CliOptions.Parse(["devices"]), fileSystem, timeProvider);

        await Assert.ThrowsAsync<UsageException>(() => session.WriteTextAsync("../escape.txt", "bad"));
        await Assert.ThrowsAsync<UsageException>(() => session.WriteJsonAsync("/tmp/escape.json", new { ok = true }));
    }


}
