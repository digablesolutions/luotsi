using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    /// <summary>
    /// Runs the command-line application.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>The process exit code.</returns>
    public async Task<int> RunAsync(string[] args)
    {
        var started = DateTimeOffset.UtcNow;
        var options = CliOptions.Parse(args);
        if (options.Command is null || options.HasFlag("help") || options.HasFlag("h"))
        {
            Console.Error.WriteLine(Help.Text);
            return options.HasFlag("help") || options.HasFlag("h") ? 0 : 2;
        }

        var artifacts = ArtifactSession.Create(options);
        var adb = new AdbClient(options.Get("adb") ?? Environment.GetEnvironmentVariable("DEVICE_E2E_ADB") ?? "adb", options.Get("device"));
        var runner = new DeviceRunner(adb, artifacts);

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
                "run" => await runner.RunScenarioAsync(options.Require("file")).ConfigureAwait(false),
                _ => throw new UsageException($"Unknown command '{options.Command}'."),
            };

            WriteEnvelope(new CommandEnvelope(true, options.Command, started, DateTimeOffset.UtcNow, data, artifacts.ToData(), null));
            return 0;
        }
        catch (UsageException ex)
        {
            WriteEnvelope(new CommandEnvelope(false, options.Command, started, DateTimeOffset.UtcNow, null, artifacts.ToData(), ErrorInfo.From(ex, "usage_error")));
            return 2;
        }
        catch (Exception ex)
        {
            WriteEnvelope(new CommandEnvelope(false, options.Command, started, DateTimeOffset.UtcNow, null, artifacts.ToData(), ErrorInfo.From(ex, ErrorInfo.Classify(ex.Message))));
            return 1;
        }
    }

    private static void WriteEnvelope(CommandEnvelope envelope)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(envelope, JsonOptions));
    }
}

/// <summary>
/// Minimal command-line parser for command plus dash-prefixed options.
/// </summary>
public sealed class CliOptions
{
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
        var command = args.FirstOrDefault(static a => !a.StartsWith("-", StringComparison.Ordinal));
        var parsed = new CliOptions(command);

        for (var i = command is null ? 0 : 1; i < args.Length; i++)
        {
            var token = args[i];
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
public sealed class AdbClient(string executable, string? serial)
{
    private readonly string _executable = string.IsNullOrWhiteSpace(executable) ? throw new ArgumentException("ADB executable is required.", nameof(executable)) : executable;
    private readonly string? _serial = string.IsNullOrWhiteSpace(serial) ? null : serial;

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
        return await ProcessRunner.RunAsync(_executable, finalArgs, cancellationToken).ConfigureAwait(false);
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
public sealed class DeviceRunner(AdbClient adb, ArtifactSession artifacts)
{
    private readonly AdbClient _adb = adb ?? throw new ArgumentNullException(nameof(adb));
    private readonly ArtifactSession _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));

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
        var doc = XDocument.Parse(xml);
        var elements = doc.Descendants("node")
            .Select(static node => ScreenElement.From(node))
            .Where(static element => element.IsUseful)
            .ToArray();
        var state = new ScreenState(DateTimeOffset.UtcNow, elements.Length, elements);
        await _artifacts.WriteJsonAsync("screen-state.json", state).ConfigureAwait(false);
        await _artifacts.WriteTextAsync("hierarchy.xml", xml).ConfigureAwait(false);
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
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSec);
        ScreenElement? last = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var state = await GetScreenStateAsync().ConfigureAwait(false);
            last = state.Elements.FirstOrDefault(e => e.Matches(text));
            if (last is not null)
            {
                return last;
            }

            await Task.Delay(500).ConfigureAwait(false);
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
        var result = await _adb.ShellAsync($"input tap {int.Parse(x)} {int.Parse(y)}").ConfigureAwait(false);
        result.EnsureSuccess("tap failed");
        return new { x = int.Parse(x), y = int.Parse(y) };
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
        var remote = $"/sdcard/device-e2e-{Guid.NewGuid():N}.mp4";
        var clamped = Math.Clamp(timeLimitSec, 1, 180);
        var record = await _adb.ShellAsync($"screenrecord --time-limit {clamped} {remote}").ConfigureAwait(false);
        record.EnsureSuccess("screenrecord failed");
        var pull = await _adb.RunAsync(new[] { "pull", remote, output }).ConfigureAwait(false);
        await _adb.ShellAsync($"rm -f {remote}").ConfigureAwait(false);
        pull.EnsureSuccess("pull recording failed");
        return new { output, time_limit_sec = clamped };
    }

    /// <summary>
    /// Runs a JSON scenario playbook.
    /// </summary>
    /// <param name="file">Scenario file path.</param>
    /// <returns>Scenario result.</returns>
    public async Task<object> RunScenarioAsync(string file)
    {
        var scenario = JsonSerializer.Deserialize<ScenarioFile>(await File.ReadAllTextAsync(file).ConfigureAwait(false), AppJson.Options)
            ?? throw new UsageException($"Scenario file '{file}' was empty.");
        var steps = new List<object>();
        foreach (var step in scenario.Steps)
        {
            var started = DateTimeOffset.UtcNow;
            object result = step.Action switch
            {
                "waitVisible" => await WaitVisibleAsync(step.Text ?? throw new UsageException("waitVisible requires text."), step.TimeoutSec ?? 15).ConfigureAwait(false),
                "tapText" => await TapTextAsync(step.Text ?? throw new UsageException("tapText requires text."), step.TimeoutSec ?? 15).ConfigureAwait(false),
                "typeText" => await TypeTextAsync(step.Text ?? throw new UsageException("typeText requires text.")).ConfigureAwait(false),
                "keyevent" => await KeyEventAsync(step.Code ?? throw new UsageException("keyevent requires code.")).ConfigureAwait(false),
                "sleep" => await SleepAsync(step.Milliseconds ?? 1000).ConfigureAwait(false),
                _ => throw new UsageException($"Unknown scenario action '{step.Action}'."),
            };
            steps.Add(new { step = step.Name ?? step.Action, action = step.Action, duration_ms = (DateTimeOffset.UtcNow - started).TotalMilliseconds, result });
        }

        return new { scenario = scenario.Name, status = "passed", steps };
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

    private static async Task<object> SleepAsync(int milliseconds)
    {
        await Task.Delay(Math.Max(0, milliseconds)).ConfigureAwait(false);
        return new { milliseconds };
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}

/// <summary>
/// Cross-platform process runner.
/// </summary>
public static class ProcessRunner
{
    /// <summary>
    /// Starts a process and captures stdout/stderr.
    /// </summary>
    /// <param name="fileName">Executable.</param>
    /// <param name="args">Arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Process result.</returns>
    public static async Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> args, CancellationToken cancellationToken = default)
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
    private ArtifactSession(string root)
    {
        Root = root;
        Directory.CreateDirectory(root);
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
    public static ArtifactSession Create(CliOptions options)
    {
        var baseDir = options.Get("artifacts") ?? Path.Combine(Path.GetTempPath(), "device-e2e-lab");
        var name = $"{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{options.Command ?? "command"}";
        return new ArtifactSession(Path.Combine(baseDir, name));
    }

    /// <summary>
    /// Writes a text artifact.
    /// </summary>
    /// <param name="name">File name.</param>
    /// <param name="text">Text content.</param>
    public Task WriteTextAsync(string name, string text) => File.WriteAllTextAsync(Path.Combine(Root, name), text, Encoding.UTF8);

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
