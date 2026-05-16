using System.Text;
using VisitLab.Cli.Artifacts;
using VisitLab.Cli.Models;
using VisitLab.Cli.Scenarios;

namespace VisitLab.Cli.Infrastructure;

public interface IDelay
{
    Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default);
}

public interface IFileSystem
{
    void CreateDirectory(string path);
    Task WriteAllTextAsync(string path, string text, Encoding encoding, CancellationToken cancellationToken = default);
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);
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
    Task<AdbLogStreamResult> MonitorLogAsync(string containsText, DateTimeOffset since, int timeoutSec, CancellationToken cancellationToken = default);
    Task<AdbLogStreamResult> MonitorLogAsync(DateTimeOffset since, int timeoutSec, Func<string, bool>? stopWhen = null, Action<string>? observeLine = null, CancellationToken cancellationToken = default);
}

public interface IAdbClientFactory
{
    IAdbClient Create(string executable, string? serial, IProcessRunner processRunner);
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
    /// Checks whether the target device and app are ready.
    /// </summary>
    /// <param name="packageName">Optional foreground package to require.</param>
    /// <returns>Preflight data.</returns>
    Task<PreflightResult> PreflightAsync(string? packageName);

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
}

/// <summary>
/// Parameters used to create a device host implementation.
/// </summary>
/// <param name="Platform">Target host platform name.</param>
/// <param name="Executable">Transport executable path.</param>
/// <param name="DeviceSerial">Optional device identifier.</param>
public sealed record DeviceHostConfiguration(string Platform, string Executable, string? DeviceSerial);

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

public sealed record AdbCommandResult(string Executable, string? Serial, IReadOnlyList<string> Args, ProcessResult Process)
{
    private int ExitCode => Process.ExitCode;

    public string Stdout => Process.Stdout;

    private string Stderr => Process.Stderr;

    public string Invocation => string.Join(" ", [Executable, .. Args.Select(QuoteArgument)]);

    public void EnsureSuccess(string message)
    {
        if (ExitCode == 0) return;
        var detail = string.IsNullOrWhiteSpace(Stderr) ? Stdout : Stderr;
        throw new InvalidOperationException($"{message}: `{Invocation}` exited {ExitCode}. {detail}".Trim());
    }

    private static string QuoteArgument(string value) =>
        value.Any(static ch => char.IsWhiteSpace(ch) || ch == '"') ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"" : value;
}

public sealed record AdbLogStreamResult(string ContainsText, string LogOutput, string? MatchedLine, int LineCount, int TimeoutSec, DateTimeOffset Since, string Invocation, int ExitCode, string Stderr);