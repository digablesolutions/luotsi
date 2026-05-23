using System.Globalization;
using System.Security.Cryptography;
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
    IFileSystem fileSystem,
    IUniqueIdGenerator idGenerator,
    IEnvironmentVariables environment,
    AndroidScreenStateReadModel screenStateReadModel)
{
    private readonly IAdbClient _adb = adb ?? throw new ArgumentNullException(nameof(adb));
    private readonly ArtifactSession _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IUniqueIdGenerator _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly AndroidScreenStateReadModel _screenStateReadModel = screenStateReadModel ?? throw new ArgumentNullException(nameof(screenStateReadModel));
    private readonly AndroidScreenshotRegionArtifacts _screenshotRegionArtifacts = new(artifacts, fileSystem);

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
        await _artifacts.RefreshIndexAsync().ConfigureAwait(false);
        return new RecordResult(targetOutput, clamped);
    }

    public async Task<FailureArtifactBundle> CaptureFailureArtifactsAsync(FailureCaptureRequest request, Exception exception)
        => await FailureArtifactCapturer.CaptureAsync(request, exception).ConfigureAwait(false);

    public async Task<TakeScreenshotResult> TakeScreenshotAsync(string label)
    {
        var fileName = DeviceArtifactNames.ScreenshotForLabel(Slugify(label));
        var artifact = await CaptureScreenshotAsync(fileName).ConfigureAwait(false);
        return new TakeScreenshotResult(label, fileName, artifact.Width, artifact.Height, artifact.Sha256);
    }

    public async Task<ScreenshotAssertionResult> AssertScreenshotAsync(string label, int? expectedWidth, int? expectedHeight, string? expectedSha256, string? expectedSha256File = null, string? baselineFile = null, bool updateBaseline = false, ScreenshotAssertionRegion? region = null, string? expectedRegionSha256 = null, string? expectedRegionSha256File = null)
    {
        var fileName = DeviceArtifactNames.ScreenshotForLabel(Slugify(label));
        var artifact = await CaptureScreenshotAsync(fileName).ConfigureAwait(false);
        expectedSha256 = await ResolveExpectedSha256Async(expectedSha256, expectedSha256File, updateBaseline ? null : baselineFile, updateBaseline).ConfigureAwait(false);
        var diffArtifact = (string?)null;
        var regionSha256 = (string?)null;
        expectedRegionSha256 = await ResolveExpectedSha256Async(expectedRegionSha256, expectedRegionSha256File, null, false).ConfigureAwait(false);
        if (region is not null)
        {
            ValidateScreenshotRegion(fileName, artifact, region);
            if (!string.IsNullOrWhiteSpace(expectedRegionSha256))
            {
                regionSha256 = await _screenshotRegionArtifacts.ComputeRegionSha256Async(ResolveArtifactDestination(fileName), region).ConfigureAwait(false);
            }
        }

        if (!string.IsNullOrWhiteSpace(expectedRegionSha256) &&
            !string.Equals(regionSha256, expectedRegionSha256, StringComparison.OrdinalIgnoreCase))
        {
            var regionPreview = await _screenshotRegionArtifacts.WritePreviewAsync(label, ResolveArtifactDestination(fileName), region).ConfigureAwait(false);
            var regionDiff = await _screenshotRegionArtifacts.WriteDiffAsync(label, ResolveArtifactDestination(fileName), baselineFile, region).ConfigureAwait(false);
            diffArtifact = await WriteScreenshotDiffAsync(label, fileName, artifact, expectedSha256, baselineFile, region, regionSha256, expectedRegionSha256, regionPreview, regionDiff).ConfigureAwait(false);
            throw new InvalidOperationException($"Screenshot '{fileName}' region SHA-256 was {regionSha256 ?? "unknown"}; expected {expectedRegionSha256}. Diff artifact: {diffArtifact}.");
        }

        if (expectedWidth is not null && artifact.Width != expectedWidth)
        {
            throw new InvalidOperationException($"Screenshot '{fileName}' width was {artifact.Width?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}; expected {expectedWidth}.");
        }

        if (expectedHeight is not null && artifact.Height != expectedHeight)
        {
            throw new InvalidOperationException($"Screenshot '{fileName}' height was {artifact.Height?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}; expected {expectedHeight}.");
        }

        if (!string.IsNullOrWhiteSpace(expectedSha256) &&
            !string.Equals(artifact.Sha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            var regionDiff = await _screenshotRegionArtifacts.WriteDiffAsync(label, ResolveArtifactDestination(fileName), baselineFile, region).ConfigureAwait(false);
            diffArtifact = await WriteScreenshotDiffAsync(label, fileName, artifact, expectedSha256, baselineFile, region, regionSha256, expectedRegionSha256, null, regionDiff).ConfigureAwait(false);
            throw new InvalidOperationException($"Screenshot '{fileName}' SHA-256 was {artifact.Sha256 ?? "unknown"}; expected {expectedSha256}. Diff artifact: {diffArtifact}.");
        }

        var baselineUpdated = false;
        if (!string.IsNullOrWhiteSpace(baselineFile) && updateBaseline)
        {
            var baselineDirectory = Path.GetDirectoryName(baselineFile);
            if (!string.IsNullOrWhiteSpace(baselineDirectory))
            {
                _fileSystem.CreateDirectory(baselineDirectory);
            }

            _fileSystem.CopyFile(ResolveArtifactDestination(fileName), baselineFile, overwrite: true);
            baselineUpdated = true;
        }

        return new ScreenshotAssertionResult(label, fileName, artifact.Width, artifact.Height, artifact.Sha256, expectedWidth, expectedHeight, expectedSha256, baselineFile, baselineUpdated, region, regionSha256, expectedRegionSha256, diffArtifact);
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

    private async Task<ScreenshotArtifactInfo> CaptureScreenshotAsync(string fileName)
    {
        var destination = ResolveArtifactDestination(fileName);
        var remote = $"/sdcard/device-e2e-{_idGenerator.NewId()}.png";
        var capture = await _adb.ShellAsync($"screencap {remote}").ConfigureAwait(false);
        capture.EnsureSuccess("screencap failed");
        var pull = await _adb.RunAsync(["pull", NormalizeDevicePathForPull(remote), destination]).ConfigureAwait(false);
        await _adb.ShellAsync($"rm -f {remote}").ConfigureAwait(false);
        pull.EnsureSuccess("pull screenshot failed");
        await _artifacts.RefreshIndexAsync().ConfigureAwait(false);
        return await ReadScreenshotArtifactAsync(fileName, destination).ConfigureAwait(false);
    }

    private async Task<ScreenshotArtifactInfo> ReadScreenshotArtifactAsync(string fileName, string destination)
    {
        if (!_fileSystem.FileExists(destination))
        {
            return new ScreenshotArtifactInfo(fileName, null, null, null);
        }

        var bytes = await _fileSystem.ReadAllBytesAsync(destination).ConfigureAwait(false);
        var (width, height) = ReadPngDimensions(bytes);
        var hash = SHA256.HashData(bytes);
        var sha256 = Convert.ToHexString(hash).ToLowerInvariant();
        return new ScreenshotArtifactInfo(fileName, width, height, sha256);
    }

    private async Task<string?> ResolveExpectedSha256Async(string? expectedSha256, string? expectedSha256File, string? baselineFile, bool updateBaseline)
    {
        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            return expectedSha256.Trim();
        }

        if (!string.IsNullOrWhiteSpace(expectedSha256File))
        {
            if (!_fileSystem.FileExists(expectedSha256File))
            {
                throw new FileNotFoundException($"Screenshot SHA-256 baseline file '{expectedSha256File}' was not found.", expectedSha256File);
            }

            var hash = (await _fileSystem.ReadAllTextAsync(expectedSha256File).ConfigureAwait(false))
                .Split(['\r', '\n', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            return !string.IsNullOrWhiteSpace(hash)
                ? hash
                : throw new InvalidOperationException($"Screenshot SHA-256 baseline file '{expectedSha256File}' did not contain a hash.");
        }

        if (!string.IsNullOrWhiteSpace(baselineFile))
        {
            if (!_fileSystem.FileExists(baselineFile))
            {
                if (updateBaseline)
                {
                    return null;
                }

                throw new FileNotFoundException($"Screenshot baseline file '{baselineFile}' was not found.", baselineFile);
            }

            await using var stream = _fileSystem.OpenRead(baselineFile);
            var hash = await SHA256.HashDataAsync(stream).ConfigureAwait(false);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        return null;
    }

    private void ValidateScreenshotRegion(string fileName, ScreenshotArtifactInfo artifact, ScreenshotAssertionRegion region)
    {
        if (artifact.Width is null || artifact.Height is null)
        {
            throw new InvalidOperationException($"Screenshot '{fileName}' dimensions were unknown; region assertions require a PNG screenshot with readable dimensions.");
        }

        if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0)
        {
            throw new InvalidOperationException($"Screenshot '{fileName}' region must have non-negative origin and positive size.");
        }

        if (region.X + region.Width > artifact.Width || region.Y + region.Height > artifact.Height)
        {
            throw new InvalidOperationException($"Screenshot '{fileName}' region {region.X},{region.Y},{region.Width}x{region.Height} exceeds image bounds {artifact.Width}x{artifact.Height}.");
        }
    }

    private async Task<string> WriteScreenshotDiffAsync(string label, string fileName, ScreenshotArtifactInfo artifact, string? expectedSha256, string? baselineFile, ScreenshotAssertionRegion? region, string? regionSha256, string? expectedRegionSha256, string? regionPreviewFile, string? regionDiffFile)
    {
        var diffFile = $"{Slugify(label)}-screenshot-diff.json";
        await _artifacts.WriteJsonAsync(diffFile, new
        {
            label,
            file = fileName,
            baseline_file = baselineFile,
            observed = new
            {
                width = artifact.Width,
                height = artifact.Height,
                sha256 = artifact.Sha256
            },
            expected = new
            {
                sha256 = expectedSha256,
                region_sha256 = expectedRegionSha256
            },
            region,
            region_sha256 = regionSha256,
            region_preview_file = regionPreviewFile,
            region_diff_file = regionDiffFile
        }).ConfigureAwait(false);
        return diffFile;
    }

    private static (int? Width, int? Height) ReadPngDimensions(byte[] bytes)
    {
        if (bytes.Length < 24 ||
            bytes[0] != 0x89 ||
            bytes[1] != 0x50 ||
            bytes[2] != 0x4e ||
            bytes[3] != 0x47)
        {
            return (null, null);
        }

        return (ReadBigEndianInt32(bytes, 16), ReadBigEndianInt32(bytes, 20));
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
        bytes[offset] << 24 |
        bytes[offset + 1] << 16 |
        bytes[offset + 2] << 8 |
        bytes[offset + 3];

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
            async fileName => await CaptureScreenshotAsync(fileName).ConfigureAwait(false),
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

    private sealed record ScreenshotArtifactInfo(string FileName, int? Width, int? Height, string? Sha256);
}
