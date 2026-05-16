using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure;
using Luotsi.Cli.Models;
using Luotsi.Cli.Telemetry;

namespace Luotsi.Cli.Hosts.Android;

/// <summary>
/// Device operation facade used by the command handlers.
/// </summary>
public sealed class DeviceRunner(
    IAdbClient adb,
    ArtifactSession artifacts,
    TimeProvider? timeProvider = null,
    IDelay? delay = null,
    IFileSystem? fileSystem = null,
    IUniqueIdGenerator? idGenerator = null,
    IEnvironmentVariables? environment = null,
    ITelemetryParser? telemetryParser = null) : IDeviceHost
{
    private static readonly (string Key, string Command)[] DeviceFingerprintReads =
    [
        ("serial", "getprop ro.serialno"),
        ("model", "getprop ro.product.model"),
        ("android_release", "getprop ro.build.version.release"),
        ("sdk", "getprop ro.build.version.sdk"),
        ("fingerprint", "getprop ro.build.fingerprint"),
        ("abi", "getprop ro.product.cpu.abilist"),
        ("current_focus", "dumpsys window | grep -E 'mCurrentFocus|mFocusedApp|mResumedActivity' | head -1")
    ];
    private static readonly IReadOnlyDictionary<string, string> DeviceFingerprintMarkers = DeviceFingerprintReads
        .ToDictionary(static field => CreateDeviceFingerprintMarker(field.Key), static field => field.Key, StringComparer.Ordinal);

    private readonly IAdbClient _adb = adb ?? throw new ArgumentNullException(nameof(adb));
    private readonly ArtifactSession _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly IDelay _delay = delay ?? new TaskDelay(timeProvider);
    private readonly IFileSystem _fileSystem = fileSystem ?? new PhysicalFileSystem();
    private readonly IUniqueIdGenerator _idGenerator = idGenerator ?? new GuidUniqueIdGenerator();
    private readonly IEnvironmentVariables _environment = environment ?? new SystemEnvironmentVariables();
    private readonly ITelemetryParser _telemetryParser = telemetryParser ?? new LuotsiDeviceTelemetryParser();

    private (int Width, int Height)? _displaySizeCache;
    private KeyboardVisibilitySnapshot? _keyboardVisibilityCache;
    private UiDumpSnapshot? _uiDumpCache;

    /// <summary>
    /// Lists connected devices.
    /// </summary>
    /// <returns>Device list data.</returns>
    public async Task<DeviceListResult> GetDevicesAsync()
    {
        var result = await _adb.RunAsync(["devices", "-l"]).ConfigureAwait(false);
        result.EnsureSuccess("adb devices failed");
        var devices = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0 && !line.StartsWith("*", StringComparison.Ordinal))
            .Select(static line =>
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return new DeviceInfo(parts.ElementAtOrDefault(0), parts.ElementAtOrDefault(1), string.Join(' ', parts.Skip(2)));
            })
            .ToArray();
        return new DeviceListResult(devices);
    }

    /// <summary>
    /// Checks device and application readiness.
    /// </summary>
    /// <param name="packageName">Optional package name expected to be installed and focused.</param>
    /// <returns>Preflight data.</returns>
    public async Task<PreflightResult> PreflightAsync(string? packageName)
    {
        var fingerprint = await WriteDeviceFingerprintAsync().ConfigureAwait(false);
        var focus = fingerprint.CurrentFocus;
        string? packageInfo = null;

        if (!string.IsNullOrWhiteSpace(packageName))
        {
            packageInfo = await ShellTextAsync($"dumpsys package {ShellQuote(packageName)} | grep -E 'versionName|versionCode|pkgFlags' | head -20").ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(packageInfo))
            {
                throw new InvalidOperationException($"Package '{packageName}' is not installed or dumpsys returned no package info.");
            }

            if (!focus.Contains(packageName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Package '{packageName}' is installed, but it is not the foreground app. Current focus: {focus}");
            }
        }

        return new PreflightResult(
            fingerprint.Model,
            fingerprint.AndroidRelease,
            fingerprint.Sdk,
            focus,
            packageName,
            packageInfo,
            fingerprint.Fingerprint,
            fingerprint.Abi,
            fingerprint.Serial);
    }

    /// <summary>
    /// Captures and normalizes the current UI hierarchy.
    /// </summary>
    /// <returns>Screen state data.</returns>
    public async Task<ScreenState> GetScreenStateAsync()
    {
        var capture = await ReadScreenCaptureAsync(writeInvalidArtifact: true).ConfigureAwait(false);
        await WriteScreenCaptureArtifactsAsync(capture).ConfigureAwait(false);
        return capture.State;
    }

    private async Task<ScreenState> CaptureScreenStateAsync(string? snapshotPrefix)
    {
        var capture = await ReadScreenCaptureAsync(writeInvalidArtifact: true).ConfigureAwait(false);
        await WriteScreenCaptureArtifactsAsync(capture, snapshotPrefix).ConfigureAwait(false);
        return capture.State;
    }

    private async Task<ScreenCapture> ReadScreenCaptureAsync(bool writeInvalidArtifact)
    {
        var xml = await ReadUiDumpXmlAsync().ConfigureAwait(false);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
            CacheUiDump(xml);
        }
        catch (Exception ex) when (ex is XmlException || ex is InvalidOperationException)
        {
            InvalidateUiDumpCache();
            if (writeInvalidArtifact)
            {
                await _artifacts.WriteTextAsync(DeviceArtifactNames.HierarchyXml, xml).ConfigureAwait(false);
                await _artifacts.WriteTextAsync(DeviceArtifactNames.InvalidHierarchyXml, xml).ConfigureAwait(false);
            }

            throw new InvalidOperationException($"UI hierarchy dump was empty or invalid XML. See {DeviceArtifactNames.InvalidHierarchyXml} for the raw dump.", ex);
        }

        var elements = doc.Descendants("node")
            .Select(static node => ScreenElement.From(node))
            .Where(static element => element.IsUseful)
            .ToArray();
        return new ScreenCapture(xml, new ScreenState(_timeProvider.GetUtcNow(), elements.Length, elements));
    }

    private async Task WriteScreenCaptureArtifactsAsync(ScreenCapture capture, string? snapshotPrefix = null)
    {
        await _artifacts.WriteTextAsync(DeviceArtifactNames.HierarchyXml, capture.Xml).ConfigureAwait(false);
        await _artifacts.WriteJsonAsync(DeviceArtifactNames.ScreenStateJson, capture.State).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(snapshotPrefix))
        {
            return;
        }

        var screenStatePath = Path.Combine(_artifacts.Root, DeviceArtifactNames.ScreenStateJson);
        var hierarchyPath = Path.Combine(_artifacts.Root, DeviceArtifactNames.HierarchyXml);
        _fileSystem.CopyFile(screenStatePath, Path.Combine(_artifacts.Root, DeviceArtifactNames.ScreenStateForLabel(snapshotPrefix)), true);
        _fileSystem.CopyFile(hierarchyPath, Path.Combine(_artifacts.Root, DeviceArtifactNames.HierarchyForLabel(snapshotPrefix)), true);

        var invalidHierarchyPath = Path.Combine(_artifacts.Root, DeviceArtifactNames.InvalidHierarchyXml);
        if (_fileSystem.FileExists(invalidHierarchyPath))
        {
            _fileSystem.CopyFile(invalidHierarchyPath, Path.Combine(_artifacts.Root, DeviceArtifactNames.InvalidHierarchyForLabel(snapshotPrefix)), true);
        }
    }

    private async Task<ScreenCapture> CapturePollingScreenStateAsync(string snapshotPrefix)
    {
        var writePerAttemptArtifacts = _artifacts.UiPollArtifactPolicy == UiPollArtifactPolicy.PerAttempt;
        var capture = await ReadScreenCaptureAsync(writePerAttemptArtifacts).ConfigureAwait(false);
        if (writePerAttemptArtifacts)
        {
            await WriteScreenCaptureArtifactsAsync(capture, snapshotPrefix).ConfigureAwait(false);
        }

        return capture;
    }

    private Task PersistPollingArtifactsAsync(ScreenCapture capture, string snapshotPrefix) =>
        _artifacts.UiPollArtifactPolicy switch
        {
            UiPollArtifactPolicy.Final => WriteScreenCaptureArtifactsAsync(capture, snapshotPrefix),
            UiPollArtifactPolicy.PerAttempt or UiPollArtifactPolicy.None => Task.CompletedTask,
            _ => throw new InvalidOperationException($"Unsupported UI poll artifact policy '{_artifacts.UiPollArtifactPolicy}'.")
        };

    /// <summary>
    /// Waits for visible text.
    /// </summary>
    /// <param name="text">Text or content description to find.</param>
    /// <param name="timeoutSec">Timeout in seconds.</param>
    /// <returns>Matched element.</returns>
    public async Task<ScreenElement> WaitVisibleAsync(string text, int timeoutSec)
    {
        var expectedText = RequireNonBlank(text, "waitVisible requires non-empty text.");
        var validatedTimeoutSec = RequirePositive(timeoutSec, "waitVisible requires timeoutSec greater than zero.");
        var deadline = _timeProvider.GetUtcNow().AddSeconds(validatedTimeoutSec);
        ScreenElement? last = null;
        var attempt = 0;
        while (_timeProvider.GetUtcNow() < deadline)
        {
            attempt++;
            var snapshotPrefix = $"wait-visible-{attempt:000}";
            ScreenCapture capture;

            try
            {
                capture = await CapturePollingScreenStateAsync(snapshotPrefix).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (IsRetryableHierarchyDumpFailure(ex))
            {
                await _delay.DelayAsync(AndroidRuntimeDefaults.UiPollDelayMs).ConfigureAwait(false);
                continue;
            }

            last = capture.State.Elements
                .Select(element => new { Element = element, Score = element.GetMatchScore(expectedText) })
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .Select(candidate => candidate.Element)
                .FirstOrDefault();
            if (last is not null)
            {
                await PersistPollingArtifactsAsync(capture, snapshotPrefix).ConfigureAwait(false);
                return last;
            }

            await _delay.DelayAsync(AndroidRuntimeDefaults.UiPollDelayMs).ConfigureAwait(false);
        }

        throw new TimeoutException($"Timed out after {validatedTimeoutSec}s waiting for visible text '{expectedText}'. Last seen: {last?.StableId ?? "none"}");
    }

    private static bool IsRetryableHierarchyDumpFailure(InvalidOperationException exception) =>
        exception.Message.Contains("UI hierarchy dump was empty or invalid XML", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Taps the center of visible text.
    /// </summary>
    /// <param name="text">Text or content description to tap.</param>
    /// <param name="timeoutSec">Timeout in seconds.</param>
    /// <returns>Tap data.</returns>
    public async Task<TapResult> TapTextAsync(string text, int timeoutSec)
    {
        var element = await WaitVisibleAsync(text, timeoutSec).ConfigureAwait(false);
        return await TapAsync(element.CenterX.ToString(), element.CenterY.ToString()).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a tap at absolute coordinates.
    /// </summary>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <returns>Tap data.</returns>
    public async Task<TapResult> TapAsync(string x, string y)
    {
        if (!int.TryParse(x, out var parsedX))
        {
            throw new UsageException("Option --x must be an integer.");
        }

        if (!int.TryParse(y, out var parsedY))
        {
            throw new UsageException("Option --y must be an integer.");
        }

        if (parsedX < 0 || parsedY < 0)
        {
            throw new UsageException("Tap coordinates must be zero or greater.");
        }

        var result = await _adb.ShellAsync($"input tap {parsedX} {parsedY}").ConfigureAwait(false);
        result.EnsureSuccess("tap failed");
        InvalidateUiReadCaches();
        return new TapResult(parsedX, parsedY);
    }

    /// <summary>
    /// Types text via adb input.
    /// </summary>
    /// <param name="text">Text to type.</param>
    /// <returns>Typed text metadata.</returns>
    public async Task<TypeTextResult> TypeTextAsync(string text)
    {
        var escaped = text.Replace("%", "%25", StringComparison.Ordinal).Replace(" ", "%s", StringComparison.Ordinal);
        var result = await _adb.ShellAsync($"input text {ShellQuote(escaped)}").ConfigureAwait(false);
        result.EnsureSuccess("type text failed");
        InvalidateUiReadCaches();
        return new TypeTextResult(text);
    }

    /// <summary>
    /// Sends an Android keyevent.
    /// </summary>
    /// <param name="code">Keyevent code or name.</param>
    /// <returns>Keyevent metadata.</returns>
    public async Task<KeyEventResult> KeyEventAsync(string code)
    {
        var keyCode = RequireNonBlank(code, "keyevent requires code.");
        var result = await _adb.ShellAsync($"input keyevent {ShellQuote(keyCode)}").ConfigureAwait(false);
        result.EnsureSuccess("keyevent failed");
        InvalidateUiReadCaches();
        return new KeyEventResult(keyCode);
    }

    public async Task<ScrollResult> ScrollAsync(int horizontalTicks, int verticalTicks)
    {
        if (horizontalTicks == 0 && verticalTicks == 0)
        {
            throw new UsageException("scroll requires at least one non-zero wheel delta.");
        }

        var (displayWidth, displayHeight) = await GetDisplaySizeAsync().ConfigureAwait(false);
        var width = Math.Max(320, displayWidth);
        var height = Math.Max(480, displayHeight);
        var centerX = width / 2;
        var centerY = height / 2;
        var horizontalDistance = Math.Clamp(Math.Abs(horizontalTicks) * Math.Max(80, width / 4), 80, Math.Max(120, width / 2));
        var verticalDistance = Math.Clamp(Math.Abs(verticalTicks) * Math.Max(80, height / 4), 80, Math.Max(120, height / 2));
        var durationMs = 180;

        var startX = centerX;
        var endX = centerX;
        var startY = centerY;
        var endY = centerY;

        if (Math.Abs(verticalTicks) >= Math.Abs(horizontalTicks) && verticalTicks != 0)
        {
            var halfDistance = verticalDistance / 2;
            if (verticalTicks > 0)
            {
                startY = Math.Max(0, centerY - halfDistance);
                endY = Math.Min(height - 1, centerY + halfDistance);
            }
            else
            {
                startY = Math.Min(height - 1, centerY + halfDistance);
                endY = Math.Max(0, centerY - halfDistance);
            }
        }
        else
        {
            var halfDistance = horizontalDistance / 2;
            if (horizontalTicks > 0)
            {
                startX = Math.Min(width - 1, centerX + halfDistance);
                endX = Math.Max(0, centerX - halfDistance);
            }
            else
            {
                startX = Math.Max(0, centerX - halfDistance);
                endX = Math.Min(width - 1, centerX + halfDistance);
            }
        }

        var result = await _adb.ShellAsync($"input swipe {startX} {startY} {endX} {endY} {durationMs}").ConfigureAwait(false);
        result.EnsureSuccess("scroll failed");
        InvalidateUiReadCaches();
        return new ScrollResult(horizontalTicks, verticalTicks, startX, startY, endX, endY, durationMs);
    }

    public async Task<PushFileResult> PushFileAsync(string localPath, string? remoteDirectory = null)
    {
        var validatedLocalPath = Path.GetFullPath(RequireNonBlank(localPath, "push file requires a local path."));
        if (!_fileSystem.FileExists(validatedLocalPath))
        {
            throw new FileNotFoundException($"Host file '{validatedLocalPath}' was not found.", validatedLocalPath);
        }

        var targetDirectory = NormalizeDeviceDirectoryForPush(remoteDirectory);
        var remotePath = $"{targetDirectory}/{Path.GetFileName(validatedLocalPath)}";
        var result = await _adb.RunAsync(["push", validatedLocalPath, remotePath]).ConfigureAwait(false);
        result.EnsureSuccess("push file failed");
        return new PushFileResult(validatedLocalPath, remotePath);
    }

    public async Task<InstallPackageResult> InstallPackageAsync(string packagePath)
    {
        var validatedPackagePath = Path.GetFullPath(RequireNonBlank(packagePath, "install package requires a local path."));
        if (!_fileSystem.FileExists(validatedPackagePath))
        {
            throw new FileNotFoundException($"Host package '{validatedPackagePath}' was not found.", validatedPackagePath);
        }

        var result = await _adb.RunAsync(["install", "-r", validatedPackagePath]).ConfigureAwait(false);
        result.EnsureSuccess("install package failed");
        InvalidateUiReadCaches();
        return new InstallPackageResult(validatedPackagePath);
    }

    public async Task<WaitLogResult> WaitForLogAsync(string text, int timeoutSec)
    {
        var containsText = RequireNonBlank(text, "waitLog requires text.");
        var validatedTimeoutSec = RequirePositive(timeoutSec, "waitLog requires timeoutSec greater than zero.");
        var started = _timeProvider.GetUtcNow();
        var monitor = await _adb.MonitorLogAsync(containsText, started, validatedTimeoutSec).ConfigureAwait(false);
        await _artifacts.WriteTextAsync(DeviceArtifactNames.TextFromBase(DeviceArtifactNames.WaitLogBaseName), monitor.LogOutput).ConfigureAwait(false);
        await _artifacts.WriteJsonAsync(
            DeviceArtifactNames.JsonFromBase(DeviceArtifactNames.WaitLogBaseName),
            new
            {
                schema = ResultSchemas.LogWait,
                contains = containsText,
                timeout_sec = validatedTimeoutSec,
                started_at = started,
                matched_line = monitor.MatchedLine,
                line_count = monitor.LineCount,
                invocation = monitor.Invocation
            }).ConfigureAwait(false);

        if (monitor.ExitCode != 0)
        {
            throw new InvalidOperationException($"adb logcat failed: {monitor.Stderr}".Trim());
        }

        if (monitor.MatchedLine is null)
        {
            throw new LogWaitTimeoutException(containsText, validatedTimeoutSec);
        }

        return new WaitLogResult(containsText, validatedTimeoutSec, monitor.MatchedLine, monitor.LineCount);
    }

    /// <summary>
    /// Reads logcat.
    /// </summary>
    /// <param name="tail">Maximum lines to return.</param>
    /// <returns>Logcat lines.</returns>
    public async Task<LogcatResult> LogcatAsync(int tail)
    {
        var validatedTail = RequirePositive(tail, "logcat requires tail greater than zero.");
        var result = await _adb.RunAsync(["logcat", "-d", "-t", validatedTail.ToString()]).ConfigureAwait(false);
        result.EnsureSuccess("logcat failed");
        await _artifacts.WriteTextAsync(DeviceArtifactNames.LogcatText, result.Stdout).ConfigureAwait(false);
        return new LogcatResult(result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Reads and parses recent semantic telemetry events.
    /// </summary>
    /// <param name="tail">Maximum logcat lines to inspect.</param>
    /// <returns>Telemetry data.</returns>
    public async Task<TelemetryResult> TelemetryTailAsync(int tail)
    {
        var validatedTail = RequirePositive(tail, "telemetryTail requires tail greater than zero.");
        var result = await _adb.RunAsync(["logcat", "-d", "-v", "brief", "-t", validatedTail.ToString()]).ConfigureAwait(false);
        result.EnsureSuccess("telemetry tail failed");
        return await CaptureTelemetryAsync(
            DeviceArtifactNames.TelemetryTailBaseName,
            result.Stdout,
            new
            {
                schema = ResultSchemas.TelemetryTail,
                tail = validatedTail,
                invocation = result.Invocation
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Collects semantic telemetry events over a bounded watch window.
    /// </summary>
    /// <param name="timeoutSec">Duration to watch for telemetry events.</param>
    /// <returns>Telemetry data.</returns>
    public async Task<TelemetryResult> TelemetryWatchAsync(int timeoutSec)
    {
        var validatedTimeoutSec = RequirePositive(timeoutSec, "telemetryWatch requires timeoutSec greater than zero.");
        var telemetrySession = await MonitorTelemetryAsync(validatedTimeoutSec).ConfigureAwait(false);
        return await CaptureTelemetryAsync(
            DeviceArtifactNames.TelemetryWatchBaseName,
            telemetrySession.LogOutput,
            new
            {
                schema = ResultSchemas.TelemetryWatch,
                started_at = telemetrySession.StartedAt,
                timeout_sec = validatedTimeoutSec,
                invocation = telemetrySession.Invocation
            },
            telemetrySession.Parsed).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for a semantic telemetry step event.
    /// </summary>
    /// <param name="step">Expected semantic step name.</param>
    /// <param name="timeoutSec">Timeout in seconds.</param>
    /// <returns>Matched telemetry data.</returns>
    public Task<TelemetryMatchResult> WaitForStepAsync(string step, int timeoutSec)
    {
        var expectedStep = NormalizeTelemetryStep(RequireNonBlank(step, "waitStep requires step."));
        var validatedTimeoutSec = RequirePositive(timeoutSec, "waitStep requires timeoutSec greater than zero.");
        return WaitForTelemetryEventAsync(
            validatedTimeoutSec,
            telemetry => string.Equals(telemetry.Event, "step", StringComparison.OrdinalIgnoreCase)
                && string.Equals(NormalizeTelemetryStep(telemetry.Step), expectedStep, StringComparison.Ordinal),
            telemetry => new TelemetryMatchResult(expectedStep, null, telemetry.RawLine, telemetry.Event!, telemetry.Payload),
            DeviceArtifactNames.WaitStepBaseName,
            invocation => new
            {
                schema = ResultSchemas.WaitStep,
                step = expectedStep,
                timeout_sec = validatedTimeoutSec,
                invocation
            },
            () => new SemanticWaitTimeoutException($"device step '{expectedStep}'", validatedTimeoutSec));
    }

    /// <summary>
    /// Waits for a semantic telemetry action-ready event.
    /// </summary>
    /// <param name="action">Expected action name.</param>
    /// <param name="step">Optional expected step name.</param>
    /// <param name="timeoutSec">Timeout in seconds.</param>
    /// <returns>Matched telemetry data.</returns>
    public Task<TelemetryMatchResult> WaitForActionReadyAsync(string action, string? step, int timeoutSec)
    {
        var expectedAction = RequireNonBlank(action, "waitActionReady requires action.");
        var normalizedStep = NormalizeTelemetryStep(step);
        var validatedTimeoutSec = RequirePositive(timeoutSec, "waitActionReady requires timeoutSec greater than zero.");
        return WaitForTelemetryEventAsync(
            validatedTimeoutSec,
            telemetry =>
                string.Equals(telemetry.Event, "action_ready", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(telemetry.Action, expectedAction, StringComparison.OrdinalIgnoreCase) &&
                (normalizedStep is null || string.Equals(NormalizeTelemetryStep(telemetry.Step), normalizedStep, StringComparison.Ordinal)),
            telemetry => new TelemetryMatchResult(normalizedStep, expectedAction, telemetry.RawLine, telemetry.Event!, telemetry.Payload),
            DeviceArtifactNames.WaitActionReadyBaseName,
            invocation => new
            {
                schema = ResultSchemas.WaitActionReady,
                action = expectedAction,
                step = normalizedStep,
                timeout_sec = validatedTimeoutSec,
                invocation
            },
            () => new SemanticWaitTimeoutException($"device action ready '{expectedAction}'" + (normalizedStep is null ? string.Empty : $" on '{normalizedStep}'"), validatedTimeoutSec));
    }

    /// <summary>
    /// Records video with Android screenrecord.
    /// </summary>
    /// <param name="output">Local output path.</param>
    /// <param name="timeLimitSec">Maximum recording duration.</param>
    /// <returns>Recording metadata.</returns>
    public async Task<RecordResult> RecordAsync(string output, int timeLimitSec)
    {
        var targetOutput = RequireNonBlank(output, "record requires output.");
        var remote = $"/sdcard/device-e2e-{_idGenerator.NewId()}.mp4";
        var clamped = Math.Clamp(timeLimitSec, AndroidRuntimeDefaults.MinRecordTimeLimitSeconds, AndroidRuntimeDefaults.MaxRecordTimeLimitSeconds);
        var record = await _adb.ShellAsync($"screenrecord --time-limit {clamped} {remote}").ConfigureAwait(false);
        record.EnsureSuccess("screenrecord failed");
        var pull = await _adb.RunAsync(["pull", NormalizeDevicePathForPull(remote), targetOutput]).ConfigureAwait(false);
        await _adb.ShellAsync($"rm -f {remote}").ConfigureAwait(false);
        pull.EnsureSuccess("pull recording failed");
        return new RecordResult(targetOutput, clamped);
    }

    public async Task<DeviceFingerprint> WriteDeviceFingerprintAsync()
    {
        var snapshot = await ReadDeviceFingerprintSnapshotAsync().ConfigureAwait(false);
        var fingerprint = new DeviceFingerprint(
            ResultSchemas.DeviceFingerprint,
            _timeProvider.GetUtcNow(),
            snapshot.Serial,
            snapshot.Model,
            snapshot.AndroidRelease,
            snapshot.Sdk,
            snapshot.Fingerprint,
            snapshot.Abi,
            snapshot.CurrentFocus);
        await _artifacts.WriteJsonAsync(DeviceArtifactNames.DeviceFingerprintJson, fingerprint).ConfigureAwait(false);
        return fingerprint;
    }

    public async Task<FailureArtifactBundle> CaptureFailureArtifactsAsync(FailureCaptureRequest request, Exception exception)
    {
        var prefix = BuildFailurePrefix(request);
        var captured = new List<FailureArtifact>();
        var captureFailures = new List<FailureCaptureError>();

        async Task CaptureAsync(string name, Func<Task<string>> action)
        {
            try
            {
                captured.Add(new FailureArtifact(name, await action().ConfigureAwait(false)));
            }
            catch (Exception captureException)
            {
                captureFailures.Add(new FailureCaptureError(name, captureException.Message));
            }
        }

        await CaptureAsync("screenshot", async () =>
        {
            var fileName = DeviceArtifactNames.ScreenshotForLabel(prefix);
            await CaptureScreenshotAsync(fileName).ConfigureAwait(false);
            return fileName;
        }).ConfigureAwait(false);

        await CaptureAsync("logcat", async () =>
        {
            var fileName = DeviceArtifactNames.LogcatForLabel(prefix);
            await CaptureLogcatSnapshotAsync(fileName, 1000).ConfigureAwait(false);
            return fileName;
        }).ConfigureAwait(false);

        await CaptureAsync("screen-state", async () =>
        {
            await CaptureScreenStateWithRetryAsync(prefix).ConfigureAwait(false);
            return DeviceArtifactNames.ScreenStateForLabel(prefix);
        }).ConfigureAwait(false);

        var metadata = new FailureArtifactBundle(
            ResultSchemas.FailureBundle,
            _timeProvider.GetUtcNow(),
            request.Scope,
            request.Name,
            request.File,
            request.StepIndex,
            request.StepName,
            request.Action,
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            captured,
            captureFailures);
        var metadataFile = DeviceArtifactNames.FailureMetadataForLabel(prefix);
        await _artifacts.WriteJsonAsync(metadataFile, metadata).ConfigureAwait(false);
        return metadata with { MetadataFile = metadataFile };
    }

    public async Task<WaitNotVisibleResult> WaitNotVisibleAsync(string text, int timeoutSec)
    {
        var expectedText = RequireNonBlank(text, "waitNotVisible requires text.");
        var validatedTimeoutSec = RequirePositive(timeoutSec, "waitNotVisible requires timeoutSec greater than zero.");
        var deadline = _timeProvider.GetUtcNow().AddSeconds(validatedTimeoutSec);
        var attempt = 0;

        while (_timeProvider.GetUtcNow() < deadline)
        {
            attempt++;
            var snapshotPrefix = $"wait-not-visible-{attempt:000}";
            ScreenCapture capture;

            try
            {
                capture = await CapturePollingScreenStateAsync(snapshotPrefix).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (IsRetryableHierarchyDumpFailure(ex))
            {
                await _delay.DelayAsync(AndroidRuntimeDefaults.UiPollDelayMs).ConfigureAwait(false);
                continue;
            }

            if (!capture.State.Elements.Any(element => element.Matches(expectedText)))
            {
                await PersistPollingArtifactsAsync(capture, snapshotPrefix).ConfigureAwait(false);
                return new WaitNotVisibleResult(expectedText, attempt, false);
            }

            await _delay.DelayAsync(AndroidRuntimeDefaults.UiPollDelayMs).ConfigureAwait(false);
        }

        throw new TimeoutException($"Timed out after {validatedTimeoutSec}s waiting for text '{expectedText}' to disappear.");
    }

    public async Task<TapPointResult> TapPointAsync(string? label, int? x, int? y, double? xRatio, double? yRatio, int postTapDelayMs)
    {
        var validatedPostTapDelayMs = RequireNonNegative(postTapDelayMs, "tapPoint postTapDelayMs must be zero or greater.");

        if (x.HasValue != y.HasValue)
        {
            throw new UsageException("tapPoint requires both x and y when using absolute coordinates.");
        }

        if (x is < 0 || y is < 0)
        {
            throw new UsageException("tapPoint coordinates must be zero or greater.");
        }

        var (resolvedX, resolvedY) = x.HasValue && y.HasValue
            ? (x.Value, y.Value)
            : await ResolveRelativePointAsync(xRatio, yRatio).ConfigureAwait(false);

        var result = await _adb.ShellAsync($"input tap {resolvedX} {resolvedY}").ConfigureAwait(false);
        result.EnsureSuccess("tap point failed");
        InvalidateUiReadCaches();

        if (validatedPostTapDelayMs > 0)
        {
            await _delay.DelayAsync(validatedPostTapDelayMs).ConfigureAwait(false);
        }

        return new TapPointResult(label, resolvedX, resolvedY, xRatio, yRatio, validatedPostTapDelayMs);
    }

    public async Task<DoubleTapHeaderLogoResult> DoubleTapHeaderLogoAsync()
    {
        var (x, y) = await ResolveHeaderLogoTargetAsync().ConfigureAwait(false);
        foreach (var _ in Enumerable.Range(0, 2))
        {
            var tap = await _adb.ShellAsync($"input tap {x} {y}").ConfigureAwait(false);
            tap.EnsureSuccess("double tap header logo failed");
            await _delay.DelayAsync(160).ConfigureAwait(false);
        }

        InvalidateUiReadCaches();

        return new DoubleTapHeaderLogoResult("header_logo", x, y, 160);
    }

    public async Task<TypePinResult> TypePinAsync(string pin, int perDigitDelayMs)
    {
        if (string.IsNullOrWhiteSpace(pin))
        {
            throw new UsageException("typePin requires text.");
        }

        var validatedPerDigitDelayMs = RequireNonNegative(perDigitDelayMs, "typePin intervalMs must be zero or greater.");

        var digits = pin.Trim();
        foreach (var digit in digits)
        {
            if (!char.IsDigit(digit))
            {
                throw new UsageException($"typePin supports digits only. Invalid character '{digit}'.");
            }

            var result = await _adb.ShellAsync($"input keyevent KEYCODE_{digit}").ConfigureAwait(false);
            result.EnsureSuccess("type pin failed");

            if (validatedPerDigitDelayMs > 0)
            {
                await _delay.DelayAsync(validatedPerDigitDelayMs).ConfigureAwait(false);
            }
        }

        InvalidateUiReadCaches();

        return new TypePinResult(digits.Length, validatedPerDigitDelayMs);
    }

    public async Task<ResetLogResult> ResetLogAsync()
    {
        var result = await _adb.RunAsync(["logcat", "-c"]).ConfigureAwait(false);
        result.EnsureSuccess("log reset failed");
        return new ResetLogResult(true);
    }

    public async Task<AssertEventResult> AssertEventAsync(string name, IReadOnlyList<string> contains, string? detailsPattern, int timeoutSec, DateTimeOffset? since = null)
    {
        var eventName = RequireNonBlank(name, "assertEvent requires event or text.");
        var validatedTimeoutSec = RequirePositive(timeoutSec, "assertEvent requires timeoutSec greater than zero.");
        var started = since ?? _timeProvider.GetUtcNow();
        var detailsRegex = CreateDetailsRegex(detailsPattern);
        var monitor = await _adb.MonitorLogAsync(
            started,
            validatedTimeoutSec,
            line => EventLineMatches(line, eventName, contains, detailsRegex)).ConfigureAwait(false);

        if (monitor.ExitCode != 0)
        {
            throw new InvalidOperationException($"adb logcat failed: {monitor.Stderr}".Trim());
        }

        await _artifacts.WriteTextAsync(DeviceArtifactNames.TextFromBase(DeviceArtifactNames.AssertEventBaseName), monitor.LogOutput).ConfigureAwait(false);
        await _artifacts.WriteJsonAsync(
            DeviceArtifactNames.JsonFromBase(DeviceArtifactNames.AssertEventBaseName),
            new
            {
                schema = ResultSchemas.AssertEvent,
                name = eventName,
                contains,
                details_pattern = detailsPattern,
                observed_since = started,
                timeout_sec = validatedTimeoutSec,
                invocation = monitor.Invocation,
                matched_line = monitor.MatchedLine
            }).ConfigureAwait(false);

        if (monitor.MatchedLine is null)
        {
            throw new SemanticWaitTimeoutException($"event '{eventName}'", validatedTimeoutSec);
        }

        return new AssertEventResult(eventName, contains, detailsPattern, monitor.MatchedLine);
    }

    public async Task<TakeScreenshotResult> TakeScreenshotAsync(string label)
    {
        var fileName = DeviceArtifactNames.ScreenshotForLabel(Slugify(label));
        await CaptureScreenshotAsync(fileName).ConfigureAwait(false);
        return new TakeScreenshotResult(label, fileName);
    }

    public async Task<CaptureArtifactsResult> CaptureArtifactsAsync(string label)
    {
        var slug = Slugify(label);
        var screenshot = DeviceArtifactNames.ScreenshotForLabel(slug);
        var logcat = DeviceArtifactNames.LogcatForLabel(slug);
        await CaptureScreenshotAsync(screenshot).ConfigureAwait(false);
        await CaptureLogcatSnapshotAsync(logcat, 500).ConfigureAwait(false);
        await CaptureScreenStateWithRetryAsync(slug).ConfigureAwait(false);
        return new CaptureArtifactsResult(label, screenshot, logcat, DeviceArtifactNames.ScreenStateForLabel(slug), DeviceArtifactNames.HierarchyForLabel(slug));
    }

    public async Task<AssertTextInputReadyResult> AssertTextInputReadyAsync(bool requireKeyboard, int timeoutSec)
    {
        var validatedTimeoutSec = RequirePositive(timeoutSec, "assertTextInputReady requires timeoutSec greater than zero.");
        var deadline = _timeProvider.GetUtcNow().AddSeconds(validatedTimeoutSec);

        while (_timeProvider.GetUtcNow() < deadline)
        {
            var document = await LoadUiDocumentWithRetryAsync().ConfigureAwait(false);
            var focused = document.Descendants("node")
                .FirstOrDefault(node =>
                    ((string?)node.Attribute("class"))?.Contains("EditText", StringComparison.OrdinalIgnoreCase) is true &&
                    bool.TryParse((string?)node.Attribute("focused"), out var isFocused) &&
                    isFocused);

            var keyboardVisible = !requireKeyboard || await IsKeyboardVisibleAsync().ConfigureAwait(false);
            if (focused is not null && keyboardVisible)
            {
                return new AssertTextInputReadyResult(
                    requireKeyboard,
                    keyboardVisible,
                    (string?)focused.Attribute("text"),
                    (string?)focused.Attribute("resource-id"),
                    (string?)focused.Attribute("bounds"));
            }

            await _delay.DelayAsync(250).ConfigureAwait(false);
        }

        throw new TimeoutException($"Timed out after {validatedTimeoutSec}s waiting for a focused text input{(requireKeyboard ? " and visible keyboard" : string.Empty)}.");
    }

    public async Task<AssertBelowResult> AssertBelowAsync(string text, string referenceText, int maxGapPx)
    {
        var subjectText = RequireNonBlank(text, "assertBelow requires text.");
        var anchorText = RequireNonBlank(referenceText, "assertBelow requires below.");
        var validatedMaxGapPx = RequireNonNegative(maxGapPx, "assertBelow maxGapPx must be zero or greater.");
        var state = await CaptureScreenStateWithRetryAsync("assert-below").ConfigureAwait(false);
        var subject = FindSingleMatch(state, subjectText, "assertBelow text");
        var reference = FindSingleMatch(state, anchorText, "assertBelow reference");
        var gapPx = subject.Top - reference.Bottom;

        if (gapPx < 0 || gapPx > validatedMaxGapPx)
        {
            throw new InvalidOperationException($"Expected '{subjectText}' below '{anchorText}' within {validatedMaxGapPx}px, but gap was {gapPx}px.");
        }

        return new AssertBelowResult(subjectText, anchorText, gapPx, validatedMaxGapPx);
    }

    public async Task<AssertAlignedResult> AssertAlignedAsync(string text, string referenceText, int maxDeltaPx)
    {
        var subjectText = RequireNonBlank(text, "assertAligned requires text.");
        var anchorText = RequireNonBlank(referenceText, "assertAligned requires with.");
        var validatedMaxDeltaPx = RequireNonNegative(maxDeltaPx, "assertAligned maxDeltaPx must be zero or greater.");
        var state = await CaptureScreenStateWithRetryAsync("assert-aligned").ConfigureAwait(false);
        var subject = FindSingleMatch(state, subjectText, "assertAligned text");
        var reference = FindSingleMatch(state, anchorText, "assertAligned reference");
        var deltaPx = Math.Abs(subject.CenterX - reference.CenterX);

        if (deltaPx > validatedMaxDeltaPx)
        {
            throw new InvalidOperationException($"Expected '{subjectText}' aligned with '{anchorText}' within {validatedMaxDeltaPx}px, but delta was {deltaPx}px.");
        }

        return new AssertAlignedResult(subjectText, anchorText, deltaPx, validatedMaxDeltaPx);
    }

    public async Task<AssertAppVersionResult> AssertAppVersionAsync(string? packageName, int maxTopInsetPx, int maxRightInsetPx)
    {
        var validatedMaxTopInsetPx = RequireNonNegative(maxTopInsetPx, "assertAppVersion maxTopInsetPx must be zero or greater.");
        var validatedMaxRightInsetPx = RequireNonNegative(maxRightInsetPx, "assertAppVersion maxRightInsetPx must be zero or greater.");
        var activePackage = string.IsNullOrWhiteSpace(packageName)
            ? _environment.GetEnvironmentVariable(AndroidRuntimeDefaults.TargetPackageEnvironmentVariable) ?? AndroidRuntimeDefaults.DefaultTargetPackage
            : packageName;
        var packageInfo = await ShellTextAsync($"dumpsys package {ShellQuote(activePackage)} | grep -E 'versionName=|versionCode=' | head -20").ConfigureAwait(false);
        var versionNameMatch = Regex.Match(packageInfo, @"versionName=(?<value>\S+)");
        var versionCodeMatch = Regex.Match(packageInfo, @"versionCode=(?<value>\d+)");
        if (!versionNameMatch.Success || !versionCodeMatch.Success)
        {
            throw new InvalidOperationException($"Could not read version metadata for package '{activePackage}'.");
        }

        var versionName = versionNameMatch.Groups["value"].Value;
        var versionCode = versionCodeMatch.Groups["value"].Value;
        var expectedLabel = versionName.Contains($"+{versionCode}", StringComparison.Ordinal)
            ? $"v{versionName}"
            : $"v{versionName}+{versionCode}";
        var state = await CaptureScreenStateWithRetryAsync("assert-app-version").ConfigureAwait(false);
        var element = FindSingleMatch(state, expectedLabel, "assertAppVersion text");
        var (width, _) = await GetDisplaySizeAsync().ConfigureAwait(false);
        var topInset = element.Top;
        var rightInset = Math.Max(0, width - element.Right);

        if (topInset > validatedMaxTopInsetPx || rightInset > validatedMaxRightInsetPx)
        {
            throw new InvalidOperationException($"Expected version label '{expectedLabel}' near the top-right corner, but top inset was {topInset}px and right inset was {rightInset}px.");
        }

        return new AssertAppVersionResult(activePackage, expectedLabel, topInset, rightInset, validatedMaxTopInsetPx, validatedMaxRightInsetPx);
    }

    private async Task<bool> IsKeyboardVisibleAsync()
    {
        var now = _timeProvider.GetUtcNow();
        if (_keyboardVisibilityCache is { } cached && now - cached.CapturedAt < AndroidRuntimeDefaults.KeyboardVisibilityCacheTtl)
        {
            return cached.IsVisible;
        }

        var result = await ShellTextAsync("dumpsys input_method | grep -E 'mInputShown=true|mIsInputViewShown=true|mShowRequested=true' | head -1").ConfigureAwait(false);
        var isVisible = !string.IsNullOrWhiteSpace(result);
        _keyboardVisibilityCache = new KeyboardVisibilitySnapshot(isVisible, now);
        return isVisible;
    }

    private async Task<(int X, int Y)> ResolveRelativePointAsync(double? xRatio, double? yRatio)
    {
        if (!xRatio.HasValue || !yRatio.HasValue)
        {
            throw new UsageException("tapPoint requires either x/y or xRatio/yRatio.");
        }

        var validatedXRatio = RequireUnitInterval(xRatio.Value, "tapPoint xRatio must be between 0 and 1.");
        var validatedYRatio = RequireUnitInterval(yRatio.Value, "tapPoint yRatio must be between 0 and 1.");

        var (width, height) = await GetDisplaySizeAsync().ConfigureAwait(false);
        var resolvedX = (int)Math.Round(width * validatedXRatio, MidpointRounding.AwayFromZero);
        var resolvedY = (int)Math.Round(height * validatedYRatio, MidpointRounding.AwayFromZero);
        return (resolvedX, resolvedY);
    }

    private async Task<(int Width, int Height)> GetDisplaySizeAsync()
    {
        if (_displaySizeCache.HasValue)
        {
            return _displaySizeCache.Value;
        }

        var text = await ShellTextAsync("wm size").ConfigureAwait(false);
        var match = Regex.Match(text, @"(?<width>\d+)x(?<height>\d+)");
        if (!match.Success)
        {
            throw new InvalidOperationException($"Could not parse device display size from '{text}'.");
        }

        _displaySizeCache = (
            int.Parse(match.Groups["width"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["height"].Value, CultureInfo.InvariantCulture));
        return _displaySizeCache.Value;
    }

    private void InvalidateKeyboardVisibilityCache() => _keyboardVisibilityCache = null;

    private async Task<(int X, int Y)> ResolveHeaderLogoTargetAsync()
    {
        var document = await LoadUiDocumentWithRetryAsync().ConfigureAwait(false);
        var (width, _) = await GetDisplaySizeAsync().ConfigureAwait(false);
        var centerX = width / 2d;

        var candidates = document.Descendants("node")
            .Where(node => string.Equals((string?)node.Attribute("class"), "android.widget.ImageView", StringComparison.Ordinal))
            .Select(node => ParseNodeBounds((string?)node.Attribute("bounds") ?? "[0,0][0,0]"))
            .Where(bounds => bounds.Top <= 140)
            .Select(bounds => new
            {
                Bounds = bounds,
                Delta = Math.Abs((bounds.Left + bounds.Right) / 2d - centerX)
            })
            .Where(candidate => candidate.Delta <= width * 0.2)
            .OrderBy(candidate => candidate.Delta)
            .ThenBy(candidate => candidate.Bounds.Top)
            .ToArray();

        var match = candidates.FirstOrDefault() ?? throw new InvalidOperationException("Could not find a header logo target near the top center of the screen.");
        return ((match.Bounds.Left + match.Bounds.Right) / 2, (match.Bounds.Top + match.Bounds.Bottom) / 2);
    }

    private async Task<XDocument> LoadUiDocumentAsync()
    {
        var xml = await ReadUiDumpXmlAsync().ConfigureAwait(false);
        try
        {
            var document = XDocument.Parse(xml);
            CacheUiDump(xml);
            return document;
        }
        catch (Exception ex) when (ex is XmlException || ex is InvalidOperationException)
        {
            InvalidateUiDumpCache();
            await _artifacts.WriteTextAsync(DeviceArtifactNames.InvalidHierarchyXml, xml).ConfigureAwait(false);
            throw new InvalidOperationException("UI hierarchy dump was empty or invalid XML.", ex);
        }
    }

    private async Task<XDocument> LoadUiDocumentWithRetryAsync(int maxAttempts = AndroidRuntimeDefaults.UiDumpRetryMaxAttempts, int retryDelayMs = AndroidRuntimeDefaults.UiPollDelayMs)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await LoadUiDocumentAsync().ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (attempt < maxAttempts && IsRetryableHierarchyDumpFailure(ex))
            {
                await _delay.DelayAsync(retryDelayMs).ConfigureAwait(false);
            }
        }
    }

    private static bool EventLineMatches(string line, string name, IReadOnlyList<string> contains, Regex? detailsRegex)
    {
        if (!line.Contains($"Log.{name}", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains(name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (contains.Any(required => !line.Contains(required, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return detailsRegex is null || detailsRegex.IsMatch(line);
    }

    private static ScreenElement FindSingleMatch(ScreenState state, string text, string role)
    {
        var matches = state.Elements.Where(element => element.Matches(text)).ToArray();
        if (matches.Length == 0)
        {
            throw new InvalidOperationException($"No visible element matched '{text}' for {role}.");
        }

        var exactMatches = matches.Where(element =>
            string.Equals(element.Text, text, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(element.ContentDescription, text, StringComparison.OrdinalIgnoreCase)).ToArray();
        var candidates = exactMatches.Length > 0 ? exactMatches : matches;
        if (candidates.Length > 1)
        {
            throw new InvalidOperationException($"Multiple visible elements matched '{text}' for {role}.");
        }

        return candidates[0];
    }

    private static Bounds ParseNodeBounds(string value)
    {
        var numbers = value.Split(['[', ']', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0)
            .ToArray();
        return numbers.Length >= 4 ? new Bounds(numbers[0], numbers[1], numbers[2], numbers[3]) : new Bounds(0, 0, 0, 0);
    }

    private async Task<string> DumpUiAsync()
    {
        var result = await _adb.RunAsync(["exec-out", "uiautomator", "dump", "/dev/tty"]).ConfigureAwait(false);
        result.EnsureSuccess("uiautomator dump failed");
        var xml = result.Stdout;
        var xmlStart = xml.IndexOf("<?xml", StringComparison.Ordinal);
        if (xmlStart < 0)
        {
            xmlStart = xml.IndexOf("<hierarchy", StringComparison.Ordinal);
        }

        var xmlEnd = xml.LastIndexOf("</hierarchy>", StringComparison.Ordinal);
        if (xmlStart >= 0 && xmlEnd >= xmlStart)
        {
            xmlEnd += "</hierarchy>".Length;
            return xml[xmlStart..xmlEnd];
        }

        return xmlStart >= 0 ? xml[xmlStart..] : xml;
    }

    private async Task<string> ReadUiDumpXmlAsync()
    {
        var now = _timeProvider.GetUtcNow();
        if (_uiDumpCache is { } cached && now - cached.CapturedAt < AndroidRuntimeDefaults.UiDumpCacheTtl)
        {
            return cached.Xml;
        }

        return await DumpUiAsync().ConfigureAwait(false);
    }
    private void CacheUiDump(string xml) => _uiDumpCache = new UiDumpSnapshot(xml, _timeProvider.GetUtcNow());

    private void InvalidateUiDumpCache() => _uiDumpCache = null;

    private void InvalidateUiReadCaches()
    {
        _keyboardVisibilityCache = null;
        InvalidateUiDumpCache();
    }

    private async Task<string> ShellTextAsync(string command)
    {
        var result = await _adb.ShellAsync(command).ConfigureAwait(false);
        result.EnsureSuccess($"adb shell failed: {command}");
        return result.Stdout.Trim();
    }

    private async Task<DeviceFingerprintSnapshot> ReadDeviceFingerprintSnapshotAsync()
    {
        var output = await ShellTextAsync(BuildDeviceFingerprintCommand()).ConfigureAwait(false);
        if (TryParseDeviceFingerprintSnapshot(output, out var snapshot))
        {
            return snapshot;
        }

        return await ReadDeviceFingerprintSnapshotIndividuallyAsync().ConfigureAwait(false);
    }

    private async Task<DeviceFingerprintSnapshot> ReadDeviceFingerprintSnapshotIndividuallyAsync() =>
        new(
            await ShellTextAsync("getprop ro.serialno").ConfigureAwait(false),
            await ShellTextAsync("getprop ro.product.model").ConfigureAwait(false),
            await ShellTextAsync("getprop ro.build.version.release").ConfigureAwait(false),
            await ShellTextAsync("getprop ro.build.version.sdk").ConfigureAwait(false),
            await ShellTextAsync("getprop ro.build.fingerprint").ConfigureAwait(false),
            await ShellTextAsync("getprop ro.product.cpu.abilist").ConfigureAwait(false),
            await ShellTextAsync("dumpsys window | grep -E 'mCurrentFocus|mFocusedApp|mResumedActivity' | head -1").ConfigureAwait(false));

    private static string BuildDeviceFingerprintCommand()
    {
        var builder = new StringBuilder();

        for (var index = 0; index < DeviceFingerprintReads.Length; index++)
        {
            if (index > 0)
            {
                builder.Append("; ");
            }

            var field = DeviceFingerprintReads[index];
            builder.Append("echo ")
                .Append(CreateDeviceFingerprintMarker(field.Key))
                .Append("; ")
                .Append(field.Command);
        }

        return builder.ToString();
    }

    private static bool TryParseDeviceFingerprintSnapshot(string output, out DeviceFingerprintSnapshot snapshot)
    {
        var values = DeviceFingerprintReads.ToDictionary(static field => field.Key, static _ => new StringBuilder(), StringComparer.Ordinal);
        var seenMarkers = new HashSet<string>(StringComparer.Ordinal);
        string? currentField = null;

        foreach (var rawLine in output.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var trimmedLine = rawLine.Trim();
            if (DeviceFingerprintMarkers.TryGetValue(trimmedLine, out var field))
            {
                currentField = field;
                seenMarkers.Add(field);
                continue;
            }

            if (currentField is null)
            {
                continue;
            }

            if (values[currentField].Length > 0)
            {
                values[currentField].Append('\n');
            }

            values[currentField].Append(rawLine);
        }

        if (seenMarkers.Count != DeviceFingerprintReads.Length)
        {
            snapshot = default!;
            return false;
        }

        snapshot = new DeviceFingerprintSnapshot(
            values["serial"].ToString().Trim(),
            values["model"].ToString().Trim(),
            values["android_release"].ToString().Trim(),
            values["sdk"].ToString().Trim(),
            values["fingerprint"].ToString().Trim(),
            values["abi"].ToString().Trim(),
            values["current_focus"].ToString().Trim());
        return true;
    }

    private static string CreateDeviceFingerprintMarker(string key) => $"{AndroidRuntimeDefaults.DeviceFingerprintMarkerPrefix}{key.ToUpperInvariant()}__";

    private async Task CaptureScreenshotAsync(string fileName)
    {
        var remote = $"/sdcard/device-e2e-{_idGenerator.NewId()}.png";
        var capture = await _adb.ShellAsync($"screencap {remote}").ConfigureAwait(false);
        capture.EnsureSuccess("screencap failed");
        var pull = await _adb.RunAsync(["pull", NormalizeDevicePathForPull(remote), Path.Combine(_artifacts.Root, fileName)]).ConfigureAwait(false);
        await _adb.ShellAsync($"rm -f {remote}").ConfigureAwait(false);
        pull.EnsureSuccess("pull screenshot failed");
    }

    private async Task CaptureLogcatSnapshotAsync(string fileName, int tail)
    {
        var result = await _adb.RunAsync(["logcat", "-d", "-t", tail.ToString()]).ConfigureAwait(false);
        result.EnsureSuccess("logcat failed");
        await _artifacts.WriteTextAsync(fileName, result.Stdout).ConfigureAwait(false);
    }

    private async Task<ScreenState> CaptureScreenStateWithRetryAsync(string? snapshotPrefix, int maxAttempts = 3, int retryDelayMs = 250)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await CaptureScreenStateAsync(snapshotPrefix).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (attempt < maxAttempts && IsRetryableHierarchyDumpFailure(ex))
            {
                await _delay.DelayAsync(retryDelayMs).ConfigureAwait(false);
            }
        }
    }

    private async Task<TelemetryResult> CaptureTelemetryAsync(string artifactBaseName, string logOutput, object metadata, TelemetryParseResult? parsed = null)
    {
        parsed ??= _telemetryParser.ParseLog(logOutput);
        var result = new TelemetryResult(
            parsed.InspectedLineCount,
            parsed.TelemetryLineCount,
            parsed.Events.Count,
            parsed.ParseErrors.Count,
            parsed.Events,
            parsed.ParseErrors);
        await _artifacts.WriteTextAsync(DeviceArtifactNames.TextFromBase(artifactBaseName), logOutput).ConfigureAwait(false);
        await _artifacts.WriteJsonAsync(
            DeviceArtifactNames.JsonFromBase(artifactBaseName),
            new
            {
                metadata,
                inspected_line_count = result.InspectedLineCount,
                telemetry_line_count = result.TelemetryLineCount,
                event_count = result.EventCount,
                parse_error_count = result.ParseErrorCount,
                events = result.Events,
                parse_errors = result.ParseErrors
            }).ConfigureAwait(false);

        return result;
    }

    private async Task<TelemetryMatchResult> WaitForTelemetryEventAsync(
        int timeoutSec,
        Func<TelemetryEvent, bool> eventMatch,
        Func<TelemetryEvent, TelemetryMatchResult> successDataFactory,
        string artifactBaseName,
        Func<string, object> metadataFactory,
        Func<Exception> timeoutExceptionFactory)
    {
        var telemetrySession = await MonitorTelemetryAsync(timeoutSec, eventMatch).ConfigureAwait(false);
        var match = telemetrySession.MatchedEvent;

        if (match is not null)
        {
            await _artifacts.WriteTextAsync(DeviceArtifactNames.TextFromBase(artifactBaseName), telemetrySession.LogOutput).ConfigureAwait(false);
            await _artifacts.WriteJsonAsync(
                DeviceArtifactNames.JsonFromBase(artifactBaseName),
                new
                {
                    metadata = metadataFactory(telemetrySession.Invocation),
                    event_count = telemetrySession.Parsed.Events.Count,
                    parse_error_count = telemetrySession.Parsed.ParseErrors.Count,
                    matched = successDataFactory(match),
                    events = telemetrySession.Parsed.Events,
                    parse_errors = telemetrySession.Parsed.ParseErrors
                }).ConfigureAwait(false);

            return successDataFactory(match);
        }

        await _artifacts.WriteTextAsync(DeviceArtifactNames.TextFromBase(artifactBaseName), telemetrySession.LogOutput).ConfigureAwait(false);
        await _artifacts.WriteJsonAsync(
            DeviceArtifactNames.JsonFromBase(artifactBaseName),
            new
            {
                metadata = metadataFactory(telemetrySession.Invocation),
                event_count = telemetrySession.Parsed.Events.Count,
                parse_error_count = telemetrySession.Parsed.ParseErrors.Count,
                events = telemetrySession.Parsed.Events,
                parse_errors = telemetrySession.Parsed.ParseErrors
            }).ConfigureAwait(false);

        throw timeoutExceptionFactory();
    }

    private async Task<TelemetryMonitorResult> MonitorTelemetryAsync(int timeoutSec, Func<TelemetryEvent, bool>? eventMatch = null)
    {
        var started = _timeProvider.GetUtcNow();
        var accumulator = new TelemetryStreamAccumulator(_telemetryParser, eventMatch);

        var monitor = await _adb.MonitorLogAsync(
            started,
            timeoutSec,
            eventMatch is null ? null : accumulator.ShouldStop,
            accumulator.ObserveLine).ConfigureAwait(false);

        if (monitor.ExitCode != 0)
        {
            throw new InvalidOperationException($"adb logcat failed: {monitor.Stderr}".Trim());
        }

        return new TelemetryMonitorResult(started, monitor.Invocation, monitor.LogOutput, accumulator.ToParseResult(), accumulator.MatchedEvent);
    }

    private static string RequireNonBlank(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new UsageException(message);
        }

        return value;
    }

    private static int RequirePositive(int value, string message)
    {
        if (value <= 0)
        {
            throw new UsageException(message);
        }

        return value;
    }

    private static int RequireNonNegative(int value, string message)
    {
        if (value < 0)
        {
            throw new UsageException(message);
        }

        return value;
    }

    private static double RequireUnitInterval(double value, string message)
    {
        if (value < 0 || value > 1)
        {
            throw new UsageException(message);
        }

        return value;
    }

    private static Regex? CreateDetailsRegex(string? detailsPattern)
    {
        if (string.IsNullOrWhiteSpace(detailsPattern))
        {
            return null;
        }

        try
        {
            return new Regex(detailsPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException ex)
        {
            throw new UsageException($"assertEvent detailsPattern is not a valid regular expression: {ex.Message}");
        }
    }

    private sealed record ScreenCapture(string Xml, ScreenState State);

    private sealed record KeyboardVisibilitySnapshot(bool IsVisible, DateTimeOffset CapturedAt);

    private sealed record UiDumpSnapshot(string Xml, DateTimeOffset CapturedAt);

    private sealed record DeviceFingerprintSnapshot(
        string Serial,
        string Model,
        string AndroidRelease,
        string Sdk,
        string Fingerprint,
        string Abi,
        string CurrentFocus);

    private sealed record TelemetryMonitorResult(
        DateTimeOffset StartedAt,
        string Invocation,
        string LogOutput,
        TelemetryParseResult Parsed,
        TelemetryEvent? MatchedEvent);

    private sealed class TelemetryStreamAccumulator(ITelemetryParser telemetryParser, Func<TelemetryEvent, bool>? eventMatch)
    {
        private readonly ITelemetryParser _telemetryParser = telemetryParser;
        private readonly Func<TelemetryEvent, bool>? _eventMatch = eventMatch;
        private readonly List<TelemetryEvent> _events = [];
        private readonly List<TelemetryParseError> _parseErrors = [];
        private int _inspectedLineCount;
        private int _telemetryLineCount;

        public TelemetryEvent? MatchedEvent { get; private set; }

        public void ObserveLine(string line)
        {
            var parsedLine = _telemetryParser.ParseLine(line);
            if (!parsedLine.Inspected)
            {
                return;
            }

            _inspectedLineCount++;
            if (parsedLine.TelemetryLine)
            {
                _telemetryLineCount++;
            }

            if (parsedLine.Event is not null)
            {
                _events.Add(parsedLine.Event);
                if (MatchedEvent is null && _eventMatch?.Invoke(parsedLine.Event) is true)
                {
                    MatchedEvent = parsedLine.Event;
                }
            }

            if (parsedLine.ParseError is not null)
            {
                _parseErrors.Add(parsedLine.ParseError);
            }
        }

        public bool ShouldStop(string _) => MatchedEvent is not null;

        public TelemetryParseResult ToParseResult() => new(_events, _parseErrors, _inspectedLineCount, _telemetryLineCount);
    }

    private static string? NormalizeTelemetryStep(string? step)
    {
        if (string.IsNullOrWhiteSpace(step))
        {
            return null;
        }

        var normalized = step.Trim().ToUpperInvariant().Replace('-', '_');
        return normalized.StartsWith("STEP_", StringComparison.Ordinal) ? normalized : $"STEP_{normalized}";
    }

    private string NormalizeDevicePathForPull(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        normalized = normalized.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal);
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Device path '{path}' must be absolute for adb pull.");
        }

        if (normalized.Contains("/../", StringComparison.Ordinal) || normalized.EndsWith("/..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Device path '{path}' contains unsupported parent traversal.");
        }

        var source = _environment.GetEnvironmentVariable("DEVICE_E2E_EMULATED_STORAGE_SOURCE")?.Trim();
        var target = _environment.GetEnvironmentVariable("DEVICE_E2E_EMULATED_STORAGE_TARGET")?.Trim();
        if (!string.IsNullOrWhiteSpace(source) &&
            !string.IsNullOrWhiteSpace(target) &&
            normalized.StartsWith(target, StringComparison.Ordinal) &&
            (normalized.Length == target.Length || normalized[target.Length] == '/'))
        {
            return source + normalized[target.Length..];
        }

        return normalized;
    }

    private static string NormalizeDeviceDirectoryForPush(string? path)
    {
        var normalized = string.IsNullOrWhiteSpace(path) ? "/sdcard/Download" : path.Replace('\\', '/').Trim();
        normalized = normalized.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal).TrimEnd('/');
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Device directory '{path}' must be absolute for adb push.");
        }

        if (normalized.Contains("/../", StringComparison.Ordinal) || normalized.EndsWith("/..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Device directory '{path}' contains unsupported parent traversal.");
        }

        return normalized;
    }

    private string BuildFailurePrefix(FailureCaptureRequest request)
    {
        var parts = new List<string> { "failure" };
        if (request.StepIndex is { } stepIndex)
        {
            parts.Add(stepIndex.ToString("000", CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(request.StepName))
        {
            parts.Add(Slugify(request.StepName));
        }
        else if (!string.IsNullOrWhiteSpace(request.Name))
        {
            parts.Add(Slugify(request.Name));
        }

        return string.Join("-", parts.Where(static part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-');
        }

        return string.Join("-", builder.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}
