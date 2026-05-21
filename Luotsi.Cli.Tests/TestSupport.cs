using System.IO.Enumeration;
using System.Text.Json;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Cli.View;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Infrastructure.Time;
using Luotsi.Cli.Models;
using Luotsi.Cli.Telemetry;
using Luotsi.Cli.View.Contracts;
using Luotsi.Cli.View.Diagnostics;
using Luotsi.Cli.View.Session;
using Luotsi.Cli.View.Transport;
using Xunit;

namespace Luotsi.Cli.Tests;

internal sealed class FakeAsyncDisposable : IAsyncDisposable
{
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
}

internal sealed class SteppingTimeProvider(DateTimeOffset utcNow, TimeSpan step) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;
    private readonly TimeSpan _step = step;

    public override DateTimeOffset GetUtcNow()
    {
        var current = _utcNow;
        _utcNow = _utcNow.Add(_step);
        return current;
    }

    public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
}

internal sealed class FakeDelay(ManualTimeProvider timeProvider) : IDelay
{
    public List<int> Calls { get; } = [];

    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default)
    {
        Calls.Add(milliseconds);
        DelayMetrics.RecordDelay(milliseconds);
        timeProvider.Advance(TimeSpan.FromMilliseconds(milliseconds));
        return Task.CompletedTask;
    }
}

internal sealed class SteppingDelay(SteppingTimeProvider timeProvider) : IDelay
{
    public List<int> Calls { get; } = [];

    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default)
    {
        Calls.Add(milliseconds);
        DelayMetrics.RecordDelay(milliseconds);
        timeProvider.Advance(TimeSpan.FromMilliseconds(milliseconds));
        return Task.CompletedTask;
    }
}

internal sealed class FakeConsole : IConsoleIo
{
    public List<string> OutputLines { get; } = [];

    public List<string> ErrorLines { get; } = [];

    private readonly Queue<string?> _inputLines = new();

    public void WriteLine(string value) => OutputLines.Add(value);

    public void WriteErrorLine(string value) => ErrorLines.Add(value);

    public string? ReadLine() => _inputLines.Count > 0 ? _inputLines.Dequeue() : null;

    public void EnqueueInput(params string[] lines)
    {
        foreach (var line in lines)
        {
            _inputLines.Enqueue(line);
        }
    }

    public JsonDocument ParseSingleOutputAsJson()
    {
        Assert.Single(OutputLines);
        return JsonDocument.Parse(OutputLines[0]);
    }
}

internal sealed class FakeUniqueIdGenerator(string value) : IUniqueIdGenerator
{
    public string NewId() => value;
}

internal sealed class FakeFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _binaryFiles = new(StringComparer.Ordinal);
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal);

    public List<string> DeletedFiles { get; } = [];

    public void AddFile(string path, string content)
    {
        CreateDirectory(Path.GetDirectoryName(path) ?? "/");
        _files[path] = content;
        _binaryFiles.Remove(path);
    }

    public void CreateDirectory(string path) => _directories.Add(NormalizeDirectory(path));

    public bool DirectoryExists(string path) => _directories.Contains(NormalizeDirectory(path));

    public IReadOnlyList<string> GetFiles(string path, string searchPattern, SearchOption searchOption)
    {
        var root = NormalizeDirectory(path);
        return _files.Keys.Concat(_binaryFiles.Keys)
            .Where(file => IsUnderDirectory(file, root, searchOption))
            .Where(file => FileSystemName.MatchesSimpleExpression(searchPattern, Path.GetFileName(file), ignoreCase: true))
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task WriteAllTextAsync(string path, string text, System.Text.Encoding encoding, CancellationToken cancellationToken = default)
    {
        AddFile(path, text);
        return Task.CompletedTask;
    }

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(_files.TryGetValue(path, out var text) ? text : System.Text.Encoding.UTF8.GetString(_binaryFiles[path]));

    public Stream OpenRead(string path)
    {
        if (_files.TryGetValue(path, out var text))
        {
            return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text), writable: false);
        }

        if (_binaryFiles.TryGetValue(path, out var binary))
        {
            return new MemoryStream(binary, writable: false);
        }

        throw new FileNotFoundException($"File '{path}' was not found.", path);
    }

    public Stream OpenWrite(string path, bool overwrite = true)
    {
        if (!overwrite && (_files.ContainsKey(path) || _binaryFiles.ContainsKey(path)))
        {
            throw new IOException($"Destination file '{path}' exists.");
        }

        CreateDirectory(Path.GetDirectoryName(path) ?? "/");
        return new FakeWriteStream(this, path);
    }

    public void DeleteFile(string path)
    {
        DeletedFiles.Add(path);
        _files.Remove(path);
        _binaryFiles.Remove(path);
    }

    public bool FileExists(string path) => _files.ContainsKey(path) || _binaryFiles.ContainsKey(path);

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite)
    {
        if (!overwrite && (_files.ContainsKey(destinationPath) || _binaryFiles.ContainsKey(destinationPath)))
        {
            throw new IOException($"Destination file '{destinationPath}' exists.");
        }

        if (_files.TryGetValue(sourcePath, out var text))
        {
            AddFile(destinationPath, text);
            return;
        }

        WriteBinaryFile(destinationPath, _binaryFiles[sourcePath]);
    }

    public string GetTempPath() => "/tmp";

    public byte[] ReadBytes(string path) => _binaryFiles[path];

    private static bool IsUnderDirectory(string file, string root, SearchOption searchOption)
    {
        var directory = NormalizeDirectory(Path.GetDirectoryName(file));
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        if (searchOption == SearchOption.TopDirectoryOnly)
        {
            return string.Equals(directory, root, StringComparison.Ordinal);
        }

        return string.Equals(directory, root, StringComparison.Ordinal) ||
            directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            directory.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string NormalizeDirectory(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private void WriteBinaryFile(string path, byte[] content)
    {
        CreateDirectory(Path.GetDirectoryName(path) ?? "/");
        _binaryFiles[path] = content;
        _files.Remove(path);
    }

    private sealed class FakeWriteStream(FakeFileSystem fileSystem, string path) : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                fileSystem.WriteBinaryFile(path, ToArray());
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            fileSystem.WriteBinaryFile(path, ToArray());
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}

internal sealed class FakeAdbClient(string? serial = null) : IAdbClient
{
    private readonly Queue<ProcessResult> _shellResults = new();
    private readonly Queue<ProcessResult> _runResults = new();
    private readonly Queue<string[]> _logLines = new();
    private readonly Queue<AdbLogStreamResult> _logResults = new();
    private readonly Dictionary<string, byte[]> _remoteFiles = new(StringComparer.Ordinal);
    private IFileSystem? _fileSystem;

    public List<string> ShellCommands { get; } = [];

    public List<string[]> RunCommands { get; } = [];

    public List<(string ContainsText, DateTimeOffset Since, int TimeoutSec)> LogRequests { get; } = [];

    public List<(DateTimeOffset Since, int TimeoutSec, bool HasStopCondition, bool HasLineObserver)> StreamingLogRequests { get; } = [];

    public void EnqueueShellResult(ProcessResult result) => _shellResults.Enqueue(result);

    public void EnqueueRunResult(ProcessResult result) => _runResults.Enqueue(result);

    public void AttachFileSystem(IFileSystem fileSystem) => _fileSystem = fileSystem;

    public void AddRemoteFile(string path, byte[] content) => _remoteFiles[path] = content;

    public void EnqueueLogLines(params string[] lines) => _logLines.Enqueue(lines);

    public void EnqueueLogResult(AdbLogStreamResult result) => _logResults.Enqueue(result);

    public Task<AdbCommandResult> RunAsync(IEnumerable<string> args, CancellationToken cancellationToken = default)
    {
        var finalArgs = args.ToArray();
        RunCommands.Add(finalArgs);
        if (TryHandlePull(finalArgs))
        {
            return Task.FromResult(new AdbCommandResult("adb", serial, finalArgs, new ProcessResult(0, string.Empty, string.Empty)));
        }

                var result = _runResults.Count > 0
                        ? _runResults.Dequeue()
                        : finalArgs.Length == 4 &&
                            string.Equals(finalArgs[0], "exec-out", StringComparison.Ordinal) &&
                            string.Equals(finalArgs[1], "uiautomator", StringComparison.Ordinal) &&
                            string.Equals(finalArgs[2], "dump", StringComparison.Ordinal) &&
                            string.Equals(finalArgs[3], "/dev/tty", StringComparison.Ordinal) &&
                            _shellResults.Count > 0
                                ? _shellResults.Dequeue()
                                : new ProcessResult(0, string.Empty, string.Empty);
        return Task.FromResult(new AdbCommandResult("adb", serial, finalArgs, result));
    }

    private bool TryHandlePull(string[] finalArgs)
    {
        if (finalArgs.Length != 3 ||
            !string.Equals(finalArgs[0], "pull", StringComparison.Ordinal) ||
            !_remoteFiles.TryGetValue(finalArgs[1], out var content))
        {
            return false;
        }

        if (_fileSystem is null)
        {
            return true;
        }

        using var stream = _fileSystem.OpenWrite(finalArgs[2]);
        stream.Write(content);
        return true;
    }

    public Task<AdbCommandResult> ShellAsync(string command, CancellationToken cancellationToken = default)
    {
        ShellCommands.Add(command);
        var result = _shellResults.Count > 0 ? _shellResults.Dequeue() : new ProcessResult(0, string.Empty, string.Empty);
        return Task.FromResult(new AdbCommandResult("adb", serial, ["shell", command], result));
    }

    public Task<IAsyncDisposable> StartShellAsync(string command, CancellationToken cancellationToken = default)
    {
        ShellCommands.Add(command);
        if (_shellResults.Count > 0)
        {
            var result = _shellResults.Dequeue();
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"view helper start failed: {result.Stderr}");
            }
        }

        return Task.FromResult<IAsyncDisposable>(new FakeAsyncDisposable());
    }

    public Task<AdbLogStreamResult> MonitorLogAsync(string containsText, DateTimeOffset since, int timeoutSec, CancellationToken cancellationToken = default)
    {
        LogRequests.Add((containsText, since, timeoutSec));
        if (_logResults.Count > 0)
        {
            return Task.FromResult(_logResults.Dequeue());
        }

        var lines = _logLines.Count > 0 ? _logLines.Dequeue() : [];
        var logOutput = string.Join(Environment.NewLine, lines);
        if (lines.Length > 0)
        {
            logOutput += Environment.NewLine;
        }

        var matchedLine = lines.FirstOrDefault(line => line.Contains(containsText, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(new AdbLogStreamResult(containsText, logOutput, matchedLine, lines.Length, timeoutSec, since, "adb logcat", 0, string.Empty));
    }

    public Task<AdbLogStreamResult> MonitorLogAsync(DateTimeOffset since, int timeoutSec, Func<string, bool>? stopWhen = null, Action<string>? observeLine = null, CancellationToken cancellationToken = default)
    {
        StreamingLogRequests.Add((since, timeoutSec, stopWhen is not null, observeLine is not null));
        if (_logResults.Count > 0)
        {
            return Task.FromResult(_logResults.Dequeue());
        }

        var lines = _logLines.Count > 0 ? _logLines.Dequeue() : [];
        var outputLines = new List<string>();
        string? matchedLine = null;

        foreach (var line in lines)
        {
            outputLines.Add(line);
            observeLine?.Invoke(line);
            if (matchedLine is null && stopWhen?.Invoke(line) is true)
            {
                matchedLine = line;
                break;
            }
        }

        var logOutput = string.Join(Environment.NewLine, outputLines);
        if (outputLines.Count > 0)
        {
            logOutput += Environment.NewLine;
        }

        return Task.FromResult(new AdbLogStreamResult(string.Empty, logOutput, matchedLine, outputLines.Count, timeoutSec, since, "adb logcat", 0, string.Empty));
    }
}

internal sealed class CountingTelemetryParser : ITelemetryParser
{
    private readonly LuotsiDeviceTelemetryParser _inner = new();

    public int ParseLogCallCount { get; private set; }

    public int ParseLineCallCount { get; private set; }

    public TelemetryParseResult ParseLog(string logOutput)
    {
        ParseLogCallCount++;
        return _inner.ParseLog(logOutput);
    }

    public TelemetryLineParseResult ParseLine(string line)
    {
        ParseLineCallCount++;
        return _inner.ParseLine(line);
    }
}

internal sealed class FakeAdbClientFactory(IAdbClient adbClient) : IAdbClientFactory
{
    public List<TimeSpan?> CommandTimeouts { get; } = [];

    public IAdbClient Create(string executable, string? serial, IProcessRunner processRunner, TimeSpan? commandTimeout = null)
    {
        CommandTimeouts.Add(commandTimeout);
        return adbClient;
    }
}

internal sealed class FakeEnvironmentVariables(Dictionary<string, string> variables) : IEnvironmentVariables
{
    public string? GetEnvironmentVariable(string variable) =>
        variables.GetValueOrDefault(variable);
}

internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Queue<ProcessResult> _results = new();

    public List<(string FileName, string[] Args)> Calls { get; } = [];

    public void EnqueueResult(ProcessResult result) => _results.Enqueue(result);

    public Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> args, CancellationToken cancellationToken = default)
    {
        var finalArgs = args.ToArray();
        Calls.Add((fileName, finalArgs));
        return Task.FromResult(_results.Count > 0 ? _results.Dequeue() : new ProcessResult(0, string.Empty, string.Empty));
    }
}

internal sealed class FakeDeviceHostFactory(IDeviceHost deviceHost) : IDeviceHostFactory
{
    public int CreateCallCount { get; private set; }

    public List<DeviceHostConfiguration> Configurations { get; } = [];

    public IDeviceHost Create(DeviceHostConfiguration configuration, ArtifactSession artifacts)
    {
        CreateCallCount++;
        Configurations.Add(configuration);
        return deviceHost;
    }
}

internal sealed class FakeDeviceHost(params ScreenState[] screenStates) : IDeviceHost, IAdbCommandHost, IWirelessDebugHost
{
    private readonly Queue<ScreenState> _screenStates = new(screenStates);

    public List<string> TapTextRequests { get; } = [];

    public List<(string? Label, double? XRatio, double? YRatio, int PostTapDelayMs)> TapPointRequests { get; } = [];

    public List<string> TakeScreenshotRequests { get; } = [];

    public List<(string Label, int? ExpectedWidth, int? ExpectedHeight, string? ExpectedSha256)> AssertScreenshotRequests { get; } = [];

    public List<string> RecordRequests { get; } = [];

    public List<int> LogcatRequests { get; } = [];

    public List<string> TypeTextRequests { get; } = [];

    public List<string> KeyEventRequests { get; } = [];

    public List<(int HorizontalTicks, int VerticalTicks)> ScrollRequests { get; } = [];

    public List<(string LocalPath, string? RemoteDirectory)> PushFileRequests { get; } = [];

    public List<(string RemotePath, string? LocalDirectory)> PullFileRequests { get; } = [];

    public List<(string Host, int Port)> WirelessRequests { get; } = [];

    public List<(string? Endpoint, string? Service, string? PairingCode)> WirelessPairRequests { get; } = [];

    public List<(string? Endpoint, string? Service)> WirelessConnectRequests { get; } = [];

    public List<WirelessMdnsService> WirelessServices { get; } = [];

    public WirelessMdnsConnectResult? WirelessConnectResponse { get; set; }

    public List<string> InstallPackageRequests { get; } = [];

    public List<string> AdbDiagnostics { get; } = [];

    public List<string> AdbReconnectTargets { get; } = [];

    public List<int> WaitForDeviceRequests { get; } = [];

    public List<string?> ReadOnlyPreflightRequests { get; } = [];

    public List<string?> CommandPreflightRequests { get; } = [];

    public List<(string Local, string Remote, bool NoRebind)> ForwardRequests { get; } = [];

    public List<string> ForwardRemoveRequests { get; } = [];

    public List<(string Remote, string Local, bool NoRebind)> ReverseRequests { get; } = [];

    public List<string> ReverseRemoveRequests { get; } = [];

    public List<(string Package, string? Activity, bool Wait)> StartAppRequests { get; } = [];

    public List<(string Uri, string? Package, string? Activity, string? Action, bool Wait)> StartUriRequests { get; } = [];

    public List<string> ForceStopRequests { get; } = [];

    public List<string> ClearAppRequests { get; } = [];

    public List<(string Activity, int TimeoutSec)> WaitForActivityRequests { get; } = [];

    public List<(string Activity, int TimeoutSec)> WaitForNotActivityRequests { get; } = [];

    public List<string> IsAppInstalledRequests { get; } = [];

    public List<bool> ListInstalledPackagesRequests { get; } = [];

    public List<(string Package, string Permission)> GrantPermissionRequests { get; } = [];

    public List<(string Package, string Permission)> RevokePermissionRequests { get; } = [];

    public List<(string Name, DateTimeOffset? Since)> AssertEventRequests { get; } = [];

    public List<DeviceInfo> ConnectedDevices { get; } = [];

    public PreflightResult PreflightTemplate { get; set; } = new("Model", "16", "36", "focus", null, null, "fingerprint", "arm64-v8a", "SER");

    public Exception? GetDevicesException { get; set; }

    public Exception? PreflightException { get; set; }

    public Exception? DeviceFingerprintException { get; set; }

    public Exception? WaitVisibleException { get; set; }

    public Exception? AssertScreenshotException { get; set; }

    public Exception? ScreenStateException { get; set; }

    public Exception? ForceStopException { get; set; }

    public FailureArtifactBundle? FailureArtifacts { get; set; }

    public Exception? FailureArtifactException { get; set; }

    public int? AssertScreenshotObservedWidth { get; set; } = 320;

    public int? AssertScreenshotObservedHeight { get; set; } = 240;

    public string? AssertScreenshotObservedSha256 { get; set; } = "observed-screenshot-sha256";

    public List<FailureCaptureRequest> FailureArtifactRequests { get; } = [];

    public Task<DeviceListResult> GetDevicesAsync()
    {
        if (GetDevicesException is not null)
        {
            throw GetDevicesException;
        }

        return Task.FromResult(new DeviceListResult(ConnectedDevices));
    }

    public Task<AdbDiagnosticResult> GetAdbServerStatusAsync()
    {
        AdbDiagnostics.Add("server-status");
        return Task.FromResult(CreateAdbDiagnostic("server-status", ["server-status"]));
    }

    public Task<AdbDiagnosticResult> GetAdbVersionAsync()
    {
        AdbDiagnostics.Add("version");
        return Task.FromResult(CreateAdbDiagnostic("version", ["version"]));
    }

    public Task<AdbDiagnosticResult> GetAdbFeaturesAsync()
    {
        AdbDiagnostics.Add("features");
        return Task.FromResult(CreateAdbDiagnostic("features", ["features"]));
    }

    public Task<AdbDiagnosticResult> CheckAdbMdnsAsync()
    {
        AdbDiagnostics.Add("mdns check");
        return Task.FromResult(CreateAdbDiagnostic("mdns check", ["mdns", "check"]));
    }

    public Task<AdbDiagnosticResult> ReconnectAdbAsync(string target)
    {
        AdbReconnectTargets.Add(target);
        return Task.FromResult(CreateAdbDiagnostic($"reconnect {target}", ["reconnect", target]));
    }

    public Task<AdbReadinessResult> WaitForDeviceAsync(int timeoutSec)
    {
        WaitForDeviceRequests.Add(timeoutSec);
        var wait = new AdbCommandOutput("adb wait-for-device", ["wait-for-device"], 0, true, string.Empty, string.Empty, 1, null, []);
        var ping = new AdbCommandOutput("adb shell \"echo ping\"", ["shell", "echo ping"], 0, true, "ping\n", string.Empty, 1, null, []);
        return Task.FromResult(new AdbReadinessResult(ResultSchemas.AdbReadiness, true, PreflightTemplate.Serial, true, true, timeoutSec, wait, ping, "ping"));
    }

    public Task<PreflightResult> ReadPreflightAsync(string? packageName)
    {
        ReadOnlyPreflightRequests.Add(packageName);
        if (PreflightException is not null)
        {
            throw PreflightException;
        }

        return Task.FromResult(PreflightTemplate with { Package = packageName });
    }

    public Task<PreflightResult> PreflightAsync(string? packageName)
    {
        CommandPreflightRequests.Add(packageName);
        if (PreflightException is not null)
        {
            throw PreflightException;
        }

        return Task.FromResult(PreflightTemplate with { Package = packageName });
    }

    public Task<ScreenState> GetScreenStateAsync()
    {
        if (ScreenStateException is not null)
        {
            throw ScreenStateException;
        }

        return Task.FromResult(_screenStates.Count > 1 ? _screenStates.Dequeue() : _screenStates.Peek());
    }

    public Task<TapResult> TapAsync(string x, string y) => Task.FromResult(new TapResult(int.Parse(x), int.Parse(y)));

    public Task<TelemetryResult> TelemetryTailAsync(int tail) => Task.FromResult(new TelemetryResult(0, 0, 0, 0, [], []));

    public Task<TelemetryResult> TelemetryWatchAsync(int timeoutSec) => Task.FromResult(new TelemetryResult(0, 0, 0, 0, [], []));

    public Task<WaitNotVisibleResult> WaitNotVisibleAsync(string text, int timeoutSec) => Task.FromResult(new WaitNotVisibleResult(text, 1, false));

    public Task<TapPointResult> TapPointAsync(string? label, int? x, int? y, double? xRatio, double? yRatio, int postTapDelayMs)
    {
        TapPointRequests.Add((label, xRatio, yRatio, postTapDelayMs));
        return Task.FromResult(new TapPointResult(label, x ?? 0, y ?? 0, xRatio, yRatio, postTapDelayMs));
    }

    public Task<DoubleTapHeaderLogoResult> DoubleTapHeaderLogoAsync() => Task.FromResult(new DoubleTapHeaderLogoResult("header_logo", 0, 0, 160));

    public Task<TelemetryMatchResult> WaitForStepAsync(string step, int timeoutSec) => Task.FromResult(new TelemetryMatchResult(step, null, string.Empty, "step", default));

    public Task<TelemetryMatchResult> WaitForActionReadyAsync(string action, string? step, int timeoutSec) => Task.FromResult(new TelemetryMatchResult(step, action, string.Empty, "action_ready", default));

    public Task<ResetLogResult> ResetLogAsync() => Task.FromResult(new ResetLogResult(true));

    public Task<AssertEventResult> AssertEventAsync(string name, IReadOnlyList<string> contains, string? detailsPattern, int timeoutSec, DateTimeOffset? since = null)
    {
        AssertEventRequests.Add((name, since));
        return Task.FromResult(new AssertEventResult(name, contains, detailsPattern, string.Empty));
    }

    public Task<TakeScreenshotResult> TakeScreenshotAsync(string label)
    {
        TakeScreenshotRequests.Add(label);
        return Task.FromResult(new TakeScreenshotResult(label, $"{label}.png"));
    }

    public Task<ScreenshotAssertionResult> AssertScreenshotAsync(string label, int? expectedWidth, int? expectedHeight, string? expectedSha256)
    {
        AssertScreenshotRequests.Add((label, expectedWidth, expectedHeight, expectedSha256));
        if (AssertScreenshotException is not null)
        {
            throw AssertScreenshotException;
        }

        return Task.FromResult(new ScreenshotAssertionResult(
            label,
            $"{label}.png",
            AssertScreenshotObservedWidth,
            AssertScreenshotObservedHeight,
            AssertScreenshotObservedSha256,
            expectedWidth,
            expectedHeight,
            expectedSha256));
    }

    public Task<CaptureArtifactsResult> CaptureArtifactsAsync(string label) => Task.FromResult(new CaptureArtifactsResult(label, $"{label}.png", $"{label}.txt", $"{label}.json", $"{label}.xml"));

    public Task<AssertTextInputReadyResult> AssertTextInputReadyAsync(bool requireKeyboard, int timeoutSec) =>
        Task.FromResult(new AssertTextInputReadyResult(requireKeyboard, true, null, null, null));

    public Task<AssertBelowResult> AssertBelowAsync(string text, string referenceText, int maxGapPx) =>
        Task.FromResult(new AssertBelowResult(text, referenceText, 8, maxGapPx));

    public Task<AssertAlignedResult> AssertAlignedAsync(string text, string referenceText, int maxDeltaPx) =>
        Task.FromResult(new AssertAlignedResult(text, referenceText, 4, maxDeltaPx));

    public Task<AssertAppVersionResult> AssertAppVersionAsync(string? packageName, int maxTopInsetPx, int maxRightInsetPx) =>
        Task.FromResult(new AssertAppVersionResult(packageName ?? string.Empty, "v1.0.0", 0, 0, maxTopInsetPx, maxRightInsetPx));

    public Task<RecordResult> RecordAsync(string output, int timeLimitSec)
    {
        RecordRequests.Add($"{output}|{timeLimitSec}");
        return Task.FromResult(new RecordResult(output, timeLimitSec));
    }

    public Task<ScreenElement> WaitVisibleAsync(string text, int timeoutSec)
    {
        if (WaitVisibleException is not null)
        {
            throw WaitVisibleException;
        }

        return Task.FromResult(new ScreenElement(text, null, $"id/{text}", "android.widget.TextView", true, true, 0, 0, 100, 100));
    }

    public Task<TapResult> TapTextAsync(string text, int timeoutSec)
    {
        TapTextRequests.Add(text);
        return Task.FromResult(new TapResult(50, 50));
    }

    public Task<TypeTextResult> TypeTextAsync(string text)
    {
        TypeTextRequests.Add(text);
        return Task.FromResult(new TypeTextResult(text));
    }

    public Task<TypePinResult> TypePinAsync(string pin, int perDigitDelayMs) => Task.FromResult(new TypePinResult(pin.Length, perDigitDelayMs));

    public Task<KeyEventResult> KeyEventAsync(string code)
    {
        KeyEventRequests.Add(code);
        return Task.FromResult(new KeyEventResult(code));
    }

    public Task<ScrollResult> ScrollAsync(int horizontalTicks, int verticalTicks)
    {
        ScrollRequests.Add((horizontalTicks, verticalTicks));
        return Task.FromResult(new ScrollResult(horizontalTicks, verticalTicks, 10, 10, 10, 100, 180));
    }

    public Task<PushFileResult> PushFileAsync(string localPath, string? remoteDirectory = null)
    {
        PushFileRequests.Add((localPath, remoteDirectory));
        return Task.FromResult(new PushFileResult(localPath, $"{remoteDirectory ?? "/sdcard/Download"}/{Path.GetFileName(localPath)}"));
    }

    public Task<PullFileResult> PullFileAsync(string remotePath, string? localDirectory = null)
    {
        PullFileRequests.Add((remotePath, localDirectory));
        return Task.FromResult(new PullFileResult(remotePath, Path.Combine(localDirectory ?? "/tmp", Path.GetFileName(remotePath))));
    }

    public Task<WirelessConnectResult> EnableWirelessAsync(string? host, int port)
    {
        var resolvedHost = string.IsNullOrWhiteSpace(host) ? "192.168.0.44" : host;
        WirelessRequests.Add((resolvedHost, port));
        return Task.FromResult(new WirelessConnectResult(resolvedHost, port, $"{resolvedHost}:{port}"));
    }

    public Task<WirelessScanResult> ScanWirelessServicesAsync() =>
        Task.FromResult(CreateWirelessScanResult(WirelessServices));

    public Task<WirelessPairResult> PairWirelessAsync(string? endpoint, string? service, string? pairingCode)
    {
        WirelessPairRequests.Add((endpoint, service, pairingCode));
        var resolvedEndpoint = endpoint ?? "192.168.0.44:37123";
        var selector = service is null ? null : $"{service}._adb-tls-pairing._tcp";
        var paired = !string.IsNullOrWhiteSpace(pairingCode);
        return Task.FromResult(new WirelessPairResult(
            resolvedEndpoint,
            service,
            service is null ? null : "_adb-tls-pairing._tcp",
            selector,
            paired,
            !paired,
            paired ? $"Successfully paired to {resolvedEndpoint}" : "Pairing code required.",
            paired ? $"Successfully paired to {resolvedEndpoint}" : null));
    }

    public Task<WirelessMdnsConnectResult> ConnectWirelessAsync(string? endpoint, string? service)
    {
        WirelessConnectRequests.Add((endpoint, service));
        if (WirelessConnectResponse is not null)
        {
            return Task.FromResult(WirelessConnectResponse);
        }

        var resolvedEndpoint = endpoint ?? "192.168.0.44:37123";
        var selector = service is null ? resolvedEndpoint : $"{service}._adb-tls-connect._tcp";
        return Task.FromResult(new WirelessMdnsConnectResult(
            resolvedEndpoint,
            service,
            service is null ? null : "_adb-tls-connect._tcp",
            service is null ? null : selector,
            resolvedEndpoint,
            selector,
            true,
            $"connected to {resolvedEndpoint}",
            $"connected to {resolvedEndpoint}"));
    }

    private static WirelessScanResult CreateWirelessScanResult(IReadOnlyList<WirelessMdnsService> services) =>
        new(
            services,
            services.Where(static service => string.Equals(service.ServiceType, "_adb-tls-pairing._tcp", StringComparison.OrdinalIgnoreCase)).ToArray(),
            services.Where(static service => string.Equals(service.ServiceType, "_adb-tls-connect._tcp", StringComparison.OrdinalIgnoreCase)).ToArray(),
            services.Where(static service => string.Equals(service.ServiceType, "_adb._tcp", StringComparison.OrdinalIgnoreCase)).ToArray());

    public Task<InstallPackageResult> InstallPackageAsync(string packagePath)
    {
        InstallPackageRequests.Add(packagePath);
        return Task.FromResult(new InstallPackageResult(packagePath));
    }

    public Task<PortForwardListResult> ListForwardsAsync() =>
        Task.FromResult(new PortForwardListResult([]));

    public Task<PortForwardResult> ForwardAsync(string local, string remote, bool noRebind)
    {
        ForwardRequests.Add((local, remote, noRebind));
        return Task.FromResult(new PortForwardResult(local, remote, noRebind));
    }

    public Task<PortForwardRemoveResult> RemoveForwardAsync(string local)
    {
        ForwardRemoveRequests.Add(local);
        return Task.FromResult(new PortForwardRemoveResult(local));
    }

    public Task<PortReverseListResult> ListReversesAsync() =>
        Task.FromResult(new PortReverseListResult([]));

    public Task<PortReverseResult> ReverseAsync(string remote, string local, bool noRebind)
    {
        ReverseRequests.Add((remote, local, noRebind));
        return Task.FromResult(new PortReverseResult(remote, local, noRebind));
    }

    public Task<PortReverseRemoveResult> RemoveReverseAsync(string remote)
    {
        ReverseRemoveRequests.Add(remote);
        return Task.FromResult(new PortReverseRemoveResult(remote));
    }

    public Task<StartAppResult> StartAppAsync(string packageName, string? activity, bool wait)
    {
        StartAppRequests.Add((packageName, activity, wait));
        var component = string.IsNullOrWhiteSpace(activity) ? null : $"{packageName}/{activity}";
        return Task.FromResult(new StartAppResult(packageName, activity, component, wait, string.Empty));
    }

    public Task<StartUriResult> StartUriAsync(string uri, string? packageName, string? activity, string? action, bool wait)
    {
        StartUriRequests.Add((uri, packageName, activity, action, wait));
        var component = string.IsNullOrWhiteSpace(packageName) || string.IsNullOrWhiteSpace(activity) ? null : $"{packageName}/{activity}";
        return Task.FromResult(new StartUriResult(uri, packageName, activity, component, action ?? "android.intent.action.VIEW", wait, string.Empty));
    }

    public Task<AppPackageCommandResult> ForceStopAsync(string packageName)
    {
        ForceStopRequests.Add(packageName);
        if (ForceStopException is not null)
        {
            throw ForceStopException;
        }

        return Task.FromResult(new AppPackageCommandResult(packageName));
    }

    public Task<AppPackageCommandResult> ClearAppAsync(string packageName)
    {
        ClearAppRequests.Add(packageName);
        return Task.FromResult(new AppPackageCommandResult(packageName));
    }

    public Task<ActivityWaitResult> WaitForActivityAsync(string activity, int timeoutSec)
    {
        WaitForActivityRequests.Add((activity, timeoutSec));
        return Task.FromResult(new ActivityWaitResult(activity, timeoutSec, activity, 1));
    }

    public Task<ActivityWaitResult> WaitForNotActivityAsync(string activity, int timeoutSec)
    {
        WaitForNotActivityRequests.Add((activity, timeoutSec));
        return Task.FromResult(new ActivityWaitResult(activity, timeoutSec, "other", 1));
    }

    public Task<AppInstalledResult> IsAppInstalledAsync(string packageName)
    {
        IsAppInstalledRequests.Add(packageName);
        return Task.FromResult(new AppInstalledResult(packageName, true));
    }

    public Task<InstalledPackageListResult> ListInstalledPackagesAsync(bool thirdPartyOnly)
    {
        ListInstalledPackagesRequests.Add(thirdPartyOnly);
        return Task.FromResult(new InstalledPackageListResult(["dev.luotsi.app"], thirdPartyOnly));
    }

    public Task<PermissionCommandResult> GrantPermissionAsync(string packageName, string permission)
    {
        GrantPermissionRequests.Add((packageName, permission));
        return Task.FromResult(new PermissionCommandResult(packageName, permission));
    }

    public Task<PermissionCommandResult> RevokePermissionAsync(string packageName, string permission)
    {
        RevokePermissionRequests.Add((packageName, permission));
        return Task.FromResult(new PermissionCommandResult(packageName, permission));
    }

    public Task<WaitLogResult> WaitForLogAsync(string text, int timeoutSec) => Task.FromResult(new WaitLogResult(text, timeoutSec, text, 1));

    public Task<DeviceFingerprint> WriteDeviceFingerprintAsync()
    {
        if (DeviceFingerprintException is not null)
        {
            throw DeviceFingerprintException;
        }

        return Task.FromResult(new DeviceFingerprint(ResultSchemas.DeviceFingerprint, DateTimeOffset.UtcNow, "SER", "Model", "16", "36", "fingerprint", "arm64-v8a", "focus"));
    }

    public Task<FailureArtifactBundle> CaptureFailureArtifactsAsync(FailureCaptureRequest request, Exception exception)
    {
        FailureArtifactRequests.Add(request);
        if (FailureArtifactException is not null)
        {
            throw FailureArtifactException;
        }

        return Task.FromResult(FailureArtifacts ?? new FailureArtifactBundle(ResultSchemas.FailureBundle, DateTimeOffset.UtcNow, request.Scope, request.Name, request.File, request.StepIndex, request.StepName, request.Action, exception.GetType().FullName ?? exception.GetType().Name, exception.Message, [], []));
    }

    public Task<LogcatResult> LogcatAsync(int tail)
    {
        LogcatRequests.Add(tail);
        return Task.FromResult(new LogcatResult([]));
    }

    private static AdbDiagnosticResult CreateAdbDiagnostic(string name, IReadOnlyList<string> args) =>
        new(
            ResultSchemas.AdbDiagnostic,
            name,
            new AdbCommandOutput(
                $"adb {string.Join(" ", args)}",
                args,
                0,
                true,
                string.Empty,
                string.Empty,
                1,
                null,
                []));
}

internal sealed class FakeViewSession(int exitCode) : IViewSession
{
    public List<ViewOptions> Options { get; } = [];

    public Task<int> RunAsync(ViewOptions options, CancellationToken cancellationToken = default)
    {
        Options.Add(options);
        return Task.FromResult(exitCode);
    }
}

internal sealed class FakeViewProfileStore : IViewProfileStore
{
    public Dictionary<string, ViewProfile> Profiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<ViewProfile?> LoadAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(Profiles.GetValueOrDefault(name));

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(Profiles.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray());

    public Task SaveAsync(string name, ViewProfile profile, CancellationToken cancellationToken = default)
    {
        Profiles[name] = profile;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(Profiles.Remove(name));
}

internal sealed class FakeArtifactFolderOpener : IArtifactFolderOpener
{
    public List<string> OpenedPaths { get; } = [];

    public Task OpenAsync(string path)
    {
        OpenedPaths.Add(Path.GetFullPath(path));
        return Task.CompletedTask;
    }
}

internal sealed class FakeViewSessionFactory(IViewSession viewSession) : IViewSessionFactory
{
    public IDeviceHost? LastDeviceHost { get; private set; }

    public ArtifactSession? LastArtifacts { get; private set; }

    public IViewSession Create(IDeviceHost deviceHost, ArtifactSession artifacts)
    {
        LastDeviceHost = deviceHost;
        LastArtifacts = artifacts;
        return viewSession;
    }
}

internal sealed class FakeViewDoctor(Func<ViewOptions, ViewDoctorResult>? resultFactory = null) : IViewDoctor
{
    private readonly Func<ViewOptions, ViewDoctorResult> _resultFactory = resultFactory ?? (options =>
        new ViewDoctorResult(true, options.PresetName, options, [], null, []));

    public List<ViewOptions> Options { get; } = [];

    public Task<ViewDoctorResult> DiagnoseAsync(ViewOptions options, CancellationToken cancellationToken = default)
    {
        Options.Add(options);
        return Task.FromResult(_resultFactory(options));
    }
}

internal sealed class FakeViewDoctorFactory(IViewDoctor viewDoctor) : IViewDoctorFactory
{
    public IDeviceHost? LastDeviceHost { get; private set; }

    public IViewDoctor Create(IDeviceHost deviceHost)
    {
        LastDeviceHost = deviceHost;
        return viewDoctor;
    }
}

internal sealed class FakeViewSetup(Func<ViewOptions, bool, ViewSetupResult>? resultFactory = null) : IViewSetup
{
    private readonly Func<ViewOptions, bool, ViewSetupResult> _resultFactory = resultFactory ?? ((options, fix) =>
        new ViewSetupResult(
            true,
            fix,
            options.PresetName,
            options,
            [new ViewSetupStep("helper_install", ViewStartupPhaseStatus.Succeeded, "Installed.")],
            new ViewDoctorResult(true, options.PresetName, options, [], null, [])));

    public List<(ViewOptions Options, bool Fix)> Calls { get; } = [];

    public Task<ViewSetupResult> SetupAsync(ViewOptions options, bool fix, CancellationToken cancellationToken = default)
    {
        Calls.Add((options, fix));
        return Task.FromResult(_resultFactory(options, fix));
    }
}

internal sealed class FakeViewSetupFactory(IViewSetup viewSetup) : IViewSetupFactory
{
    public IDeviceHost? LastDeviceHost { get; private set; }

    public IViewSetup Create(IDeviceHost deviceHost)
    {
        LastDeviceHost = deviceHost;
        return viewSetup;
    }
}

internal sealed class FakeViewRendererFactory(IViewRenderer renderer) : IViewRendererFactory
{
    public Func<ViewInteractionRequest, Task>? LastInteractionHandler { get; private set; }

    public IViewRenderer? Create(ViewOptions options, Func<ViewInteractionRequest, Task> interactionHandler)
    {
        LastInteractionHandler = interactionHandler;
        return options.Headless ? null : renderer;
    }
}

internal sealed class FakeViewTransportBootstrap(IEnumerable<object> outcomes, IEnumerable<ViewStartupPhase>? startupPhases = null) : IViewTransportBootstrap
{
    private readonly Queue<object> _outcomes = new(outcomes);
    private readonly ViewStartupPhase[] _startupPhases = startupPhases?.ToArray() ?? [];

    public FakeViewTransportBootstrap(ViewConnectionInfo connectionInfo)
        : this([connectionInfo])
    {
    }

    public int StartCallCount { get; private set; }

    public int StopCallCount { get; private set; }

    public List<ViewStartRequest> StartRequests { get; } = [];

    public Task<ViewConnectionInfo> StartAsync(ViewStartRequest request, Action<ViewStartupPhase>? reportPhase = null, CancellationToken cancellationToken = default)
    {
        StartCallCount++;
        StartRequests.Add(request);
        foreach (var phase in _startupPhases)
        {
            reportPhase?.Invoke(phase);
        }

        var outcome = _outcomes.Count > 1 ? _outcomes.Dequeue() : _outcomes.Peek();
        if (outcome is Exception exception)
        {
            throw exception;
        }

        return Task.FromResult((ViewConnectionInfo)outcome);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        StopCallCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeViewBackend(string name = "stub") : IViewBackend
{
    public List<ViewPacket> Packets { get; } = [];

    public IViewRecorder? LastRecorder { get; private set; }

    public string Name => name;

    public Task InitializeAsync(ViewConnectionInfo connectionInfo, IViewRenderer? renderer, IViewRecorder? recorder, CancellationToken cancellationToken = default)
    {
        LastRecorder = recorder;
        return Task.CompletedTask;
    }

    public async Task RunAsync(IAsyncEnumerable<ViewPacket> packets, CancellationToken cancellationToken = default)
    {
        await foreach (var packet in packets.WithCancellation(cancellationToken))
        {
            Packets.Add(packet);
            if (packet.PacketType == ViewPacketType.StreamEnd)
            {
                return;
            }
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class StatsEmittingViewBackend : IViewBackend
{
    private IViewRenderer? _renderer;

    public string Name => "stats";

    public Task InitializeAsync(ViewConnectionInfo connectionInfo, IViewRenderer? renderer, IViewRecorder? recorder, CancellationToken cancellationToken = default)
    {
        _renderer = renderer;
        return Task.CompletedTask;
    }

    public async Task RunAsync(IAsyncEnumerable<ViewPacket> packets, CancellationToken cancellationToken = default)
    {
        if (_renderer is not null)
        {
            await _renderer.UpdateStatsAsync(new ViewStats(12, 11, 1, 59.9d, 58.7d, 84), cancellationToken).ConfigureAwait(false);
        }

        await foreach (var packet in packets.WithCancellation(cancellationToken))
        {
            if (packet.PacketType == ViewPacketType.StreamEnd)
            {
                return;
            }
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class ThrottledStatsViewBackend(ManualTimeProvider timeProvider) : IViewBackend
{
    private IViewRenderer? _renderer;

    public string Name => "stats-throttled";

    public Task InitializeAsync(ViewConnectionInfo connectionInfo, IViewRenderer? renderer, IViewRecorder? recorder, CancellationToken cancellationToken = default)
    {
        _renderer = renderer;
        return Task.CompletedTask;
    }

    public async Task RunAsync(IAsyncEnumerable<ViewPacket> packets, CancellationToken cancellationToken = default)
    {
        if (_renderer is not null)
        {
            await _renderer.UpdateStatsAsync(new ViewStats(10, 9, 1, 59.9d, 58.7d, 84), cancellationToken).ConfigureAwait(false);
            timeProvider.Advance(TimeSpan.FromMilliseconds(100));
            await _renderer.UpdateStatsAsync(new ViewStats(11, 10, 1, 59.6d, 58.3d, 86), cancellationToken).ConfigureAwait(false);
            timeProvider.Advance(TimeSpan.FromMilliseconds(100));
            await _renderer.UpdateStatsAsync(new ViewStats(12, 11, 1, 59.3d, 58.0d, 88), cancellationToken).ConfigureAwait(false);
        }

        await foreach (var packet in packets.WithCancellation(cancellationToken))
        {
            if (packet.PacketType == ViewPacketType.StreamEnd)
            {
                return;
            }
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class BlockingViewBackend : IViewBackend
{
    public string Name => "blocking";

    public Task InitializeAsync(ViewConnectionInfo connectionInfo, IViewRenderer? renderer, IViewRecorder? recorder, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RunAsync(IAsyncEnumerable<ViewPacket> packets, CancellationToken cancellationToken = default) => Task.Delay(Timeout.Infinite, cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class TimeAdvancingViewBackend(ManualTimeProvider timeProvider, TimeSpan advanceAfterFirstPacket) : IViewBackend
{
    private int _packetCount;

    public string Name => "time-advancing";

    public Task InitializeAsync(ViewConnectionInfo connectionInfo, IViewRenderer? renderer, IViewRecorder? recorder, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task RunAsync(IAsyncEnumerable<ViewPacket> packets, CancellationToken cancellationToken = default)
    {
        await foreach (var packet in packets.WithCancellation(cancellationToken))
        {
            if (packet.PacketType == ViewPacketType.StreamEnd)
            {
                return;
            }

            _packetCount++;
            if (_packetCount == 1)
            {
                timeProvider.Advance(advanceAfterFirstPacket);
            }
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class ClosingViewRenderer : IViewRenderer
{
    private readonly TaskCompletionSource _closedSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task InitializeAsync(ViewDisplayInfo displayInfo, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task PresentAsync(ViewFrame frame, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task UpdateStatsAsync(ViewStats stats, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task UpdateChromeAsync(ViewChromeState chrome, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task WaitForCloseAsync(CancellationToken cancellationToken = default) => _closedSource.Task.WaitAsync(cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Close() => _closedSource.TrySetResult();
}

internal sealed class StatsCapturingViewRenderer : IViewRenderer
{
    public List<ViewStats> StatsUpdates { get; } = [];

    public ViewStats? LastStats { get; private set; }

    public ViewChromeState? LastChrome { get; private set; }

    public Task InitializeAsync(ViewDisplayInfo displayInfo, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task PresentAsync(ViewFrame frame, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task UpdateStatsAsync(ViewStats stats, CancellationToken cancellationToken = default)
    {
        StatsUpdates.Add(stats);
        LastStats = stats;
        return Task.CompletedTask;
    }

    public Task UpdateChromeAsync(ViewChromeState chrome, CancellationToken cancellationToken = default)
    {
        LastChrome = chrome;
        return Task.CompletedTask;
    }

    public Task WaitForCloseAsync(CancellationToken cancellationToken = default) => Task.Delay(Timeout.Infinite, cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeViewBackendFactory(IReadOnlyDictionary<string, IViewBackend> backends) : IViewBackendFactory
{
    public FakeViewBackendFactory(IViewBackend backend)
        : this(new Dictionary<string, IViewBackend>(StringComparer.OrdinalIgnoreCase)
        {
            ["ffmpeg"] = backend
        })
    {
    }

    public List<string> RequestedDecoders { get; } = [];

    public IViewBackend Create(ViewOptions options)
    {
        RequestedDecoders.Add(options.Decoder);
        if (backends.TryGetValue(options.Decoder, out var backend))
        {
            return backend;
        }

        throw new InvalidOperationException($"No fake backend configured for decoder '{options.Decoder}'.");
    }
}

internal sealed class FakeViewStreamConnector(params Stream[] streams) : IViewStreamConnector
{
    private readonly Queue<Stream> _streams = new(streams);

    public int ConnectCallCount { get; private set; }

    public Task<IViewStreamConnection> ConnectAsync(ViewConnectionInfo connectionInfo, CancellationToken cancellationToken = default)
    {
        ConnectCallCount++;
        if (_streams.Count == 0)
        {
            throw new InvalidOperationException("No fake view streams remain.");
        }

        var stream = _streams.Count > 1 ? _streams.Dequeue() : _streams.Peek();
        return Task.FromResult<IViewStreamConnection>(new FakeViewStreamConnection(stream));
    }
}

internal sealed class FakeViewStreamConnection(Stream stream) : IViewStreamConnection
{
    public Stream Stream { get; } = stream;

    public ValueTask DisposeAsync()
    {
        Stream.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeViewRecorderFactory : IViewRecorderFactory
{
    public FakeViewRecorder? LastRecorder { get; private set; }

    public IViewRecorder? Create(ViewOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RecordPath))
        {
            return null;
        }

        LastRecorder = new FakeViewRecorder();
        return LastRecorder;
    }
}

internal sealed class FakeViewRecorder : IViewRecorder
{
    public bool Disposed { get; private set; }

    public Task InitializeAsync(ViewConnectionInfo connectionInfo, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task WritePacketAsync(ViewPacket packet, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task CompleteAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal static class ViewTestWaitHelpers
{
    public static async Task<Func<ViewInteractionRequest, Task>> WaitForInteractionHandlerAsync(FakeViewRendererFactory factory)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (factory.LastInteractionHandler is not null)
            {
                return factory.LastInteractionHandler;
            }

            await Task.Yield();
        }

        throw new InvalidOperationException("Timed out waiting for the view interaction handler.");
    }

    public static async Task WaitForStartCallsAsync(FakeViewTransportBootstrap bootstrap, int minimumCalls)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (bootstrap.StartCallCount >= minimumCalls)
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new InvalidOperationException($"Timed out waiting for {minimumCalls} view transport start calls; observed {bootstrap.StartCallCount}.");
    }

    public static async Task WaitForOutputLineAsync(FakeConsole console, string contains)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (console.OutputLines.Any(line => line.Contains(contains, StringComparison.Ordinal)))
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new InvalidOperationException($"Timed out waiting for output containing '{contains}'.");
    }

    public static async Task WaitForShareObserverAsync(TcpViewShareServer shareServer, int minimumObservers)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (shareServer.ObserverCount >= minimumObservers)
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new InvalidOperationException($"Timed out waiting for {minimumObservers} share observer connections.");
    }
}
