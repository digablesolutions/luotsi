using System.Diagnostics;
using System.Globalization;
using System.Text;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Telemetry;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Hosts.Android;

/// <summary>
/// Executes ADB commands with stdout and stderr captured separately.
/// </summary>
public sealed class AdbClient(string executable, string? serial, IProcessRunner processRunner, TimeSpan? commandTimeout = null) : IAdbClient
{
    private const int MaxCapturedLogChars = 512 * 1024;
    private const int MaxCapturedStreamChars = 512 * 1024;

    private readonly string _executable = string.IsNullOrWhiteSpace(executable) ? throw new ArgumentException("ADB executable is required.", nameof(executable)) : executable;
    private readonly string? _serial = string.IsNullOrWhiteSpace(serial) ? null : serial;
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    private readonly TimeSpan? _commandTimeout = commandTimeout is { } timeout && timeout > TimeSpan.Zero ? timeout : null;

    /// <summary>
    /// Runs adb and captures the result.
    /// </summary>
    /// <param name="args">ADB arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Process result.</returns>
    public async Task<AdbCommandResult> RunAsync(IEnumerable<string> args, CancellationToken cancellationToken = default)
    {
        var requestedArgs = args.ToArray();
        var finalArgs = BuildFinalArgs(requestedArgs);
        var result = await RunProcessAsync(finalArgs, cancellationToken).ConfigureAwait(false);
        var retryReason = FindTransientTransportReason(result);
        if (retryReason is null || !IsSafeToRetry(requestedArgs))
        {
            return new AdbCommandResult(_executable, _serial, finalArgs, result);
        }

        var recoveryActions = new List<AdbRecoveryActionResult>();
        await RunRecoveryActionAsync(["start-server"], recoveryActions, cancellationToken).ConfigureAwait(false);

        if (ShouldReconnectOffline(result))
        {
            await RunRecoveryActionAsync(BuildFinalArgs(["reconnect", "offline"]), recoveryActions, cancellationToken).ConfigureAwait(false);
        }

        if (ShouldWaitForDevice(retryReason, requestedArgs))
        {
            await RunRecoveryActionAsync(BuildFinalArgs(["wait-for-device"]), recoveryActions, cancellationToken).ConfigureAwait(false);
        }

        var retryResult = await RunProcessAsync(finalArgs, cancellationToken).ConfigureAwait(false);
        return new AdbCommandResult(
            _executable,
            _serial,
            finalArgs,
            retryResult,
            new AdbRetryInfo(retryReason, 2, recoveryActions));
    }

    /// <summary>
    /// Runs an adb shell command.
    /// </summary>
    /// <param name="command">Shell command text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Process result.</returns>
    public Task<AdbCommandResult> ShellAsync(string command, CancellationToken cancellationToken = default) =>
        RunAsync(["shell", command], cancellationToken);

    public Task<IAsyncDisposable> StartShellAsync(string command, CancellationToken cancellationToken = default)
    {
        var finalArgs = BuildFinalArgs(["shell", command]);
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

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start '{_executable}'.");
        return Task.FromResult<IAsyncDisposable>(new AdbShellProcess(
            process,
            ReadBoundedToEndAsync(process.StandardOutput, MaxCapturedStreamChars, cancellationToken),
            ReadBoundedToEndAsync(process.StandardError, MaxCapturedStreamChars, cancellationToken)));
    }

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
        var stderrTask = ReadBoundedToEndAsync(process.StandardError, MaxCapturedStreamChars, cancellationToken);
        var matchSignal = stopWhen is null ? null : new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stdout = process.StandardOutput;
        var readerTask = ReadLogOutputAsync(stdout, stopWhen, observeLine, matchSignal, MaxCapturedLogChars, cancellationToken);

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
        int maxCapturedChars,
        CancellationToken cancellationToken)
    {
        var retainedLines = new Queue<string>();
        var retainedCharCount = 0;
        var truncatedLineCount = 0;
        var lineCount = 0;
        string? matchedLine = null;

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } rawLine)
        {
            var line = rawLine.TrimEnd('\r');
            retainedLines.Enqueue(line);
            retainedCharCount += line.Length + 1;
            while (retainedCharCount > maxCapturedChars && retainedLines.Count > 0)
            {
                retainedCharCount -= retainedLines.Dequeue().Length + 1;
                truncatedLineCount++;
            }

            lineCount++;
            observeLine?.Invoke(line);
            if (matchedLine is null && stopWhen?.Invoke(line) is true)
            {
                matchedLine = line;
                matchSignal?.TrySetResult(line);
            }
        }

        var output = BuildRetainedLogOutput(retainedLines, retainedCharCount, truncatedLineCount);
        return new LogReaderResult(output, matchedLine, lineCount);
    }

    private static string BuildRetainedLogOutput(IEnumerable<string> retainedLines, int retainedCharCount, int truncatedLineCount)
    {
        var capacity = Math.Max(64, retainedCharCount + 64);
        var builder = new StringBuilder(capacity);
        if (truncatedLineCount > 0)
        {
            builder.AppendLine($"[truncated {truncatedLineCount} lines]");
        }

        foreach (var line in retainedLines)
        {
            builder.AppendLine(line);
        }

        return builder.ToString();
    }

    private static async Task<string> ReadBoundedToEndAsync(StreamReader reader, int maxChars, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var builder = new StringBuilder(Math.Min(maxChars, 4096));
        long truncatedChars = 0;
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (builder.Length < maxChars)
            {
                var copyCount = Math.Min(read, maxChars - builder.Length);
                builder.Append(buffer, 0, copyCount);
                truncatedChars += read - copyCount;
                continue;
            }

            truncatedChars += read;
        }

        if (truncatedChars == 0)
        {
            return builder.ToString();
        }

        builder.AppendLine();
        builder.Append($"[truncated {truncatedChars} chars]");
        return builder.ToString();
    }

    private sealed class AdbShellProcess(Process process, Task<string> stdoutTask, Task<string> stderrTask) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            await IgnoreDrainFailureAsync(stdoutTask).ConfigureAwait(false);
            await IgnoreDrainFailureAsync(stderrTask).ConfigureAwait(false);
            process.Dispose();
        }

        private static async Task IgnoreDrainFailureAsync(Task drainTask)
        {
            try
            {
                await drainTask.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or InvalidOperationException)
            {
                // The process may close or dispose redirected streams while the teardown drain is completing.
            }
        }
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

    private async Task<ProcessResult> RunProcessAsync(IReadOnlyList<string> finalArgs, CancellationToken cancellationToken)
    {
        var timeout = GetTimeoutFor(finalArgs);
        if (timeout is null)
        {
            return await _processRunner.RunAsync(_executable, finalArgs, cancellationToken).ConfigureAwait(false);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout.Value);

        try
        {
            return await _processRunner.RunAsync(_executable, finalArgs, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            throw new TimeoutException($"adb command timed out after {FormatTimeout(timeout.Value)}s: {FormatInvocation(finalArgs)}");
        }
    }

    private async Task RunRecoveryActionAsync(IReadOnlyList<string> finalArgs, List<AdbRecoveryActionResult> recoveryActions, CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunProcessAsync(finalArgs, cancellationToken).ConfigureAwait(false);
            recoveryActions.Add(new AdbRecoveryActionResult(FormatInvocation(finalArgs), result.ExitCode, result.Stdout, result.Stderr));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or OperationCanceledException)
        {
            recoveryActions.Add(new AdbRecoveryActionResult(FormatInvocation(finalArgs), -1, string.Empty, ex.Message));
        }
    }

    private TimeSpan? GetTimeoutFor(IReadOnlyList<string> finalArgs)
    {
        if (_commandTimeout is null)
        {
            return null;
        }

        if (TryGetScreenrecordTimeLimit(finalArgs, out var timeLimitSec))
        {
            return TimeSpan.FromSeconds(Math.Max(_commandTimeout.Value.TotalSeconds, timeLimitSec + 15));
        }

        return _commandTimeout;
    }

    private static bool TryGetScreenrecordTimeLimit(IReadOnlyList<string> args, out int timeLimitSec)
    {
        timeLimitSec = 0;
        var shellIndex = IndexOfArg(args, "shell");
        if (shellIndex < 0 || shellIndex + 1 >= args.Count)
        {
            return false;
        }

        var command = args[shellIndex + 1].Trim();
        if (!command.StartsWith("screenrecord ", StringComparison.Ordinal))
        {
            return false;
        }

        var tokens = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < tokens.Length - 1; index++)
        {
            if (string.Equals(tokens[index], "--time-limit", StringComparison.Ordinal) &&
                int.TryParse(tokens[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
                parsed > 0)
            {
                timeLimitSec = parsed;
                return true;
            }
        }

        return false;
    }

    private static int IndexOfArg(IReadOnlyList<string> args, string value)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (string.Equals(args[index], value, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static string? FindTransientTransportReason(ProcessResult result)
    {
        if (result.ExitCode == 0)
        {
            return null;
        }

        var output = $"{result.Stderr}\n{result.Stdout}";
        if (output.Contains("protocol fault", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("no status", StringComparison.OrdinalIgnoreCase))
        {
            return "adb protocol fault";
        }

        if (output.Contains("device still connecting", StringComparison.OrdinalIgnoreCase))
        {
            return "adb device still connecting";
        }

        if (output.Contains("device offline", StringComparison.OrdinalIgnoreCase))
        {
            return "adb device offline";
        }

        if (output.Contains("device not found", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("no devices/emulators found", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("no devices found", StringComparison.OrdinalIgnoreCase))
        {
            return "adb device not found";
        }

        if (output.Contains("transport is not ready", StringComparison.OrdinalIgnoreCase))
        {
            return "adb transport not ready";
        }

        return null;
    }

    private static bool ShouldReconnectOffline(ProcessResult result)
    {
        var output = $"{result.Stderr}\n{result.Stdout}";
        return output.Contains("device offline", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("device still connecting", StringComparison.OrdinalIgnoreCase) ||
               output.Contains("transport is not ready", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldWaitForDevice(string retryReason, IReadOnlyList<string> args)
    {
        if (IsWaitForDeviceCommand(args))
        {
            return false;
        }

        return retryReason is "adb device still connecting"
            or "adb device offline"
            or "adb device not found"
            or "adb transport not ready";
    }

    private static bool IsSafeToRetry(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return false;
        }

        return args[0] switch
        {
            "devices" or "features" or "server-status" or "version" or "wait-for-device" => true,
            "mdns" => true,
            "reconnect" => true,
            "exec-out" => args.Count >= 3 &&
                string.Equals(args[1], "uiautomator", StringComparison.Ordinal) &&
                string.Equals(args[2], "dump", StringComparison.Ordinal),
            "logcat" => args.Contains("-d", StringComparer.Ordinal) && !args.Contains("-c", StringComparer.Ordinal),
            "shell" => args.Count >= 2 && IsSafeShellCommand(args[1]),
            _ => false
        };
    }

    private static bool IsSafeShellCommand(string command)
    {
        var segments = command.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length > 0 && segments.All(IsSafeShellSegment);
    }

    private static bool IsSafeShellSegment(string command)
    {
        var trimmed = command.Trim();
        return string.Equals(trimmed, "echo ping", StringComparison.Ordinal) ||
               IsReadOnlyMarkerEcho(trimmed) ||
               trimmed.StartsWith("getprop ", StringComparison.Ordinal) ||
               trimmed.StartsWith("dumpsys ", StringComparison.Ordinal) ||
               string.Equals(trimmed, "wm size", StringComparison.Ordinal) ||
               trimmed.StartsWith("ip route get ", StringComparison.Ordinal);
    }

    private static bool IsReadOnlyMarkerEcho(string command) =>
        command.StartsWith("echo __LUOTSI_DEVICE_FINGERPRINT_", StringComparison.Ordinal) &&
        !command.Contains('<', StringComparison.Ordinal) &&
        !command.Contains('>', StringComparison.Ordinal) &&
        !command.Contains('|', StringComparison.Ordinal) &&
        !command.Contains('&', StringComparison.Ordinal);

    private static bool IsWaitForDeviceCommand(IReadOnlyList<string> args) =>
        args.Count > 0 && string.Equals(args[0], "wait-for-device", StringComparison.Ordinal);

    private string FormatInvocation(IReadOnlyList<string> args) =>
        string.Join(" ", [_executable, .. args.Select(QuoteArgument)]);

    private static string QuoteArgument(string value) =>
        value.Any(static ch => char.IsWhiteSpace(ch) || ch == '"') ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"" : value;

    private static string FormatTimeout(TimeSpan timeout) =>
        timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed record LogReaderResult(string LogOutput, string? MatchedLine, int LineCount);
}
