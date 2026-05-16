using System.Text.Json;
using System.Text.Json.Serialization;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Hosts.Android;
using Luotsi.Cli.Hosts.Android.View;
using Luotsi.Cli.Infrastructure;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Backends.Ffmpeg;

namespace Luotsi.Cli.View;

internal sealed class SessionControlledViewRecorder(IViewRecorderFactory recorderFactory, ViewOptions baseOptions) : IViewRecorder
{
    private readonly IViewRecorderFactory _recorderFactory = recorderFactory ?? throw new ArgumentNullException(nameof(recorderFactory));
    private readonly ViewOptions _baseOptions = baseOptions ?? throw new ArgumentNullException(nameof(baseOptions));
    private readonly SemaphoreSlim _gate = new(1, 1);

    private ViewConnectionInfo? _connectionInfo;
    private IViewRecorder? _activeRecorder;
    private string? _activeRecordPath;

    public bool IsRecording => _activeRecorder is not null;

    public string? ActiveRecordPath => _activeRecordPath;

    public Task InitializeAsync(ViewConnectionInfo connectionInfo, CancellationToken cancellationToken = default)
    {
        _connectionInfo = connectionInfo ?? throw new ArgumentNullException(nameof(connectionInfo));
        return Task.CompletedTask;
    }

    public async Task WritePacketAsync(ViewPacket packet, CancellationToken cancellationToken = default)
    {
        var activeRecorder = _activeRecorder;
        if (activeRecorder is null)
        {
            return;
        }

        await activeRecorder.WritePacketAsync(packet, cancellationToken).ConfigureAwait(false);
    }

    public Task CompleteAsync(CancellationToken cancellationToken = default) => StopAsync(cancellationToken);

    public async Task StartAsync(string recordPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recordPath))
        {
            throw new ArgumentException("Recording output path is required.", nameof(recordPath));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_activeRecorder is not null)
            {
                return;
            }

            var connectionInfo = _connectionInfo ?? throw new InvalidOperationException("View recorder cannot start before the stream connection is ready.");
            var recorder = _recorderFactory.Create(_baseOptions with { RecordPath = recordPath })
                ?? throw new InvalidOperationException("View recorder factory returned no recorder for the requested record path.");
            await recorder.InitializeAsync(connectionInfo, cancellationToken).ConfigureAwait(false);
            _activeRecorder = recorder;
            _activeRecordPath = recordPath;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_activeRecorder is null)
            {
                return;
            }

            await _activeRecorder.CompleteAsync(cancellationToken).ConfigureAwait(false);
            await _activeRecorder.DisposeAsync().ConfigureAwait(false);
            _activeRecorder = null;
            _activeRecordPath = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
