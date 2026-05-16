using VisitLab.Cli.Errors;
using VisitLab.Cli.Infrastructure;

namespace VisitLab.Cli.View;

/// <summary>
/// Creates the optional local recorder used by the view session.
/// </summary>
public sealed class DefaultViewRecorderFactory(IFileSystem fileSystem) : IViewRecorderFactory
{
    private static readonly string[] SupportedExtensions = [".h264", ".264"];

    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

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
        if (!SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new UsageException("The live view recorder currently writes raw H.264 Annex B streams. Use a .h264 output path.");
        }

        return new AnnexBViewRecorder(_fileSystem, options.RecordPath);
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