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

    [Fact]
    public async Task ArtifactSession_Writes_Markdown_Index_For_Text_And_Json_Artifacts()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var session = ArtifactSession.Create(CliOptions.Parse(["screen-state"]), fileSystem, timeProvider);

        await session.WriteTextAsync("logcat.txt", "log");
        await session.WriteJsonAsync("screen-state.json", new { element_count = 1 });
        await session.WriteTextAsync("hierarchy.xml", "<hierarchy />");

        var index = await fileSystem.ReadAllTextAsync(Path.Join(session.Root, "index.md"));

        Assert.Contains("# Luotsi Artifacts", index, StringComparison.Ordinal);
        Assert.Contains("## Logs", index, StringComparison.Ordinal);
        Assert.Contains("- [logcat.txt](logcat.txt)", index, StringComparison.Ordinal);
        Assert.Contains("## Screen State", index, StringComparison.Ordinal);
        Assert.Contains("- [screen-state.json](screen-state.json)", index, StringComparison.Ordinal);
        Assert.Contains("## Hierarchy", index, StringComparison.Ordinal);
        Assert.Contains("- [hierarchy.xml](hierarchy.xml)", index, StringComparison.Ordinal);
        Assert.DoesNotContain("[index.md]", index, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArtifactSession_Writes_Html_Index_For_Browsing_Artifacts()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var session = ArtifactSession.Create(CliOptions.Parse(["run"]), fileSystem, timeProvider);

        await session.WriteTextAsync("logcat.txt", "log");
        await session.WriteJsonAsync("run-report.json", new { schema = "luotsi-scenario-run-report.v1", status = "passed", total = 2, passed = 2, failed = 0, durationMs = 1234 });
        await session.WriteTextAsync("events.jsonl", """
        {"type":"scenario_run_started"}
        {"type":"scenario_started"}
        {"type":"scenario_run_ended","status":"passed"}
        """);
        await using (var screenshot = fileSystem.OpenWrite(Path.Join(session.Root, "home shot.png")))
        {
            await screenshot.WriteAsync(new byte[] { 1, 2, 3 });
        }

        await session.RefreshIndexAsync();

        var index = await fileSystem.ReadAllTextAsync(Path.Join(session.Root, "index.html"));

        Assert.Contains("<title>Luotsi Artifacts</title>", index, StringComparison.Ordinal);
        Assert.Contains("<h2>Screenshots</h2>", index, StringComparison.Ordinal);
        Assert.Contains("href=\"home%20shot.png\"", index, StringComparison.Ordinal);
        Assert.Contains("<h2>Reports</h2>", index, StringComparison.Ordinal);
        Assert.Contains("run-report.json", index, StringComparison.Ordinal);
        Assert.Contains("status=passed | total=2 | passed=2 | failed=0 | duration_ms=1234", index, StringComparison.Ordinal);
        Assert.Contains("events=3 | terminal=passed", index, StringComparison.Ordinal);
        Assert.Contains("<h2>Logs</h2>", index, StringComparison.Ordinal);
        Assert.DoesNotContain("index.md", index, StringComparison.Ordinal);
        Assert.DoesNotContain("index.html", index, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArtifactSession_RefreshIndex_Includes_Pulled_Media_Files()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-18T10:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var session = ArtifactSession.Create(CliOptions.Parse(["record"]), fileSystem, timeProvider);
        await using (var screenshot = fileSystem.OpenWrite(Path.Join(session.Root, "demo shot.png")))
        {
            await screenshot.WriteAsync(new byte[] { 1, 2, 3 });
        }

        await using (var recording = fileSystem.OpenWrite(Path.Join(session.Root, "demo.mp4")))
        {
            await recording.WriteAsync(new byte[] { 4, 5, 6 });
        }

        await session.RefreshIndexAsync();

        var index = await fileSystem.ReadAllTextAsync(Path.Join(session.Root, "index.md"));

        Assert.Contains("## Screenshots", index, StringComparison.Ordinal);
        Assert.Contains("- [demo shot.png](demo%20shot.png)", index, StringComparison.Ordinal);
        Assert.Contains("## Recordings", index, StringComparison.Ordinal);
        Assert.Contains("- [demo.mp4](demo.mp4)", index, StringComparison.Ordinal);
    }


}
