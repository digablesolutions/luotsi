using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Infrastructure.Devices;

internal sealed class UnsupportedDeviceHost : IDeviceHost
{
    public Task<DeviceListResult> GetDevicesAsync() => Unsupported<DeviceListResult>();

    public Task<ScreenState> GetScreenStateAsync() => Unsupported<ScreenState>();

    public Task<TapResult> TapAsync(string x, string y) => Unsupported<TapResult>();

    public Task<LogcatResult> LogcatAsync(int tail) => Unsupported<LogcatResult>();

    public Task<TelemetryResult> TelemetryTailAsync(int tail) => Unsupported<TelemetryResult>();

    public Task<TelemetryResult> TelemetryWatchAsync(int timeoutSec) => Unsupported<TelemetryResult>();

    public Task<RecordResult> RecordAsync(string output, int timeLimitSec) => Unsupported<RecordResult>();

    public Task<ScrollResult> ScrollAsync(int horizontalTicks, int verticalTicks) => Unsupported<ScrollResult>();

    public Task<PushFileResult> PushFileAsync(string localPath, string? remoteDirectory = null) => Unsupported<PushFileResult>();

    public Task<PullFileResult> PullFileAsync(string remotePath, string? localDirectory = null) => Unsupported<PullFileResult>();

    public Task<InstallPackageResult> InstallPackageAsync(string packagePath) => Unsupported<InstallPackageResult>();

    public Task<ScreenElement> WaitVisibleAsync(string text, int timeoutSec) => Unsupported<ScreenElement>();

    public Task<WaitNotVisibleResult> WaitNotVisibleAsync(string text, int timeoutSec) => Unsupported<WaitNotVisibleResult>();

    public Task<TapResult> TapTextAsync(string text, int timeoutSec) => Unsupported<TapResult>();

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

    public Task<CaptureArtifactsResult> CaptureArtifactsAsync(string label) => Unsupported<CaptureArtifactsResult>();

    public Task<AssertTextInputReadyResult> AssertTextInputReadyAsync(bool requireKeyboard, int timeoutSec) => Unsupported<AssertTextInputReadyResult>();

    public Task<AssertBelowResult> AssertBelowAsync(string text, string referenceText, int maxGapPx) => Unsupported<AssertBelowResult>();

    public Task<AssertAlignedResult> AssertAlignedAsync(string text, string referenceText, int maxDeltaPx) => Unsupported<AssertAlignedResult>();

    public Task<AssertAppVersionResult> AssertAppVersionAsync(string? packageName, int maxTopInsetPx, int maxRightInsetPx) => Unsupported<AssertAppVersionResult>();

    public Task<DeviceFingerprint> WriteDeviceFingerprintAsync() => Unsupported<DeviceFingerprint>();

    public Task<FailureArtifactBundle> CaptureFailureArtifactsAsync(FailureCaptureRequest request, Exception exception) => Unsupported<FailureArtifactBundle>();

    private static Task<T> Unsupported<T>() => Task.FromException<T>(new InvalidOperationException("Joined share sessions do not expose direct device-host actions."));
}
