using System.Diagnostics;
using System.Text;

namespace VisitLab.Cli;

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
        RunAsync(new[] { "shell", command }, cancellationToken);

    public async Task<AdbLogStreamResult> MonitorLogAsync(string containsText, DateTimeOffset since, int timeoutSec, CancellationToken cancellationToken = default)
    {
        var finalArgs = BuildFinalArgs(["logcat", "-v", "brief", "-T", LogcatTime.FormatSince(since), "*:V"]);
        var startInfo = new ProcessStartInfo(_executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in finalArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start '{_executable}'.");
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var logBuilder = new StringBuilder();
        var lineCount = 0;
        string? matchedLine = null;
        var matchTask = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readerTask = Task.Run(async () =>
        {
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                line = line.TrimEnd('\r');
                logBuilder.AppendLine(line);
                lineCount++;
                if (matchedLine is null && line.Contains(containsText, StringComparison.OrdinalIgnoreCase))
                {
                    matchedLine = line;
                    matchTask.TrySetResult(line);
                }
            }
        }, cancellationToken);

        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(Math.Max(1, timeoutSec)), cancellationToken);
        await Task.WhenAny(matchTask.Task, readerTask, timeoutTask).ConfigureAwait(false);

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        try
        {
            await readerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }

        return new AdbLogStreamResult(
            containsText,
            logBuilder.ToString(),
            matchedLine,
            lineCount,
            timeoutSec,
            since,
            string.Join(" ", [_executable, .. finalArgs]),
            process.ExitCode,
            await stderrTask.ConfigureAwait(false));
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
}