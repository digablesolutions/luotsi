using System.Globalization;
using System.Text;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Hosts.Android;

internal sealed class AndroidArtifactOperations(
    IAdbClient adb,
    ArtifactSession artifacts,
    TimeProvider timeProvider,
    IUniqueIdGenerator idGenerator,
    IEnvironmentVariables environment,
    AndroidScreenStateReadModel screenStateReadModel)
{
    private readonly IAdbClient _adb = adb ?? throw new ArgumentNullException(nameof(adb));
    private readonly ArtifactSession _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IUniqueIdGenerator _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly AndroidScreenStateReadModel _screenStateReadModel = screenStateReadModel ?? throw new ArgumentNullException(nameof(screenStateReadModel));

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

    public async Task<FailureArtifactBundle> CaptureFailureArtifactsAsync(FailureCaptureRequest request, Exception exception)
        => await FailureArtifactCapturer.CaptureAsync(request, exception).ConfigureAwait(false);

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
        await _screenStateReadModel.CaptureScreenStateWithRetryAsync(slug).ConfigureAwait(false);
        return new CaptureArtifactsResult(label, screenshot, logcat, DeviceArtifactNames.ScreenStateForLabel(slug), DeviceArtifactNames.HierarchyForLabel(slug));
    }

    private async Task CaptureScreenshotAsync(string fileName)
    {
        var destination = ResolveArtifactDestination(fileName);
        var remote = $"/sdcard/device-e2e-{_idGenerator.NewId()}.png";
        var capture = await _adb.ShellAsync($"screencap {remote}").ConfigureAwait(false);
        capture.EnsureSuccess("screencap failed");
        var pull = await _adb.RunAsync(["pull", NormalizeDevicePathForPull(remote), destination]).ConfigureAwait(false);
        await _adb.ShellAsync($"rm -f {remote}").ConfigureAwait(false);
        pull.EnsureSuccess("pull screenshot failed");
    }

    private string ResolveArtifactDestination(string fileName)
    {
        var root = Path.GetFullPath(_artifacts.Root);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(fileName, root);
        if (!destination.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Artifact file name '{fileName}' must be relative.");
        }

        return destination;
    }

    private async Task CaptureLogcatSnapshotAsync(string fileName, int tail)
    {
        var result = await _adb.RunAsync(["logcat", "-d", "-t", tail.ToString()]).ConfigureAwait(false);
        result.EnsureSuccess("logcat failed");
        await _artifacts.WriteTextAsync(fileName, result.Stdout).ConfigureAwait(false);
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

        var source = _environment.GetEnvironmentVariable("LUOTSI_EMULATED_STORAGE_SOURCE")?.Trim();
        var target = _environment.GetEnvironmentVariable("LUOTSI_EMULATED_STORAGE_TARGET")?.Trim();
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

    private AndroidFailureArtifactCapturer FailureArtifactCapturer =>
        field ??= new AndroidFailureArtifactCapturer(
            _artifacts,
            _timeProvider,
            BuildFailurePrefix,
            CaptureScreenshotAsync,
            CaptureLogcatSnapshotAsync,
            prefix => _screenStateReadModel.CaptureScreenStateWithRetryAsync(prefix));

    private static string RequireNonBlank(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new UsageException(message);
        }

        return value;
    }
}
