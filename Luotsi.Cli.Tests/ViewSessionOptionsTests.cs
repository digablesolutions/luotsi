using System.Text.Json;
using Luotsi.Cli;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Hosts.Android;
using Luotsi.Cli.Hosts.Android.View;
using Luotsi.Cli.Infrastructure;
using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;
using Luotsi.Cli.Telemetry;
using Luotsi.Cli.View;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed partial class AppTests
{
    [Fact]
    public async Task RunAsync_View_Without_Device_Returns_Usage_Error_Envelope()
    {
        var console = new FakeConsole();
        var app = new App(console: console);

        var exitCode = await app.RunAsync(["view"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("view", envelope.RootElement.GetProperty("command").GetString());
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
    }



    [Fact]
    public async Task RunAsync_View_Uses_Injected_ViewSessionFactory()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var session = new FakeViewSession(23);
        var factory = new FakeViewSessionFactory(session);
        var app = new App(
            console: console,
            timeProvider: timeProvider,
            deviceHostFactory: new FakeDeviceHostFactory(host),
            viewSessionFactory: factory);

        var exitCode = await app.RunAsync([
            "view",
            "--device", "192.168.0.134:5555",
            "--decoder", "wmf",
            "--headless",
            "--record", "capture.mkv",
            "--max-size", "1280",
            "--max-fps", "30",
            "--video-bit-rate", "12M",
            "--stats-interval-ms", "0",
            "--renderer-stats-interval-ms", "125",
            "--overlay-screen-state",
            "--overlay-telemetry"]);

        Assert.Equal(23, exitCode);
        Assert.Same(host, factory.LastDeviceHost);
        Assert.NotNull(factory.LastArtifacts);
        var options = Assert.Single(session.Options);
        Assert.Equal("192.168.0.134:5555", options.DeviceSelector);
        Assert.Equal("h264", options.Codec);
        Assert.Equal("wmf", options.Decoder);
        Assert.True(options.Headless);
        Assert.Equal("capture.mkv", options.RecordPath);
        Assert.Equal(1280, options.MaxSize);
        Assert.Equal(30, options.MaxFps);
        Assert.Equal("12M", options.VideoBitRate);
        Assert.Equal(0, options.StatsIntervalMs);
        Assert.Equal(125, options.RendererStatsIntervalMs);
        Assert.True(options.OverlayScreenState);
        Assert.True(options.OverlayTelemetry);
        Assert.Equal("balanced", options.PresetName);
    }



    [Fact]
    public async Task RunAsync_View_Uses_Safe_Preset_Defaults()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var session = new FakeViewSession(23);
        var factory = new FakeViewSessionFactory(session);
        var app = new App(
            console: console,
            timeProvider: timeProvider,
            deviceHostFactory: new FakeDeviceHostFactory(host),
            viewSessionFactory: factory);

        var exitCode = await app.RunAsync([
            "view",
            "--device", "192.168.0.134:5555",
            "--preset", "safe"]);

        Assert.Equal(23, exitCode);
        var options = Assert.Single(session.Options);
        Assert.Equal("safe", options.PresetName);
        Assert.Equal(1280, options.MaxSize);
        Assert.Equal(30, options.MaxFps);
        Assert.Equal("4M", options.VideoBitRate);
        Assert.Equal(1000, options.StatsIntervalMs);
        Assert.Equal(250, options.RendererStatsIntervalMs);
    }



    [Fact]
    public async Task RunAsync_View_With_Defaults_Flag_Uses_Safe_Preset()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var session = new FakeViewSession(23);
        var factory = new FakeViewSessionFactory(session);
        var app = new App(
            console: console,
            timeProvider: timeProvider,
            deviceHostFactory: new FakeDeviceHostFactory(host),
            viewSessionFactory: factory);

        var exitCode = await app.RunAsync([
            "view",
            "--device", "192.168.0.134:5555",
            "--defaults"]);

        Assert.Equal(23, exitCode);
        var options = Assert.Single(session.Options);
        Assert.Equal("safe", options.PresetName);
        Assert.Equal(1280, options.MaxSize);
        Assert.Equal(30, options.MaxFps);
        Assert.Equal("4M", options.VideoBitRate);
    }



    [Fact]
    public async Task RunAsync_View_Explicit_Options_Override_Preset_Defaults()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var session = new FakeViewSession(23);
        var factory = new FakeViewSessionFactory(session);
        var app = new App(
            console: console,
            timeProvider: timeProvider,
            deviceHostFactory: new FakeDeviceHostFactory(host),
            viewSessionFactory: factory);

        var exitCode = await app.RunAsync([
            "view",
            "--device", "192.168.0.134:5555",
            "--preset", "low-latency",
            "--max-size", "1920",
            "--stats-interval-ms", "0",
            "--video-bit-rate", "10M"]);

        Assert.Equal(23, exitCode);
        var options = Assert.Single(session.Options);
        Assert.Equal("low-latency", options.PresetName);
        Assert.Equal(1920, options.MaxSize);
        Assert.Equal(60, options.MaxFps);
        Assert.Equal("10M", options.VideoBitRate);
        Assert.Equal(0, options.StatsIntervalMs);
        Assert.Equal(0, options.RendererStatsIntervalMs);
    }



    [Fact]
    public async Task RunAsync_View_With_Invalid_Preset_Returns_Usage_Error_Envelope()
    {
        var console = new FakeConsole();
        var app = new App(console: console);

        var exitCode = await app.RunAsync(["view", "--device", "192.168.0.134:5555", "--preset", "turbo"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("Unknown view preset", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }



    [Fact]
    public async Task RunAsync_View_With_Negative_Stats_Interval_Returns_Usage_Error_Envelope()
    {
        var console = new FakeConsole();
        var app = new App(console: console);

        var exitCode = await app.RunAsync(["view", "--device", "192.168.0.134:5555", "--stats-interval-ms", "-1"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("--stats-interval-ms", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }



    [Fact]
    public async Task RunAsync_View_With_Negative_Renderer_Stats_Interval_Returns_Usage_Error_Envelope()
    {
        var console = new FakeConsole();
        var app = new App(console: console);

        var exitCode = await app.RunAsync(["view", "--device", "192.168.0.134:5555", "--renderer-stats-interval-ms", "-1"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("usage_error", envelope.RootElement.GetProperty("error").GetProperty("category").GetString());
        Assert.Contains("--renderer-stats-interval-ms", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }



}
