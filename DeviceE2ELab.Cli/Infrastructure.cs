using System.Diagnostics;
using System.Text;

namespace DeviceE2ELab.Cli;

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
    Task<object> GetDevicesAsync();

    /// <summary>
    /// Checks whether the target device and app are ready.
    /// </summary>
    /// <param name="packageName">Optional foreground package to require.</param>
    /// <returns>Preflight data.</returns>
    Task<object> PreflightAsync(string? packageName);

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
    Task<object> TapAsync(string x, string y);

    /// <summary>
    /// Reads logcat lines from the active device.
    /// </summary>
    /// <param name="tail">Maximum lines to return.</param>
    /// <returns>Logcat data.</returns>
    Task<object> LogcatAsync(int tail);

    /// <summary>
    /// Reads and parses recent semantic telemetry events.
    /// </summary>
    /// <param name="tail">Maximum logcat lines to inspect.</param>
    /// <returns>Telemetry data.</returns>
    Task<object> TelemetryTailAsync(int tail);

    /// <summary>
    /// Collects semantic telemetry events over a bounded watch window.
    /// </summary>
    /// <param name="timeoutSec">Duration to watch for telemetry events.</param>
    /// <returns>Telemetry data.</returns>
    Task<object> TelemetryWatchAsync(int timeoutSec);

    /// <summary>
    /// Records the device screen to a local file.
    /// </summary>
    /// <param name="output">Local output path.</param>
    /// <param name="timeLimitSec">Maximum recording duration.</param>
    /// <returns>Recording metadata.</returns>
    Task<object> RecordAsync(string output, int timeLimitSec);
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

public interface IConsoleIO
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
    public int ExitCode => Process.ExitCode;

    public string Stdout => Process.Stdout;

    public string Stderr => Process.Stderr;

    public string Invocation => string.Join(" ", [Executable, .. Args.Select(QuoteArgument)]);

    public void EnsureSuccess(string message)
    {
        if (ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(Stderr) ? Stdout : Stderr;
            throw new InvalidOperationException($"{message}: `{Invocation}` exited {ExitCode}. {detail}".Trim());
        }
    }

    private static string QuoteArgument(string value) =>
        value.Any(static ch => char.IsWhiteSpace(ch) || ch == '"') ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"" : value;
}

public sealed record AdbLogStreamResult(string ContainsText, string LogOutput, string? MatchedLine, int LineCount, int TimeoutSec, DateTimeOffset Since, string Invocation, int ExitCode, string Stderr);

public sealed class TaskDelay(TimeProvider? timeProvider = null) : IDelay
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default) =>
        Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, milliseconds)), _timeProvider, cancellationToken);
}

public sealed class PhysicalFileSystem : IFileSystem
{
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public Task WriteAllTextAsync(string path, string text, Encoding encoding, CancellationToken cancellationToken = default) =>
        File.WriteAllTextAsync(path, text, encoding, cancellationToken);

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) =>
        File.ReadAllTextAsync(path, cancellationToken);

    public bool FileExists(string path) => File.Exists(path);

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite) =>
        File.Copy(sourcePath, destinationPath, overwrite);

    public string GetTempPath() => Path.GetTempPath();
}

public sealed class DefaultProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> args, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }
}

public sealed class DefaultAdbClientFactory : IAdbClientFactory
{
    public IAdbClient Create(string executable, string? serial, IProcessRunner processRunner) =>
        new AdbClient(executable, serial, processRunner);
}

/// <summary>
/// Default factory that currently supports Android hosts backed by ADB.
/// </summary>
public sealed class DefaultDeviceHostFactory(
    IAdbClientFactory adbClientFactory,
    IProcessRunner processRunner,
    IDelay delay,
    IFileSystem fileSystem,
    TimeProvider timeProvider,
    IEnvironmentVariables environment,
    IUniqueIdGenerator idGenerator) : IDeviceHostFactory
{
    private readonly IAdbClientFactory _adbClientFactory = adbClientFactory ?? throw new ArgumentNullException(nameof(adbClientFactory));
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    private readonly IDelay _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly IUniqueIdGenerator _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));

    /// <summary>
    /// Creates a concrete device host.
    /// </summary>
    /// <param name="configuration">Host creation parameters.</param>
    /// <param name="artifacts">Artifact session for the command.</param>
    /// <returns>Concrete device host.</returns>
    public IDeviceHost Create(DeviceHostConfiguration configuration, ArtifactSession artifacts)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(artifacts);

        if (!string.Equals(configuration.Platform, "android", StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageException($"Unsupported platform '{configuration.Platform}'. The current build only supports --platform android.");
        }

        var adb = _adbClientFactory.Create(configuration.Executable, configuration.DeviceSerial, _processRunner);
        return new DeviceRunner(adb, artifacts, _timeProvider, _delay, _fileSystem, _idGenerator, _environment);
    }
}

public sealed class SystemConsoleIO : IConsoleIO
{
    public void WriteLine(string value) => Console.Out.WriteLine(value);

    public void WriteErrorLine(string value) => Console.Error.WriteLine(value);

    public string? ReadLine() => Console.In.ReadLine();
}

public sealed class SystemEnvironmentVariables : IEnvironmentVariables
{
    public string? GetEnvironmentVariable(string variable) => Environment.GetEnvironmentVariable(variable);
}

public sealed class GuidUniqueIdGenerator : IUniqueIdGenerator
{
    public string NewId() => Guid.NewGuid().ToString("N");
}