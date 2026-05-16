using System.Text.Json;
using Luotsi.Cli.Telemetry;

namespace Luotsi.Cli.Models;

// Device listing
public sealed record DeviceInfo(string? Serial, string? Status, string Details);

public sealed record DeviceListResult(IReadOnlyList<DeviceInfo> Devices);

// Preflight
public sealed record PreflightResult(
    string Model,
    string AndroidRelease,
    string Sdk,
    string CurrentFocus,
    string? Package,
    string? PackageInfo,
    string Fingerprint,
    string Abi,
    string Serial);

// Tap
public sealed record TapResult(int X, int Y);

// Type text
public sealed record TypeTextResult(string Text);

// Key event
public sealed record KeyEventResult(string Code);

// Wait for log
public sealed record WaitLogResult(string Contains, int TimeoutSec, string MatchedLine, int LineCount);

// Logcat
public sealed record LogcatResult(string[] Lines);

// Telemetry (shared between tail and watch)
public sealed record TelemetryResult(
    int InspectedLineCount,
    int TelemetryLineCount,
    int EventCount,
    int ParseErrorCount,
    IReadOnlyList<TelemetryEvent> Events,
    IReadOnlyList<TelemetryParseError> ParseErrors);

// Wait for step / action ready
public sealed record TelemetryMatchResult(
    string? Step,
    string? Action,
    string Line,
    string EventName,
    JsonElement Payload);

// Record video
public sealed record RecordResult(string Output, int TimeLimitSec);

// Scroll gesture
public sealed record ScrollResult(int HorizontalTicks, int VerticalTicks, int StartX, int StartY, int EndX, int EndY, int DurationMs);

// Push file to device
public sealed record PushFileResult(string LocalPath, string RemotePath);

// Pull file from device
public sealed record PullFileResult(string RemotePath, string LocalPath);

// Install package on device
public sealed record InstallPackageResult(string PackagePath);

// Enable adb-over-TCP and connect to the target endpoint.
public sealed record WirelessConnectResult(string Host, int Port, string Endpoint);

// Wait not visible
public sealed record WaitNotVisibleResult(string Text, int AttemptCount, bool Visible);

// Tap point
public sealed record TapPointResult(string? Label, int X, int Y, double? XRatio, double? YRatio, int PostTapDelayMs);

// Double tap header logo
public sealed record DoubleTapHeaderLogoResult(string Target, int X, int Y, int IntervalMs);

// Type pin
public sealed record TypePinResult(int PinLength, int PerDigitDelayMs);

// Reset log
public sealed record ResetLogResult(bool Cleared);

// Assert event
public sealed record AssertEventResult(string Name, IReadOnlyList<string> Contains, string? DetailsPattern, string MatchedLine);

// Take screenshot
public sealed record TakeScreenshotResult(string Label, string File);

// Capture artifacts
public sealed record CaptureArtifactsResult(string Label, string Screenshot, string Logcat, string ScreenState, string Hierarchy);

// Assert text input ready
public sealed record AssertTextInputReadyResult(bool RequireKeyboard, bool KeyboardVisible, string? Text, string? ResourceId, string? Bounds);

// Assert below
public sealed record AssertBelowResult(string Text, string Below, int GapPx, int MaxGapPx);

// Assert aligned
public sealed record AssertAlignedResult(string Text, string With, int DeltaPx, int MaxDeltaPx);

// Assert app version
public sealed record AssertAppVersionResult(
    string Package,
    string Label,
    int TopInsetPx,
    int RightInsetPx,
    int MaxTopInsetPx,
    int MaxRightInsetPx);

// Sleep
public sealed record SleepResult(int Milliseconds);

// Artifact data
public sealed record ArtifactData(string ArtifactRoot, string PollArtifacts);
