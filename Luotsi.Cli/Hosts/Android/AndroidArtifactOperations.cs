using System.Globalization;
using System.IO.Compression;
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
        expectedSha256 = await ResolveExpectedSha256Async(expectedSha256, expectedSha256File, baselineFile, updateBaseline).ConfigureAwait(false);
        var diffArtifact = (string?)null;
        var regionSha256 = (string?)null;
        expectedRegionSha256 = await ResolveExpectedSha256Async(expectedRegionSha256, expectedRegionSha256File, null, false).ConfigureAwait(false);
        if (region is not null)
        {
            ValidateScreenshotRegion(fileName, artifact, region);
            if (!string.IsNullOrWhiteSpace(expectedRegionSha256))
            {
                regionSha256 = ComputeRegionSha256(ResolveArtifactDestination(fileName), region);
            }
        }

        if (!string.IsNullOrWhiteSpace(expectedRegionSha256) &&
            !string.Equals(regionSha256, expectedRegionSha256, StringComparison.OrdinalIgnoreCase))
        {
            var regionPreview = await WriteRegionPreviewAsync(label, fileName, region).ConfigureAwait(false);
            var regionDiff = await WriteRegionDiffAsync(label, fileName, baselineFile, region).ConfigureAwait(false);
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
            var regionDiff = await WriteRegionDiffAsync(label, fileName, baselineFile, region).ConfigureAwait(false);
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
        return ReadScreenshotArtifact(fileName, destination);
    }

    private ScreenshotArtifactInfo ReadScreenshotArtifact(string fileName, string destination)
    {
        if (!_fileSystem.FileExists(destination))
        {
            return new ScreenshotArtifactInfo(fileName, null, null, null);
        }

        using var stream = _fileSystem.OpenRead(destination);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        var (width, height) = ReadPngDimensions(bytes);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
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

            using var stream = _fileSystem.OpenRead(baselineFile);
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

    private async Task<string?> WriteRegionPreviewAsync(string label, string fileName, ScreenshotAssertionRegion? region)
    {
        if (region is null)
        {
            return null;
        }

        var source = ResolveArtifactDestination(fileName);
        using var stream = _fileSystem.OpenRead(source);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var image = PngRgbaImage.Decode(memory.ToArray());
        var preview = image.Crop(region);
        var previewFile = $"{Slugify(label)}-screenshot-region.png";
        await using var output = _fileSystem.OpenWrite(Path.Join(_artifacts.Root, previewFile));
        await output.WriteAsync(preview.EncodePng()).ConfigureAwait(false);
        await _artifacts.RefreshIndexAsync().ConfigureAwait(false);
        return previewFile;
    }

    private async Task<string?> WriteRegionDiffAsync(string label, string fileName, string? baselineFile, ScreenshotAssertionRegion? region)
    {
        if (region is null || string.IsNullOrWhiteSpace(baselineFile) || !_fileSystem.FileExists(baselineFile))
        {
            return null;
        }

        var current = PngRgbaImage.Decode(ReadAllBytes(ResolveArtifactDestination(fileName))).Crop(region);
        var baseline = PngRgbaImage.Decode(ReadAllBytes(baselineFile)).Crop(region);
        if (current.Width != baseline.Width || current.Height != baseline.Height)
        {
            return null;
        }

        var overlay = new byte[current.Rgba.Length];
        for (var offset = 0; offset < overlay.Length; offset += 4)
        {
            var same =
                current.Rgba[offset] == baseline.Rgba[offset] &&
                current.Rgba[offset + 1] == baseline.Rgba[offset + 1] &&
                current.Rgba[offset + 2] == baseline.Rgba[offset + 2] &&
                current.Rgba[offset + 3] == baseline.Rgba[offset + 3];
            overlay[offset] = same ? (byte)0 : (byte)255;
            overlay[offset + 1] = same ? (byte)180 : (byte)0;
            overlay[offset + 2] = 0;
            overlay[offset + 3] = 255;
        }

        var diffFile = $"{Slugify(label)}-screenshot-region-diff.png";
        await using var output = _fileSystem.OpenWrite(Path.Join(_artifacts.Root, diffFile));
        await output.WriteAsync(new PngRgbaImage(current.Width, current.Height, overlay).EncodePng()).ConfigureAwait(false);
        await _artifacts.RefreshIndexAsync().ConfigureAwait(false);
        return diffFile;
    }

    private string ComputeRegionSha256(string path, ScreenshotAssertionRegion region)
    {
        var image = PngRgbaImage.Decode(ReadAllBytes(path));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var y = region.Y; y < region.Y + region.Height; y++)
        {
            var offset = ((y * image.Width) + region.X) * 4;
            hash.AppendData(image.Rgba, offset, region.Width * 4);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private byte[] ReadAllBytes(string path)
    {
        using var stream = _fileSystem.OpenRead(path);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
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

    private static void WriteBigEndian(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
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

    private sealed record PngRgbaImage(int Width, int Height, byte[] Rgba)
    {
        public PngRgbaImage Crop(ScreenshotAssertionRegion region)
        {
            var cropped = new byte[region.Width * region.Height * 4];
            for (var y = 0; y < region.Height; y++)
            {
                Buffer.BlockCopy(
                    Rgba,
                    (((region.Y + y) * Width) + region.X) * 4,
                    cropped,
                    y * region.Width * 4,
                    region.Width * 4);
            }

            return new PngRgbaImage(region.Width, region.Height, cropped);
        }

        public byte[] EncodePng()
        {
            using var output = new MemoryStream();
            output.Write([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
            var ihdr = new byte[13];
            WriteBigEndian(ihdr, 0, Width);
            WriteBigEndian(ihdr, 4, Height);
            ihdr[8] = 8;
            ihdr[9] = 6;
            WritePngChunk(output, "IHDR", ihdr);
            using var raw = new MemoryStream();
            for (var y = 0; y < Height; y++)
            {
                raw.WriteByte(0);
                raw.Write(Rgba, y * Width * 4, Width * 4);
            }

            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                raw.Position = 0;
                raw.CopyTo(zlib);
            }

            WritePngChunk(output, "IDAT", compressed.ToArray());
            WritePngChunk(output, "IEND", []);
            return output.ToArray();
        }

        public static PngRgbaImage Decode(byte[] bytes)
        {
            var (width, height) = ReadPngDimensions(bytes);
            if (width is null || height is null)
            {
                throw new InvalidOperationException("Screenshot region assertions require a PNG image.");
            }

            var colorType = bytes[25];
            var bitDepth = bytes[24];
            var interlace = bytes[28];
            if (bitDepth != 8 || interlace != 0 || (colorType != 2 && colorType != 6 && colorType != 0 && colorType != 4 && colorType != 3))
            {
                throw new InvalidOperationException($"Screenshot region assertions support non-interlaced 8-bit grayscale, grayscale+alpha, indexed, RGB, or RGBA PNG files; got bit depth {bitDepth}, color type {colorType}, interlace {interlace}.");
            }

            var bytesPerPixel = colorType switch
            {
                6 => 4,
                4 => 2,
                2 => 3,
                _ => 1
            };
            var compressed = new MemoryStream();
            var palette = Array.Empty<byte>();
            var transparency = Array.Empty<byte>();
            var offset = 8;
            while (offset + 8 <= bytes.Length)
            {
                var length = ReadBigEndianInt32(bytes, offset);
                var type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
                offset += 8;
                if (type == "IDAT")
                {
                    compressed.Write(bytes, offset, length);
                }
                else if (type == "PLTE")
                {
                    palette = bytes.Skip(offset).Take(length).ToArray();
                }
                else if (type == "tRNS")
                {
                    transparency = bytes.Skip(offset).Take(length).ToArray();
                }

                offset += length + 4;
                if (type == "IEND")
                {
                    break;
                }
            }

            compressed.Position = 0;
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            using var raw = new MemoryStream();
            zlib.CopyTo(raw);
            var inflated = raw.ToArray();
            var stride = width.Value * bytesPerPixel;
            var previous = new byte[stride];
            var current = new byte[stride];
            var rgba = new byte[width.Value * height.Value * 4];
            var source = 0;
            for (var y = 0; y < height.Value; y++)
            {
                var filter = inflated[source++];
                Array.Copy(inflated, source, current, 0, stride);
                source += stride;
                Unfilter(current, previous, bytesPerPixel, filter);
                for (var x = 0; x < width.Value; x++)
                {
                    var src = x * bytesPerPixel;
                    var dst = ((y * width.Value) + x) * 4;
                    if (colorType == 0)
                    {
                        rgba[dst] = rgba[dst + 1] = rgba[dst + 2] = current[src];
                        rgba[dst + 3] = 255;
                    }
                    else if (colorType == 4)
                    {
                        rgba[dst] = rgba[dst + 1] = rgba[dst + 2] = current[src];
                        rgba[dst + 3] = current[src + 1];
                    }
                    else if (colorType == 3)
                    {
                        var paletteIndex = current[src];
                        var paletteOffset = paletteIndex * 3;
                        if (paletteOffset + 2 >= palette.Length)
                        {
                            throw new InvalidOperationException($"PNG palette index {paletteIndex} was outside the palette.");
                        }

                        rgba[dst] = palette[paletteOffset];
                        rgba[dst + 1] = palette[paletteOffset + 1];
                        rgba[dst + 2] = palette[paletteOffset + 2];
                        rgba[dst + 3] = paletteIndex < transparency.Length ? transparency[paletteIndex] : (byte)255;
                    }
                    else
                    {
                        rgba[dst] = current[src];
                        rgba[dst + 1] = current[src + 1];
                        rgba[dst + 2] = current[src + 2];
                        rgba[dst + 3] = colorType == 6 ? current[src + 3] : (byte)255;
                    }
                }

                (previous, current) = (current, previous);
            }

            return new PngRgbaImage(width.Value, height.Value, rgba);
        }

        private static void Unfilter(byte[] row, byte[] previous, int bytesPerPixel, int filter)
        {
            for (var i = 0; i < row.Length; i++)
            {
                var left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
                var up = previous[i];
                var upLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
                row[i] = filter switch
                {
                    0 => row[i],
                    1 => unchecked((byte)(row[i] + left)),
                    2 => unchecked((byte)(row[i] + up)),
                    3 => unchecked((byte)(row[i] + ((left + up) / 2))),
                    4 => unchecked((byte)(row[i] + Paeth(left, up, upLeft))),
                    _ => throw new InvalidOperationException($"Unsupported PNG filter {filter}.")
                };
            }
        }

        private static int Paeth(int left, int up, int upLeft)
        {
            var estimate = left + up - upLeft;
            var leftDistance = Math.Abs(estimate - left);
            var upDistance = Math.Abs(estimate - up);
            var upLeftDistance = Math.Abs(estimate - upLeft);
            return leftDistance <= upDistance && leftDistance <= upLeftDistance ? left : upDistance <= upLeftDistance ? up : upLeft;
        }

        private static void WritePngChunk(Stream output, string type, byte[] data)
        {
            var length = new byte[4];
            WriteBigEndian(length, 0, data.Length);
            var typeBytes = Encoding.ASCII.GetBytes(type);
            output.Write(length);
            output.Write(typeBytes);
            output.Write(data);
            var crc = new Crc32();
            crc.Append(typeBytes);
            crc.Append(data);
            var crcBytes = new byte[4];
            WriteBigEndian(crcBytes, 0, unchecked((int)crc.Value));
            output.Write(crcBytes);
        }
    }

    private sealed class Crc32
    {
        private static readonly uint[] Table = CreateTable();

        private uint _value = 0xffffffff;

        public uint Value => _value ^ 0xffffffff;

        public void Append(byte[] bytes)
        {
            foreach (var value in bytes)
            {
                _value = Table[(_value ^ value) & 0xff] ^ (_value >> 8);
            }
        }

        private static uint[] CreateTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                var value = i;
                for (var bit = 0; bit < 8; bit++)
                {
                    value = (value & 1) == 1 ? 0xedb88320 ^ (value >> 1) : value >> 1;
                }

                table[i] = value;
            }

            return table;
        }
    }
}
