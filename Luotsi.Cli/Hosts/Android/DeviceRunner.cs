using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Ids;
using Luotsi.Cli.Infrastructure.System;
using Luotsi.Cli.Infrastructure.Time;
using Luotsi.Cli.Models;
using Luotsi.Cli.Telemetry;

namespace Luotsi.Cli.Hosts.Android;

/// <summary>
/// Device operation facade used by the command handlers.
/// </summary>
public sealed class DeviceRunner(
    IAdbClient adb,
    ArtifactSession artifacts,
    TimeProvider? timeProvider = null,
    IDelay? delay = null,
    IFileSystem? fileSystem = null,
    IUniqueIdGenerator? idGenerator = null,
    IEnvironmentVariables? environment = null,
    ITelemetryParser? telemetryParser = null) : IDeviceHost, IAdbCommandHost, IWirelessDebugHost
{
    private readonly IAdbClient _adb = adb ?? throw new ArgumentNullException(nameof(adb));
    private readonly ArtifactSession _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly IDelay _delay = delay ?? new TaskDelay(timeProvider);
    private readonly IFileSystem _fileSystem = fileSystem ?? new PhysicalFileSystem();
    private readonly AndroidScreenCaptureService _screenCapture = new(
        adb ?? throw new ArgumentNullException(nameof(adb)),
        artifacts ?? throw new ArgumentNullException(nameof(artifacts)),
        timeProvider ?? TimeProvider.System,
        delay ?? new TaskDelay(timeProvider),
        fileSystem ?? new PhysicalFileSystem());
    private readonly AndroidTelemetryMonitor _telemetryMonitor = new(
        adb ?? throw new ArgumentNullException(nameof(adb)),
        artifacts ?? throw new ArgumentNullException(nameof(artifacts)),
        timeProvider ?? TimeProvider.System,
        telemetryParser ?? new LuotsiDeviceTelemetryParser());
    private readonly IUniqueIdGenerator _idGenerator = idGenerator ?? new GuidUniqueIdGenerator();
    private readonly IEnvironmentVariables _environment = environment ?? new SystemEnvironmentVariables();

    /// <summary>
    /// Lists connected devices.
    /// </summary>
    /// <returns>Device list data.</returns>
    public async Task<DeviceListResult> GetDevicesAsync()
    {
        var result = await _adb.RunAsync(["devices", "-l"]).ConfigureAwait(false);
        result.EnsureSuccess("adb devices failed");
        var devices = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0 && !line.StartsWith("*", StringComparison.Ordinal))
            .Select(static line =>
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var detailIndex = Array.FindIndex(parts, 1, static part => part.Contains(':', StringComparison.Ordinal));
                var statusEndIndex = detailIndex >= 0 ? detailIndex : parts.Length;
                var status = statusEndIndex > 1 ? string.Join(' ', parts[1..statusEndIndex]) : null;
                var details = detailIndex >= 0 ? string.Join(' ', parts[detailIndex..]) : string.Empty;
                return new DeviceInfo(parts.ElementAtOrDefault(0), status, details);
            })
            .ToArray();
        return new DeviceListResult(devices);
    }

    public Task<AdbDiagnosticResult> GetAdbServerStatusAsync() =>
        DeviceReadiness.GetAdbServerStatusAsync();

    public Task<AdbDiagnosticResult> GetAdbVersionAsync() =>
        DeviceReadiness.GetAdbVersionAsync();

    public Task<AdbDiagnosticResult> GetAdbFeaturesAsync() =>
        DeviceReadiness.GetAdbFeaturesAsync();

    public Task<AdbDiagnosticResult> CheckAdbMdnsAsync() =>
        DeviceReadiness.CheckAdbMdnsAsync();

    public Task<AdbDiagnosticResult> ReconnectAdbAsync(string target) =>
        DeviceReadiness.ReconnectAdbAsync(target);

    public async Task<AdbReadinessResult> WaitForDeviceAsync(int timeoutSec)
        => await DeviceReadiness.WaitForDeviceAsync(timeoutSec).ConfigureAwait(false);

    /// <summary>
    /// Reads device and application readiness without writing command artifacts.
    /// </summary>
    /// <param name="packageName">Optional package name expected to be installed and focused.</param>
    /// <returns>Preflight data.</returns>
    public async Task<PreflightResult> ReadPreflightAsync(string? packageName)
        => await DeviceReadiness.ReadPreflightAsync(packageName).ConfigureAwait(false);

    /// <summary>
    /// Checks device and application readiness.
    /// </summary>
    /// <param name="packageName">Optional package name expected to be installed and focused.</param>
    /// <returns>Preflight data.</returns>
    public async Task<PreflightResult> PreflightAsync(string? packageName)
        => await DeviceReadiness.PreflightAsync(packageName).ConfigureAwait(false);

    /// <summary>
    /// Captures and normalizes the current UI hierarchy.
    /// </summary>
    /// <returns>Screen state data.</returns>
    public async Task<ScreenState> GetScreenStateAsync() =>
        await ScreenStateReadModel.GetScreenStateAsync().ConfigureAwait(false);

    /// <summary>
    /// Waits for visible text.
    /// </summary>
    /// <param name="text">Text or content description to find.</param>
    /// <param name="timeoutSec">Timeout in seconds.</param>
    /// <returns>Matched element.</returns>
    public async Task<ScreenElement> WaitVisibleAsync(string text, int timeoutSec)
        => await UiInteractions.WaitVisibleAsync(text, timeoutSec).ConfigureAwait(false);

    /// <summary>
    /// Taps the center of visible text.
    /// </summary>
    /// <param name="text">Text or content description to tap.</param>
    /// <param name="timeoutSec">Timeout in seconds.</param>
    /// <returns>Tap data.</returns>
    public async Task<TapResult> TapTextAsync(string text, int timeoutSec)
        => await UiInteractions.TapTextAsync(text, timeoutSec).ConfigureAwait(false);

    /// <summary>
    /// Sends a tap at absolute coordinates.
    /// </summary>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <returns>Tap data.</returns>
    public async Task<TapResult> TapAsync(string x, string y)
        => await UiInteractions.TapAsync(x, y).ConfigureAwait(false);

    /// <summary>
    /// Types text via adb input.
    /// </summary>
    /// <param name="text">Text to type.</param>
    /// <returns>Typed text metadata.</returns>
    public async Task<TypeTextResult> TypeTextAsync(string text)
        => await UiInteractions.TypeTextAsync(text).ConfigureAwait(false);

    /// <summary>
    /// Sends an Android keyevent.
    /// </summary>
    /// <param name="code">Keyevent code or name.</param>
    /// <returns>Keyevent metadata.</returns>
    public async Task<KeyEventResult> KeyEventAsync(string code)
        => await UiInteractions.KeyEventAsync(code).ConfigureAwait(false);

    public async Task<ScrollResult> ScrollAsync(int horizontalTicks, int verticalTicks)
        => await UiInteractions.ScrollAsync(horizontalTicks, verticalTicks).ConfigureAwait(false);

    public async Task<PushFileResult> PushFileAsync(string localPath, string? remoteDirectory = null)
        => await FileAndPortControl.PushFileAsync(localPath, remoteDirectory).ConfigureAwait(false);

    public async Task<PullFileResult> PullFileAsync(string remotePath, string? localDirectory = null)
        => await FileAndPortControl.PullFileAsync(remotePath, localDirectory).ConfigureAwait(false);

    public async Task<PortForwardListResult> ListForwardsAsync()
        => await FileAndPortControl.ListForwardsAsync().ConfigureAwait(false);

    public async Task<PortForwardResult> ForwardAsync(string local, string remote, bool noRebind)
        => await FileAndPortControl.ForwardAsync(local, remote, noRebind).ConfigureAwait(false);

    public async Task<PortForwardRemoveResult> RemoveForwardAsync(string local)
        => await FileAndPortControl.RemoveForwardAsync(local).ConfigureAwait(false);

    public async Task<PortReverseListResult> ListReversesAsync()
        => await FileAndPortControl.ListReversesAsync().ConfigureAwait(false);

    public async Task<PortReverseResult> ReverseAsync(string remote, string local, bool noRebind)
        => await FileAndPortControl.ReverseAsync(remote, local, noRebind).ConfigureAwait(false);

    public async Task<PortReverseRemoveResult> RemoveReverseAsync(string remote)
        => await FileAndPortControl.RemoveReverseAsync(remote).ConfigureAwait(false);

    public async Task<WirelessConnectResult> EnableWirelessAsync(string? host, int port)
        => await WirelessDebug.EnableWirelessAsync(host, port).ConfigureAwait(false);

    public async Task<WirelessScanResult> ScanWirelessServicesAsync()
        => await WirelessDebug.ScanWirelessServicesAsync().ConfigureAwait(false);

    public async Task<WirelessPairResult> PairWirelessAsync(string? endpoint, string? service, string? pairingCode)
        => await WirelessDebug.PairWirelessAsync(endpoint, service, pairingCode).ConfigureAwait(false);

    public async Task<WirelessMdnsConnectResult> ConnectWirelessAsync(string? endpoint, string? service)
        => await WirelessDebug.ConnectWirelessAsync(endpoint, service).ConfigureAwait(false);

    internal static IReadOnlyList<WirelessMdnsService> ParseWirelessMdnsServices(string output) =>
        WirelessDebugResolver.ParseMdnsServices(output);

    public async Task<InstallPackageResult> InstallPackageAsync(string packagePath)
        => await DeviceControl.InstallPackageAsync(packagePath).ConfigureAwait(false);

    public async Task<StartAppResult> StartAppAsync(string packageName, string? activity, bool wait)
        => await DeviceControl.StartAppAsync(packageName, activity, wait).ConfigureAwait(false);

    public async Task<StartUriResult> StartUriAsync(string uri, string? packageName, string? activity, string? action, bool wait)
        => await DeviceControl.StartUriAsync(uri, packageName, activity, action, wait).ConfigureAwait(false);

    public async Task<AppPackageCommandResult> ForceStopAsync(string packageName)
        => await DeviceControl.ForceStopAsync(packageName).ConfigureAwait(false);

    public async Task<AppPackageCommandResult> ClearAppAsync(string packageName)
        => await DeviceControl.ClearAppAsync(packageName).ConfigureAwait(false);

    public async Task<ActivityWaitResult> WaitForActivityAsync(string activity, int timeoutSec)
        => await DeviceControl.WaitForActivityAsync(activity, timeoutSec).ConfigureAwait(false);

    public async Task<ActivityWaitResult> WaitForNotActivityAsync(string activity, int timeoutSec)
        => await DeviceControl.WaitForNotActivityAsync(activity, timeoutSec).ConfigureAwait(false);

    public async Task<AppInstalledResult> IsAppInstalledAsync(string packageName)
        => await DeviceControl.IsAppInstalledAsync(packageName).ConfigureAwait(false);

    public async Task<InstalledPackageListResult> ListInstalledPackagesAsync(bool thirdPartyOnly)
        => await DeviceControl.ListInstalledPackagesAsync(thirdPartyOnly).ConfigureAwait(false);

    public async Task<PermissionCommandResult> GrantPermissionAsync(string packageName, string permission)
        => await DeviceControl.GrantPermissionAsync(packageName, permission).ConfigureAwait(false);

    public async Task<PermissionCommandResult> RevokePermissionAsync(string packageName, string permission)
        => await DeviceControl.RevokePermissionAsync(packageName, permission).ConfigureAwait(false);

    public async Task<WaitLogResult> WaitForLogAsync(string text, int timeoutSec)
        => await LogMonitor.WaitForLogAsync(text, timeoutSec).ConfigureAwait(false);

    /// <summary>
    /// Reads logcat.
    /// </summary>
    /// <param name="tail">Maximum lines to return.</param>
    /// <returns>Logcat lines.</returns>
    public async Task<LogcatResult> LogcatAsync(int tail)
        => await LogMonitor.LogcatAsync(tail).ConfigureAwait(false);

    /// <summary>
    /// Reads and parses recent semantic telemetry events.
    /// </summary>
    /// <param name="tail">Maximum logcat lines to inspect.</param>
    /// <returns>Telemetry data.</returns>
    public async Task<TelemetryResult> TelemetryTailAsync(int tail)
        => await SemanticTelemetry.TelemetryTailAsync(tail).ConfigureAwait(false);

    /// <summary>
    /// Collects semantic telemetry events over a bounded watch window.
    /// </summary>
    /// <param name="timeoutSec">Duration to watch for telemetry events.</param>
    /// <returns>Telemetry data.</returns>
    public async Task<TelemetryResult> TelemetryWatchAsync(int timeoutSec)
        => await SemanticTelemetry.TelemetryWatchAsync(timeoutSec).ConfigureAwait(false);

    /// <summary>
    /// Waits for a semantic telemetry step event.
    /// </summary>
    /// <param name="step">Expected semantic step name.</param>
    /// <param name="timeoutSec">Timeout in seconds.</param>
    /// <returns>Matched telemetry data.</returns>
    public Task<TelemetryMatchResult> WaitForStepAsync(string step, int timeoutSec) =>
        SemanticTelemetry.WaitForStepAsync(step, timeoutSec);

    /// <summary>
    /// Waits for a semantic telemetry action-ready event.
    /// </summary>
    /// <param name="action">Expected action name.</param>
    /// <param name="step">Optional expected step name.</param>
    /// <param name="timeoutSec">Timeout in seconds.</param>
    /// <returns>Matched telemetry data.</returns>
    public Task<TelemetryMatchResult> WaitForActionReadyAsync(string action, string? step, int timeoutSec) =>
        SemanticTelemetry.WaitForActionReadyAsync(action, step, timeoutSec);

    /// <summary>
    /// Records video with Android screenrecord.
    /// </summary>
    /// <param name="output">Local output path.</param>
    /// <param name="timeLimitSec">Maximum recording duration.</param>
    /// <returns>Recording metadata.</returns>
    public async Task<RecordResult> RecordAsync(string output, int timeLimitSec)
        => await ArtifactOperations.RecordAsync(output, timeLimitSec).ConfigureAwait(false);

    public async Task<DeviceFingerprint> WriteDeviceFingerprintAsync()
        => await DeviceReadiness.WriteDeviceFingerprintAsync().ConfigureAwait(false);

    public async Task<FailureArtifactBundle> CaptureFailureArtifactsAsync(FailureCaptureRequest request, Exception exception)
        => await ArtifactOperations.CaptureFailureArtifactsAsync(request, exception).ConfigureAwait(false);

    public async Task<WaitNotVisibleResult> WaitNotVisibleAsync(string text, int timeoutSec)
        => await UiInteractions.WaitNotVisibleAsync(text, timeoutSec).ConfigureAwait(false);

    public async Task<TapPointResult> TapPointAsync(string? label, int? x, int? y, double? xRatio, double? yRatio, int postTapDelayMs)
        => await UiInteractions.TapPointAsync(label, x, y, xRatio, yRatio, postTapDelayMs).ConfigureAwait(false);

    public async Task<DoubleTapHeaderLogoResult> DoubleTapHeaderLogoAsync()
        => await UiInteractions.DoubleTapHeaderLogoAsync().ConfigureAwait(false);

    public async Task<TypePinResult> TypePinAsync(string pin, int perDigitDelayMs)
        => await UiInteractions.TypePinAsync(pin, perDigitDelayMs).ConfigureAwait(false);

    public async Task<ResetLogResult> ResetLogAsync()
        => await LogMonitor.ResetLogAsync().ConfigureAwait(false);

    public async Task<AssertEventResult> AssertEventAsync(string name, IReadOnlyList<string> contains, string? detailsPattern, int timeoutSec, DateTimeOffset? since = null)
        => await LogMonitor.AssertEventAsync(name, contains, detailsPattern, timeoutSec, since).ConfigureAwait(false);

    public async Task<TakeScreenshotResult> TakeScreenshotAsync(string label)
        => await ArtifactOperations.TakeScreenshotAsync(label).ConfigureAwait(false);

    public async Task<ScreenshotAssertionResult> AssertScreenshotAsync(string label, int? expectedWidth, int? expectedHeight, string? expectedSha256)
        => await ArtifactOperations.AssertScreenshotAsync(label, expectedWidth, expectedHeight, expectedSha256).ConfigureAwait(false);

    public async Task<CaptureArtifactsResult> CaptureArtifactsAsync(string label)
        => await ArtifactOperations.CaptureArtifactsAsync(label).ConfigureAwait(false);

    public async Task<AssertTextInputReadyResult> AssertTextInputReadyAsync(bool requireKeyboard, int timeoutSec)
        => await UiInteractions.AssertTextInputReadyAsync(requireKeyboard, timeoutSec).ConfigureAwait(false);

    public async Task<AssertBelowResult> AssertBelowAsync(string text, string referenceText, int maxGapPx)
        => await UiInteractions.AssertBelowAsync(text, referenceText, maxGapPx).ConfigureAwait(false);

    public async Task<AssertAlignedResult> AssertAlignedAsync(string text, string referenceText, int maxDeltaPx)
        => await UiInteractions.AssertAlignedAsync(text, referenceText, maxDeltaPx).ConfigureAwait(false);

    public async Task<AssertAppVersionResult> AssertAppVersionAsync(string? packageName, int maxTopInsetPx, int maxRightInsetPx)
        => await UiInteractions.AssertAppVersionAsync(packageName, maxTopInsetPx, maxRightInsetPx).ConfigureAwait(false);

    private void InvalidateUiReadCaches()
    {
        ScreenStateReadModel.InvalidateUiReadCaches();
    }

    private AndroidFileAndPortOperations FileAndPortControl =>
        field ??= new AndroidFileAndPortOperations(
            _adb,
            _fileSystem);

    private AndroidDeviceControlOperations DeviceControl =>
        field ??= new AndroidDeviceControlOperations(
            _adb,
            _timeProvider,
            _delay,
            _fileSystem,
            InvalidateUiReadCaches);

    private AndroidWirelessDebugOperations WirelessDebug =>
        field ??= new AndroidWirelessDebugOperations(_adb);

    private AndroidDeviceReadinessOperations DeviceReadiness =>
        field ??= new AndroidDeviceReadinessOperations(
            _adb,
            _artifacts,
            _timeProvider);

    private AndroidScreenStateReadModel ScreenStateReadModel =>
        field ??= new AndroidScreenStateReadModel(
            _adb,
            _screenCapture,
            _timeProvider,
            _delay);

    private AndroidUiInteractionService UiInteractions =>
        field ??= new AndroidUiInteractionService(
            _adb,
            ScreenStateReadModel,
            _timeProvider,
            _delay,
            _environment);

    private AndroidLogMonitorOperations LogMonitor =>
        field ??= new AndroidLogMonitorOperations(
            _adb,
            _artifacts,
            _timeProvider);

    private AndroidSemanticTelemetryOperations SemanticTelemetry =>
        field ??= new AndroidSemanticTelemetryOperations(
            _adb,
            _telemetryMonitor);

    private AndroidArtifactOperations ArtifactOperations =>
        field ??= new AndroidArtifactOperations(
            _adb,
            _artifacts,
            _timeProvider,
            _fileSystem,
            _idGenerator,
            _environment,
            ScreenStateReadModel);
}
