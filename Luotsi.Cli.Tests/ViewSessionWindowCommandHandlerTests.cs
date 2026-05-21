using System.Text.Json;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Contracts;
using Luotsi.Cli.View.Session;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed class ViewSessionWindowCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_TakeScreenshot_And_PauseStream_Emit_Window_Command_Events()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var host = new FakeDeviceHost();
        var events = new List<string>();
        var pausedStates = new List<bool>();
        var options = new ViewOptions("device-a", "adb", "h264", "ffmpeg", false, null, 1600, 60, "8M", false, false);
        var context = CreateContext(host, fileSystem, timeProvider, options, value => events.Add(JsonSerializer.Serialize(value)));
        var recording = new ViewSessionRecordingCoordinator(context.CreateRecordingContext(), () => Task.CompletedTask);
        var handler = new ViewSessionWindowCommandHandler(
            context.CreateWindowCommandContext(),
            recording,
            new ViewSessionInteractionCallbacks(() => "device-a", (_, _) => true),
            _ => false);

        handler.AttachStreamPauseUpdater(isPaused => pausedStates.Add(isPaused));

        await handler.HandleAsync(ViewWindowCommand.TakeScreenshot);
        await handler.HandleAsync(ViewWindowCommand.PauseStream);
        await handler.HandleAsync(ViewWindowCommand.PauseStream);

        Assert.Equal(["view-window-001"], host.TakeScreenshotRequests);
        Assert.Equal([true, false], pausedStates);

        using var screenshot = JsonDocument.Parse(events[0]);
        Assert.Equal(SessionEventTypes.View.ScreenshotCaptured, screenshot.RootElement.GetProperty("type").GetString());
        Assert.Equal("view-window-001", screenshot.RootElement.GetProperty("label").GetString());

        using var paused = JsonDocument.Parse(events[1]);
        Assert.Equal(SessionEventTypes.View.StreamPaused, paused.RootElement.GetProperty("type").GetString());
        Assert.Equal("device-a", paused.RootElement.GetProperty("device").GetString());

        using var resumed = JsonDocument.Parse(events[2]);
        Assert.Equal(SessionEventTypes.View.StreamResumed, resumed.RootElement.GetProperty("type").GetString());
        Assert.Equal("device-a", resumed.RootElement.GetProperty("device").GetString());
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