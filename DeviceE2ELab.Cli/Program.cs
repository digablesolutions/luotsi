using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;

namespace DeviceE2ELab.Cli;

/// <summary>
/// Console program entry point.
/// </summary>
public static class Program
{
    /// <summary>
    /// Runs the CLI.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>The process exit code.</returns>
    public static async Task<int> Main(string[] args)
    {
        var app = new App();
        return await app.RunAsync(args).ConfigureAwait(false);
    }
}

/// <summary>
/// Entry point for the device E2E lab command-line application.
/// </summary>
public sealed class App
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly TimeProvider _timeProvider;
    private readonly IFileSystem _fileSystem;
    private readonly IProcessRunner _processRunner;
    private readonly IDelay _delay;
    private readonly IAdbClientFactory _adbClientFactory;
    private readonly IConsoleIO _console;
    private readonly IEnvironmentVariables _environment;
    private readonly IUniqueIdGenerator _idGenerator;

    public App(
        TimeProvider? timeProvider = null,
        IFileSystem? fileSystem = null,
        IProcessRunner? processRunner = null,
        IDelay? delay = null,
        IAdbClientFactory? adbClientFactory = null,
        IConsoleIO? console = null,
        IEnvironmentVariables? environment = null,
        IUniqueIdGenerator? idGenerator = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _fileSystem = fileSystem ?? new PhysicalFileSystem();
        _processRunner = processRunner ?? new DefaultProcessRunner();
        _delay = delay ?? new TaskDelay(_timeProvider);
        _adbClientFactory = adbClientFactory ?? new DefaultAdbClientFactory();
        _console = console ?? new SystemConsoleIO();
        _environment = environment ?? new SystemEnvironmentVariables();
        _idGenerator = idGenerator ?? new GuidUniqueIdGenerator();
    }

    /// <summary>
    /// Runs the command-line application.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>The process exit code.</returns>
    public async Task<int> RunAsync(string[] args)
    {
        var started = _timeProvider.GetUtcNow();
        var options = CliOptions.Parse(args);
        if (options.Command is null || options.HasFlag("help") || options.HasFlag("h"))
        {
            _console.WriteErrorLine(Help.Text);
            return options.HasFlag("help") || options.HasFlag("h") ? 0 : 2;
        }

        var artifacts = ArtifactSession.Create(options, _fileSystem, _timeProvider);
        var adb = _adbClientFactory.Create(options.Get("adb") ?? _environment.GetEnvironmentVariable("DEVICE_E2E_ADB") ?? "adb", options.Get("device"), _processRunner);
        var runner = new DeviceRunner(adb, artifacts, _timeProvider, _delay, _fileSystem, _idGenerator);
        var scenarios = new ScenarioExecutor(runner, _fileSystem, _timeProvider, _delay);

        try
        {
            object data = options.Command switch
            {
                "devices" => await runner.GetDevicesAsync().ConfigureAwait(false),
                "preflight" => await runner.PreflightAsync(options.Get("package")).ConfigureAwait(false),
                "screen-state" => await runner.GetScreenStateAsync().ConfigureAwait(false),
                "tap" => await runner.TapAsync(options.Require("x"), options.Require("y")).ConfigureAwait(false),
                "tap-text" => await runner.TapTextAsync(options.Require("text"), options.Int("timeout-sec", 15)).ConfigureAwait(false),
                "wait-visible" => await runner.WaitVisibleAsync(options.Require("text"), options.Int("timeout-sec", 15)).ConfigureAwait(false),
                "type-text" => await runner.TypeTextAsync(options.Require("text")).ConfigureAwait(false),
                "keyevent" => await runner.KeyEventAsync(options.Require("code")).ConfigureAwait(false),
                "logcat" => await runner.LogcatAsync(options.Int("tail", 200)).ConfigureAwait(false),
                "record" => await runner.RecordAsync(options.Require("output"), options.Int("time-limit-sec", 30)).ConfigureAwait(false),
                "run" => await scenarios.RunAsync(options.Require("file")).ConfigureAwait(false),
                _ => throw new UsageException($"Unknown command '{options.Command}'."),
            };

            WriteEnvelope(new CommandEnvelope(true, options.Command, started, _timeProvider.GetUtcNow(), data, artifacts.ToData(), null));
            return 0;
        }
        catch (UsageException ex)
        {
            WriteEnvelope(new CommandEnvelope(false, options.Command, started, _timeProvider.GetUtcNow(), null, artifacts.ToData(), ErrorInfo.From(ex, "usage_error")));
            return 2;
        }
        catch (Exception ex)
        {
            WriteEnvelope(new CommandEnvelope(false, options.Command, started, _timeProvider.GetUtcNow(), null, artifacts.ToData(), ErrorInfo.From(ex, ErrorInfo.Classify(ex.Message))));
            return 1;
        }
    }

    private void WriteEnvelope(CommandEnvelope envelope)
    {
        _console.WriteLine(JsonSerializer.Serialize(envelope, JsonOptions));
    }
}

/// <summary>
/// Minimal command-line parser for command plus dash-prefixed options.
/// </summary>
public sealed class CliOptions
{
    private static readonly FrozenSet<string> KnownCommands =
    new[]
    {
        "devices",
        "preflight",
        "screen-state",
        "tap",
        "tap-text",
        "wait-visible",
        "type-text",
        "keyevent",
        "logcat",
        "record",
        "run",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

    private CliOptions(string? command)
    {
        Command = command;
    }

    /// <summary>
    /// Gets the command name.
    /// </summary>
    public string? Command { get; }

    /// <summary>
    /// Parses command-line arguments.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Parsed options.</returns>
    public static CliOptions Parse(string[] args)
    {
        var command = args.FirstOrDefault(static a => KnownCommands.Contains(a));
        var parsed = new CliOptions(command);

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (string.Equals(token, command, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!token.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            var key = token.TrimStart('-');
            string? value = "true";
            if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
            {
                value = args[++i];
            }

            parsed._values[key] = value;
        }

        return parsed;
    }

    /// <summary>
    /// Gets an optional option value.
    /// </summary>
    /// <param name="key">Option name.</param>
    /// <returns>The option value, if supplied.</returns>
    public string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// Gets whether a flag was supplied.
    /// </summary>
    /// <param name="key">Flag name.</param>
    /// <returns>True when the flag was supplied.</returns>
    public bool HasFlag(string key) => _values.ContainsKey(key);

    /// <summary>
    /// Gets a required option.
    /// </summary>
    /// <param name="key">Option name.</param>
    /// <returns>The option value.</returns>
    public string Require(string key) => Get(key) ?? throw new UsageException($"Missing required option --{key}.");

    /// <summary>
    /// Gets an integer option.
    /// </summary>
    /// <param name="key">Option name.</param>
    /// <param name="defaultValue">Default value.</param>
    /// <returns>The parsed integer.</returns>
    public int Int(string key, int defaultValue)
    {
        var value = Get(key);
        if (value is null)
        {
            return defaultValue;
        }

        return int.TryParse(value, out var parsed) ? parsed : throw new UsageException($"Option --{key} must be an integer.");
    }
}

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
    public async Task<ProcessResult> RunAsync(IEnumerable<string> args, CancellationToken cancellationToken = default)
    {
        var finalArgs = new List<string>();
        if (_serial is not null)
        {
            finalArgs.Add("-s");
            finalArgs.Add(_serial);
        }

        finalArgs.AddRange(args);
        return await _processRunner.RunAsync(_executable, finalArgs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs an adb shell command.
    /// </summary>
    /// <param name="command">Shell command text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Process result.</returns>
    public Task<ProcessResult> ShellAsync(string command, CancellationToken cancellationToken = default) =>
        RunAsync(new[] { "shell", command }, cancellationToken);
}

/// <summary>
/// Device operation facade used by the command handlers.
/// </summary>
public sealed class DeviceRunner(IAdbClient adb, ArtifactSession artifacts, TimeProvider? timeProvider = null, IDelay? delay = null, IFileSystem? fileSystem = null, IUniqueIdGenerator? idGenerator = null) : IScenarioActionHost
{
    private readonly IAdbClient _adb = adb ?? throw new ArgumentNullException(nameof(adb));
    private readonly ArtifactSession _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly IDelay _delay = delay ?? new TaskDelay(timeProvider);
    private readonly IFileSystem _fileSystem = fileSystem ?? new PhysicalFileSystem();
    private readonly IUniqueIdGenerator _idGenerator = idGenerator ?? new GuidUniqueIdGenerator();

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
        var model = await ShellTextAsync("getprop ro.product.model").ConfigureAwait(false);
        var release = await ShellTextAsync("getprop ro.build.version.release").ConfigureAwait(false);
        var sdk = await ShellTextAsync("getprop ro.build.version.sdk").ConfigureAwait(false);
        var focus = await ShellTextAsync("dumpsys window | grep -E 'mCurrentFocus|mFocusedApp|mResumedActivity' | head -1").ConfigureAwait(false);
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

        return new { model, android_release = release, sdk, current_focus = focus, package = packageName, package_info = packageInfo };
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
            var state = await CaptureScreenStateAsync($"wait-visible-{attempt:000}").ConfigureAwait(false);
            last = state.Elements.FirstOrDefault(e => e.Matches(text));
            if (last is not null)
            {
                return last;
            }

            await _delay.DelayAsync(500).ConfigureAwait(false);
        }

        throw new TimeoutException($"Timed out after {timeoutSec}s waiting for visible text '{text}'. Last seen: {last?.StableId ?? "none"}");
    }

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
        var pull = await _adb.RunAsync(new[] { "pull", remote, output }).ConfigureAwait(false);
        await _adb.ShellAsync($"rm -f {remote}").ConfigureAwait(false);
        pull.EnsureSuccess("pull recording failed");
        return new { output, time_limit_sec = clamped };
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

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}

/// <summary>
/// Process result.
/// </summary>
/// <param name="ExitCode">Exit code.</param>
/// <param name="Stdout">Captured stdout.</param>
/// <param name="Stderr">Captured stderr.</param>
public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr)
{
    /// <summary>
    /// Throws when the process failed.
    /// </summary>
    /// <param name="message">Failure context.</param>
    public void EnsureSuccess(string message)
    {
        if (ExitCode != 0)
        {
            throw new InvalidOperationException($"{message}: exit {ExitCode}. {Stderr}".Trim());
        }
    }
}

/// <summary>
/// A per-command artifact session.
/// </summary>
public sealed class ArtifactSession
{
    private readonly IFileSystem _fileSystem;

    private ArtifactSession(string root, IFileSystem fileSystem)
    {
        Root = root;
        _fileSystem = fileSystem;
        _fileSystem.CreateDirectory(root);
    }

    /// <summary>
    /// Gets the artifact root path.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// Creates an artifact session from CLI options.
    /// </summary>
    /// <param name="options">CLI options.</param>
    /// <returns>Artifact session.</returns>
    public static ArtifactSession Create(CliOptions options, IFileSystem? fileSystem = null, TimeProvider? timeProvider = null)
    {
        var activeFileSystem = fileSystem ?? new PhysicalFileSystem();
        var activeTimeProvider = timeProvider ?? TimeProvider.System;
        var baseDir = options.Get("artifacts") ?? Path.Combine(activeFileSystem.GetTempPath(), "device-e2e-lab");
        var name = $"{activeTimeProvider.GetUtcNow():yyyyMMdd-HHmmss}-{options.Command ?? "command"}";
        return new ArtifactSession(Path.Combine(baseDir, name), activeFileSystem);
    }

    /// <summary>
    /// Writes a text artifact.
    /// </summary>
    /// <param name="name">File name.</param>
    /// <param name="text">Text content.</param>
    public Task WriteTextAsync(string name, string text) => _fileSystem.WriteAllTextAsync(Path.Combine(Root, name), text, Encoding.UTF8);

    /// <summary>
    /// Writes a JSON artifact.
    /// </summary>
    /// <param name="name">File name.</param>
    /// <param name="value">Value to serialize.</param>
    public Task WriteJsonAsync(string name, object value) => WriteTextAsync(name, JsonSerializer.Serialize(value, AppJson.Options));

    /// <summary>
    /// Returns JSON envelope artifact data.
    /// </summary>
    /// <returns>Artifact metadata.</returns>
    public object ToData() => new { artifact_root = Root };
}

/// <summary>
/// Normalized screen state.
/// </summary>
/// <param name="CapturedAt">Capture time.</param>
/// <param name="ElementCount">Element count.</param>
/// <param name="Elements">Elements.</param>
public sealed record ScreenState(DateTimeOffset CapturedAt, int ElementCount, IReadOnlyList<ScreenElement> Elements);

/// <summary>
/// Normalized UI element from uiautomator XML.
/// </summary>
public sealed record ScreenElement(
    string? Text,
    string? ContentDescription,
    string? ResourceId,
    string? ClassName,
    bool Enabled,
    bool Clickable,
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    /// <summary>
    /// Gets whether the element is useful for agent reasoning.
    /// </summary>
    public bool IsUseful => !string.IsNullOrWhiteSpace(Text) || !string.IsNullOrWhiteSpace(ContentDescription) || Clickable;

    /// <summary>
    /// Gets the X center.
    /// </summary>
    public int CenterX => (Left + Right) / 2;

    /// <summary>
    /// Gets the Y center.
    /// </summary>
    public int CenterY => (Top + Bottom) / 2;

    /// <summary>
    /// Gets a stable-ish identifier for debugging.
    /// </summary>
    public string StableId => string.Join("|", new[] { Text, ContentDescription, ResourceId, ClassName }.Where(static value => !string.IsNullOrWhiteSpace(value)));

    /// <summary>
    /// Creates an element from a UIAutomator XML node.
    /// </summary>
    /// <param name="node">XML node.</param>
    /// <returns>Screen element.</returns>
    public static ScreenElement From(XElement node)
    {
        var bounds = ParseBounds((string?)node.Attribute("bounds") ?? "[0,0][0,0]");
        return new ScreenElement(
            (string?)node.Attribute("text"),
            (string?)node.Attribute("content-desc"),
            (string?)node.Attribute("resource-id"),
            (string?)node.Attribute("class"),
            bool.TryParse((string?)node.Attribute("enabled"), out var enabled) && enabled,
            bool.TryParse((string?)node.Attribute("clickable"), out var clickable) && clickable,
            bounds.Left,
            bounds.Top,
            bounds.Right,
            bounds.Bottom);
    }

    /// <summary>
    /// Returns whether this element matches text.
    /// </summary>
    /// <param name="value">Text to find.</param>
    /// <returns>True on text or content-desc match.</returns>
    public bool Matches(string value) =>
        string.Equals(Text, value, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ContentDescription, value, StringComparison.OrdinalIgnoreCase) ||
        (Text?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (ContentDescription?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false);

    private static Bounds ParseBounds(string value)
    {
        var numbers = value.Split(new[] { '[', ']', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => int.TryParse(part, out var parsed) ? parsed : 0)
            .ToArray();
        return numbers.Length >= 4 ? new Bounds(numbers[0], numbers[1], numbers[2], numbers[3]) : new Bounds(0, 0, 0, 0);
    }
}

/// <summary>
/// Rectangle bounds.
/// </summary>
/// <param name="Left">Left edge.</param>
/// <param name="Top">Top edge.</param>
/// <param name="Right">Right edge.</param>
/// <param name="Bottom">Bottom edge.</param>
public sealed record Bounds(int Left, int Top, int Right, int Bottom);

/// <summary>
/// Scenario playbook file.
/// </summary>
/// <param name="Name">Scenario name.</param>
/// <param name="Steps">Scenario steps.</param>
public sealed record ScenarioFile(string Name, IReadOnlyList<ScenarioStep> Steps);

/// <summary>
/// Scenario playbook step.
/// </summary>
/// <param name="Name">Optional step name.</param>
/// <param name="Action">Action name.</param>
/// <param name="Text">Text argument.</param>
/// <param name="Code">Keyevent argument.</param>
/// <param name="TimeoutSec">Timeout in seconds.</param>
/// <param name="Milliseconds">Sleep duration.</param>
public sealed record ScenarioStep(string? Name, string Action, string? Text, string? Code, int? TimeoutSec, int? Milliseconds);

/// <summary>
/// JSON command envelope.
/// </summary>
public sealed record CommandEnvelope(bool Ok, string? Command, DateTimeOffset StartedAt, DateTimeOffset EndedAt, object? Data, object Artifacts, ErrorInfo? Error)
{
    /// <summary>
    /// Gets the schema name.
    /// </summary>
    public string Schema => "device-e2e-lab-command.v1";

    /// <summary>
    /// Gets duration in milliseconds.
    /// </summary>
    public long DurationMs => (long)(EndedAt - StartedAt).TotalMilliseconds;
}

/// <summary>
/// Structured error information.
/// </summary>
public sealed record ErrorInfo(string Type, string Message, string Category)
{
    /// <summary>
    /// Creates error info from an exception.
    /// </summary>
    /// <param name="exception">Exception.</param>
    /// <param name="category">Error category.</param>
    /// <returns>Error info.</returns>
    public static ErrorInfo From(Exception exception, string category) => new(exception.GetType().FullName ?? exception.GetType().Name, exception.Message, category);

    /// <summary>
    /// Classifies an error message.
    /// </summary>
    /// <param name="message">Error message.</param>
    /// <returns>Error category.</returns>
    public static string Classify(string message)
    {
        if (message.Contains("must be an integer", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("not valid JSON", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Missing required option", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Unknown command", StringComparison.OrdinalIgnoreCase))
        {
            return "usage_error";
        }

        if (message.Contains("Timed out", StringComparison.OrdinalIgnoreCase))
        {
            return "selector_or_screen_state";
        }

        if (message.Contains("not the foreground app", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("not installed", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("trying to start process", StringComparison.OrdinalIgnoreCase))
        {
            return "configuration_error";
        }

        return "scenario_error";
    }
}

/// <summary>
/// Usage error.
/// </summary>
public sealed class UsageException(string message) : Exception(message);

/// <summary>
/// Application JSON settings.
/// </summary>
public static class AppJson
{
    /// <summary>
    /// Shared serializer options.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}

/// <summary>
/// Help text.
/// </summary>
public static class Help
{
    /// <summary>
    /// Gets command-line help.
    /// </summary>
    public const string Text = """
DeviceE2ELab.Cli

Usage:
  dotnet run --project DeviceE2ELab.Cli -- <command> [options]

Commands:
  devices
  preflight --package <app.id>
  screen-state
  wait-visible --text <label> [--timeout-sec 15]
  tap-text --text <label> [--timeout-sec 15]
  tap --x <px> --y <px>
  type-text --text <value>
  keyevent --code <code>
  logcat [--tail 200]
  record --output <file.mp4> [--time-limit-sec 30]
  run --file <scenario.json>

Common options:
  --device <adb serial>
  --adb <adb executable>
  --artifacts <directory>

Design:
  The CLI is intentionally host-side and cross-platform. It borrows scrcpy's
  separation of host orchestration from device primitives, but it keeps the v1
  implementation on boring ADB commands so it is easy for agents and CI to run.
""";
}
