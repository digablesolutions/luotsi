using System.Text.Json;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Contracts;
using Luotsi.Cli.View.Session;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed class ViewSessionDeviceInputHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_Routes_Device_Input_And_Clipboard_Event()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var fileSystem = new FakeFileSystem();
        var host = new FakeDeviceHost();
        var events = new List<string>();
        var options = new ViewOptions("device-a", "adb", "h264", "ffmpeg", false, null, 1600, 60, "8M", false, false);
        var context = CreateContext(host, fileSystem, timeProvider, options, value => events.Add(JsonSerializer.Serialize(value)));
        var handler = new ViewSessionDeviceInputHandler(context.CreateDeviceInputContext(), _ => false);

        Assert.True(await handler.TryHandleAsync(new ViewTapRequest(0.25d, 0.75d)));
        Assert.True(await handler.TryHandleAsync(new ViewTextInputRequest("hello")));
        Assert.True(await handler.TryHandleAsync(new ViewKeyInputRequest("KEYCODE_ENTER")));
        Assert.True(await handler.TryHandleAsync(new ViewScrollRequest(1, -1)));
        Assert.True(await handler.TryHandleAsync(new ViewClipboardPasteRequest("paste")));

        Assert.Equal([("view-window", 0.25d, 0.75d, 0)], host.TapPointRequests);
        Assert.Equal(["hello", "paste"], host.TypeTextRequests);
        Assert.Equal(["KEYCODE_ENTER"], host.KeyEventRequests);
        Assert.Equal([(1, -1)], host.ScrollRequests);

        using var clipboard = JsonDocument.Parse(Assert.Single(events));
        Assert.Equal(SessionEventTypes.View.ClipboardPasted, clipboard.RootElement.GetProperty("type").GetString());
        Assert.Equal(5, clipboard.RootElement.GetProperty("length").GetInt32());
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