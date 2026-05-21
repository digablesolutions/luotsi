using System.Text.Json;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Contracts;
using Luotsi.Cli.View.Session;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed class ViewSessionFileTransferHandlerTests
{
    [Fact]
    public async Task HandleFilePullAsync_UsesArtifactRoot_WhenLocalDirectoryMissing()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var host = new FakeDeviceHost();
        var events = new List<string>();
        var options = new ViewOptions("device-a", "adb", "h264", "ffmpeg", false, null, 1600, 60, "8M", false, false);
        var context = CreateContext(host, fileSystem, timeProvider, options, value => events.Add(JsonSerializer.Serialize(value)));
        var handler = new ViewSessionFileTransferHandler(context.CreateFileTransferContext(), _ => false);

        await handler.HandleFilePullAsync(new ViewFilePullRequest("/sdcard/Download/report.txt"));

        Assert.Equal([("/sdcard/Download/report.txt", context.Artifacts.Root)], host.PullFileRequests);

        using var pulled = JsonDocument.Parse(events[0]);
        Assert.Equal(SessionEventTypes.View.FilePulled, pulled.RootElement.GetProperty("type").GetString());
        Assert.Equal("/sdcard/Download/report.txt", pulled.RootElement.GetProperty("remote_path").GetString());
        Assert.Equal(Path.Join(context.Artifacts.Root, "report.txt"), pulled.RootElement.GetProperty("local_path").GetString());
    }

    private static ViewSessionInteractionContext CreateContext(
        FakeDeviceHost host,
        FakeFileSystem fileSystem,
        ManualTimeProvider timeProvider,
        ViewOptions options,
        Action<object> writeEvent)
    {
        var artifacts = ArtifactSession.Create(CliOptions.Parse(["view"]), fileSystem, timeProvider);
        var recorder = new SessionControlledViewRecorder(new FakeViewRecorderFactory(), options);
        return new ViewSessionInteractionContext(
            host,
            artifacts,
            options,
            recorder,
            timeProvider,
            "session",
            writeEvent,
            new FakeArtifactFolderOpener());
    }
}