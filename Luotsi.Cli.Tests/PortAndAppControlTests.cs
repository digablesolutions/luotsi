using System.Text.Json;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Hosts.Android;
using Luotsi.Cli.Infrastructure.Serialization;
using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed partial class AppTests
{
    [Fact]
    public async Task DeviceRunner_PortCommands_Invoke_Adb_And_Parse_Lists()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["forward-list"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);

        adb.EnqueueRunResult(new ProcessResult(0, "emulator-5554 tcp:8080 localabstract:mock-api\n", string.Empty));
        var forwards = await runner.ListForwardsAsync();
        await runner.ForwardAsync("tcp:8081", "localabstract:dev-api", noRebind: true);
        await runner.RemoveForwardAsync("tcp:8081");

        adb.EnqueueRunResult(new ProcessResult(0, "emulator-5554 tcp:8080 tcp:3000\n", string.Empty));
        var reverses = await runner.ListReversesAsync();
        await runner.ReverseAsync("tcp:8080", "tcp:3000", noRebind: false);
        await runner.RemoveReverseAsync("tcp:8080");

        Assert.Equal("tcp:8080", forwards.Entries[0].Local);
        Assert.Equal("localabstract:mock-api", forwards.Entries[0].Remote);
        Assert.Equal("tcp:8080", reverses.Entries[0].Remote);
        Assert.Equal("tcp:3000", reverses.Entries[0].Local);
        Assert.Equal(["forward", "--list"], adb.RunCommands[0]);
        Assert.Equal(["forward", "--no-rebind", "tcp:8081", "localabstract:dev-api"], adb.RunCommands[1]);
        Assert.Equal(["forward", "--remove", "tcp:8081"], adb.RunCommands[2]);
        Assert.Equal(["reverse", "--list"], adb.RunCommands[3]);
        Assert.Equal(["reverse", "tcp:8080", "tcp:3000"], adb.RunCommands[4]);
        Assert.Equal(["reverse", "--remove", "tcp:8080"], adb.RunCommands[5]);
    }

    [Fact]
    public async Task DeviceRunner_AppLifecycleCommands_Invoke_Android_Shell_APIs()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["start-app"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);

        adb.EnqueueShellResult(new ProcessResult(0, "Starting: Intent\n", string.Empty));
        var startApp = await runner.StartAppAsync("dev.luotsi.app", "MainActivity", wait: true);
        adb.EnqueueShellResult(new ProcessResult(0, "Starting: Intent\n", string.Empty));
        var startUri = await runner.StartUriAsync("luotsi://item/42", "dev.luotsi.app", ".DeepLinkActivity", null, wait: true);
        await runner.ForceStopAsync("dev.luotsi.app");
        await runner.ClearAppAsync("dev.luotsi.app");
        adb.EnqueueShellResult(new ProcessResult(0, "package:/data/app/dev.luotsi.app/base.apk\n", string.Empty));
        var installed = await runner.IsAppInstalledAsync("dev.luotsi.app");
        adb.EnqueueShellResult(new ProcessResult(0, "package:com.android.settings\npackage:dev.luotsi.app\n", string.Empty));
        var packages = await runner.ListInstalledPackagesAsync(thirdPartyOnly: true);
        await runner.GrantPermissionAsync("dev.luotsi.app", "android.permission.CAMERA");
        await runner.RevokePermissionAsync("dev.luotsi.app", "android.permission.CAMERA");

        Assert.Equal("dev.luotsi.app/.MainActivity", startApp.Component);
        Assert.Equal("dev.luotsi.app/.DeepLinkActivity", startUri.Component);
        Assert.True(installed.Installed);
        Assert.Equal(["com.android.settings", "dev.luotsi.app"], packages.Packages);
        Assert.Equal("am start -W -n 'dev.luotsi.app/.MainActivity'", adb.ShellCommands[0]);
        Assert.Equal("am start -W -a 'android.intent.action.VIEW' -d 'luotsi://item/42' -n 'dev.luotsi.app/.DeepLinkActivity'", adb.ShellCommands[1]);
        Assert.Equal("am force-stop 'dev.luotsi.app'", adb.ShellCommands[2]);
        Assert.Equal("pm clear 'dev.luotsi.app'", adb.ShellCommands[3]);
        Assert.Equal("pm path 'dev.luotsi.app'", adb.ShellCommands[4]);
        Assert.Equal("pm list packages -3", adb.ShellCommands[5]);
        Assert.Equal("pm grant 'dev.luotsi.app' 'android.permission.CAMERA'", adb.ShellCommands[6]);
        Assert.Equal("pm revoke 'dev.luotsi.app' 'android.permission.CAMERA'", adb.ShellCommands[7]);
    }

    [Fact]
    public async Task DeviceRunner_IsAppInstalled_Returns_False_When_Package_Is_Missing()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["is-app-installed"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);

        adb.EnqueueShellResult(new ProcessResult(1, "Error: package dev.luotsi.app was not found\n", string.Empty));

        var result = await runner.IsAppInstalledAsync("dev.luotsi.app");

        Assert.Equal("dev.luotsi.app", result.Package);
        Assert.False(result.Installed);
    }

    [Fact]
    public async Task DeviceRunner_IsAppInstalled_Throws_When_Adb_Shell_Fails()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var adb = new FakeAdbClient();
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["is-app-installed"]), fileSystem, timeProvider), timeProvider, new FakeDelay(timeProvider), fileSystem);

        adb.EnqueueShellResult(new ProcessResult(1, string.Empty, "error: device offline"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.IsAppInstalledAsync("dev.luotsi.app"));

        Assert.Contains("device offline", exception.Message);
    }

    [Fact]
    public async Task DeviceRunner_WaitForActivity_Polls_Focused_Window()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        var delay = new FakeDelay(timeProvider);
        var adb = new FakeAdbClient();
        adb.EnqueueShellResult(new ProcessResult(0, "mCurrentFocus=Window{u0 dev.luotsi.app/.SplashActivity}\n", string.Empty));
        adb.EnqueueShellResult(new ProcessResult(0, "mCurrentFocus=Window{u0 dev.luotsi.app/.MainActivity}\n", string.Empty));
        var runner = new DeviceRunner(adb, ArtifactSession.Create(CliOptions.Parse(["wait-for-activity"]), fileSystem, timeProvider), timeProvider, delay, fileSystem);

        var result = await runner.WaitForActivityAsync("*.MainActivity", 2);

        Assert.Equal("*.MainActivity", result.Activity);
        Assert.Equal(2, result.AttemptCount);
        Assert.Contains(".MainActivity", result.CurrentActivity, StringComparison.Ordinal);
        Assert.Single(delay.Calls);
    }

    [Fact]
    public async Task RunAsync_Forward_Command_Routes_To_DeviceHost()
    {
        var console = new FakeConsole();
        var host = new FakeDeviceHost();
        var app = new App(new AppDependencies
        {
            Console = console,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["forward", "--local", "tcp:8080", "--remote", "tcp:3000"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal([("tcp:8080", "tcp:3000", false)], host.ForwardRequests);
    }

    [Fact]
    public async Task RunAsync_StartUri_Command_Passes_Action_To_DeviceHost()
    {
        var console = new FakeConsole();
        var host = new FakeDeviceHost();
        var app = new App(new AppDependencies
        {
            Console = console,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync([
            "start-uri",
            "--uri", "luotsi://item/42",
            "--package", "dev.luotsi.app",
            "--activity", ".DeepLinkActivity",
            "--action", "android.intent.action.SEND"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal([("luotsi://item/42", "dev.luotsi.app", ".DeepLinkActivity", "android.intent.action.SEND", false)], host.StartUriRequests);
    }

    [Fact]
    public async Task RunAsync_ClearApp_Alias_Routes_To_DeviceHost()
    {
        var console = new FakeConsole();
        var host = new FakeDeviceHost();
        var app = new App(new AppDependencies
        {
            Console = console,
            DeviceHostFactory = new FakeDeviceHostFactory(host)
        });

        var exitCode = await app.RunAsync(["clear-app", "--package", "dev.luotsi.app"]);
        using var envelope = console.ParseSingleOutputAsJson();

        Assert.Equal(0, exitCode);
        Assert.True(envelope.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(["dev.luotsi.app"], host.ClearAppRequests);
    }

    [Fact]
    public async Task ScenarioExecutor_AppLifecycle_Actions_Invoke_DeviceHost()
    {
        var fileSystem = new FakeFileSystem();
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-15T12:00:00Z", null, System.Globalization.DateTimeStyles.RoundtripKind));
        const string scenarioPath = "/tmp/app-controls.json";
        fileSystem.AddFile(
            scenarioPath,
            """
            {
              "name": "app controls",
              "steps": [
                { "action": "startApp", "package": "dev.luotsi.app", "activity": ".MainActivity", "wait": true },
                { "action": "startUri", "uri": "luotsi://item/42", "package": "dev.luotsi.app", "activity": ".DeepLinkActivity", "intentAction": "android.intent.action.VIEW" },
                { "action": "forceStop", "package": "dev.luotsi.app" },
                { "action": "clear", "package": "dev.luotsi.app" },
                { "action": "waitForActivity", "activity": ".MainActivity", "timeoutSec": 3 },
                { "action": "waitForNotActivity", "text": ".SplashActivity", "timeoutSec": 3 },
                { "action": "isAppInstalled", "package": "dev.luotsi.app" },
                { "action": "listInstalledPackages", "thirdPartyOnly": true },
                { "action": "grantPermission", "package": "dev.luotsi.app", "permission": "android.permission.CAMERA" },
                { "action": "revokePermission", "package": "dev.luotsi.app", "permission": "android.permission.CAMERA" }
              ]
            }
            """);
        var host = new FakeDeviceHost(new ScreenState(timeProvider.GetUtcNow(), 0, []));
        var scenarios = new ScenarioExecutor(host, fileSystem, timeProvider, new FakeDelay(timeProvider));

        var result = await scenarios.RunAsync(scenarioPath);
        var json = JsonSerializer.SerializeToElement(result, AppJson.Options);

        Assert.Equal("passed", json.GetProperty("status").GetString());
        Assert.Equal([("dev.luotsi.app", ".MainActivity", true)], host.StartAppRequests);
        Assert.Equal([("luotsi://item/42", "dev.luotsi.app", ".DeepLinkActivity", "android.intent.action.VIEW", false)], host.StartUriRequests);
        Assert.Equal(["dev.luotsi.app"], host.ForceStopRequests);
        Assert.Equal(["dev.luotsi.app"], host.ClearAppRequests);
        Assert.Equal([(".MainActivity", 3)], host.WaitForActivityRequests);
        Assert.Equal([(".SplashActivity", 3)], host.WaitForNotActivityRequests);
        Assert.Equal(["dev.luotsi.app"], host.IsAppInstalledRequests);
        Assert.Equal([true], host.ListInstalledPackagesRequests);
        Assert.Equal([("dev.luotsi.app", "android.permission.CAMERA")], host.GrantPermissionRequests);
        Assert.Equal([("dev.luotsi.app", "android.permission.CAMERA")], host.RevokePermissionRequests);
    }
}
