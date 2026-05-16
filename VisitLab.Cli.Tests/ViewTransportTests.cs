using System.Buffers.Binary;
using VisitLab.Cli.Errors;
using VisitLab.Cli.Hosts.Android.View;
using VisitLab.Cli.Infrastructure;
using VisitLab.Cli.Models;
using VisitLab.Cli.View;
using VisitLab.Cli.View.Backends.Ffmpeg;
using Xunit;

namespace VisitLab.Cli.Tests;

public sealed class ViewTransportTests
{
    [Fact]
    public async Task ReadHeaderAsync_Parses_Valid_Stream_Header()
    {
        var stream = new ViewPacketStreamHarness()
            .WriteHeader("h264", 1080, 1920)
            .Build();
        var reader = new ViewPacketStreamReader();

        var header = await reader.ReadHeaderAsync(stream);

        Assert.Equal(ViewPacketStreamReader.CurrentProtocolVersion, header.ProtocolVersion);
        Assert.Equal("h264", header.Codec);
        Assert.Equal(1080, header.Width);
        Assert.Equal(1920, header.Height);
    }

    [Fact]
    public async Task ReadHeaderAsync_Invalid_Magic_Throws()
    {
        var bytes = new byte[ViewPacketStreamReader.StreamHeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), 0x12345678);
        bytes[4] = ViewPacketStreamReader.CurrentProtocolVersion;
        var stream = new MemoryStream(bytes);
        var reader = new ViewPacketStreamReader();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadHeaderAsync(stream));

        Assert.Contains("magic", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadPacketsAsync_Parses_Config_Frame_And_StreamEnd()
    {
        var stream = new ViewPacketStreamHarness()
            .WriteHeader("h264", 1080, 1920)
            .WritePacket(ViewPacketType.Config, 1, 0, false, [0x01, 0x02])
            .WritePacket(ViewPacketType.Frame, 2, 33_000, true, [0x03, 0x04, 0x05])
            .WritePacket(ViewPacketType.StreamEnd, 3, 66_000, false, [])
            .Build();
        var reader = new ViewPacketStreamReader();

        await reader.ReadHeaderAsync(stream);
        var packets = await ReadAllAsync(reader.ReadPacketsAsync(stream));

        Assert.Equal(3, packets.Count);
        Assert.Equal(ViewPacketType.Config, packets[0].PacketType);
        Assert.True(packets[1].IsKeyFrame);
        Assert.Equal(ViewPacketType.StreamEnd, packets[2].PacketType);
        Assert.Equal([0x03, 0x04, 0x05], packets[1].Payload.ToArray());
    }

    [Fact]
    public async Task AndroidViewBootstrap_StartAsync_Pushes_Forwards_And_Starts_Helper()
    {
        var adb = new FakeAdbClient();
        adb.EnqueueRunResult(new ProcessResult(0, string.Empty, string.Empty));
        adb.EnqueueRunResult(new ProcessResult(0, "38543\n", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, string.Empty, string.Empty));
        var locator = new FakeAndroidViewHelperPackageLocator(new AndroidViewHelperPackage("C:/tmp/helper.apk", "/data/local/tmp/visitlab-view-server.apk", "fi.systam.visitlab.view.Main", "test-helper"));
        var bootstrap = new AndroidViewBootstrap(new FakeAdbClientFactory(adb), new DefaultProcessRunner(), locator, new FakeUniqueIdGenerator("session123"));

        var connection = await bootstrap.StartAsync(new ViewStartRequest("adb", "device-1", 1280, 30, "8M", "h264"));

        Assert.Equal("session123", connection.SessionId);
        Assert.Equal("h264", connection.Codec);
        Assert.Equal(ViewPacketStreamReader.CurrentProtocolVersion, connection.ProtocolVersion);
        Assert.Equal(38543, connection.LocalPort);
        Assert.Equal(["push", "C:/tmp/helper.apk", "/data/local/tmp/visitlab-view-server.apk"], adb.RunCommands[0]);
        Assert.Equal(["forward", "tcp:0", "localabstract:visitlab_view_session123"], adb.RunCommands[1]);
        Assert.Contains("sh -c 'CLASSPATH=/data/local/tmp/visitlab-view-server.apk app_process / fi.systam.visitlab.view.Main", adb.ShellCommands[0], StringComparison.Ordinal);
        Assert.Contains("--codec h264", adb.ShellCommands[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AndroidViewBootstrap_StopAsync_Removes_Forward_And_Remote_Helper()
    {
        var adb = new FakeAdbClient();
        adb.EnqueueRunResult(new ProcessResult(0, string.Empty, string.Empty));
        adb.EnqueueRunResult(new ProcessResult(0, "38543\n", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, string.Empty, string.Empty));
        adb.EnqueueRunResult(new ProcessResult(0, string.Empty, string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, string.Empty, string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, string.Empty, string.Empty));
        var locator = new FakeAndroidViewHelperPackageLocator(new AndroidViewHelperPackage("C:/tmp/helper.apk", "/data/local/tmp/visitlab-view-server.apk", "fi.systam.visitlab.view.Main", "test-helper"));
        var bootstrap = new AndroidViewBootstrap(new FakeAdbClientFactory(adb), new DefaultProcessRunner(), locator, new FakeUniqueIdGenerator("session123"));

        await bootstrap.StartAsync(new ViewStartRequest("adb", "device-1", 1280, 30, "8M", "h264"));
        await bootstrap.StopAsync();

        Assert.Equal(["forward", "--remove", "tcp:38543"], adb.RunCommands[2]);
        Assert.Contains("pkill -f visitlab_view_session123", adb.ShellCommands[1], StringComparison.Ordinal);
        Assert.Contains("rm -f /data/local/tmp/visitlab-view-server.apk", adb.ShellCommands[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AndroidViewBootstrap_StartAsync_Cleans_Up_When_Helper_Start_Fails()
    {
        var adb = new FakeAdbClient();
        adb.EnqueueRunResult(new ProcessResult(0, string.Empty, string.Empty));
        adb.EnqueueRunResult(new ProcessResult(0, "38543\n", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(1, string.Empty, "boom"));
        adb.EnqueueRunResult(new ProcessResult(0, string.Empty, string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, string.Empty, string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, string.Empty, string.Empty));
        var locator = new FakeAndroidViewHelperPackageLocator(new AndroidViewHelperPackage("C:/tmp/helper.apk", "/data/local/tmp/visitlab-view-server.apk", "fi.systam.visitlab.view.Main", "test-helper"));
        var bootstrap = new AndroidViewBootstrap(new FakeAdbClientFactory(adb), new DefaultProcessRunner(), locator, new FakeUniqueIdGenerator("session123"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => bootstrap.StartAsync(new ViewStartRequest("adb", "device-1", 1280, 30, "8M", "h264")));

        Assert.Contains("view helper start failed", error.Message, StringComparison.Ordinal);
        Assert.Equal(["forward", "--remove", "tcp:38543"], adb.RunCommands[2]);
        Assert.Contains("pkill -f visitlab_view_session123", adb.ShellCommands[1], StringComparison.Ordinal);
        Assert.Contains("rm -f /data/local/tmp/visitlab-view-server.apk", adb.ShellCommands[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AndroidViewBootstrap_StartAsync_Uses_Forward_List_Fallback_When_Forwarded_Port_Output_Is_Missing()
    {
        var adb = new FakeAdbClient();
        adb.EnqueueRunResult(new ProcessResult(0, string.Empty, string.Empty));
        adb.EnqueueRunResult(new ProcessResult(0, string.Empty, string.Empty));
        adb.EnqueueRunResult(new ProcessResult(0, "device-1 tcp:41237 localabstract:visitlab_view_session123\n", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, string.Empty, string.Empty));
        var locator = new FakeAndroidViewHelperPackageLocator(new AndroidViewHelperPackage("C:/tmp/helper.apk", "/data/local/tmp/visitlab-view-server.apk", "fi.systam.visitlab.view.Main", "test-helper"));
        var bootstrap = new AndroidViewBootstrap(new FakeAdbClientFactory(adb), new DefaultProcessRunner(), locator, new FakeUniqueIdGenerator("session123"));

        var connection = await bootstrap.StartAsync(new ViewStartRequest("adb", "device-1", 1280, 30, "8M", "h264"));

        Assert.Equal(41237, connection.LocalPort);
        Assert.Equal(["forward", "--list"], adb.RunCommands[2]);
    }

    [Fact]
    public async Task AndroidViewBootstrap_StartAsync_Rejects_Unsupported_Codec()
    {
        var adb = new FakeAdbClient();
        var locator = new FakeAndroidViewHelperPackageLocator(new AndroidViewHelperPackage("C:/tmp/helper.apk", "/data/local/tmp/visitlab-view-server.apk", "fi.systam.visitlab.view.Main", "test-helper"));
        var bootstrap = new AndroidViewBootstrap(new FakeAdbClientFactory(adb), new DefaultProcessRunner(), locator, new FakeUniqueIdGenerator("session123"));

        var error = await Assert.ThrowsAsync<UsageException>(() => bootstrap.StartAsync(new ViewStartRequest("adb", "device-1", 1280, 30, "8M", "h265")));

        Assert.Contains("h264", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(adb.RunCommands);
    }

    [Fact]
    public void AndroidViewHelperPackageLocator_Uses_Default_Project_Output_When_Environment_Is_Missing()
    {
        var fileSystem = new FakeFileSystem();
        var expectedPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "VisitLab.ViewServer.Android", "app", "build", "outputs", "apk", "debug", "app-debug.apk"));
        fileSystem.AddFile(expectedPath, "apk");
        var locator = new AndroidViewHelperPackageLocator(new FakeEnvironmentVariables(new Dictionary<string, string>()), fileSystem);

        var package = locator.Resolve();

        Assert.Equal(expectedPath, package.LocalPath);
        Assert.Equal("/data/local/tmp/visitlab-view-server.apk", package.RemotePath);
    }

    [Fact]
    public void LibavNativeLibraryLoader_Prefers_Configured_Root_And_Caches_Result()
    {
        var binder = new FakeLibavNativeLibraryBinder();
        binder.SucceedFor("C:\\ffmpeg-custom");
        var loader = new LibavNativeLibraryLoader(
            new FakeEnvironmentVariables(new Dictionary<string, string>
            {
                ["DEVICE_E2E_FFMPEG_ROOT"] = "C:\\ffmpeg-custom"
            }),
            binder);

        var firstRoot = loader.EnsureLoaded();
        var secondRoot = loader.EnsureLoaded();

        Assert.Equal("C:\\ffmpeg-custom", firstRoot);
        Assert.Equal(firstRoot, secondRoot);
        Assert.Equal(["C:\\ffmpeg-custom"], binder.AttemptedRoots);
    }

    [Fact]
    public void LibavNativeLibraryLoader_Missing_Libraries_Throws_Clear_Error()
    {
        var loader = new LibavNativeLibraryLoader(
            new FakeEnvironmentVariables(new Dictionary<string, string>()),
            new FakeLibavNativeLibraryBinder());

        var error = Assert.Throws<InvalidOperationException>(() => loader.EnsureLoaded());

        Assert.Contains("DEVICE_E2E_FFMPEG_ROOT", error.Message, StringComparison.Ordinal);
        Assert.Contains("ffmpeg", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LibavViewBackend_Decodes_Frames_And_Initializes_Renderer()
    {
        var decoder = new FakeLibavVideoDecoder();
        decoder.EnqueueDecodedFrames([]);
        decoder.EnqueueDecodedFrames([
            new ViewFrame(1, 33_000, 1080, 1920, "AV_PIX_FMT_YUV420P", null)
        ]);
        decoder.EnqueueFlushedFrames([
            new ViewFrame(2, 66_000, 1080, 1920, "AV_PIX_FMT_YUV420P", null)
        ]);
        var renderer = new FakeViewRenderer();
        var backend = new LibavViewBackend(new FakeLibavVideoDecoderFactory(decoder));
        await backend.InitializeAsync(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward"), renderer, null);

        await backend.RunAsync(GetPackets(
            new ViewPacket(ViewPacketType.Config, 1, 0, false, new byte[] { 0x01 }),
            new ViewPacket(ViewPacketType.Frame, 2, 33_000, true, new byte[] { 0x02 }),
            new ViewPacket(ViewPacketType.StreamEnd, 3, 66_000, false, Array.Empty<byte>())));

        Assert.Equal(2, decoder.DecodedPackets.Count);
        Assert.Equal(1, decoder.FlushCount);
        Assert.NotNull(renderer.DisplayInfo);
        Assert.Equal("AV_PIX_FMT_YUV420P", renderer.DisplayInfo!.PixelFormat);
        Assert.Equal(2, renderer.PresentedFrames.Count);
        Assert.NotEmpty(renderer.StatsUpdates);
        Assert.Equal(2, renderer.StatsUpdates[^1].DecodedFrames);
        Assert.Equal(2, renderer.StatsUpdates[^1].PresentedFrames);
    }

    [Fact]
    public async Task LibavViewBackend_RotationReset_Flushes_And_Resets_Decoder()
    {
        var decoder = new FakeLibavVideoDecoder();
        decoder.EnqueueDecodedFrames([]);
        decoder.EnqueueFlushedFrames([
            new ViewFrame(1, 0, 1080, 1920, "AV_PIX_FMT_YUV420P", null)
        ]);
        decoder.EnqueueFlushedFrames([]);
        var backend = new LibavViewBackend(new FakeLibavVideoDecoderFactory(decoder));
        await backend.InitializeAsync(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward"), null, null);

        await backend.RunAsync(GetPackets(
            new ViewPacket(ViewPacketType.Config, 1, 0, false, new byte[] { 0x01 }),
            new ViewPacket(ViewPacketType.RotationReset, 2, 0, false, Array.Empty<byte>()),
            new ViewPacket(ViewPacketType.StreamEnd, 3, 0, false, Array.Empty<byte>())));

        Assert.Equal(1, decoder.ResetCount);
        Assert.Equal(2, decoder.FlushCount);
    }

    [Fact]
    public async Task LibavViewBackend_Ignores_Empty_Compressed_Packets()
    {
        var decoder = new FakeLibavVideoDecoder();
        var backend = new LibavViewBackend(new FakeLibavVideoDecoderFactory(decoder));
        await backend.InitializeAsync(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward"), null, null);

        await backend.RunAsync(GetPackets(
            new ViewPacket(ViewPacketType.Config, 1, 0, false, Array.Empty<byte>()),
            new ViewPacket(ViewPacketType.Frame, 2, 0, false, Array.Empty<byte>()),
            new ViewPacket(ViewPacketType.StreamEnd, 3, 0, false, Array.Empty<byte>())));

        Assert.Empty(decoder.DecodedPackets);
        Assert.Equal(1, decoder.FlushCount);
    }

    [Fact]
    public async Task AnnexBViewRecorder_Writes_Config_And_Frame_Packets_To_Output()
    {
        var fileSystem = new FakeFileSystem();
        var outputPath = Path.GetFullPath("capture.h264");
        var recorder = new AnnexBViewRecorder(fileSystem, outputPath);

        await recorder.InitializeAsync(new ViewConnectionInfo("session", "h264", 1, 1080, 1920, 27183, "helper", "adb-forward"));
        await recorder.WritePacketAsync(new ViewPacket(ViewPacketType.Config, 1, 0, false, new byte[] { 0x01, 0x02 }));
        await recorder.WritePacketAsync(new ViewPacket(ViewPacketType.Frame, 2, 33_000, true, new byte[] { 0x03, 0x04 }));
        await recorder.WritePacketAsync(new ViewPacket(ViewPacketType.StreamEnd, 3, 66_000, false, Array.Empty<byte>()));
        await recorder.CompleteAsync();

        Assert.True(fileSystem.FileExists(outputPath));
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04 }, fileSystem.ReadBytes(outputPath));
    }

    [Fact]
    public void DefaultViewRecorderFactory_Rejects_NonRaw_Output_Extensions()
    {
        var factory = new DefaultViewRecorderFactory(new FakeFileSystem());

        var error = Assert.Throws<UsageException>(() => factory.Create(new ViewOptions("device-1", "adb", "h264", "ffmpeg", true, "capture.mkv", 1600, 60, "8M", false, false)));

        Assert.Contains(".h264", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NativeWindowViewRenderer_Presents_Frames_To_Window_Surface()
    {
        var windowSurface = new FakeViewWindowSurface();
        var renderer = new NativeWindowViewRenderer(new FakeViewWindowSurfaceFactory(windowSurface), new FakeDeviceHost());
        var frame = new ViewFrame(1, 33_000, 2, 1, "AV_PIX_FMT_BGRA", null)
        {
            PixelData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 },
            RowStride = 8
        };
        await renderer.InitializeAsync(new ViewDisplayInfo(2, 1, "h264", "AV_PIX_FMT_BGRA"));

        await renderer.PresentAsync(frame);
        await renderer.DisposeAsync();

        Assert.Equal("VisitLab View", windowSurface.Title);
        Assert.NotNull(windowSurface.DisplayInfo);
        Assert.Single(windowSurface.PresentedFrames);
        Assert.Equal([0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08], windowSurface.PresentedFrames[0].PixelData.ToArray());
        Assert.True(windowSurface.Disposed);
    }

    [Fact]
    public async Task NativeWindowViewRenderer_Forwards_Stats_To_Window_Surface()
    {
        var windowSurface = new FakeViewWindowSurface();
        var renderer = new NativeWindowViewRenderer(new FakeViewWindowSurfaceFactory(windowSurface), new FakeDeviceHost());
        var stats = new ViewStats(12, 12, 0, 59.9d, 59.7d, 84);
        await renderer.InitializeAsync(new ViewDisplayInfo(1600, 900, "h264", "AV_PIX_FMT_BGRA"));

        await renderer.UpdateStatsAsync(stats);

        Assert.Equal(stats, windowSurface.Stats);
    }

    [Fact]
    public async Task NativeWindowViewRenderer_Maps_Clicks_To_Relative_TapPoint()
    {
        var windowSurface = new FakeViewWindowSurface();
        var host = new FakeDeviceHost();
        var renderer = new NativeWindowViewRenderer(new FakeViewWindowSurfaceFactory(windowSurface), host);
        await renderer.InitializeAsync(new ViewDisplayInfo(1600, 900, "h264", "AV_PIX_FMT_BGRA"));
        await renderer.PresentAsync(new ViewFrame(1, 33_000, 1600, 900, "AV_PIX_FMT_BGRA", null)
        {
            PixelData = new byte[] { 0x01, 0x02, 0x03, 0x04 },
            RowStride = 6400
        });

        await windowSurface.RaisePointerAsync(new ViewPointerEvent(400, 225, 800, 450));

        var request = Assert.Single(host.TapPointRequests);
        Assert.Equal(0.5, request.XRatio!.Value, 3);
        Assert.Equal(0.5, request.YRatio!.Value, 3);
        Assert.Equal(0, request.PostTapDelayMs);
    }

    [Fact]
    public async Task NativeWindowViewRenderer_Ignores_Clicks_Outside_Letterboxed_Frame()
    {
        var windowSurface = new FakeViewWindowSurface();
        var host = new FakeDeviceHost();
        var renderer = new NativeWindowViewRenderer(new FakeViewWindowSurfaceFactory(windowSurface), host);
        await renderer.InitializeAsync(new ViewDisplayInfo(1600, 900, "h264", "AV_PIX_FMT_BGRA"));
        await renderer.PresentAsync(new ViewFrame(1, 33_000, 1600, 900, "AV_PIX_FMT_BGRA", null)
        {
            PixelData = new byte[] { 0x01, 0x02, 0x03, 0x04 },
            RowStride = 6400
        });

        await windowSurface.RaisePointerAsync(new ViewPointerEvent(10, 10, 800, 800));

        Assert.Empty(host.TapPointRequests);
    }

    [Fact]
    public async Task NativeWindowViewRenderer_Maps_Clicks_When_Window_Is_Larger_Than_Source_Without_Upscaling()
    {
        var windowSurface = new FakeViewWindowSurface();
        var host = new FakeDeviceHost();
        var renderer = new NativeWindowViewRenderer(new FakeViewWindowSurfaceFactory(windowSurface), host);
        await renderer.InitializeAsync(new ViewDisplayInfo(1600, 900, "h264", "AV_PIX_FMT_BGRA"));
        await renderer.PresentAsync(new ViewFrame(1, 33_000, 1600, 900, "AV_PIX_FMT_BGRA", null)
        {
            PixelData = new byte[] { 0x01, 0x02, 0x03, 0x04 },
            RowStride = 6400
        });

        await windowSurface.RaisePointerAsync(new ViewPointerEvent(1000, 600, 2000, 1200));

        var request = Assert.Single(host.TapPointRequests);
        Assert.Equal(0.5, request.XRatio!.Value, 3);
        Assert.Equal(0.5, request.YRatio!.Value, 3);
    }

    private static async Task<List<ViewPacket>> ReadAllAsync(IAsyncEnumerable<ViewPacket> packets)
    {
        var result = new List<ViewPacket>();
        await foreach (var packet in packets)
        {
            result.Add(packet);
        }

        return result;
    }

    private static async IAsyncEnumerable<ViewPacket> GetPackets(params ViewPacket[] packets)
    {
        foreach (var packet in packets)
        {
            yield return packet;
            await Task.Yield();
        }
    }
}

internal sealed class ViewPacketStreamHarness
{
    private readonly MemoryStream _stream = new();

    public ViewPacketStreamHarness WriteHeader(string codec, int width, int height, ushort flags = 0)
    {
        var buffer = new byte[ViewPacketStreamReader.StreamHeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), ViewPacketStreamReader.Magic);
        buffer[4] = ViewPacketStreamReader.CurrentProtocolVersion;
        buffer[5] = string.Equals(codec, "h265", StringComparison.OrdinalIgnoreCase) ? (byte)2 : (byte)1;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(6, 2), flags);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(8, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(12, 4), height);
        _stream.Write(buffer, 0, buffer.Length);
        return this;
    }

    public ViewPacketStreamHarness WritePacket(ViewPacketType packetType, long sequence, long ptsUs, bool isKeyFrame, byte[] payload)
    {
        var header = new byte[ViewPacketStreamReader.PacketHeaderSize];
        header[0] = (byte)packetType;
        header[1] = isKeyFrame ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(4, 8), sequence);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(12, 8), ptsUs);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(20, 4), payload.Length);
        _stream.Write(header, 0, header.Length);
        if (payload.Length > 0)
        {
            _stream.Write(payload, 0, payload.Length);
        }

        return this;
    }

    public MemoryStream Build()
    {
        _stream.Position = 0;
        return _stream;
    }
}

internal sealed class FakeAndroidViewHelperPackageLocator(AndroidViewHelperPackage package) : IAndroidViewHelperPackageLocator
{
    private readonly AndroidViewHelperPackage _package = package;

    public AndroidViewHelperPackage Resolve() => _package;
}

internal sealed class FakeViewWindowSurfaceFactory(FakeViewWindowSurface windowSurface) : IViewWindowSurfaceFactory
{
    private readonly FakeViewWindowSurface _windowSurface = windowSurface;

    public IViewWindowSurface Create() => _windowSurface;
}

internal sealed class FakeViewWindowSurface : IViewWindowSurface
{
    private Func<ViewPointerEvent, Task>? _pointerHandler;

    public string? Title { get; private set; }

    public ViewDisplayInfo? DisplayInfo { get; private set; }

    public List<ViewFrame> PresentedFrames { get; } = [];

    public ViewStats? Stats { get; private set; }

    public bool Disposed { get; private set; }

    public Task InitializeAsync(string title, ViewDisplayInfo displayInfo, Func<ViewPointerEvent, Task> pointerHandler, CancellationToken cancellationToken = default)
    {
        Title = title;
        DisplayInfo = displayInfo;
        _pointerHandler = pointerHandler;
        return Task.CompletedTask;
    }

    public Task PresentAsync(ViewFrame frame, CancellationToken cancellationToken = default)
    {
        PresentedFrames.Add(frame);
        return Task.CompletedTask;
    }

    public Task UpdateStatsAsync(ViewStats stats, CancellationToken cancellationToken = default)
    {
        Stats = stats;
        return Task.CompletedTask;
    }

    public Task WaitForCloseAsync(CancellationToken cancellationToken = default) => Task.Delay(Timeout.Infinite, cancellationToken);

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    public Task RaisePointerAsync(ViewPointerEvent pointerEvent) => (_pointerHandler ?? throw new InvalidOperationException("Pointer handler was not initialized."))(pointerEvent);
}

internal sealed class FakeLibavNativeLibraryBinder : ILibavNativeLibraryBinder
{
    private readonly HashSet<string?> _successfulRoots = new(StringComparer.OrdinalIgnoreCase);

    public List<string?> AttemptedRoots { get; } = [];

    public void SucceedFor(string? rootPath) => _successfulRoots.Add(rootPath);

    public void Bind(string? rootPath)
    {
        AttemptedRoots.Add(rootPath);
        if (_successfulRoots.Contains(rootPath))
        {
            return;
        }

        throw new DllNotFoundException($"Missing native libraries for '{rootPath ?? "<process-path>"}'.");
    }
}

internal sealed class FakeLibavVideoDecoderFactory(FakeLibavVideoDecoder decoder) : ILibavVideoDecoderFactory
{
    private readonly FakeLibavVideoDecoder _decoder = decoder;

    public ILibavVideoDecoder Create(ViewConnectionInfo connectionInfo) => _decoder;
}

internal sealed class FakeLibavVideoDecoder : ILibavVideoDecoder
{
    private readonly Queue<IReadOnlyList<ViewFrame>> _decodedFrames = new();
    private readonly Queue<IReadOnlyList<ViewFrame>> _flushedFrames = new();

    public List<ViewPacket> DecodedPackets { get; } = [];

    public int FlushCount { get; private set; }

    public int ResetCount { get; private set; }

    public void EnqueueDecodedFrames(IReadOnlyList<ViewFrame> frames) => _decodedFrames.Enqueue(frames);

    public void EnqueueFlushedFrames(IReadOnlyList<ViewFrame> frames) => _flushedFrames.Enqueue(frames);

    public IReadOnlyList<ViewFrame> Decode(ViewPacket packet)
    {
        DecodedPackets.Add(packet);
        return _decodedFrames.Count > 0 ? _decodedFrames.Dequeue() : Array.Empty<ViewFrame>();
    }

    public IReadOnlyList<ViewFrame> Flush()
    {
        FlushCount++;
        return _flushedFrames.Count > 0 ? _flushedFrames.Dequeue() : Array.Empty<ViewFrame>();
    }

    public void Reset() => ResetCount++;

    public void Dispose()
    {
    }
}

internal sealed class FakeViewRenderer : IViewRenderer
{
    public ViewDisplayInfo? DisplayInfo { get; private set; }

    public List<ViewFrame> PresentedFrames { get; } = [];

    public List<ViewStats> StatsUpdates { get; } = [];

    public Task InitializeAsync(ViewDisplayInfo displayInfo, CancellationToken cancellationToken = default)
    {
        DisplayInfo = displayInfo;
        return Task.CompletedTask;
    }

    public Task PresentAsync(ViewFrame frame, CancellationToken cancellationToken = default)
    {
        PresentedFrames.Add(frame);
        return Task.CompletedTask;
    }

    public Task UpdateStatsAsync(ViewStats stats, CancellationToken cancellationToken = default)
    {
        StatsUpdates.Add(stats);
        return Task.CompletedTask;
    }

    public Task WaitForCloseAsync(CancellationToken cancellationToken = default) => Task.Delay(Timeout.Infinite, cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}