using Luotsi.Cli.Cli;
using Luotsi.Cli.Hosts.Android.View;
using Luotsi.Cli.Models;
using Luotsi.Cli.View;
using Luotsi.Cli.View.Contracts;
using Luotsi.Cli.View.Diagnostics;
using Luotsi.Cli.View.Recording;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed partial class AppTests
{
    [Fact]
    public async Task RunAsync_ViewDoctor_Uses_Injected_ViewDoctorFactory()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var doctor = new FakeViewDoctor(options => new ViewDoctorResult(
            false,
            options.PresetName,
            options,
            [],
            null,
            [new ViewDoctorCheck("decoder", false, "FFmpeg native decoder is not ready.")]));
        var factory = new FakeViewDoctorFactory(doctor);
        var app = new App(new AppDependencies
        {
            Console = console,
            TimeProvider = timeProvider,
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            ViewDoctorFactory = factory
        });

        var exitCode = await app.RunAsync([
            "view-doctor",
            "--device", "192.168.0.134:5555",
            "--preset", "low-latency"]);

        using var envelope = console.ParseSingleOutputAsJson();
        Assert.Equal(0, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("view-doctor", envelope.RootElement.GetProperty("command").GetString());
        Assert.False(envelope.RootElement.GetProperty("data").GetProperty("ready").GetBoolean());
        Assert.Equal("low-latency", envelope.RootElement.GetProperty("data").GetProperty("preset").GetString());
        Assert.Same(host, factory.LastDeviceHost);
        var options = Assert.Single(doctor.Options);
        Assert.Equal("low-latency", options.PresetName);
        Assert.Equal(1280, options.MaxSize);
        Assert.Equal(250, options.StatsIntervalMs);
    }

    [Fact]
    public async Task RunAsync_ViewSetup_Uses_Injected_ViewSetupFactory()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var setup = new FakeViewSetup();
        var factory = new FakeViewSetupFactory(setup);
        var app = new App(new AppDependencies
        {
            Console = console,
            TimeProvider = timeProvider,
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            ViewSetupFactory = factory
        });

        var exitCode = await app.RunAsync([
            "view-setup",
            "--device", "192.168.0.134:5555",
            "--preset", "safe"]);

        using var envelope = console.ParseSingleOutputAsJson();
        Assert.Equal(0, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("view-setup", envelope.RootElement.GetProperty("command").GetString());
        Assert.True(envelope.RootElement.GetProperty("data").GetProperty("fix").GetBoolean());
        Assert.True(envelope.RootElement.GetProperty("data").GetProperty("ready").GetBoolean());
        Assert.Same(host, factory.LastDeviceHost);
        var call = Assert.Single(setup.Calls);
        Assert.True(call.Fix);
        Assert.Equal("safe", call.Options.PresetName);
    }

    [Fact]
    public async Task RunAsync_ViewSetup_Alias_Writes_ViewSetup_Command_Envelope()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var setup = new FakeViewSetup();
        var fileSystem = new FakeFileSystem();
        var app = new App(new AppDependencies
        {
            Console = console,
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            ViewSetupFactory = new FakeViewSetupFactory(setup)
        });

        var exitCode = await app.RunAsync([
            "view",
            "setup",
            "--device", "192.168.0.134:5555",
            "--artifacts", "/tmp/artifacts"]);

        using var envelope = console.ParseSingleOutputAsJson();
        Assert.Equal(0, exitCode);
        Assert.Equal("view-setup", envelope.RootElement.GetProperty("command").GetString());
        Assert.True(Assert.Single(setup.Calls).Fix);
        Assert.True(fileSystem.DirectoryExists("/tmp/artifacts/20260515-120000-view-setup"));
    }

    [Fact]
    public async Task RunAsync_ViewDoctor_Fix_Runs_Setup()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var setup = new FakeViewSetup();
        var factory = new FakeViewSetupFactory(setup);
        var app = new App(new AppDependencies
        {
            Console = console,
            TimeProvider = timeProvider,
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            ViewSetupFactory = factory
        });

        var exitCode = await app.RunAsync([
            "view-doctor",
            "--device", "192.168.0.134:5555",
            "--fix"]);

        using var envelope = console.ParseSingleOutputAsJson();
        Assert.Equal(0, exitCode);
        Assert.Equal("view-doctor", envelope.RootElement.GetProperty("command").GetString());
        Assert.True(envelope.RootElement.GetProperty("data").GetProperty("fix").GetBoolean());
        Assert.True(Assert.Single(setup.Calls).Fix);
    }


    [Fact]
    public async Task ViewDoctor_DiagnoseAsync_Reports_Ready_For_Healthy_Ffmpeg_View_Setup()
    {
        var fileSystem = new FakeFileSystem();
        var helperPath = "/tmp/luotsi-view-helper.apk";
        fileSystem.AddFile(helperPath, "apk");
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>
        {
            ["LUOTSI_VIEW_HELPER_APK"] = helperPath
        });
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        host.ConnectedDevices.Add(new DeviceInfo("192.168.0.134:5555", "device", "Pixel 9"));
        var binder = new FakeLibavNativeLibraryBinder();
        binder.SucceedFor(null);
        var doctor = new ViewDoctor(
            host,
            new AndroidViewHelperPackageLocator(environment, fileSystem),
            new DefaultViewRecorderFactory(fileSystem, new FakeProcessRunner(), environment),
            environment,
            binder);

        var result = await doctor.DiagnoseAsync(new ViewOptions(
            "192.168.0.134:5555",
            "adb",
            "h264",
            "ffmpeg",
            true,
            null,
            1280,
            30,
            "4M",
            false,
            false,
            1000,
            250,
            "safe"));

        Assert.True(result.Ready);
        Assert.Equal("safe", result.Preset);
        Assert.Equal(9, result.Checks.Count);
        Assert.All(result.Checks, static check => Assert.True(check.Ok, check.Summary));
        Assert.Equal("Pixel 9", Assert.Single(result.ConnectedDevices).Details);
        var preflight = result.Preflight;
        Assert.NotNull(preflight);
        Assert.Equal("Model", preflight.Model);
        Assert.Equal([null], host.ReadOnlyPreflightRequests);
        Assert.Empty(host.CommandPreflightRequests);
    }

    [Fact]
    public async Task ViewDoctor_DiagnoseAsync_Flags_Unauthorized_Device_With_Recommendation()
    {
        var fileSystem = new FakeFileSystem();
        var helperPath = "/tmp/luotsi-view-helper.apk";
        fileSystem.AddFile(helperPath, "apk");
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>
        {
            ["LUOTSI_VIEW_HELPER_APK"] = helperPath
        });
        var host = new FakeDeviceHost(CreateScreenState(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind), "Sign in"));
        host.ConnectedDevices.Add(new DeviceInfo("usb-device", "unauthorized", "Pixel 9"));
        var binder = new FakeLibavNativeLibraryBinder();
        binder.SucceedFor(null);
        var doctor = new ViewDoctor(
            host,
            new AndroidViewHelperPackageLocator(environment, fileSystem),
            new DefaultViewRecorderFactory(fileSystem, new FakeProcessRunner(), environment),
            environment,
            binder);

        var result = await doctor.DiagnoseAsync(new ViewOptions("usb-device", "adb", "h264", "ffmpeg", true, null, 1280, 30, "4M", false, false));

        Assert.False(result.Ready);
        var deviceCheck = Assert.Single(result.Checks, check => check.Name == "device_visibility");
        Assert.False(deviceCheck.Ok);
        Assert.Contains("unauthorized", deviceCheck.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("USB debugging", deviceCheck.Recommendation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ViewDoctor_DiagnoseAsync_Flags_Explicit_MediaProjection_Consent_As_Interactive()
    {
        var fileSystem = new FakeFileSystem();
        var helperPath = "/tmp/luotsi-view-helper.apk";
        fileSystem.AddFile(helperPath, "apk");
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>
        {
            ["LUOTSI_VIEW_HELPER_APK"] = helperPath
        });
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        host.ConnectedDevices.Add(new DeviceInfo("192.168.0.134:5555", "device", "Pixel 9"));
        var binder = new FakeLibavNativeLibraryBinder();
        binder.SucceedFor(null);
        var doctor = new ViewDoctor(
            host,
            new AndroidViewHelperPackageLocator(environment, fileSystem),
            new DefaultViewRecorderFactory(fileSystem, new FakeProcessRunner(), environment),
            environment,
            binder);

        var result = await doctor.DiagnoseAsync(new ViewOptions(
            "192.168.0.134:5555",
            "adb",
            "h264",
            "ffmpeg",
            true,
            null,
            1280,
            30,
            "4M",
            false,
            false,
            CaptureBackend: ViewCaptureBackends.MediaProjection));

        Assert.False(result.Ready);
        var consentCheck = Assert.Single(result.Checks, check => check.Name == "mediaprojection_consent");
        Assert.False(consentCheck.Ok);
        Assert.Contains("cannot be preflighted", consentCheck.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fallback=none", consentCheck.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AndroidViewHelperSetupProvisioner_ResolveOrBuildAsync_Skips_Build_When_Fix_Is_Disabled()
    {
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>());
        var processRunner = new FakeProcessRunner();
        var provisioner = new AndroidViewHelperSetupProvisioner(
            new SequencedAndroidViewHelperPackageLocator(new InvalidOperationException("missing helper")),
            new ViewHostPathResolver(environment),
            new FakeFileSystem(),
            processRunner);
        var steps = new List<ViewSetupStep>();

        var package = await provisioner.ResolveOrBuildAsync(fix: false, steps.Add);

        Assert.Null(package);
        Assert.Empty(processRunner.Calls);
        Assert.Collection(
            steps,
            step =>
            {
                Assert.Equal("helper_resolve", step.Name);
                Assert.Equal(ViewStartupPhaseStatus.Failed, step.Status);
            },
            step =>
            {
                Assert.Equal("helper_build", step.Name);
                Assert.Equal(ViewStartupPhaseStatus.Skipped, step.Status);
            });
    }

    [Fact]
    public async Task AndroidViewHelperSetupProvisioner_ResolveOrBuildAsync_Builds_And_Reresolves_Helper()
    {
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>());
        var fileSystem = new FakeFileSystem();
        var pathResolver = new ViewHostPathResolver(environment);
        var projectDirectory = pathResolver.GetRepositoryRelativeDirectoryCandidates("Luotsi.ViewServer.Android").First();
        var wrapperPath = OperatingSystem.IsWindows()
            ? Path.Join(projectDirectory, "gradlew.bat")
            : Path.Join(projectDirectory, "gradlew");
        fileSystem.CreateDirectory(projectDirectory);
        fileSystem.AddFile(wrapperPath, string.Empty);

        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(new ProcessResult(0, "BUILD SUCCESSFUL", string.Empty));

        var package = new AndroidViewHelperPackage("C:/tmp/helper.apk", "/data/local/tmp/luotsi-view-server.apk", "dev.luotsi.view.Main", "test-helper");
        var provisioner = new AndroidViewHelperSetupProvisioner(
            new SequencedAndroidViewHelperPackageLocator(new InvalidOperationException("missing helper"), package),
            pathResolver,
            fileSystem,
            processRunner);
        var steps = new List<ViewSetupStep>();

        var resolved = await provisioner.ResolveOrBuildAsync(fix: true, steps.Add);

        Assert.Same(package, resolved);
        var call = Assert.Single(processRunner.Calls);
        Assert.Equal(wrapperPath, call.FileName);
        Assert.Equal(["-p", projectDirectory, ":app:assembleDebug"], call.Args);
        Assert.Collection(
            steps,
            step =>
            {
                Assert.Equal("helper_resolve", step.Name);
                Assert.Equal(ViewStartupPhaseStatus.Failed, step.Status);
            },
            step =>
            {
                Assert.Equal("helper_build", step.Name);
                Assert.Equal(ViewStartupPhaseStatus.Started, step.Status);
                Assert.Equal(projectDirectory, step.Detail);
            },
            step =>
            {
                Assert.Equal("helper_build", step.Name);
                Assert.Equal(ViewStartupPhaseStatus.Succeeded, step.Status);
                Assert.Equal("BUILD SUCCESSFUL", step.Detail);
            },
            step =>
            {
                Assert.Equal("helper_resolve", step.Name);
                Assert.Equal(ViewStartupPhaseStatus.Succeeded, step.Status);
            });
    }

    private sealed class SequencedAndroidViewHelperPackageLocator(params object[] outcomes) : IAndroidViewHelperPackageLocator
    {
        private readonly Queue<object> _outcomes = new(outcomes);

        public AndroidViewHelperPackage Resolve()
        {
            if (_outcomes.Count == 0)
            {
                throw new InvalidOperationException("No fake helper locator outcomes remain.");
            }

            var outcome = _outcomes.Count > 1 ? _outcomes.Dequeue() : _outcomes.Peek();
            return outcome switch
            {
                AndroidViewHelperPackage package => package,
                Exception exception => throw exception,
                _ => throw new InvalidOperationException($"Unsupported fake helper locator outcome '{outcome.GetType().Name}'.")
            };
        }
    }


}
