using System.Text;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;

namespace Luotsi.Cli.Infrastructure;

public interface IDelay
{
    Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default);
}

public interface IFileSystem
{
    void CreateDirectory(string path);
    Task WriteAllTextAsync(string path, string text, Encoding encoding, CancellationToken cancellationToken = default);
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);
    Stream OpenWrite(string path, bool overwrite = true);
    void DeleteFile(string path);
    bool FileExists(string path);
    void CopyFile(string sourcePath, string destinationPath, bool overwrite);
    string GetTempPath();
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> args, CancellationToken cancellationToken = default);
}

public interface IAdbClient
{
    Task<AdbCommandResult> RunAsync(IEnumerable<string> args, CancellationToken cancellationToken = default);
    Task<AdbCommandResult> ShellAsync(string command, CancellationToken cancellationToken = default);
    Task<IAsyncDisposable> StartShellAsync(string command, CancellationToken cancellationToken = default);
    Task<AdbLogStreamResult> MonitorLogAsync(string containsText, DateTimeOffset since, int timeoutSec, CancellationToken cancellationToken = default);
    Task<AdbLogStreamResult> MonitorLogAsync(DateTimeOffset since, int timeoutSec, Func<string, bool>? stopWhen = null, Action<string>? observeLine = null, CancellationToken cancellationToken = default);
}

public interface IAdbClientFactory
{
    IAdbClient Create(string executable, string? serial, IProcessRunner processRunner, TimeSpan? commandTimeout = null);
}

public interface IAdbCommandHost
{
    Task<AdbDiagnosticResult> GetAdbServerStatusAsync();

    Task<AdbDiagnosticResult> GetAdbVersionAsync();

    Task<AdbDiagnosticResult> GetAdbFeaturesAsync();

    Task<AdbDiagnosticResult> CheckAdbMdnsAsync();

    Task<AdbDiagnosticResult> ReconnectAdbAsync(string target);

    Task<AdbReadinessResult> WaitForDeviceAsync(int timeoutSec);

    /// <summary>
    /// Reads device and application readiness without writing command artifacts.
    /// </summary>
    /// <param name="packageName">Optional foreground package to require.</param>
    /// <returns>Preflight data.</returns>
    Task<PreflightResult> ReadPreflightAsync(string? packageName);

    /// <summary>
    /// Checks whether the target device and app are ready.
    /// </summary>
    /// <param name="packageName">Optional foreground package to require.</param>
    /// <returns>Preflight data.</returns>
    Task<PreflightResult> PreflightAsync(string? packageName);
}

/// <summary>
/// Host-side device operations shared by commands and scenarios.
/// </summary>
public interface IDeviceHost : IScenarioActionHost
{
    /// <summary>
    /// Lists connected devices.
    /// </summary>
    /// <returns>Device list data.</returns>
    Task<DeviceListResult> GetDevicesAsync();

    /// <summary>
    /// Captures the current normalized screen state.
    /// </summary>
    /// <returns>Screen state data.</returns>
    Task<ScreenState> GetScreenStateAsync();

    /// <summary>
    /// Sends a tap at absolute coordinates.
    /// </summary>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <returns>Tap metadata.</returns>
    Task<TapResult> TapAsync(string x, string y);

    /// <summary>
    /// Reads logcat lines from the active device.
    /// </summary>
    /// <param name="tail">Maximum lines to return.</param>
    /// <returns>Logcat data.</returns>
    Task<LogcatResult> LogcatAsync(int tail);

    /// <summary>
    /// Reads and parses recent semantic telemetry events.
    /// </summary>
    /// <param name="tail">Maximum logcat lines to inspect.</param>
    /// <returns>Telemetry data.</returns>
    Task<TelemetryResult> TelemetryTailAsync(int tail);

    /// <summary>
    /// Collects semantic telemetry events over a bounded watch window.
    /// </summary>
    /// <param name="timeoutSec">Duration to watch for telemetry events.</param>
    /// <returns>Telemetry data.</returns>
    Task<TelemetryResult> TelemetryWatchAsync(int timeoutSec);

    /// <summary>
    /// Records the device screen to a local file.
    /// </summary>
    /// <param name="output">Local output path.</param>
    /// <param name="timeLimitSec">Maximum recording duration.</param>
    /// <returns>Recording metadata.</returns>
    Task<RecordResult> RecordAsync(string output, int timeLimitSec);

    /// <summary>
    /// Scrolls the current surface using a host-side gesture.
    /// </summary>
    /// <param name="horizontalTicks">Horizontal wheel ticks.</param>
    /// <param name="verticalTicks">Vertical wheel ticks.</param>
    /// <returns>Scroll metadata.</returns>
    Task<ScrollResult> ScrollAsync(int horizontalTicks, int verticalTicks);

    /// <summary>
    /// Pushes a host-local file to the device.
    /// </summary>
    /// <param name="localPath">Host-local path.</param>
    /// <param name="remoteDirectory">Optional device directory.</param>
    /// <returns>Transfer metadata.</returns>
    Task<PushFileResult> PushFileAsync(string localPath, string? remoteDirectory = null);

    Task<PullFileResult> PullFileAsync(string remotePath, string? localDirectory = null);

    /// <summary>
    /// Installs an APK from the host onto the device.
    /// </summary>
    /// <param name="packagePath">Host-local package path.</param>
    /// <returns>Installation metadata.</returns>
    Task<InstallPackageResult> InstallPackageAsync(string packagePath);
}

/// <summary>
/// Wireless ADB workflows exposed only to wireless CLI commands.
/// </summary>
public interface IWirelessDebugHost
{
    Task<WirelessConnectResult> EnableWirelessAsync(string? host, int port);

    Task<WirelessScanResult> ScanWirelessServicesAsync();

    Task<WirelessPairResult> PairWirelessAsync(string? endpoint, string? service, string? pairingCode);

    Task<WirelessMdnsConnectResult> ConnectWirelessAsync(string? endpoint, string? service);
}

/// <summary>
/// Parameters used to create a device host implementation.
/// </summary>
/// <param name="Platform">Target host platform name.</param>
/// <param name="Executable">Transport executable path.</param>
/// <param name="DeviceSerial">Optional device identifier.</param>
/// <param name="CommandTimeout">Optional bounded ADB command timeout.</param>
public sealed record DeviceHostConfiguration(string Platform, string Executable, string? DeviceSerial, TimeSpan? CommandTimeout = null);

/// <summary>
/// Creates a concrete device host for a requested platform.
/// </summary>
public interface IDeviceHostFactory
{
    /// <summary>
    /// Creates a concrete device host.
    /// </summary>
    /// <param name="configuration">Host creation parameters.</param>
    /// <param name="artifacts">Artifact session for the command.</param>
    /// <returns>Concrete device host.</returns>
    IDeviceHost Create(DeviceHostConfiguration configuration, ArtifactSession artifacts);
}

public interface IConsoleIo
{
    void WriteLine(string value);
    void WriteErrorLine(string value);
    string? ReadLine();
}

public interface IEnvironmentVariables
{
    string? GetEnvironmentVariable(string variable);
}

public interface IUniqueIdGenerator
{
    string NewId();
}

public sealed record AdbCommandResult(string Executable, string? Serial, IReadOnlyList<string> Args, ProcessResult Process, AdbRetryInfo? Retry = null)
{
    public int ExitCode => Process.ExitCode;

    public string Stdout => Process.Stdout;

    public string Stderr => Process.Stderr;

    public int AttemptCount => Retry?.AttemptCount ?? 1;

    public string Invocation => string.Join(" ", [Executable, .. Args.Select(QuoteArgument)]);

    public void EnsureSuccess(string message)
    {
        if (ExitCode == 0) return;
        var detail = string.IsNullOrWhiteSpace(Stderr) ? Stdout : Stderr;
        var retryDetail = Retry is null
            ? string.Empty
            : $" Retried once after {Retry.Reason}; recovery actions: {string.Join(", ", Retry.RecoveryActions.Select(static action => $"{action.Command} => {action.ExitCode}"))}.";
        throw new InvalidOperationException($"{message}: `{Invocation}` exited {ExitCode}. {detail}{retryDetail}".Trim());
    }

    private static string QuoteArgument(string value) =>
        value.Any(static ch => char.IsWhiteSpace(ch) || ch == '"') ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"" : value;
}

public sealed record AdbLogStreamResult(string ContainsText, string LogOutput, string? MatchedLine, int LineCount, int TimeoutSec, DateTimeOffset Since, string Invocation, int ExitCode, string Stderr);
