using Luotsi.Cli.Cli;
using Luotsi.Cli.Cli.View;
using Luotsi.Cli.Errors;
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
    public async Task RunAsync_ViewSetup_Without_Device_Returns_Actionable_Usage_Error()
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var exitCode = await app.RunAsync(["view", "setup", "--fix"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("view-setup", envelope.RootElement.GetProperty("command").GetString());
        Assert.Contains("view-setup requires --device <adb serial>.", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ViewSetup_Fix_Stages_Ffmpeg_When_Decoder_Is_Not_Ready()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var fileSystem = new FakeFileSystem();
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>());
        var pathResolver = new ViewHostPathResolver(environment);
        fileSystem.AddFile(ViewHostPathResolver.GetRepositoryRelativeFileCandidates("ffmpeg/download-ffmpeg.ps1").First(), "Write-Host 'ok'");

        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(new ProcessResult(0, "Done. Staged native libraries.", string.Empty));
        var doctor = new FakeViewDoctor(options => new ViewDoctorResult(
            false,
            options.PresetName,
            options,
            [],
            null,
            [new ViewDoctorCheck("decoder", false, "FFmpeg native decoder is not ready.")]));
        var setup = new FakeViewSetup((options, fix) => new ViewSetupResult(
            true,
            fix,
            options.PresetName,
            options,
            [new ViewSetupStep("helper_install", ViewStartupPhaseStatus.Succeeded, "Installed.")],
            new ViewDoctorResult(true, options.PresetName, options, [], null, [new ViewDoctorCheck("decoder", true, "FFmpeg native decoder is ready.")])));
        var app = new App(new AppDependencies
        {
            Console = console,
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            Environment = environment,
            ProcessRunner = processRunner,
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            ViewDoctorFactory = new FakeViewDoctorFactory(doctor),
            ViewSetupFactory = new FakeViewSetupFactory(setup)
        });

        var exitCode = await app.RunAsync([
            "view",
            "setup",
            "--device", "192.168.0.134:5555",
            "--fix"]);

        using var envelope = console.ParseSingleOutputAsJson();
        Assert.Equal(0, exitCode);
        Assert.Equal("view-setup", envelope.RootElement.GetProperty("command").GetString());
        var stepNames = envelope.RootElement.GetProperty("data").GetProperty("steps").EnumerateArray().Select(static item => item.GetProperty("name").GetString()).ToArray();
        Assert.Collection(
            stepNames,
            name => Assert.Equal("ffmpeg_stage", name),
            name => Assert.Equal("ffmpeg_stage", name),
            name => Assert.Equal("helper_install", name));
        Assert.Single(processRunner.Calls);
        Assert.True(Assert.Single(setup.Calls).Fix);
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
    public async Task RunAsync_Doctor_Reuses_ViewDoctor_And_PackagePreflight()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var doctor = new FakeViewDoctor(options => new ViewDoctorResult(
            true,
            options.PresetName,
            options,
            [],
            null,
            [new ViewDoctorCheck("decoder", true, "FFmpeg native decoder is ready.")]));
        var factory = new FakeViewDoctorFactory(doctor);
        var app = new App(new AppDependencies
        {
            Console = console,
            TimeProvider = timeProvider,
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            ViewDoctorFactory = factory
        });

        var exitCode = await app.RunAsync([
            "doctor",
            "--device", "192.168.0.134:5555",
            "--package", "dev.luotsi.app",
            "--preset", "safe"]);

        using var envelope = console.ParseSingleOutputAsJson();
        Assert.Equal(0, exitCode);
        Assert.Equal("doctor", envelope.RootElement.GetProperty("command").GetString());
        Assert.True(envelope.RootElement.GetProperty("data").GetProperty("ready").GetBoolean());
        Assert.Equal("dev.luotsi.app", envelope.RootElement.GetProperty("data").GetProperty("package").GetString());
        Assert.Equal("safe", envelope.RootElement.GetProperty("data").GetProperty("view").GetProperty("preset").GetString());
        Assert.Contains("adb_server_status", envelope.RootElement.GetProperty("data").GetProperty("checks").EnumerateArray().Select(static item => item.GetProperty("name").GetString()));
        var readinessPlan = envelope.RootElement.GetProperty("data").GetProperty("readiness_plan");
        Assert.Equal("ready", readinessPlan.GetProperty("status").GetString());
        Assert.Equal("luotsi run --path <scenarios> --device 192.168.0.134:5555 --package dev.luotsi.app", readinessPlan.GetProperty("next_command").GetString());
        Assert.Empty(readinessPlan.GetProperty("blockers").EnumerateArray());
        Assert.Contains(readinessPlan.GetProperty("recommended_commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "view_device" &&
            command.GetProperty("command").GetString() == "luotsi view --device 192.168.0.134:5555 --preset safe");
        Assert.Equal(
            readinessPlan.GetProperty("recommended_commands").GetArrayLength(),
            envelope.RootElement.GetProperty("data").GetProperty("recommended_commands").GetArrayLength());
        Assert.Equal(["server-status", "version"], host.AdbDiagnostics);
        Assert.Equal(["dev.luotsi.app"], host.ReadOnlyPreflightRequests);
        Assert.Same(host, factory.LastDeviceHost);
    }

    [Fact]
    public async Task RunAsync_Doctor_Blocked_ReadinessPlan_Returns_Blockers_And_Commands()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"))
        {
            PreflightException = new InvalidOperationException("target package is not foreground")
        };
        var doctor = new FakeViewDoctor(options => new ViewDoctorResult(
            false,
            options.PresetName,
            options,
            [],
            null,
            [new ViewDoctorCheck("helper_package", false, "Android view helper package is not ready.", "missing helper", "Run view setup --fix.")]));
        var app = new App(new AppDependencies
        {
            Console = console,
            TimeProvider = timeProvider,
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            ViewDoctorFactory = new FakeViewDoctorFactory(doctor)
        });

        var exitCode = await app.RunAsync([
            "doctor",
            "--device", "192.168.0.134:5555",
            "--package", "dev.luotsi.app"]);

        using var envelope = console.ParseSingleOutputAsJson();
        Assert.Equal(1, exitCode);
        var data = envelope.RootElement.GetProperty("data");
        Assert.False(data.GetProperty("ready").GetBoolean());
        var readinessPlan = data.GetProperty("readiness_plan");
        Assert.Equal("blocked", readinessPlan.GetProperty("status").GetString());
        Assert.Equal("luotsi doctor --device 192.168.0.134:5555 --package dev.luotsi.app --fix", readinessPlan.GetProperty("next_command").GetString());
        var blockers = readinessPlan.GetProperty("blockers").EnumerateArray().ToArray();
        Assert.Contains(blockers, blocker =>
            blocker.GetProperty("source").GetString() == "doctor" &&
            blocker.GetProperty("name").GetString() == "package_preflight" &&
            blocker.GetProperty("command").GetString() == "luotsi preflight --device 192.168.0.134:5555 --package dev.luotsi.app");
        Assert.Contains(blockers, blocker =>
            blocker.GetProperty("source").GetString() == "view" &&
            blocker.GetProperty("name").GetString() == "helper_package" &&
            blocker.GetProperty("command").GetString() == "luotsi view setup --device 192.168.0.134:5555");
        Assert.Contains(data.GetProperty("recommended_commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "doctor_fix" &&
            command.GetProperty("command").GetString() == "luotsi doctor --device 192.168.0.134:5555 --package dev.luotsi.app --fix");
    }

    [Fact]
    public async Task RunAsync_Doctor_ReadinessPlan_Uses_Resolved_Adb_For_Server_Status_Command()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        host.AdbServerStatusResults.Enqueue(CreateAdbDiagnostic("server-status", ["server-status"], exitCode: 1, stderr: "adb server did not ACK"));
        var doctor = new FakeViewDoctor(options => new ViewDoctorResult(
            true,
            options.PresetName,
            options,
            [],
            null,
            [new ViewDoctorCheck("decoder", true, "FFmpeg native decoder is ready.")]));
        var app = new App(new AppDependencies
        {
            Console = console,
            TimeProvider = timeProvider,
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            ViewDoctorFactory = new FakeViewDoctorFactory(doctor)
        });

        var exitCode = await app.RunAsync([
            "doctor",
            "--device", "192.168.0.134:5555",
            "--adb", "/opt/android platform-tools/adb"]);

        using var envelope = console.ParseSingleOutputAsJson();
        Assert.Equal(1, exitCode);
        const string expectedCommand = "\"/opt/android platform-tools/adb\" start-server";
        var data = envelope.RootElement.GetProperty("data");
        var readinessPlan = data.GetProperty("readiness_plan");
        var adbBlocker = Assert.Single(
            readinessPlan.GetProperty("blockers").EnumerateArray(),
            blocker => blocker.GetProperty("name").GetString() == "adb_server_status");
        Assert.Equal(expectedCommand, adbBlocker.GetProperty("command").GetString());
        Assert.Contains(expectedCommand, adbBlocker.GetProperty("recommendation").GetString(), StringComparison.Ordinal);
        Assert.Contains(data.GetProperty("recommended_commands").EnumerateArray(), command =>
            command.GetProperty("kind").GetString() == "resolve_adb_server_status" &&
            command.GetProperty("command").GetString() == expectedCommand);
    }

    [Fact]
    public async Task RunAsync_Doctor_ReadinessPlan_Preserves_View_Options_For_Blocker_Commands()
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
            [new ViewDoctorCheck(
                "mediaprojection_consent",
                false,
                "MediaProjection consent cannot be preflighted before the Android consent activity runs.",
                "consent_state=interactive; fallback=none",
                "Start a MediaProjection view session on the physical device and approve the Android screen-capture prompt.")]));
        var app = new App(new AppDependencies
        {
            Console = console,
            TimeProvider = timeProvider,
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            ViewDoctorFactory = new FakeViewDoctorFactory(doctor)
        });

        var exitCode = await app.RunAsync([
            "doctor",
            "--device", "192.168.0.134:5555",
            "--capture-backend", "mediaprojection",
            "--preset", "low-latency"]);

        using var envelope = console.ParseSingleOutputAsJson();
        Assert.Equal(1, exitCode);
        var blocker = Assert.Single(envelope.RootElement.GetProperty("data").GetProperty("readiness_plan").GetProperty("blockers").EnumerateArray());
        Assert.Equal("mediaprojection_consent", blocker.GetProperty("name").GetString());
        Assert.Equal("luotsi view --device 192.168.0.134:5555 --preset low-latency --capture-backend mediaprojection", blocker.GetProperty("command").GetString());
        Assert.Contains(envelope.RootElement.GetProperty("data").GetProperty("recommended_commands").EnumerateArray(), command =>
            command.GetProperty("command").GetString() == "luotsi doctor --device 192.168.0.134:5555 --preset low-latency --capture-backend mediaprojection --fix");
    }

    [Fact]
    public async Task RunAsync_Doctor_Fix_Stages_Ffmpeg_And_Runs_Setup()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var console = new FakeConsole();
        var host = new FakeDeviceHost(CreateScreenState(timeProvider.GetUtcNow(), "Sign in"));
        var fileSystem = new FakeFileSystem();
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>());
        var pathResolver = new ViewHostPathResolver(environment);
        fileSystem.AddFile(ViewHostPathResolver.GetRepositoryRelativeFileCandidates("ffmpeg/download-ffmpeg.ps1").First(), "Write-Host 'ok'");

        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(new ProcessResult(0, "Done. Staged native libraries.", string.Empty));

        var doctor = new FakeViewDoctor(options => new ViewDoctorResult(
            false,
            options.PresetName,
            options,
            [],
            null,
            [new ViewDoctorCheck("decoder", false, "FFmpeg native decoder is not ready.")]));
        var setup = new FakeViewSetup((options, fix) => new ViewSetupResult(
            true,
            fix,
            options.PresetName,
            options,
            [new ViewSetupStep("helper_install", ViewStartupPhaseStatus.Succeeded, "Installed.")],
            new ViewDoctorResult(true, options.PresetName, options, [], null, [new ViewDoctorCheck("decoder", true, "FFmpeg native decoder is ready.")])));
        var app = new App(new AppDependencies
        {
            Console = console,
            TimeProvider = timeProvider,
            FileSystem = fileSystem,
            Environment = environment,
            ProcessRunner = processRunner,
            DeviceHostFactory = new FakeDeviceHostFactory(host),
            ViewDoctorFactory = new FakeViewDoctorFactory(doctor),
            ViewSetupFactory = new FakeViewSetupFactory(setup)
        });

        var exitCode = await app.RunAsync([
            "doctor",
            "--device", "192.168.0.134:5555",
            "--fix"]);

        using var envelope = console.ParseSingleOutputAsJson();
        Assert.Equal(0, exitCode);
        Assert.Equal("doctor", envelope.RootElement.GetProperty("command").GetString());
        Assert.True(envelope.RootElement.GetProperty("data").GetProperty("fix").GetBoolean());
        Assert.True(envelope.RootElement.GetProperty("data").GetProperty("ready").GetBoolean());
        var readinessPlan = envelope.RootElement.GetProperty("data").GetProperty("readiness_plan");
        Assert.Equal("ready", readinessPlan.GetProperty("status").GetString());
        Assert.Contains("repairs completed", readinessPlan.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Equal("luotsi run --path <scenarios> --device 192.168.0.134:5555", readinessPlan.GetProperty("next_command").GetString());
        var repairNames = envelope.RootElement.GetProperty("data").GetProperty("repairs").EnumerateArray().Select(static item => item.GetProperty("name").GetString()).ToArray();
        Assert.Collection(
            repairNames,
            name => Assert.Equal("ffmpeg_stage", name),
            name => Assert.Equal("ffmpeg_stage", name),
            name => Assert.Equal("helper_install", name));
        var call = Assert.Single(processRunner.Calls);
        Assert.Equal(OperatingSystem.IsWindows() ? "pwsh" : "pwsh", call.FileName);
        Assert.Contains("-File", call.Args);
        Assert.True(Assert.Single(setup.Calls).Fix);
    }

    [Fact]
    public async Task ViewSetup_Fix_Uninstalls_And_Reinstalls_Incompatible_Helper()
    {
        var fileSystem = new FakeFileSystem();
        var helperPath = "/tmp/luotsi-view-helper.apk";
        fileSystem.AddFile(helperPath, "apk");
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>
        {
            ["LUOTSI_VIEW_HELPER_APK"] = helperPath
        });
        var adb = new FakeAdbClient("192.168.0.134:5555");
        adb.EnqueueRunResult(new ProcessResult(1, string.Empty, "Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE: Package dev.luotsi.view signatures do not match previously installed version; ignoring!]"));
        adb.EnqueueRunResult(new ProcessResult(0, "Success", string.Empty));
        adb.EnqueueRunResult(new ProcessResult(0, "Success", string.Empty));
        adb.EnqueueRunResult(new ProcessResult(0, "dev.luotsi.view/.ConsentActivity", string.Empty));
        adb.EnqueueRunResult(new ProcessResult(0, "dev.luotsi.view.CaptureService", string.Empty));

        var setup = new ViewSetup(
            new FakeDeviceHost(),
            new AndroidViewHelperPackageLocator(environment, fileSystem),
            new ViewHostPathResolver(environment),
            new FakeViewDoctorFactory(new FakeViewDoctor()),
            fileSystem,
            new FakeProcessRunner(),
            new FakeAdbClientFactory(adb));

        var result = await setup.SetupAsync(new ViewOptions("192.168.0.134:5555", "adb", "h264", "ffmpeg", false, null, 1600, 60, "8M", false, false), fix: true);

        Assert.True(result.Ready);
        Assert.Contains(result.Steps, step => step is {Name: "helper_uninstall", Status: ViewStartupPhaseStatus.Succeeded});
        Assert.Equal(
            [
                ["install", "-r", helperPath],
                ["uninstall", "dev.luotsi.view"],
                ["install", "-r", helperPath],
                ["shell", "cmd", "package", "resolve-activity", "--brief", "dev.luotsi.view/.ConsentActivity"],
                ["shell", "pm", "dump", "dev.luotsi.view"]
            ],
            adb.RunCommands);
    }

    [Fact]
    public void ViewCommandOptionsFactory_Build_With_Negative_Stats_Interval_Uses_Doctor_Command_Name()
    {
        var constructor = typeof(CliOptions).GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            [typeof(string)],
            modifiers: null);
        Assert.NotNull(constructor);

        var options = (CliOptions)constructor.Invoke(["doctor"]);
        var valuesField = typeof(CliOptions).GetField("_values", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(valuesField);
        var values = (Dictionary<string, string?>)valuesField.GetValue(options)!;
        values["device"] = "192.168.0.134:5555";
        values["stats-interval-ms"] = "-1";

        var error = Assert.Throws<UsageException>(() => ViewCommandOptionsFactory.Build(options, "adb", allowJoinShare: false, commandTimeout: TimeSpan.FromSeconds(5), commandName: "doctor"));

        Assert.Equal("doctor requires --stats-interval-ms zero or greater.", error.Message);
    }

    [Fact]
    public async Task RunAsync_Doctor_With_Invalid_Capture_Backend_Returns_Doctor_Usage_Error_Envelope()
    {
        var console = new FakeConsole();
        var app = new App(new AppDependencies { Console = console });

        var exitCode = await app.RunAsync(["doctor", "--device", "192.168.0.134:5555", "--capture-backend", "invalid"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(2, exitCode);
        Assert.False(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("doctor", envelope.RootElement.GetProperty("command").GetString());
        Assert.Contains("doctor requires --capture-backend to be auto, screenrecord, or mediaprojection.", envelope.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
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
        var projectDirectory = ViewHostPathResolver.GetRepositoryRelativeDirectoryCandidates("Luotsi.ViewServer.Android").First();
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
        Assert.Equal(["-p", projectDirectory, ":app:assembleRelease"], call.Args);
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

    [Fact]
    public async Task FfmpegSetupProvisioner_StageAsync_Uses_Published_App_Script_When_Source_Checkout_Is_Missing()
    {
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>());
        var fileSystem = new FakeFileSystem();
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(new ProcessResult(0, "Done. Staged native libraries.", string.Empty));
        var scriptPath = Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "ffmpeg", "download-ffmpeg.ps1"));
        fileSystem.AddFile(scriptPath, "Write-Host 'ok'");
        var provisioner = new FfmpegSetupProvisioner(environment, fileSystem, processRunner);
        var steps = new List<ViewSetupStep>();

        var staged = await provisioner.StageAsync(steps.Add);

        Assert.True(staged);
        var call = Assert.Single(processRunner.Calls);
        Assert.Equal("pwsh", call.FileName);
        Assert.Equal(["-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath], call.Args);
        Assert.Contains(steps, step => step is {Name: "ffmpeg_stage", Status: ViewStartupPhaseStatus.Started} && step.Detail == scriptPath);
    }

    [Fact]
    public async Task FfmpegSetupProvisioner_StageAsync_Retries_Transient_Download_Failure()
    {
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>());
        var fileSystem = new FakeFileSystem();
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(new ProcessResult(1, string.Empty, "download timed out"));
        processRunner.EnqueueResult(new ProcessResult(0, "Done. Staged native libraries.", string.Empty));
        var scriptPath = Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "ffmpeg", "download-ffmpeg.ps1"));
        fileSystem.AddFile(scriptPath, "Write-Host 'ok'");
        var provisioner = new FfmpegSetupProvisioner(environment, fileSystem, processRunner);
        var steps = new List<ViewSetupStep>();

        var staged = await provisioner.StageAsync(steps.Add);

        Assert.True(staged);
        Assert.Equal(2, processRunner.Calls.Count);
        Assert.Contains(steps, step =>
            step is {Name: "ffmpeg_stage", Status: ViewStartupPhaseStatus.Succeeded} &&
            step.Summary.Contains("after 2 attempts", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FfmpegSetupProvisioner_StageAsync_Does_Not_Retry_ScriptPath_Download_Text()
    {
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>());
        var fileSystem = new FakeFileSystem();
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(new ProcessResult(1, string.Empty, "C:/repo/ffmpeg/download-ffmpeg.ps1: access denied"));
        processRunner.EnqueueResult(new ProcessResult(0, "Done. Staged native libraries.", string.Empty));
        var scriptPath = Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "ffmpeg", "download-ffmpeg.ps1"));
        fileSystem.AddFile(scriptPath, "Write-Host 'ok'");
        var provisioner = new FfmpegSetupProvisioner(environment, fileSystem, processRunner);
        var steps = new List<ViewSetupStep>();

        var staged = await provisioner.StageAsync(steps.Add);

        Assert.False(staged);
        Assert.Single(processRunner.Calls);
        Assert.Contains(steps, step =>
            step is {Name: "ffmpeg_stage", Status: ViewStartupPhaseStatus.Failed} &&
            step.Detail is not null &&
            step.Detail.Contains("download-ffmpeg.ps1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AndroidViewHelperSetupProvisioner_ResolveOrBuildAsync_Builds_From_Published_App_Project_When_Source_Checkout_Is_Missing()
    {
        var environment = new FakeEnvironmentVariables(new Dictionary<string, string>());
        var fileSystem = new FakeFileSystem();
        var pathResolver = new ViewHostPathResolver(environment);
        var projectDirectory = Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "Luotsi.ViewServer.Android"));
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
        Assert.Equal(["-p", projectDirectory, ":app:assembleRelease"], call.Args);
        Assert.Contains(steps, step => step is {Name: "helper_build", Status: ViewStartupPhaseStatus.Started} && step.Detail == projectDirectory);
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
