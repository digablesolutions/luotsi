namespace Luotsi.Cli.Cli;

/// <summary>
/// Shared default values for CLI command handling.
/// </summary>
internal static class CliDefaults
{
    public const string AdbExecutableEnvironmentVariable = "LUOTSI_ADB";
    public const string AdbCommandTimeoutEnvironmentVariable = "LUOTSI_ADB_TIMEOUT_SEC";
    public const string DefaultAdbExecutable = "adb";
    public const string DefaultPlatform = "android";
    public const string DefaultPollArtifactsPolicy = "final";
    public const int DefaultAdbCommandTimeoutSeconds = 120;
    public const int DefaultLogTail = 200;
    public const int DefaultTimeoutSeconds = 15;
    public const int DefaultRecordTimeLimitSeconds = 30;
    public const string DefaultViewCodec = "h264";
    public const string DefaultViewDecoder = "ffmpeg";
}
