using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Infrastructure.Devices;

internal sealed class UnsupportedDeviceHost : IDeviceHost
{
    public Task<DeviceListResult> GetDevicesAsync() => Unsupported<DeviceListResult>();

    public Task<PreflightResult> PreflightAsync(string? packageName) => Unsupported<PreflightResult>();

    public Task<ScreenState> GetScreenStateAsync() => Unsupported<ScreenState>();

    public Task<TapResult> TapAsync(string x, string y) => Unsupported<TapResult>();

    public Task<LogcatResult> LogcatAsync(int tail) => Unsupported<LogcatResult>();

    public Task<TelemetryResult> TelemetryTailAsync(int tail) => Unsupported<TelemetryResult>();

    public Task<TelemetryResult> TelemetryWatchAsync(int timeoutSec) => Unsupported<TelemetryResult>();

    public Task<RecordResult> RecordAsync(string output, int timeLimitSec) => Unsupported<RecordResult>();

    public Task<ScrollResult> ScrollAsync(int horizontalTicks, int verticalTicks) => Unsupported<ScrollResult>();

    public Task<PushFileResult> PushFileAsync(string localPath, string? remoteDirectory = null) => Unsupported<PushFileResult>();

    public Task<PullFileResult> PullFileAsync(string remotePath, string? localDirectory = null) => Unsupported<PullFileResult>();

    public Task<PortForwardListResult> ListForwardsAsync() => Unsupported<PortForwardListResult>();

    public Task<PortForwardResult> ForwardAsync(string local, string remote, bool noRebind) => Unsupported<PortForwardResult>();

    public Task<PortForwardRemoveResult> RemoveForwardAsync(string local) => Unsupported<PortForwardRemoveResult>();

    public Task<PortReverseListResult> ListReversesAsync() => Unsupported<PortReverseListResult>();

    public Task<PortReverseResult> ReverseAsync(string remote, string local, bool noRebind) => Unsupported<PortReverseResult>();

    public Task<PortReverseRemoveResult> RemoveReverseAsync(string remote) => Unsupported<PortReverseRemoveResult>();
    public Task<InstallPackageResult> InstallPackageAsync(string packagePath) => Unsupported<InstallPackageResult>();

    public Task<StartAppResult> StartAppAsync(string packageName, string? activity, bool wait) => Unsupported<StartAppResult>();

    public Task<StartUriResult> StartUriAsync(string uri, string? packageName, string? activity, string? action, bool wait) => Unsupported<StartUriResult>();

    public Task<AppPackageCommandResult> ForceStopAsync(string packageName) => Unsupported<AppPackageCommandResult>();

    public Task<AppPackageCommandResult> ClearAppAsync(string packageName) => Unsupported<AppPackageCommandResult>();

    public Task<ActivityWaitResult> WaitForActivityAsync(string activity, int timeoutSec) => Unsupported<ActivityWaitResult>();

    public Task<ActivityWaitResult> WaitForNotActivityAsync(string activity, int timeoutSec) => Unsupported<ActivityWaitResult>();

    public Task<AppInstalledResult> IsAppInstalledAsync(string packageName) => Unsupported<AppInstalledResult>();

    public Task<InstalledPackageListResult> ListInstalledPackagesAsync(bool thirdPartyOnly) => Unsupported<InstalledPackageListResult>();

    public Task<PermissionCommandResult> GrantPermissionAsync(string packageName, string permission) => Unsupported<PermissionCommandResult>();

    public Task<PermissionCommandResult> RevokePermissionAsync(string packageName, string permission) => Unsupported<PermissionCommandResult>();

    public Task<ScreenElement> WaitVisibleAsync(string text, int timeoutSec) => Unsupported<ScreenElement>();

    public Task<ScreenElement> WaitVisibleAsync(ScreenElementSelector selector, int timeoutSec) => Unsupported<ScreenElement>();

    public Task<WaitNotVisibleResult> WaitNotVisibleAsync(string text, int timeoutSec) => Unsupported<WaitNotVisibleResult>();

    public Task<TapResult> TapTextAsync(string text, int timeoutSec) => Unsupported<TapResult>();

    public Task<TapResult> TapElementAsync(ScreenElementSelector selector, int timeoutSec) => Unsupported<TapResult>();

    public Task<TapPointResult> TapPointAsync(string? label, int? x, int? y, double? xRatio, double? yRatio, int postTapDelayMs) => Unsupported<TapPointResult>();

    public Task<DoubleTapHeaderLogoResult> DoubleTapHeaderLogoAsync() => Unsupported<DoubleTapHeaderLogoResult>();

    public Task<TypeTextResult> TypeTextAsync(string text) => Unsupported<TypeTextResult>();

    public Task<TypePinResult> TypePinAsync(string pin, int perDigitDelayMs) => Unsupported<TypePinResult>();

    public Task<KeyEventResult> KeyEventAsync(string code) => Unsupported<KeyEventResult>();

    public Task<WaitLogResult> WaitForLogAsync(string text, int timeoutSec) => Unsupported<WaitLogResult>();

    public Task<TelemetryMatchResult> WaitForStepAsync(string step, int timeoutSec) => Unsupported<TelemetryMatchResult>();

    public Task<TelemetryMatchResult> WaitForActionReadyAsync(string action, string? step, int timeoutSec) => Unsupported<TelemetryMatchResult>();

    public Task<ResetLogResult> ResetLogAsync() => Unsupported<ResetLogResult>();

    public Task<AssertEventResult> AssertEventAsync(string name, IReadOnlyList<string> contains, string? detailsPattern, int timeoutSec, DateTimeOffset? since = null) => Unsupported<AssertEventResult>();

    public Task<TakeScreenshotResult> TakeScreenshotAsync(string label) => Unsupported<TakeScreenshotResult>();

    public Task<ScreenshotAssertionResult> AssertScreenshotAsync(string label, int? expectedWidth, int? expectedHeight, string? expectedSha256, string? expectedSha256File = null, string? baselineFile = null, bool updateBaseline = false, ScreenshotAssertionRegion? region = null, string? expectedRegionSha256 = null, string? expectedRegionSha256File = null) => Unsupported<ScreenshotAssertionResult>();

    public Task<CaptureArtifactsResult> CaptureArtifactsAsync(string label) => Unsupported<CaptureArtifactsResult>();

    public Task<AssertTextInputReadyResult> AssertTextInputReadyAsync(bool requireKeyboard, int timeoutSec) => Unsupported<AssertTextInputReadyResult>();

    public Task<AssertBelowResult> AssertBelowAsync(string text, string referenceText, int maxGapPx) => Unsupported<AssertBelowResult>();

    public Task<AssertAlignedResult> AssertAlignedAsync(string text, string referenceText, int maxDeltaPx) => Unsupported<AssertAlignedResult>();

    public Task<AssertAppVersionResult> AssertAppVersionAsync(string? packageName, int maxTopInsetPx, int maxRightInsetPx) => Unsupported<AssertAppVersionResult>();

    public Task<DeviceFingerprint> WriteDeviceFingerprintAsync() => Unsupported<DeviceFingerprint>();

    public Task<FailureArtifactBundle> CaptureFailureArtifactsAsync(FailureCaptureRequest request, Exception exception) => Unsupported<FailureArtifactBundle>();

    private static Task<T> Unsupported<T>() => Task.FromException<T>(new InvalidOperationException("Joined share sessions do not expose direct device-host actions."));
}
