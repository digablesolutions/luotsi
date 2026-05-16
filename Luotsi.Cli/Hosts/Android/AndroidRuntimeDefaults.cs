namespace Luotsi.Cli.Hosts.Android;

/// <summary>
/// Shared Android runtime defaults for the device host and view helper bootstrap.
/// </summary>
internal static class AndroidRuntimeDefaults
{
    public const string TargetPackageEnvironmentVariable = "LUOTSI_TARGET_PACKAGE";
    public const string DefaultTargetPackage = "dev.luotsi.app";
    public const string DeviceFingerprintMarkerPrefix = "__LUOTSI_DEVICE_FINGERPRINT__";
    public const int UiPollDelayMs = 250;
    public static readonly TimeSpan KeyboardVisibilityCacheTtl = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan UiDumpCacheTtl = TimeSpan.FromMilliseconds(UiPollDelayMs);
    public const int UiDumpRetryMaxAttempts = 3;
    public const int MinRecordTimeLimitSeconds = 1;
    public const int MaxRecordTimeLimitSeconds = 180;
    public const string ViewHelperPathEnvironmentVariable = "DEVICE_E2E_VIEW_HELPER_JAR";
    public const string DefaultViewHelperRelativePath = "Luotsi.ViewServer.Android\\app\\build\\outputs\\apk\\debug\\app-debug.apk";
    public const string ViewHelperRemotePath = "/data/local/tmp/luotsi-view-server.apk";
    public const string ViewHelperMainClass = "dev.luotsi.view.Main";
    public const string ViewHelperVersion = "phase-3-screenrecord";
    public const string ViewSocketPrefix = "luotsi_view_";
}