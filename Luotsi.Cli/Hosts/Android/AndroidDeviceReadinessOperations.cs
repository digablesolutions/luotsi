using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Hosts.Android;

internal sealed class AndroidDeviceReadinessOperations(
    IAdbClient adb,
    ArtifactSession artifacts,
    TimeProvider timeProvider)
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
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public Task<AdbDiagnosticResult> GetAdbServerStatusAsync() =>
        RunAdbDiagnosticAsync("server-status", ["server-status"]);

    public Task<AdbDiagnosticResult> GetAdbVersionAsync() =>
        RunAdbDiagnosticAsync("version", ["version"]);

    public Task<AdbDiagnosticResult> GetAdbFeaturesAsync() =>
        RunAdbDiagnosticAsync("features", ["features"]);

    public Task<AdbDiagnosticResult> CheckAdbMdnsAsync() =>
        RunAdbDiagnosticAsync("mdns check", ["mdns", "check"]);

    public Task<AdbDiagnosticResult> ReconnectAdbAsync(string target)
    {
        var reconnectTarget = string.IsNullOrWhiteSpace(target) ? "offline" : target.Trim();
        return RunAdbDiagnosticAsync($"reconnect {reconnectTarget}", ["reconnect", reconnectTarget]);
    }

    public async Task<AdbReadinessResult> WaitForDeviceAsync(int timeoutSec)
    {
        var validatedTimeoutSec = RequirePositive(timeoutSec, "wait-for-device requires timeoutSec greater than zero.");
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(validatedTimeoutSec));

        try
        {
            var wait = await _adb.RunAsync(["wait-for-device"], timeoutSource.Token).ConfigureAwait(false);
            wait.EnsureSuccess("adb wait-for-device failed");

            var ping = await _adb.ShellAsync("echo ping", timeoutSource.Token).ConfigureAwait(false);
            ping.EnsureSuccess("adb readiness ping failed");
            var pingOutput = ping.Stdout.Trim();
            if (!string.Equals(pingOutput, "ping", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"adb readiness ping returned '{pingOutput}'.");
            }

            string? serial = wait.Serial;
            if (string.IsNullOrWhiteSpace(serial))
            {
                var serialProbe = await _adb.ShellAsync("getprop ro.serialno", timeoutSource.Token).ConfigureAwait(false);
                serialProbe.EnsureSuccess("adb readiness serial probe failed");
                serial = serialProbe.Stdout.Trim();
            }

            serial = string.IsNullOrWhiteSpace(serial) ? null : serial;

            return new AdbReadinessResult(
                ResultSchemas.AdbReadiness,
                true,
                serial,
                serial is not null || ping.ExitCode == 0,
                ping.ExitCode == 0,
                validatedTimeoutSec,
                ToAdbCommandOutput(wait),
                ToAdbCommandOutput(ping),
                pingOutput);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out after {validatedTimeoutSec}s waiting for adb device readiness.");
        }
    }

    public async Task<PreflightResult> ReadPreflightAsync(string? packageName)
    {
        var fingerprint = await ReadDeviceFingerprintAsync().ConfigureAwait(false);
        return await CreatePreflightResultAsync(fingerprint, packageName).ConfigureAwait(false);
    }

    public async Task<PreflightResult> PreflightAsync(string? packageName)
    {
        var fingerprint = await WriteDeviceFingerprintAsync().ConfigureAwait(false);
        return await CreatePreflightResultAsync(fingerprint, packageName).ConfigureAwait(false);
    }

    public async Task<DeviceFingerprint> WriteDeviceFingerprintAsync()
    {
        var fingerprint = await ReadDeviceFingerprintAsync().ConfigureAwait(false);
        await _artifacts.WriteJsonAsync(DeviceArtifactNames.DeviceFingerprintJson, fingerprint).ConfigureAwait(false);
        return fingerprint;
    }

    private async Task<DeviceFingerprint> ReadDeviceFingerprintAsync()
    {
        var snapshot = await ReadDeviceFingerprintSnapshotAsync().ConfigureAwait(false);
        return new DeviceFingerprint(
            ResultSchemas.DeviceFingerprint,
            _timeProvider.GetUtcNow(),
            snapshot.Serial,
            snapshot.Model,
            snapshot.AndroidRelease,
            snapshot.Sdk,
            snapshot.Fingerprint,
            snapshot.Abi,
            snapshot.CurrentFocus);
    }

    private async Task<PreflightResult> CreatePreflightResultAsync(DeviceFingerprint fingerprint, string? packageName)
    {
        var focus = fingerprint.CurrentFocus;
        var foregroundPackage = ParseForegroundPackage(focus);
        string? packageInfo = null;
        var displayLayout = await TryReadDisplayLayoutAsync().ConfigureAwait(false);

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
            fingerprint.Serial,
            foregroundPackage,
            displayLayout?.Width,
            displayLayout?.Height,
            displayLayout?.Orientation);
    }

    private async Task<string> ShellTextAsync(string command)
    {
        var result = await _adb.ShellAsync(command).ConfigureAwait(false);
        result.EnsureSuccess($"adb shell failed: {command}");
        return result.Stdout.Trim();
    }

    private async Task<AdbDiagnosticResult> RunAdbDiagnosticAsync(string name, IReadOnlyList<string> args)
    {
        var result = await _adb.RunAsync(args).ConfigureAwait(false);
        return new AdbDiagnosticResult(ResultSchemas.AdbDiagnostic, name, ToAdbCommandOutput(result));
    }

    private async Task<DisplayLayoutSnapshot?> TryReadDisplayLayoutAsync()
    {
        try
        {
            return ParseDisplayLayout(await ShellTextAsync("wm size").ConfigureAwait(false));
        }
        catch (Exception ex) when (!IsFatalException(ex))
        {
            return null;
        }
    }

    private static DisplayLayoutSnapshot? ParseDisplayLayout(string value)
    {
        var match = Regex.Match(value, @"(?<width>\d+)x(?<height>\d+)");
        if (!match.Success)
        {
            return null;
        }

        var width = int.Parse(match.Groups["width"].Value, CultureInfo.InvariantCulture);
        var height = int.Parse(match.Groups["height"].Value, CultureInfo.InvariantCulture);
        var orientation = width == height
            ? "square"
            : width > height
                ? "landscape"
                : "portrait";
        return new DisplayLayoutSnapshot(width, height, orientation);
    }

    private static string? ParseForegroundPackage(string currentFocus)
    {
        if (string.IsNullOrWhiteSpace(currentFocus))
        {
            return null;
        }

        var match = Regex.Match(currentFocus, @"(?<package>[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+)/");
        return match.Success ? match.Groups["package"].Value : null;
    }

    private static AdbCommandOutput ToAdbCommandOutput(AdbCommandResult result) =>
        new(
            result.Invocation,
            result.Args,
            result.ExitCode,
            result.ExitCode == 0,
            result.Stdout,
            result.Stderr,
            result.AttemptCount,
            result.Retry?.Reason,
            result.Retry?.RecoveryActions ?? []);

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
            snapshot = null!;
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

    private static int RequirePositive(int value, string message)
    {
        if (value <= 0)
        {
            throw new UsageException(message);
        }

        return value;
    }

    private static bool IsFatalException(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException;

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private sealed record DisplayLayoutSnapshot(int Width, int Height, string Orientation);

    private sealed record DeviceFingerprintSnapshot(
        string Serial,
        string Model,
        string AndroidRelease,
        string Sdk,
        string Fingerprint,
        string Abi,
        string CurrentFocus);
}