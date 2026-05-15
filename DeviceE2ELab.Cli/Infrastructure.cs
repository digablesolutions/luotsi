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

public interface IConsoleIO
{
    void WriteLine(string value);
    void WriteErrorLine(string value);
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

public sealed record AdbLogStreamResult(string ContainsText, string LogOutput, string? MatchedLine, int LineCount, int TimeoutSec, DateTimeOffset Since, string Invocation, string Stderr);

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

public sealed class SystemConsoleIO : IConsoleIO
{
    public void WriteLine(string value) => Console.Out.WriteLine(value);

    public void WriteErrorLine(string value) => Console.Error.WriteLine(value);
}

public sealed class SystemEnvironmentVariables : IEnvironmentVariables
{
    public string? GetEnvironmentVariable(string variable) => Environment.GetEnvironmentVariable(variable);
}

public sealed class GuidUniqueIdGenerator : IUniqueIdGenerator
{
    public string NewId() => Guid.NewGuid().ToString("N");
}