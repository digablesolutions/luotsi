using System.Diagnostics;
using System.Text;
using Luotsi.Cli.Infrastructure;

namespace Luotsi.Cli.Hosts.Android;

/// <summary>
/// Executes ADB commands with stdout and stderr captured separately.
/// </summary>
public sealed class AdbClient(string executable, string? serial, IProcessRunner processRunner) : IAdbClient
{
    private readonly string _executable = string.IsNullOrWhiteSpace(executable) ? throw new ArgumentException("ADB executable is required.", nameof(executable)) : executable;
    private readonly string? _serial = string.IsNullOrWhiteSpace(serial) ? null : serial;
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));

    /// <summary>
    /// Runs adb and captures the result.
    /// </summary>
    /// <param name="args">ADB arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Process result.</returns>
    public async Task<AdbCommandResult> RunAsync(IEnumerable<string> args, CancellationToken cancellationToken = default)
    {
        var finalArgs = BuildFinalArgs(args);
        var result = await _processRunner.RunAsync(_executable, finalArgs, cancellationToken).ConfigureAwait(false);
        return new AdbCommandResult(_executable, _serial, finalArgs, result);
    }

    /// <summary>
    /// Runs an adb shell command.
    /// </summary>
    /// <param name="command">Shell command text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Process result.</returns>
    public Task<AdbCommandResult> ShellAsync(string command, CancellationToken cancellationToken = default) =>
        RunAsync(["shell", command], cancellationToken);

    public Task<AdbLogStreamResult> MonitorLogAsync(string containsText, DateTimeOffset since, int timeoutSec, CancellationToken cancellationToken = default) =>
        MonitorLogAsyncCore(containsText, since, timeoutSec, line => line.Contains(containsText, StringComparison.OrdinalIgnoreCase), null, cancellationToken);

    public Task<AdbLogStreamResult> MonitorLogAsync(DateTimeOffset since, int timeoutSec, Func<string, bool>? stopWhen = null, Action<string>? observeLine = null, CancellationToken cancellationToken = default) =>
        MonitorLogAsyncCore(string.Empty, since, timeoutSec, stopWhen, observeLine, cancellationToken);

    private async Task<AdbLogStreamResult> MonitorLogAsyncCore(string containsText, DateTimeOffset since, int timeoutSec, Func<string, bool>? stopWhen, Action<string>? observeLine, CancellationToken cancellationToken)
    {
        var finalArgs = BuildFinalArgs(["logcat", "-v", "brief", "-T", LogcatTime.FormatSince(since), "*:V"]);
        var startInfo = new ProcessStartInfo(_executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in finalArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start '{_executable}'.");
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var matchSignal = stopWhen is null ? null : new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stdout = process.StandardOutput;
        var readerTask = Task.Run(() => ReadLogOutputAsync(stdout, stopWhen, observeLine, matchSignal, cancellationToken), cancellationToken);

        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(Math.Max(1, timeoutSec)), cancellationToken);
        Task completedTask;
        if (matchSignal is null)
        {
            completedTask = await Task.WhenAny(readerTask, timeoutTask).ConfigureAwait(false);
        }
        else
        {
            completedTask = await Task.WhenAny(matchSignal.Task, readerTask, timeoutTask).ConfigureAwait(false);
        }

        var terminatedByMonitor = false;
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            terminatedByMonitor = completedTask != readerTask;
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        LogReaderResult readerResult;
        try
        {
            readerResult = await readerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            readerResult = new LogReaderResult(string.Empty, null, 0);
        }

        var stderr = await stderrTask.ConfigureAwait(false);
        return new AdbLogStreamResult(
            containsText,
            readerResult.LogOutput,
            readerResult.MatchedLine,
            readerResult.LineCount,
            timeoutSec,
            since,
            string.Join(" ", [_executable, .. finalArgs]),
            terminatedByMonitor ? 0 : process.ExitCode,
            terminatedByMonitor && string.IsNullOrWhiteSpace(stderr) ? string.Empty : stderr);
    }

    private static async Task<LogReaderResult> ReadLogOutputAsync(
        StreamReader reader,
        Func<string, bool>? stopWhen,
        Action<string>? observeLine,
        TaskCompletionSource<string?>? matchSignal,
        CancellationToken cancellationToken)
    {
        var logBuilder = new StringBuilder();
        var lineCount = 0;
        string? matchedLine = null;

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } rawLine)
        {
            var line = rawLine.TrimEnd('\r');
            logBuilder.AppendLine(line);
            lineCount++;
            observeLine?.Invoke(line);
            if (matchedLine is null && stopWhen?.Invoke(line) is true)
            {
                matchedLine = line;
                matchSignal?.TrySetResult(line);
            }
        }

        return new LogReaderResult(logBuilder.ToString(), matchedLine, lineCount);
    }

    private List<string> BuildFinalArgs(IEnumerable<string> args)
    {
        var finalArgs = new List<string>();
        if (_serial is not null)
        {
            finalArgs.Add("-s");
            finalArgs.Add(_serial);
        }

        finalArgs.AddRange(args);
        return finalArgs;
    }

    private sealed record LogReaderResult(string LogOutput, string? MatchedLine, int LineCount);
}