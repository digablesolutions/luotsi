using System.Collections.Frozen;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace VisitLab.Cli;

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
    private const string DefaultKioskPackage = "fi.systam.visit";

    private readonly IAdbClient _adb = adb ?? throw new ArgumentNullException(nameof(adb));
    private readonly ArtifactSession _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly IDelay _delay = delay ?? new TaskDelay(timeProvider);
    private readonly IFileSystem _fileSystem = fileSystem ?? new PhysicalFileSystem();
    private readonly IUniqueIdGenerator _idGenerator = idGenerator ?? new GuidUniqueIdGenerator();
    private readonly IEnvironmentVariables _environment = environment ?? new SystemEnvironmentVariables();
    private readonly ITelemetryParser _telemetryParser = telemetryParser ?? new DeviceTestTelemetryParser();

    /// <summary>
    /// Lists connected devices.
    /// </summary>
    /// <returns>Device list data.</returns>
    public async Task<object> GetDevicesAsync()
    {
        var result = await _adb.RunAsync(new[] { "devices", "-l" }).ConfigureAwait(false);
        result.EnsureSuccess("adb devices failed");
        var devices = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0 && !line.StartsWith("*", StringComparison.Ordinal))
            .Select(static line =>
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return new { serial = parts.ElementAtOrDefault(0), status = parts.ElementAtOrDefault(1), details = string.Join(' ', parts.Skip(2)) };
            })
            .ToArray();
        return new { devices };
    }

    /// <summary>
    /// Checks device and application readiness.
    /// </summary>
    /// <param name="packageName">Optional package name expected to be installed and focused.</param>
    /// <returns>Preflight data.</returns>
    public async Task<object> PreflightAsync(string? packageName)
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

        return new
        {
            model = fingerprint.Model,
            android_release = fingerprint.AndroidRelease,
            sdk = fingerprint.Sdk,
            current_focus = focus,
            package = packageName,
            package_info = packageInfo,
            fingerprint = fingerprint.Fingerprint,
            abi = fingerprint.Abi,
            serial = fingerprint.Serial,
        };
    }

    /// <summary>
    /// Captures and normalizes the current UI hierarchy.
    /// </summary>
    /// <returns>Screen state data.</returns>
    public async Task<ScreenState> GetScreenStateAsync()
    {
        var xml = await DumpUiAsync().ConfigureAwait(false);
        await _artifacts.WriteTextAsync("hierarchy.xml", xml).ConfigureAwait(false);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (Exception ex) when (ex is XmlException || ex is InvalidOperationException)
        {
            await _artifacts.WriteTextAsync("hierarchy-invalid.xml", xml).ConfigureAwait(false);
            throw new InvalidOperationException("UI hierarchy dump was empty or invalid XML. See hierarchy-invalid.xml for the raw dump.", ex);
        }

        var elements = doc.Descendants("node")
            .Select(static node => ScreenElement.From(node))
            .Where(static element => element.IsUseful)
            .ToArray();
        var state = new ScreenState(_timeProvider.GetUtcNow(), elements.Length, elements);
        await _artifacts.WriteJsonAsync("screen-state.json", state).ConfigureAwait(false);
        return state;
    }

    private async Task<ScreenState> CaptureScreenStateAsync(string? snapshotPrefix)
    {
        var state = await GetScreenStateAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(snapshotPrefix))
        {
            return state;
        }

        var screenStatePath = Path.Combine(_artifacts.Root, "screen-state.json");
        var hierarchyPath = Path.Combine(_artifacts.Root, "hierarchy.xml");
        _fileSystem.CopyFile(screenStatePath, Path.Combine(_artifacts.Root, $"{snapshotPrefix}-screen-state.json"), true);
        _fileSystem.CopyFile(hierarchyPath, Path.Combine(_artifacts.Root, $"{snapshotPrefix}-hierarchy.xml"), true);

        var invalidHierarchyPath = Path.Combine(_artifacts.Root, "hierarchy-invalid.xml");
        if (_fileSystem.FileExists(invalidHierarchyPath))
        {
            _fileSystem.CopyFile(invalidHierarchyPath, Path.Combine(_artifacts.Root, $"{snapshotPrefix}-hierarchy-invalid.xml"), true);
        }

        return state;
    }

    /// <summary>
    /// Waits for visible text.
    /// </summary>
    /// <param name="text">Text or content description to find.</param>
    /// <param name="timeoutSec">Timeout in seconds.</param>
    /// <returns>Matched element.</returns>
    public async Task<ScreenElement> WaitVisibleAsync(string text, int timeoutSec)
    {
        var deadline = _timeProvider.GetUtcNow().AddSeconds(timeoutSec);
        ScreenElement? last = null;
        var attempt = 0;
        while (_timeProvider.GetUtcNow() < deadline)
        {
            attempt++;
            ScreenState state;

            try
            {
                state = await CaptureScreenStateAsync($"wait-visible-{attempt:000}").ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (IsRetryableHierarchyDumpFailure(ex))
            {
                await _delay.DelayAsync(500).ConfigureAwait(false);
                continue;
            }

            last = state.Elements
                .Select(element => new { Element = element, Score = element.GetMatchScore(text) })
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .Select(candidate => candidate.Element)
                .FirstOrDefault();
            if (last is not null)
            {
                return last;
            }

            await _delay.DelayAsync(500).ConfigureAwait(false);
        }

        throw new TimeoutException($"Timed out after {timeoutSec}s waiting for visible text '{text}'. Last seen: {last?.StableId ?? "none"}");
    }

    private static bool IsRetryableHierarchyDumpFailure(InvalidOperationException exception) =>
        exception.Message.Contains("UI hierarchy dump was empty or invalid XML", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Taps the center of visible text.
    /// </summary>
    /// <param name="text">Text or content description to tap.</param>
    /// <param name="timeoutSec">Timeout in seconds.</param>
    /// <returns>Tap data.</returns>
    public async Task<object> TapTextAsync(string text, int timeoutSec)
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
    public async Task<object> TapAsync(string x, string y)
    {
        if (!int.TryParse(x, out var parsedX))
        {
            throw new UsageException("Option --x must be an integer.");
        }

        if (!int.TryParse(y, out var parsedY))
        {
            throw new UsageException("Option --y must be an integer.");
        }

        var result = await _adb.ShellAsync($"input tap {parsedX} {parsedY}").ConfigureAwait(false);
        result.EnsureSuccess("tap failed");
        return new { x = parsedX, y = parsedY };
    }

    /// <summary>
    /// Types text via adb input.
    /// </summary>
    /// <param name="text">Text to type.</param>
    /// <returns>Typed text metadata.</returns>
    public async Task<object> TypeTextAsync(string text)
    {
        var escaped = text.Replace("%", "%25", StringComparison.Ordinal).Replace(" ", "%s", StringComparison.Ordinal);
        var result = await _adb.ShellAsync($"input text {ShellQuote(escaped)}").ConfigureAwait(false);
        result.EnsureSuccess("type text failed");
        return new { text };
    }

    /// <summary>
    /// Sends an Android keyevent.
    /// </summary>
    /// <param name="code">Keyevent code or name.</param>
    /// <returns>Keyevent metadata.</returns>
    public async Task<object> KeyEventAsync(string code)
    {
        var result = await _adb.ShellAsync($"input keyevent {ShellQuote(code)}").ConfigureAwait(false);
        result.EnsureSuccess("keyevent failed");
        return new { code };
    }

    public async Task<object> WaitForLogAsync(string text, int timeoutSec)
    {
        var started = _timeProvider.GetUtcNow();
        var monitor = await _adb.MonitorLogAsync(text, started, timeoutSec).ConfigureAwait(false);
        await _artifacts.WriteTextAsync("wait-log.txt", monitor.LogOutput).ConfigureAwait(false);
        await _artifacts.WriteJsonAsync(
            "wait-log.json",
            new
            {
                schema = "visit-lab-log-wait.v1",
                contains = text,
                timeout_sec = timeoutSec,
                started_at = started,
                matched_line = monitor.MatchedLine,
                line_count = monitor.LineCount,
                invocation = monitor.Invocation,
            }).ConfigureAwait(false);

        if (monitor.ExitCode != 0)
        {
            throw new InvalidOperationException($"adb logcat failed: {monitor.Stderr}".Trim());
        }

        if (monitor.MatchedLine is null)
        {
            throw new LogWaitTimeoutException(text, timeoutSec);
        }

        return new { contains = text, timeout_sec = timeoutSec, matched_line = monitor.MatchedLine, line_count = monitor.LineCount };
    }

    /// <summary>
    /// Reads logcat.
    /// </summary>
    /// <param name="tail">Maximum lines to return.</param>
    /// <returns>Logcat lines.</returns>
    public async Task<object> LogcatAsync(int tail)
    {
        var result = await _adb.RunAsync(new[] { "logcat", "-d", "-t", tail.ToString() }).ConfigureAwait(false);
        result.EnsureSuccess("logcat failed");
        await _artifacts.WriteTextAsync("logcat.txt", result.Stdout).ConfigureAwait(false);
        return new { lines = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries) };
    }

    /// <summary>
    /// Reads and parses recent semantic telemetry events.
    /// </summary>
    /// <param name="tail">Maximum logcat lines to inspect.</param>
    /// <returns>Telemetry data.</returns>
    public async Task<object> TelemetryTailAsync(int tail)
    {
        var result = await _adb.RunAsync(["logcat", "-d", "-v", "brief", "-t", tail.ToString()]).ConfigureAwait(false);
        result.EnsureSuccess("telemetry tail failed");
        return await CaptureTelemetryAsync(
            "telemetry-tail",
            result.Stdout,
            new
            {
                schema = "visit-lab-telemetry-tail.v1",
                tail,
                invocation = result.Invocation,
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Collects semantic telemetry events over a bounded watch window.
    /// </summary>
    /// <param name="timeoutSec">Duration to watch for telemetry events.</param>
    /// <returns>Telemetry data.</returns>
    public async Task<object> TelemetryWatchAsync(int timeoutSec)
    {
        var started = _timeProvider.GetUtcNow();
        await _delay.DelayAsync(Math.Max(1, timeoutSec) * 1000).ConfigureAwait(false);
        var result = await _adb.RunAsync([
            "logcat",
            "-v",
            "brief",
            "-T",
            LogcatTime.FormatSince(started),
            "-d",
            "*:V"]).ConfigureAwait(false);
        result.EnsureSuccess("telemetry watch failed");
        return await CaptureTelemetryAsync(
            "telemetry-watch",
            result.Stdout,
            new
            {
                schema = "visit-lab-telemetry-watch.v1",
                started_at = started,
                timeout_sec = timeoutSec,
                invocation = result.Invocation,
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for a semantic telemetry step event.
    /// </summary>
    /// <param name="step">Expected semantic step name.</param>
    /// <param name="timeoutSec">Timeout in seconds.</param>
    /// <returns>Matched telemetry data.</returns>
    public Task<object> WaitForStepAsync(string step, int timeoutSec)
    {
        var expectedStep = NormalizeTelemetryStep(step);
        return WaitForTelemetryEventAsync(
            timeoutSec,
            telemetry => string.Equals(telemetry.Event, "step", StringComparison.OrdinalIgnoreCase)
                && string.Equals(NormalizeTelemetryStep(telemetry.Step), expectedStep, StringComparison.Ordinal),
            telemetry => new
            {
                step = expectedStep,
                line = telemetry.RawLine,
                event_name = telemetry.Event,
                payload = telemetry.Payload,
            },
            "wait-step",
            invocation => new
            {
                schema = "visit-lab-wait-step.v1",
                step = expectedStep,
                timeout_sec = timeoutSec,
                invocation,
            },
            () => new SemanticWaitTimeoutException($"device step '{expectedStep}'", timeoutSec));
    }

    /// <summary>
    /// Waits for a semantic telemetry action-ready event.
    /// </summary>
    /// <param name="action">Expected action name.</param>
    /// <param name="step">Optional expected step name.</param>
    /// <param name="timeoutSec">Timeout in seconds.</param>
    /// <returns>Matched telemetry data.</returns>
    public Task<object> WaitForActionReadyAsync(string action, string? step, int timeoutSec)
    {
        var normalizedStep = NormalizeTelemetryStep(step);
        return WaitForTelemetryEventAsync(
            timeoutSec,
            telemetry =>
                string.Equals(telemetry.Event, "action_ready", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(telemetry.Action, action, StringComparison.OrdinalIgnoreCase) &&
                (normalizedStep is null || string.Equals(NormalizeTelemetryStep(telemetry.Step), normalizedStep, StringComparison.Ordinal)),
            telemetry => new
            {
                action,
                step = normalizedStep,
                line = telemetry.RawLine,
                event_name = telemetry.Event,
                payload = telemetry.Payload,
            },
            "wait-action-ready",
            invocation => new
            {
                schema = "visit-lab-wait-action-ready.v1",
                action,
                step = normalizedStep,
                timeout_sec = timeoutSec,
                invocation,
            },
            () => new SemanticWaitTimeoutException($"device action ready '{action}'" + (normalizedStep is null ? string.Empty : $" on '{normalizedStep}'"), timeoutSec));
    }

    /// <summary>
    /// Records video with Android screenrecord.
    /// </summary>
    /// <param name="output">Local output path.</param>
    /// <param name="timeLimitSec">Maximum recording duration.</param>
    /// <returns>Recording metadata.</returns>
    public async Task<object> RecordAsync(string output, int timeLimitSec)
    {
        var remote = $"/sdcard/device-e2e-{_idGenerator.NewId()}.mp4";
        var clamped = Math.Clamp(timeLimitSec, 1, 180);
        var record = await _adb.ShellAsync($"screenrecord --time-limit {clamped} {remote}").ConfigureAwait(false);
        record.EnsureSuccess("screenrecord failed");
        var pull = await _adb.RunAsync(new[] { "pull", NormalizeDevicePathForPull(remote), output }).ConfigureAwait(false);
        await _adb.ShellAsync($"rm -f {remote}").ConfigureAwait(false);
        pull.EnsureSuccess("pull recording failed");
        return new { output, time_limit_sec = clamped };
    }

    public async Task<DeviceFingerprint> WriteDeviceFingerprintAsync()
    {
        var fingerprint = new DeviceFingerprint(
            "device-fingerprint.v1",
            _timeProvider.GetUtcNow(),
            await ShellTextAsync("getprop ro.serialno").ConfigureAwait(false),
            await ShellTextAsync("getprop ro.product.model").ConfigureAwait(false),
            await ShellTextAsync("getprop ro.build.version.release").ConfigureAwait(false),
            await ShellTextAsync("getprop ro.build.version.sdk").ConfigureAwait(false),
            await ShellTextAsync("getprop ro.build.fingerprint").ConfigureAwait(false),
            await ShellTextAsync("getprop ro.product.cpu.abilist").ConfigureAwait(false),
            await ShellTextAsync("dumpsys window | grep -E 'mCurrentFocus|mFocusedApp|mResumedActivity' | head -1").ConfigureAwait(false));
        await _artifacts.WriteJsonAsync("device-fingerprint.json", fingerprint).ConfigureAwait(false);
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
            var fileName = $"{prefix}-screenshot.png";
            await CaptureScreenshotAsync(fileName).ConfigureAwait(false);
            return fileName;
        }).ConfigureAwait(false);

        await CaptureAsync("logcat", async () =>
        {
            var fileName = $"{prefix}-logcat.txt";
            await CaptureLogcatSnapshotAsync(fileName, 1000).ConfigureAwait(false);
            return fileName;
        }).ConfigureAwait(false);

        await CaptureAsync("screen-state", async () =>
        {
            await CaptureScreenStateAsync(prefix).ConfigureAwait(false);
            return $"{prefix}-screen-state.json";
        }).ConfigureAwait(false);

        var metadata = new FailureArtifactBundle(
            "visit-lab-failure-bundle.v1",
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
        await _artifacts.WriteJsonAsync($"{prefix}-failure.json", metadata).ConfigureAwait(false);
        return metadata with { MetadataFile = $"{prefix}-failure.json" };
    }

    public async Task<object> WaitNotVisibleAsync(string text, int timeoutSec)
    {
        var deadline = _timeProvider.GetUtcNow().AddSeconds(Math.Max(1, timeoutSec));
        var attempt = 0;

        while (_timeProvider.GetUtcNow() < deadline)
        {
            attempt++;
            ScreenState state;

            try
            {
                state = await CaptureScreenStateAsync($"wait-not-visible-{attempt:000}").ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (IsRetryableHierarchyDumpFailure(ex))
            {
                await _delay.DelayAsync(500).ConfigureAwait(false);
                continue;
            }

            if (!state.Elements.Any(element => element.Matches(text)))
            {
                return new { text, attempt_count = attempt, visible = false };
            }

            await _delay.DelayAsync(500).ConfigureAwait(false);
        }

        throw new TimeoutException($"Timed out after {timeoutSec}s waiting for text '{text}' to disappear.");
    }

    public async Task<object> TapPointAsync(string? label, int? x, int? y, double? xRatio, double? yRatio, int postTapDelayMs)
    {
        if (x.HasValue != y.HasValue)
        {
            throw new UsageException("tapPoint requires both x and y when using absolute coordinates.");
        }

        var (resolvedX, resolvedY) = x.HasValue && y.HasValue
            ? (x.Value, y.Value)
            : await ResolveRelativePointAsync(xRatio, yRatio).ConfigureAwait(false);

        var result = await _adb.ShellAsync($"input tap {resolvedX} {resolvedY}").ConfigureAwait(false);
        result.EnsureSuccess("tap point failed");

        if (postTapDelayMs > 0)
        {
            await _delay.DelayAsync(postTapDelayMs).ConfigureAwait(false);
        }

        return new
        {
            label,
            x = resolvedX,
            y = resolvedY,
            x_ratio = xRatio,
            y_ratio = yRatio,
            post_tap_delay_ms = Math.Max(0, postTapDelayMs),
        };
    }

    public async Task<object> DoubleTapHeaderLogoAsync()
    {
        var (x, y) = await ResolveHeaderLogoTargetAsync().ConfigureAwait(false);
        foreach (var _ in Enumerable.Range(0, 2))
        {
            var tap = await _adb.ShellAsync($"input tap {x} {y}").ConfigureAwait(false);
            tap.EnsureSuccess("double tap header logo failed");
            await _delay.DelayAsync(160).ConfigureAwait(false);
        }

        return new { target = "header_logo", x, y, interval_ms = 160 };
    }

    public async Task<object> TypePinAsync(string pin, int perDigitDelayMs)
    {
        if (string.IsNullOrWhiteSpace(pin))
        {
            throw new UsageException("typePin requires text.");
        }

        var digits = pin.Trim();
        foreach (var digit in digits)
        {
            if (!char.IsDigit(digit))
            {
                throw new UsageException($"typePin supports digits only. Invalid character '{digit}'.");
            }

            var result = await _adb.ShellAsync($"input keyevent KEYCODE_{digit}").ConfigureAwait(false);
            result.EnsureSuccess("type pin failed");

            if (perDigitDelayMs > 0)
            {
                await _delay.DelayAsync(perDigitDelayMs).ConfigureAwait(false);
            }
        }

        return new { pin_length = digits.Length, per_digit_delay_ms = Math.Max(0, perDigitDelayMs) };
    }

    public async Task<object> ResetLogAsync()
    {
        var result = await _adb.RunAsync(["logcat", "-c"]).ConfigureAwait(false);
        result.EnsureSuccess("log reset failed");
        return new { cleared = true };
    }

    public async Task<object> AssertEventAsync(string name, IReadOnlyList<string> contains, string? detailsPattern, int timeoutSec)
    {
        var started = _timeProvider.GetUtcNow();
        var deadline = started.AddSeconds(Math.Max(1, timeoutSec));
        string? invocation = null;
        string lastLog = string.Empty;
        string? matchedLine = null;
        var detailsRegex = string.IsNullOrWhiteSpace(detailsPattern) ? null : new Regex(detailsPattern, RegexOptions.IgnoreCase);

        while (_timeProvider.GetUtcNow() < deadline)
        {
            var result = await _adb.RunAsync([
                "logcat",
                "-d",
                "-v",
                "brief",
                "-T",
                LogcatTime.FormatSince(started),
                "*:V"]).ConfigureAwait(false);
            result.EnsureSuccess("assert event failed");
            invocation = result.Invocation;
            lastLog = result.Stdout;
            matchedLine = lastLog.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => line.TrimEnd('\r'))
                .LastOrDefault(line => EventLineMatches(line, name, contains, detailsRegex));

            if (matchedLine is not null)
            {
                break;
            }

            await _delay.DelayAsync(250).ConfigureAwait(false);
        }

        await _artifacts.WriteTextAsync("assert-event.txt", lastLog).ConfigureAwait(false);
        await _artifacts.WriteJsonAsync(
            "assert-event.json",
            new
            {
                schema = "visit-lab-assert-event.v1",
                name,
                contains,
                details_pattern = detailsPattern,
                timeout_sec = timeoutSec,
                invocation,
                matched_line = matchedLine,
            }).ConfigureAwait(false);

        if (matchedLine is null)
        {
            throw new SemanticWaitTimeoutException($"event '{name}'", timeoutSec);
        }

        return new { name, contains, details_pattern = detailsPattern, matched_line = matchedLine };
    }

    public async Task<object> TakeScreenshotAsync(string label)
    {
        var fileName = $"{Slugify(label)}-screenshot.png";
        await CaptureScreenshotAsync(fileName).ConfigureAwait(false);
        return new { label, file = fileName };
    }

    public async Task<object> CaptureArtifactsAsync(string label)
    {
        var slug = Slugify(label);
        var screenshot = $"{slug}-screenshot.png";
        var logcat = $"{slug}-logcat.txt";
        await CaptureScreenshotAsync(screenshot).ConfigureAwait(false);
        await CaptureLogcatSnapshotAsync(logcat, 500).ConfigureAwait(false);
        await CaptureScreenStateAsync(slug).ConfigureAwait(false);
        return new
        {
            label,
            screenshot,
            logcat,
            screen_state = $"{slug}-screen-state.json",
            hierarchy = $"{slug}-hierarchy.xml",
        };
    }

    public async Task<object> AssertTextInputReadyAsync(bool requireKeyboard, int timeoutSec)
    {
        var deadline = _timeProvider.GetUtcNow().AddSeconds(Math.Max(1, timeoutSec));

        while (_timeProvider.GetUtcNow() < deadline)
        {
            var document = await LoadUiDocumentAsync().ConfigureAwait(false);
            var focused = document.Descendants("node")
                .FirstOrDefault(node =>
                    ((string?)node.Attribute("class"))?.Contains("EditText", StringComparison.OrdinalIgnoreCase) is true &&
                    bool.TryParse((string?)node.Attribute("focused"), out var isFocused) &&
                    isFocused);

            var keyboardVisible = !requireKeyboard || await IsKeyboardVisibleAsync().ConfigureAwait(false);
            if (focused is not null && keyboardVisible)
            {
                return new
                {
                    require_keyboard = requireKeyboard,
                    keyboard_visible = keyboardVisible,
                    text = (string?)focused.Attribute("text"),
                    resource_id = (string?)focused.Attribute("resource-id"),
                    bounds = (string?)focused.Attribute("bounds"),
                };
            }

            await _delay.DelayAsync(250).ConfigureAwait(false);
        }

        throw new TimeoutException($"Timed out after {timeoutSec}s waiting for a focused text input{(requireKeyboard ? " and visible keyboard" : string.Empty)}.");
    }

    public async Task<object> AssertBelowAsync(string text, string referenceText, int maxGapPx)
    {
        var state = await CaptureScreenStateAsync("assert-below").ConfigureAwait(false);
        var subject = FindSingleMatch(state, text, "assertBelow text");
        var reference = FindSingleMatch(state, referenceText, "assertBelow reference");
        var gapPx = subject.Top - reference.Bottom;

        if (gapPx < 0 || gapPx > maxGapPx)
        {
            throw new InvalidOperationException($"Expected '{text}' below '{referenceText}' within {maxGapPx}px, but gap was {gapPx}px.");
        }

        return new { text, below = referenceText, gap_px = gapPx, max_gap_px = maxGapPx };
    }

    public async Task<object> AssertAlignedAsync(string text, string referenceText, int maxDeltaPx)
    {
        var state = await CaptureScreenStateAsync("assert-aligned").ConfigureAwait(false);
        var subject = FindSingleMatch(state, text, "assertAligned text");
        var reference = FindSingleMatch(state, referenceText, "assertAligned reference");
        var deltaPx = Math.Abs(subject.CenterX - reference.CenterX);

        if (deltaPx > maxDeltaPx)
        {
            throw new InvalidOperationException($"Expected '{text}' aligned with '{referenceText}' within {maxDeltaPx}px, but delta was {deltaPx}px.");
        }

        return new { text, with = referenceText, delta_px = deltaPx, max_delta_px = maxDeltaPx };
    }

    public async Task<object> AssertAppVersionAsync(string? packageName, int maxTopInsetPx, int maxRightInsetPx)
    {
        var activePackage = string.IsNullOrWhiteSpace(packageName)
            ? _environment.GetEnvironmentVariable("DEVICE_TEST_PACKAGE") ?? DefaultKioskPackage
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
        var state = await CaptureScreenStateAsync("assert-app-version").ConfigureAwait(false);
        var element = FindSingleMatch(state, expectedLabel, "assertAppVersion text");
        var (width, _) = await GetDisplaySizeAsync().ConfigureAwait(false);
        var topInset = element.Top;
        var rightInset = Math.Max(0, width - element.Right);

        if (topInset > maxTopInsetPx || rightInset > maxRightInsetPx)
        {
            throw new InvalidOperationException($"Expected version label '{expectedLabel}' near the top-right corner, but top inset was {topInset}px and right inset was {rightInset}px.");
        }

        return new
        {
            package = activePackage,
            label = expectedLabel,
            top_inset_px = topInset,
            right_inset_px = rightInset,
            max_top_inset_px = maxTopInsetPx,
            max_right_inset_px = maxRightInsetPx,
        };
    }

    private async Task<bool> IsKeyboardVisibleAsync()
    {
        var result = await ShellTextAsync("dumpsys input_method | grep -E 'mInputShown=true|mIsInputViewShown=true|mShowRequested=true' | head -1").ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(result);
    }

    private async Task<(int X, int Y)> ResolveRelativePointAsync(double? xRatio, double? yRatio)
    {
        if (!xRatio.HasValue || !yRatio.HasValue)
        {
            throw new UsageException("tapPoint requires either x/y or xRatio/yRatio.");
        }

        var (width, height) = await GetDisplaySizeAsync().ConfigureAwait(false);
        var resolvedX = (int)Math.Round(width * xRatio.Value, MidpointRounding.AwayFromZero);
        var resolvedY = (int)Math.Round(height * yRatio.Value, MidpointRounding.AwayFromZero);
        return (resolvedX, resolvedY);
    }

    private async Task<(int Width, int Height)> GetDisplaySizeAsync()
    {
        var text = await ShellTextAsync("wm size").ConfigureAwait(false);
        var match = Regex.Match(text, @"(?<width>\d+)x(?<height>\d+)");
        if (!match.Success)
        {
            throw new InvalidOperationException($"Could not parse device display size from '{text}'.");
        }

        return (
            int.Parse(match.Groups["width"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["height"].Value, CultureInfo.InvariantCulture));
    }

    private async Task<(int X, int Y)> ResolveHeaderLogoTargetAsync()
    {
        var document = await LoadUiDocumentAsync().ConfigureAwait(false);
        var (width, _) = await GetDisplaySizeAsync().ConfigureAwait(false);
        var centerX = width / 2d;

        var candidates = document.Descendants("node")
            .Where(node => string.Equals((string?)node.Attribute("class"), "android.widget.ImageView", StringComparison.Ordinal))
            .Select(node => ParseNodeBounds((string?)node.Attribute("bounds") ?? "[0,0][0,0]"))
            .Where(bounds => bounds.Top <= 140)
            .Select(bounds => new
            {
                Bounds = bounds,
                Delta = Math.Abs(((bounds.Left + bounds.Right) / 2d) - centerX),
            })
            .Where(candidate => candidate.Delta <= width * 0.2)
            .OrderBy(candidate => candidate.Delta)
            .ThenBy(candidate => candidate.Bounds.Top)
            .ToArray();

        var match = candidates.FirstOrDefault() ?? throw new InvalidOperationException("Could not find a header logo target near the top center of the screen.");
        return (((match.Bounds.Left + match.Bounds.Right) / 2), ((match.Bounds.Top + match.Bounds.Bottom) / 2));
    }

    private async Task<XDocument> LoadUiDocumentAsync()
    {
        var xml = await DumpUiAsync().ConfigureAwait(false);
        try
        {
            return XDocument.Parse(xml);
        }
        catch (Exception ex) when (ex is XmlException || ex is InvalidOperationException)
        {
            throw new InvalidOperationException("UI hierarchy dump was empty or invalid XML.", ex);
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
        var numbers = value.Split(new[] { '[', ']', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0)
            .ToArray();
        return numbers.Length >= 4 ? new Bounds(numbers[0], numbers[1], numbers[2], numbers[3]) : new Bounds(0, 0, 0, 0);
    }

    private async Task<string> DumpUiAsync()
    {
        var command = "rm -f /sdcard/.device-e2e-dump.xml; uiautomator dump /sdcard/.device-e2e-dump.xml >/dev/null 2>&1; cat /sdcard/.device-e2e-dump.xml; rm -f /sdcard/.device-e2e-dump.xml";
        var result = await _adb.ShellAsync(command).ConfigureAwait(false);
        result.EnsureSuccess("uiautomator dump failed");
        return result.Stdout;
    }

    private async Task<string> ShellTextAsync(string command)
    {
        var result = await _adb.ShellAsync(command).ConfigureAwait(false);
        result.EnsureSuccess($"adb shell failed: {command}");
        return result.Stdout.Trim();
    }

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

    private async Task<object> CaptureTelemetryAsync(string artifactBaseName, string logOutput, object metadata)
    {
        var parsed = _telemetryParser.ParseLog(logOutput);
        await _artifacts.WriteTextAsync($"{artifactBaseName}.txt", logOutput).ConfigureAwait(false);
        await _artifacts.WriteJsonAsync(
            $"{artifactBaseName}.json",
            new
            {
                metadata,
                inspected_line_count = parsed.InspectedLineCount,
                telemetry_line_count = parsed.TelemetryLineCount,
                event_count = parsed.Events.Count,
                parse_error_count = parsed.ParseErrors.Count,
                events = parsed.Events,
                parse_errors = parsed.ParseErrors,
            }).ConfigureAwait(false);

        return new
        {
            inspected_line_count = parsed.InspectedLineCount,
            telemetry_line_count = parsed.TelemetryLineCount,
            event_count = parsed.Events.Count,
            parse_error_count = parsed.ParseErrors.Count,
            events = parsed.Events,
            parse_errors = parsed.ParseErrors,
        };
    }

    private async Task<object> WaitForTelemetryEventAsync(
        int timeoutSec,
        Func<TelemetryEvent, bool> eventMatch,
        Func<TelemetryEvent, object> successDataFactory,
        string artifactBaseName,
        Func<string, object> metadataFactory,
        Func<Exception> timeoutExceptionFactory)
    {
        var started = _timeProvider.GetUtcNow();
        var deadline = started.AddSeconds(Math.Max(1, timeoutSec));
        string lastLogOutput = string.Empty;
        string? invocation = null;
        TelemetryParseResult lastParsed = new([], [], 0, 0);

        while (_timeProvider.GetUtcNow() < deadline)
        {
            var result = await _adb.RunAsync([
                "logcat",
                "-d",
                "-v",
                "brief",
                "-T",
                LogcatTime.FormatSince(started),
                "*:V"]).ConfigureAwait(false);
            result.EnsureSuccess("telemetry wait failed");
            invocation = result.Invocation;
            lastLogOutput = result.Stdout;
            lastParsed = _telemetryParser.ParseLog(lastLogOutput);

            var match = lastParsed.Events.LastOrDefault(eventMatch);
            if (match is not null)
            {
                await _artifacts.WriteTextAsync($"{artifactBaseName}.txt", lastLogOutput).ConfigureAwait(false);
                await _artifacts.WriteJsonAsync(
                    $"{artifactBaseName}.json",
                    new
                    {
                        metadata = metadataFactory(invocation),
                        event_count = lastParsed.Events.Count,
                        parse_error_count = lastParsed.ParseErrors.Count,
                        matched = successDataFactory(match),
                        events = lastParsed.Events,
                        parse_errors = lastParsed.ParseErrors,
                    }).ConfigureAwait(false);
                return successDataFactory(match);
            }

            await _delay.DelayAsync(250).ConfigureAwait(false);
        }

        if (invocation is not null)
        {
            await _artifacts.WriteTextAsync($"{artifactBaseName}.txt", lastLogOutput).ConfigureAwait(false);
            await _artifacts.WriteJsonAsync(
                $"{artifactBaseName}.json",
                new
                {
                    metadata = metadataFactory(invocation),
                    event_count = lastParsed.Events.Count,
                    parse_error_count = lastParsed.ParseErrors.Count,
                    events = lastParsed.Events,
                    parse_errors = lastParsed.ParseErrors,
                }).ConfigureAwait(false);
        }

        throw timeoutExceptionFactory();
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

    private string BuildFailurePrefix(FailureCaptureRequest request)
    {
        var parts = new List<string> { "failure" };
        if (request.StepIndex is int stepIndex)
        {
            parts.Add(stepIndex.ToString("000", System.Globalization.CultureInfo.InvariantCulture));
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
