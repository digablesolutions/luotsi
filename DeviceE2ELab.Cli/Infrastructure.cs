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
    Task<ProcessResult> RunAsync(IEnumerable<string> args, CancellationToken cancellationToken = default);
    Task<ProcessResult> ShellAsync(string command, CancellationToken cancellationToken = default);
}

public interface IAdbClientFactory
{
    IAdbClient Create(string executable, string? serial, IProcessRunner processRunner);
}

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