using System.Text.Json;
using Luotsi.Cli;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Hosts.Android;
using Luotsi.Cli.Hosts.Android.View;
using Luotsi.Cli.Infrastructure;
using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;
using Luotsi.Cli.Telemetry;
using Luotsi.Cli.View;
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

internal sealed class FakeDelay(ManualTimeProvider timeProvider) : IDelay
{
    private readonly ManualTimeProvider _timeProvider = timeProvider;

    public List<int> Calls { get; } = [];

    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken = default)
    {
        Calls.Add(milliseconds);
        DelayMetrics.RecordDelay(milliseconds);
        _timeProvider.Advance(TimeSpan.FromMilliseconds(milliseconds));
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
    private readonly string _value = value;

    public string NewId() => _value;
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

    public void CreateDirectory(string path) => _directories.Add(path);

    public Task WriteAllTextAsync(string path, string text, System.Text.Encoding encoding, CancellationToken cancellationToken = default)
    {
        AddFile(path, text);
        return Task.CompletedTask;
    }

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(_files.TryGetValue(path, out var text) ? text : System.Text.Encoding.UTF8.GetString(_binaryFiles[path]));

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

    private void WriteBinaryFile(string path, byte[] content)
    {
        CreateDirectory(Path.GetDirectoryName(path) ?? "/");
        _binaryFiles[path] = content;
        _files.Remove(path);
    }

    private sealed class FakeWriteStream(FakeFileSystem fileSystem, string path) : MemoryStream
    {
        private readonly FakeFileSystem _fileSystem = fileSystem;
        private readonly string _path = path;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _fileSystem.WriteBinaryFile(_path, ToArray());
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            _fileSystem.WriteBinaryFile(_path, ToArray());
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}

internal sealed class FakeAdbClient : IAdbClient
{
    private readonly Queue<ProcessResult> _shellResults = new();
    private readonly Queue<ProcessResult> _runResults = new();
    private readonly Queue<string[]> _logLines = new();
    private readonly Queue<AdbLogStreamResult> _logResults = new();

    public List<string> ShellCommands { get; } = [];

    public List<string[]> RunCommands { get; } = [];

    public List<(string ContainsText, DateTimeOffset Since, int TimeoutSec)> LogRequests { get; } = [];

    public List<(DateTimeOffset Since, int TimeoutSec, bool HasStopCondition, bool HasLineObserver)> StreamingLogRequests { get; } = [];

    public void EnqueueShellResult(ProcessResult result) => _shellResults.Enqueue(result);

    public void EnqueueRunResult(ProcessResult result) => _runResults.Enqueue(result);

    public void EnqueueLogLines(params string[] lines) => _logLines.Enqueue(lines);

    public void EnqueueLogResult(AdbLogStreamResult result) => _logResults.Enqueue(result);

    public Task<AdbCommandResult> RunAsync(IEnumerable<string> args, CancellationToken cancellationToken = default)
    {
        var finalArgs = args.ToArray();
        RunCommands.Add(finalArgs);
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
        return Task.FromResult(new AdbCommandResult("adb", null, finalArgs, result));
    }

    public Task<AdbCommandResult> ShellAsync(string command, CancellationToken cancellationToken = default)
    {
        ShellCommands.Add(command);
        var result = _shellResults.Count > 0 ? _shellResults.Dequeue() : new ProcessResult(0, string.Empty, string.Empty);
        return Task.FromResult(new AdbCommandResult("adb", null, ["shell", command], result));
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
    private readonly IAdbClient _adbClient = adbClient;

    public IAdbClient Create(string executable, string? serial, IProcessRunner processRunner) => _adbClient;
}

internal sealed class FakeEnvironmentVariables(Dictionary<string, string> variables) : IEnvironmentVariables
{
    private readonly Dictionary<string, string> _variables = variables;

    public string? GetEnvironmentVariable(string variable) =>
        _variables.TryGetValue(variable, out var value) ? value : null;
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
    private readonly IDeviceHost _deviceHost = deviceHost;

    public int CreateCallCount { get; private set; }

    public IDeviceHost Create(DeviceHostConfiguration configuration, ArtifactSession artifacts)
    {
        CreateCallCount++;
        return _deviceHost;
    }
}

internal sealed class FakeDeviceHost(params ScreenState[] screenStates) : IDeviceHost
{
    private readonly Queue<ScreenState> _screenStates = new(screenStates);

    public List<string> TapTextRequests { get; } = [];

    public List<(string? Label, double? XRatio, double? YRatio, int PostTapDelayMs)> TapPointRequests { get; } = [];

    public List<string> TakeScreenshotRequests { get; } = [];

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

    public List<DeviceInfo> ConnectedDevices { get; } = [];

    public PreflightResult PreflightTemplate { get; set; } = new("Model", "16", "36", "focus", null, null, "fingerprint", "arm64-v8a", "SER");

    public Exception? GetDevicesException { get; set; }

    public Exception? PreflightException { get; set; }

    public Task<DeviceListResult> GetDevicesAsync()
    {
        if (GetDevicesException is not null)
        {
            throw GetDevicesException;
        }

        return Task.FromResult(new DeviceListResult(ConnectedDevices));
    }

    public Task<PreflightResult> PreflightAsync(string? packageName)
    {
        if (PreflightException is not null)
        {
            throw PreflightException;
        }

        return Task.FromResult(PreflightTemplate with { Package = packageName });
    }

    public Task<ScreenState> GetScreenStateAsync() =>
        Task.FromResult(_screenStates.Count > 1 ? _screenStates.Dequeue() : _screenStates.Peek());

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

    public Task<AssertEventResult> AssertEventAsync(string name, IReadOnlyList<string> contains, string? detailsPattern, int timeoutSec, DateTimeOffset? since = null) =>
        Task.FromResult(new AssertEventResult(name, contains, detailsPattern, string.Empty));

    public Task<TakeScreenshotResult> TakeScreenshotAsync(string label)
    {
        TakeScreenshotRequests.Add(label);
        return Task.FromResult(new TakeScreenshotResult(label, $"{label}.png"));
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

    public Task<RecordResult> RecordAsync(string output, int timeLimitSec) => Task.FromResult(new RecordResult(output, timeLimitSec));

    public Task<ScreenElement> WaitVisibleAsync(string text, int timeoutSec) =>
        Task.FromResult(new ScreenElement(text, null, $"id/{text}", "android.widget.TextView", true, true, 0, 0, 100, 100));

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
            selector,
            selector,
            true,
            $"connected to {selector}",
            $"connected to {selector}"));
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

    public Task<WaitLogResult> WaitForLogAsync(string text, int timeoutSec) => Task.FromResult(new WaitLogResult(text, timeoutSec, text, 1));

    public Task<DeviceFingerprint> WriteDeviceFingerprintAsync() =>
        Task.FromResult(new DeviceFingerprint(ResultSchemas.DeviceFingerprint, DateTimeOffset.UtcNow, "SER", "Model", "16", "36", "fingerprint", "arm64-v8a", "focus"));

    public Task<FailureArtifactBundle> CaptureFailureArtifactsAsync(FailureCaptureRequest request, Exception exception) =>
        Task.FromResult(new FailureArtifactBundle(ResultSchemas.FailureBundle, DateTimeOffset.UtcNow, request.Scope, request.Name, request.File, request.StepIndex, request.StepName, request.Action, exception.GetType().FullName ?? exception.GetType().Name, exception.Message, [], []));

    public Task<LogcatResult> LogcatAsync(int tail) => Task.FromResult(new LogcatResult([]));
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
        Task.FromResult(Profiles.TryGetValue(name, out var profile) ? profile : null);

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
    private readonly IViewSession _viewSession = viewSession;

    public IDeviceHost? LastDeviceHost { get; private set; }

    public ArtifactSession? LastArtifacts { get; private set; }

    public IViewSession Create(IDeviceHost deviceHost, ArtifactSession artifacts)
    {
        LastDeviceHost = deviceHost;
        LastArtifacts = artifacts;
        return _viewSession;
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
    private readonly IViewDoctor _viewDoctor = viewDoctor;

    public IDeviceHost? LastDeviceHost { get; private set; }

    public IViewDoctor Create(IDeviceHost deviceHost)
    {
        LastDeviceHost = deviceHost;
        return _viewDoctor;
    }
}

internal sealed class FakeViewRendererFactory(IViewRenderer renderer) : IViewRendererFactory
{
    private readonly IViewRenderer _renderer = renderer;

    public Func<ViewInteractionRequest, Task>? LastInteractionHandler { get; private set; }

    public IViewRenderer? Create(ViewOptions options, Func<ViewInteractionRequest, Task> interactionHandler)
    {
        LastInteractionHandler = interactionHandler;
        return options.Headless ? null : _renderer;
    }
}

internal sealed class FakeViewTransportBootstrap : IViewTransportBootstrap
{
    private readonly Queue<object> _outcomes;

    public FakeViewTransportBootstrap(ViewConnectionInfo connectionInfo)
        : this([connectionInfo])
    {
    }

    public FakeViewTransportBootstrap(IEnumerable<object> outcomes)
    {
        _outcomes = new Queue<object>(outcomes);
    }

    public int StartCallCount { get; private set; }

    public int StopCallCount { get; private set; }

    public List<ViewStartRequest> StartRequests { get; } = [];

    public Task<ViewConnectionInfo> StartAsync(ViewStartRequest request, CancellationToken cancellationToken = default)
    {
        StartCallCount++;
        StartRequests.Add(request);
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
    private readonly string _name = name;

    public List<ViewPacket> Packets { get; } = [];

    public IViewRecorder? LastRecorder { get; private set; }

    public string Name => _name;

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
    private readonly ManualTimeProvider _timeProvider = timeProvider;
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
            _timeProvider.Advance(TimeSpan.FromMilliseconds(100));
            await _renderer.UpdateStatsAsync(new ViewStats(11, 10, 1, 59.6d, 58.3d, 86), cancellationToken).ConfigureAwait(false);
            _timeProvider.Advance(TimeSpan.FromMilliseconds(100));
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
    private readonly ManualTimeProvider _timeProvider = timeProvider;
    private readonly TimeSpan _advanceAfterFirstPacket = advanceAfterFirstPacket;
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
                _timeProvider.Advance(_advanceAfterFirstPacket);
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

internal sealed class FakeViewBackendFactory : IViewBackendFactory
{
    private readonly IReadOnlyDictionary<string, IViewBackend> _backends;

    public FakeViewBackendFactory(IViewBackend backend)
        : this(new Dictionary<string, IViewBackend>(StringComparer.OrdinalIgnoreCase)
        {
            ["ffmpeg"] = backend
        })
    {
    }

    public FakeViewBackendFactory(IReadOnlyDictionary<string, IViewBackend> backends)
    {
        _backends = backends;
    }

    public List<string> RequestedDecoders { get; } = [];

    public IViewBackend Create(ViewOptions options)
    {
        RequestedDecoders.Add(options.Decoder);
        if (_backends.TryGetValue(options.Decoder, out var backend))
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

        throw new InvalidOperationException($"Timed out waiting for {minimumCalls} view transport start calls.");
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
