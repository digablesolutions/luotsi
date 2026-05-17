using System.Globalization;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure;

namespace Luotsi.Cli.View;

/// <summary>
/// Creates the optional local recorder used by the view session.
/// </summary>
public sealed class DefaultViewRecorderFactory(IFileSystem fileSystem, IProcessRunner processRunner, IEnvironmentVariables environment) : IViewRecorderFactory
{
    private static readonly string[] RawExtensions = [".h264", ".264"];
    private static readonly string[] ContainerExtensions = [".mp4", ".mkv"];

    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    private readonly FfmpegExecutableResolver _ffmpegExecutableResolver = new(environment ?? throw new ArgumentNullException(nameof(environment)), fileSystem);

    /// <inheritdoc />
    public IViewRecorder? Create(ViewOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.RecordPath))
        {
            return null;
        }

        if (!string.Equals(options.Codec, "h264", StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageException("The live view recorder currently supports only --codec h264.");
        }

        var extension = Path.GetExtension(options.RecordPath);
        if (RawExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return new AnnexBViewRecorder(_fileSystem, options.RecordPath);
        }

        if (ContainerExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return new FfmpegMuxingViewRecorder(
                _fileSystem,
                _processRunner,
                _ffmpegExecutableResolver.Resolve(),
                options.RecordPath,
                options.MaxFps);
        }

        throw new UsageException("The live view recorder supports .h264, .mp4, and .mkv outputs.");
    }
}

/// <summary>
/// Resolves an ffmpeg executable used for post-record container muxing.
/// </summary>
public sealed class FfmpegExecutableResolver(IEnvironmentVariables environment, IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly ViewHostPathResolver _pathResolver = new(environment ?? throw new ArgumentNullException(nameof(environment)));

    /// <summary>
    /// Resolves an ffmpeg executable path.
    /// </summary>
    /// <returns>Resolved executable path.</returns>
    public string Resolve()
    {
        foreach (var candidate in GetCandidateExecutablePaths())
        {
            if (_fileSystem.FileExists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "Container view recording requires an ffmpeg executable. Stage ffmpeg under ffmpeg/bin, set LUOTSI_FFMPEG_ROOT to a directory containing ffmpeg, or place ffmpeg on PATH.");
    }

    private IEnumerable<string> GetCandidateExecutablePaths()
    {
        foreach (var candidate in _pathResolver.GetFfmpegExecutablePathCandidates())
        {
            yield return candidate;
        }
    }
}

/// <summary>
/// Records the mirrored H.264 stream as a raw Annex B elementary stream.
/// </summary>
public sealed class AnnexBViewRecorder(IFileSystem fileSystem, string outputPath) : IViewRecorder
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly string _outputPath = string.IsNullOrWhiteSpace(outputPath)
        ? throw new ArgumentException("Recording output path is required.", nameof(outputPath))
        : Path.GetFullPath(outputPath);

    private Stream? _output;
    private bool _completed;

    /// <inheritdoc />
    public Task InitializeAsync(ViewConnectionInfo connectionInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connectionInfo);

        if (!string.Equals(connectionInfo.Codec, "h264", StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageException("The live view recorder currently supports only H.264 streams.");
        }

        var directory = Path.GetDirectoryName(_outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _fileSystem.CreateDirectory(directory);
        }

        _output = _fileSystem.OpenWrite(_outputPath, overwrite: true);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task WritePacketAsync(ViewPacket packet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packet);

        var output = _output ?? throw new InvalidOperationException("View recorder was not initialized.");
        if (_completed || packet.Payload.IsEmpty || (packet.PacketType != ViewPacketType.Config && packet.PacketType != ViewPacketType.Frame))
        {
            return;
        }

        await output.WriteAsync(packet.Payload, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        if (_output is null)
        {
            return;
        }

        await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        await _output.DisposeAsync().ConfigureAwait(false);
        _output = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_output is null)
        {
            return;
        }

        await _output.DisposeAsync().ConfigureAwait(false);
        _output = null;
        _completed = true;
    }
}

/// <summary>
/// Records the live stream as raw Annex B H.264 first, then remuxes that capture into a container format with ffmpeg.
/// </summary>
public sealed class FfmpegMuxingViewRecorder : IViewRecorder
{
    private readonly IFileSystem _fileSystem;
    private readonly IProcessRunner _processRunner;
    private readonly string _ffmpegExecutable;
    private readonly string _outputPath;
    private readonly int _inputFrameRate;

    private AnnexBViewRecorder? _rawRecorder;
    private string? _rawCapturePath;
    private bool _keepRawCapture;

    public FfmpegMuxingViewRecorder(IFileSystem fileSystem, IProcessRunner processRunner, string ffmpegExecutable, string outputPath, int inputFrameRate)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _ffmpegExecutable = string.IsNullOrWhiteSpace(ffmpegExecutable)
            ? throw new ArgumentException("An ffmpeg executable path is required.", nameof(ffmpegExecutable))
            : ffmpegExecutable;
        _outputPath = string.IsNullOrWhiteSpace(outputPath)
            ? throw new ArgumentException("Recording output path is required.", nameof(outputPath))
            : Path.GetFullPath(outputPath);
        _inputFrameRate = Math.Max(1, inputFrameRate);
    }

    /// <inheritdoc />
    public Task InitializeAsync(ViewConnectionInfo connectionInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connectionInfo);

        var outputDirectory = Path.GetDirectoryName(_outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            _fileSystem.CreateDirectory(outputDirectory);
        }

        _rawCapturePath = Path.Combine(_fileSystem.GetTempPath(), $"luotsi-view-record-{Guid.NewGuid():N}.h264");
        _rawRecorder = new AnnexBViewRecorder(_fileSystem, _rawCapturePath);
        return _rawRecorder.InitializeAsync(connectionInfo, cancellationToken);
    }

    /// <inheritdoc />
    public Task WritePacketAsync(ViewPacket packet, CancellationToken cancellationToken = default) =>
        (_rawRecorder ?? throw new InvalidOperationException("Muxing recorder was not initialized."))
        .WritePacketAsync(packet, cancellationToken);

    /// <inheritdoc />
    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (_rawRecorder is null)
        {
            return;
        }

        await _rawRecorder.CompleteAsync(cancellationToken).ConfigureAwait(false);
        var rawCapturePath = _rawCapturePath ?? throw new InvalidOperationException("Raw recording path was not initialized.");
        var result = await _processRunner.RunAsync(_ffmpegExecutable, BuildRemuxArguments(rawCapturePath), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            _keepRawCapture = true;
            var detail = string.IsNullOrWhiteSpace(result.Stderr) ? result.Stdout : result.Stderr;
            throw new InvalidOperationException($"Container view recording remux failed. Raw capture was kept at {rawCapturePath}. {detail}".Trim());
        }

        _fileSystem.DeleteFile(rawCapturePath);
        _rawCapturePath = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_rawRecorder is not null)
        {
            await _rawRecorder.DisposeAsync().ConfigureAwait(false);
            _rawRecorder = null;
        }

        if (!_keepRawCapture && _rawCapturePath is not null && _fileSystem.FileExists(_rawCapturePath))
        {
            _fileSystem.DeleteFile(_rawCapturePath);
        }

        _rawCapturePath = null;
    }

    private IReadOnlyList<string> BuildRemuxArguments(string rawCapturePath)
    {
        var arguments = new List<string>
        {
            "-y",
            "-hide_banner",
            "-loglevel", "error",
            "-fflags", "+genpts",
            "-f", "h264",
            "-r", _inputFrameRate.ToString(CultureInfo.InvariantCulture),
            "-i", rawCapturePath,
            "-c:v", "copy"
        };

        if (string.Equals(Path.GetExtension(_outputPath), ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            arguments.Add("-movflags");
            arguments.Add("+faststart");
        }

        arguments.Add(_outputPath);
        return arguments;
    }
}
