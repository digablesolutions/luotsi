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
    public async Task RunAsync_View_HighQuality_Preset_Uses_Quality_Defaults()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var session = new FakeViewSession(0);
        var app = new App(
            console: console,
            timeProvider: timeProvider,
            deviceHostFactory: new FakeDeviceHostFactory(host),
            viewSessionFactory: new FakeViewSessionFactory(session));

        var exitCode = await app.RunAsync([
            "view",
            "--device", "192.168.0.134:5555",
            "--preset", "high-quality"]);

        Assert.Equal(0, exitCode);
        var options = Assert.Single(session.Options);
        Assert.Equal("high-quality", options.PresetName);
        Assert.Equal(1920, options.MaxSize);
        Assert.Equal(60, options.MaxFps);
        Assert.Equal("12M", options.VideoBitRate);
    }

    [Fact]
    public async Task RunAsync_View_Quality_Preset_Alias_Uses_HighQuality_Name()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var session = new FakeViewSession(0);
        var app = new App(
            console: console,
            timeProvider: timeProvider,
            deviceHostFactory: new FakeDeviceHostFactory(host),
            viewSessionFactory: new FakeViewSessionFactory(session));

        var exitCode = await app.RunAsync([
            "view",
            "--device", "192.168.0.134:5555",
            "--preset", "quality"]);

        Assert.Equal(0, exitCode);
        var options = Assert.Single(session.Options);
        Assert.Equal("high-quality", options.PresetName);
        Assert.Equal(1920, options.MaxSize);
        Assert.Equal("12M", options.VideoBitRate);
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
    public async Task RunAsync_View_Profile_Seeds_View_Options()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var session = new FakeViewSession(23);
        var factory = new FakeViewSessionFactory(session);
        var profiles = new FakeViewProfileStore();
        profiles.Profiles["desk"] = new ViewProfile(
            Device: "profile-device",
            Decoder: "wmf",
            Preset: "low-latency",
            Headless: true,
            Record: "profile.mkv",
            MaxSize: 1024,
            MaxFps: 24,
            VideoBitRate: "3M",
            StatsIntervalMs: 500,
            RendererStatsIntervalMs: 125,
            OverlayScreenState: true,
            OverlayTelemetry: true,
            ScaleMode: "fill",
            PollArtifacts: "per-attempt");
        var app = new App(
            console: console,
            timeProvider: timeProvider,
            deviceHostFactory: new FakeDeviceHostFactory(host),
            viewSessionFactory: factory,
            viewProfileStore: profiles);

        var exitCode = await app.RunAsync(["view", "--profile", "desk"]);

        Assert.Equal(23, exitCode);
        var options = Assert.Single(session.Options);
        Assert.Equal("profile-device", options.DeviceSelector);
        Assert.Equal("wmf", options.Decoder);
        Assert.Equal("low-latency", options.PresetName);
        Assert.True(options.Headless);
        Assert.Equal("profile.mkv", options.RecordPath);
        Assert.Equal(1024, options.MaxSize);
        Assert.Equal(24, options.MaxFps);
        Assert.Equal("3M", options.VideoBitRate);
        Assert.Equal(500, options.StatsIntervalMs);
        Assert.Equal(125, options.RendererStatsIntervalMs);
        Assert.True(options.OverlayScreenState);
        Assert.True(options.OverlayTelemetry);
        Assert.Equal("fill", options.ScaleMode);
        Assert.Equal("per-attempt", factory.LastArtifacts!.ToData().PollArtifacts);
    }

    [Fact]
    public async Task RunAsync_View_Cli_Options_Override_Profile()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var session = new FakeViewSession(23);
        var factory = new FakeViewSessionFactory(session);
        var profiles = new FakeViewProfileStore();
        profiles.Profiles["desk"] = new ViewProfile(Device: "profile-device", Decoder: "wmf", MaxSize: 1024);
        var app = new App(
            console: console,
            timeProvider: timeProvider,
            deviceHostFactory: new FakeDeviceHostFactory(host),
            viewSessionFactory: factory,
            viewProfileStore: profiles);

        var exitCode = await app.RunAsync(["view", "--profile", "desk", "--device", "cli-device", "--decoder", "ffmpeg", "--max-size", "1920"]);

        Assert.Equal(23, exitCode);
        var options = Assert.Single(session.Options);
        Assert.Equal("cli-device", options.DeviceSelector);
        Assert.Equal("ffmpeg", options.Decoder);
        Assert.Equal(1920, options.MaxSize);
    }

    [Fact]
    public async Task RunAsync_View_SaveProfile_Writes_Resolved_View_Profile()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var session = new FakeViewSession(0);
        var profiles = new FakeViewProfileStore();
        var app = new App(
            console: console,
            timeProvider: timeProvider,
            deviceHostFactory: new FakeDeviceHostFactory(host),
            viewSessionFactory: new FakeViewSessionFactory(session),
            viewProfileStore: profiles);

        var exitCode = await app.RunAsync([
            "view",
            "--device", "desk-device",
            "--preset", "safe",
            "--record", "capture.mkv",
            "--scale-mode", "fill",
            "--poll-artifacts", "none",
            "--save-profile", "desk"]);

        Assert.Equal(0, exitCode);
        var profile = profiles.Profiles["desk"];
        Assert.Equal("desk-device", profile.Device);
        Assert.Equal("safe", profile.Preset);
        Assert.Equal("capture.mkv", profile.Record);
        Assert.Equal("none", profile.PollArtifacts);
        Assert.Equal(1280, profile.MaxSize);
        Assert.Equal(30, profile.MaxFps);
        Assert.Equal("4M", profile.VideoBitRate);
        Assert.Equal("fill", profile.ScaleMode);
        Assert.True(profiles.Profiles.ContainsKey("last"));
    }

    [Fact]
    public async Task RunAsync_View_Profile_Defaults_Uses_Safe_Tuning_Over_Profile_Tuning()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var session = new FakeViewSession(23);
        var profiles = new FakeViewProfileStore();
        profiles.Profiles["physical-live"] = new ViewProfile(
            Device: "profile-device",
            Decoder: "wmf",
            Preset: "high-quality",
            MaxSize: 2560,
            MaxFps: 60,
            VideoBitRate: "12M",
            StatsIntervalMs: 20,
            RendererStatsIntervalMs: 20);
        var app = new App(
            console: console,
            timeProvider: timeProvider,
            deviceHostFactory: new FakeDeviceHostFactory(host),
            viewSessionFactory: new FakeViewSessionFactory(session),
            viewProfileStore: profiles);

        var exitCode = await app.RunAsync(["view", "--profile", "physical-live", "--defaults"]);

        Assert.Equal(23, exitCode);
        var options = Assert.Single(session.Options);
        Assert.Equal("profile-device", options.DeviceSelector);
        Assert.Equal("safe", options.PresetName);
        Assert.Equal("ffmpeg", options.Decoder);
        Assert.Equal(1280, options.MaxSize);
        Assert.Equal(30, options.MaxFps);
        Assert.Equal("4M", options.VideoBitRate);
        Assert.Equal(1000, options.StatsIntervalMs);
    }

    [Fact]
    public async Task RunAsync_ViewDoctor_Profile_Defaults_Allows_Safe_Reset_When_Profile_Has_Preset()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        host.ConnectedDevices.Add(new DeviceInfo("profile-device", "device", "usb:1-1 product:test"));
        var profiles = new FakeViewProfileStore();
        profiles.Profiles["physical-live"] = new ViewProfile(Device: "profile-device", Preset: "high-quality", MaxSize: 2560, MaxFps: 60);
        var app = new App(
            console: console,
            timeProvider: timeProvider,
            deviceHostFactory: new FakeDeviceHostFactory(host),
            viewProfileStore: profiles);

        var exitCode = await app.RunAsync(["view-doctor", "--profile", "physical-live", "--defaults"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("safe", envelope.RootElement.GetProperty("data").GetProperty("preset").GetString());
    }

    [Fact]
    public async Task RunAsync_View_Does_Not_Refresh_Last_Profile_When_Session_Fails()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var session = new FakeViewSession(23);
        var profiles = new FakeViewProfileStore();
        var app = new App(
            console: console,
            timeProvider: timeProvider,
            deviceHostFactory: new FakeDeviceHostFactory(host),
            viewSessionFactory: new FakeViewSessionFactory(session),
            viewProfileStore: profiles);

        var exitCode = await app.RunAsync(["view", "--device", "desk-device"]);

        Assert.Equal(23, exitCode);
        Assert.False(profiles.Profiles.ContainsKey("last"));
    }

    [Fact]
    public async Task RunAsync_View_Last_Loads_Last_Profile()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var session = new FakeViewSession(23);
        var profiles = new FakeViewProfileStore();
        profiles.Profiles["last"] = new ViewProfile(Device: "last-device", Preset: "safe", AlwaysOnTop: true);
        var app = new App(
            console: console,
            timeProvider: timeProvider,
            deviceHostFactory: new FakeDeviceHostFactory(host),
            viewSessionFactory: new FakeViewSessionFactory(session),
            viewProfileStore: profiles);

        var exitCode = await app.RunAsync(["view", "--last"]);

        Assert.Equal(23, exitCode);
        var options = Assert.Single(session.Options);
        Assert.Equal("last-device", options.DeviceSelector);
        Assert.Equal("safe", options.PresetName);
        Assert.True(options.AlwaysOnTop);
    }

    [Fact]
    public async Task RunAsync_Reconnect_Loads_Last_Profile_By_Default()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var session = new FakeViewSession(23);
        var profiles = new FakeViewProfileStore();
        profiles.Profiles["last"] = new ViewProfile(Device: "last-device", Preset: "safe", AlwaysOnTop: true);
        var app = new App(
            console: console,
            timeProvider: timeProvider,
            deviceHostFactory: new FakeDeviceHostFactory(host),
            viewSessionFactory: new FakeViewSessionFactory(session),
            viewProfileStore: profiles);

        var exitCode = await app.RunAsync(["reconnect"]);

        Assert.Equal(23, exitCode);
        var options = Assert.Single(session.Options);
        Assert.Equal("last-device", options.DeviceSelector);
        Assert.Equal("safe", options.PresetName);
        Assert.True(options.AlwaysOnTop);
    }

    [Fact]
    public async Task RunAsync_ProfileList_Returns_Profile_Names()
    {
        var console = new FakeConsole();
        var profiles = new FakeViewProfileStore();
        profiles.Profiles["desk"] = new ViewProfile(Device: "desk-device");
        profiles.Profiles["safe"] = new ViewProfile(Device: "safe-device");
        var app = new App(console: console, viewProfileStore: profiles);

        var exitCode = await app.RunAsync(["profile-list"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        var names = envelope.RootElement.GetProperty("data").GetProperty("profiles").EnumerateArray().Select(value => value.GetString()!).ToArray();
        Assert.Equal(["desk", "safe"], names);
    }

    [Fact]
    public async Task RunAsync_ProfileDelete_Removes_Profile()
    {
        var console = new FakeConsole();
        var profiles = new FakeViewProfileStore();
        profiles.Profiles["desk"] = new ViewProfile(Device: "desk-device");
        var app = new App(console: console, viewProfileStore: profiles);

        var exitCode = await app.RunAsync(["profile-delete", "--name", "desk"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.True(envelope.RootElement.GetProperty("data").GetProperty("deleted").GetBoolean());
        Assert.False(profiles.Profiles.ContainsKey("desk"));
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
