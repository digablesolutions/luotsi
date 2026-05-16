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

/// <summary>
/// Default view session factory.
/// </summary>
public sealed class DefaultViewSessionFactory(
    IConsoleIo console,
    TimeProvider timeProvider,
    IAdbClientFactory adbClientFactory,
    IProcessRunner processRunner,
    IEnvironmentVariables environment,
    IFileSystem fileSystem,
    IUniqueIdGenerator idGenerator) : IViewSessionFactory
{
    private readonly IConsoleIo _console = console ?? throw new ArgumentNullException(nameof(console));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IAdbClientFactory _adbClientFactory = adbClientFactory ?? throw new ArgumentNullException(nameof(adbClientFactory));
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly IUniqueIdGenerator _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));

    /// <inheritdoc />
    public IViewSession Create(IDeviceHost deviceHost, ArtifactSession artifacts) =>
        new ViewSession(
            deviceHost,
            artifacts,
            _console,
            _timeProvider,
            new AndroidViewBootstrap(
                _adbClientFactory,
                _processRunner,
                new AndroidViewHelperPackageLocator(_environment, _fileSystem),
                _idGenerator),
            new DefaultViewBackendFactory(_environment),
            new LocalhostViewStreamConnector(),
            new ViewPacketStreamReader(),
            new DefaultViewRendererFactory(),
            new DefaultViewRecorderFactory(_fileSystem, _processRunner, _environment));
}

/// <summary>
/// Creates the built-in decoder backends supported by the current CLI.
/// </summary>
public sealed class DefaultViewBackendFactory(IEnvironmentVariables environment) : IViewBackendFactory
{
    private readonly IEnvironmentVariables _environment = environment ?? throw new ArgumentNullException(nameof(environment));

    /// <inheritdoc />
    public IViewBackend Create(ViewOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.Decoder.ToLowerInvariant() switch
        {
            "ffmpeg" => new LibavViewBackend(new DefaultLibavVideoDecoderFactory(new LibavNativeLibraryLoader(_environment))),
            "wmf" => throw new UsageException("The WMF view backend is not implemented yet. Use --decoder ffmpeg for now."),
            _ => throw new UsageException($"Unsupported view decoder '{options.Decoder}'.")
        };
    }
}

/// <summary>
/// Creates the optional local renderer used by the view session.
/// </summary>
public sealed class DefaultViewRendererFactory : IViewRendererFactory
{
    /// <inheritdoc />
    public IViewRenderer? Create(ViewOptions options, IDeviceHost deviceHost)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(deviceHost);

        if (options.Headless)
        {
            return null;
        }

        return new NativeWindowViewRenderer(new Sdl3ViewWindowSurfaceFactory(), deviceHost);
    }
}

/// <summary>
/// Phase 1 scaffold for the built-in device mirror session.
/// </summary>
public sealed class ViewSession(
    IDeviceHost deviceHost,
    ArtifactSession artifacts,
    IConsoleIo console,
    TimeProvider timeProvider,
    IViewTransportBootstrap transportBootstrap,
    IViewBackendFactory viewBackendFactory,
    IViewStreamConnector streamConnector,
    IViewPacketStreamReader packetStreamReader,
    IViewRendererFactory? viewRendererFactory = null,
    IViewRecorderFactory? viewRecorderFactory = null) : IViewSession
{
    private static readonly JsonSerializerOptions OutputJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private const int InitialStreamAttempts = 20;
    private static readonly TimeSpan InitialStreamRetryDelay = TimeSpan.FromMilliseconds(100);
    private readonly IDeviceHost _deviceHost = deviceHost ?? throw new ArgumentNullException(nameof(deviceHost));
    private readonly ArtifactSession _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    private readonly IConsoleIo _console = console ?? throw new ArgumentNullException(nameof(console));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IViewTransportBootstrap _transportBootstrap = transportBootstrap ?? throw new ArgumentNullException(nameof(transportBootstrap));
    private readonly IViewBackendFactory _viewBackendFactory = viewBackendFactory ?? throw new ArgumentNullException(nameof(viewBackendFactory));
    private readonly IViewStreamConnector _streamConnector = streamConnector ?? throw new ArgumentNullException(nameof(streamConnector));
    private readonly IViewPacketStreamReader _packetStreamReader = packetStreamReader ?? throw new ArgumentNullException(nameof(packetStreamReader));
    private readonly IViewRendererFactory _viewRendererFactory = viewRendererFactory ?? new NullViewRendererFactory();
    private readonly IViewRecorderFactory _viewRecorderFactory = viewRecorderFactory ?? new NullViewRecorderFactory();

    /// <inheritdoc />
    public async Task<int> RunAsync(ViewOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var sessionId = Guid.NewGuid().ToString("N");

        try
        {
            await using var recorder = _viewRecorderFactory.Create(options);
            await using var viewBackend = _viewBackendFactory.Create(options);
            IViewRenderer? renderer = null;
            SessionViewRenderer? sessionRenderer = null;
            IViewRenderer? backendRenderer = null;
            string endReason = "stream_ended";
            try
            {
                using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var connectionInfo = await _transportBootstrap.StartAsync(
                    new ViewStartRequest(
                        options.AdbExecutable,
                        options.DeviceSelector,
                        options.MaxSize,
                        options.MaxFps,
                        options.VideoBitRate,
                        options.Codec),
                    cancellationToken).ConfigureAwait(false);

                var streamStart = await ConnectAndReadHeaderAsync(connectionInfo, cancellationToken).ConfigureAwait(false);
                await using var streamConnection = streamStart.Connection;
                var header = streamStart.Header;
                var negotiatedConnection = connectionInfo with
                {
                    Codec = header.Codec,
                    ProtocolVersion = header.ProtocolVersion,
                    Width = header.Width,
                    Height = header.Height
                };

                renderer = _viewRendererFactory.Create(options, _deviceHost);
                sessionRenderer = new SessionViewRenderer(
                    renderer,
                    _timeProvider,
                    TimeSpan.FromMilliseconds(options.RendererStatsIntervalMs),
                    TimeSpan.FromMilliseconds(options.StatsIntervalMs),
                    stats =>
                {
                    WriteJsonLine(new
                    {
                        type = "view_stats",
                        session_id = sessionId,
                        observed_at = _timeProvider.GetUtcNow(),
                        stats
                    });

                    return Task.CompletedTask;
                });
                backendRenderer = sessionRenderer;
                await viewBackend.InitializeAsync(negotiatedConnection, backendRenderer, recorder, sessionCancellation.Token).ConfigureAwait(false);

                WriteJsonLine(new
                {
                    type = "view_started",
                    session_id = sessionId,
                    started_at = _timeProvider.GetUtcNow(),
                    device = options.DeviceSelector,
                    decoder = options.Decoder,
                    codec = negotiatedConnection.Codec,
                    backend = viewBackend.Name,
                    headless = options.Headless,
                    record_path = options.RecordPath,
                    max_size = options.MaxSize,
                    max_fps = options.MaxFps,
                    video_bit_rate = options.VideoBitRate,
                    stats_interval_ms = options.StatsIntervalMs,
                    renderer_stats_interval_ms = options.RendererStatsIntervalMs,
                    overlay_screen_state = options.OverlayScreenState,
                    overlay_telemetry = options.OverlayTelemetry,
                    connection = negotiatedConnection,
                    artifacts = _artifacts.ToData()
                });

                var viewTask = viewBackend.RunAsync(_packetStreamReader.ReadPacketsAsync(streamConnection.Stream, sessionCancellation.Token), sessionCancellation.Token);
                if (renderer is not null)
                {
                    var windowCloseTask = renderer.WaitForCloseAsync(sessionCancellation.Token);
                    var completedTask = await Task.WhenAny(viewTask, windowCloseTask).ConfigureAwait(false);
                    if (completedTask == windowCloseTask)
                    {
                        endReason = "window_closed";
                        sessionCancellation.Cancel();
                        await _transportBootstrap.StopAsync(CancellationToken.None).ConfigureAwait(false);
                        try
                        {
                            await viewTask.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested)
                        {
                        }
                    }
                    else
                    {
                        await viewTask.ConfigureAwait(false);
                    }
                }
                else
                {
                    await viewTask.ConfigureAwait(false);
                }

                if (sessionRenderer is not null)
                {
                    await sessionRenderer.FlushPendingStatsAsync().ConfigureAwait(false);
                }

                WriteJsonLine(new
                {
                    type = "view_ended",
                    session_id = sessionId,
                    ended_at = _timeProvider.GetUtcNow(),
                    reason = endReason
                });

                return 0;
            }
            finally
            {
                if (renderer is not null)
                {
                    await renderer.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            WriteJsonLine(new
            {
                type = "view_error",
                session_id = sessionId,
                occurred_at = _timeProvider.GetUtcNow(),
                error = ErrorInfo.From(ex, ex is UsageException ? "usage_error" : ErrorInfo.Classify(ex.Message))
            });

            WriteJsonLine(new
            {
                type = "view_ended",
                session_id = sessionId,
                ended_at = _timeProvider.GetUtcNow(),
                reason = "error"
            });

            return 1;
        }
        finally
        {
            await _transportBootstrap.StopAsync(cancellationToken).ConfigureAwait(false);
            _ = _deviceHost;
        }
    }

    private void WriteJsonLine(object value) => _console.WriteLine(JsonSerializer.Serialize(value, OutputJsonOptions));

    private async Task<(IViewStreamConnection Connection, ViewStreamHeader Header)> ConnectAndReadHeaderAsync(
        ViewConnectionInfo connectionInfo,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= InitialStreamAttempts; attempt++)
        {
            var connection = await _streamConnector.ConnectAsync(connectionInfo, cancellationToken).ConfigureAwait(false);
            try
            {
                var header = await _packetStreamReader.ReadHeaderAsync(connection.Stream, cancellationToken).ConfigureAwait(false);
                return (connection, header);
            }
            catch (Exception ex) when (attempt < InitialStreamAttempts && IsTransientStartFailure(ex))
            {
                lastError = ex;
                await connection.DisposeAsync().ConfigureAwait(false);
                await Task.Delay(InitialStreamRetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        throw new InvalidOperationException(
            $"Failed to read the view stream startup header from localhost:{connectionInfo.LocalPort} after {InitialStreamAttempts} attempts.",
            lastError);
    }

    private static bool IsTransientStartFailure(Exception exception) =>
        exception is InvalidOperationException invalidOperationException &&
        invalidOperationException.Message.Contains("Unexpected end of stream", StringComparison.Ordinal);
}

internal sealed class NullViewRendererFactory : IViewRendererFactory
{
    public IViewRenderer? Create(ViewOptions options, IDeviceHost deviceHost) => null;
}

internal sealed class NullViewRecorderFactory : IViewRecorderFactory
{
    public IViewRecorder? Create(ViewOptions options) => null;
}

internal sealed class SessionViewRenderer(
    IViewRenderer? innerRenderer,
    TimeProvider timeProvider,
    TimeSpan rendererStatsInterval,
    TimeSpan statsEventInterval,
    Func<ViewStats, Task> onStatsAsync) : IViewRenderer
{
    private readonly IViewRenderer? _innerRenderer = innerRenderer;
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly TimeSpan _rendererStatsInterval = rendererStatsInterval >= TimeSpan.Zero
        ? rendererStatsInterval
        : throw new ArgumentOutOfRangeException(nameof(rendererStatsInterval));
    private readonly TimeSpan _statsEventInterval = statsEventInterval >= TimeSpan.Zero
        ? statsEventInterval
        : throw new ArgumentOutOfRangeException(nameof(statsEventInterval));
    private readonly Func<ViewStats, Task> _onStatsAsync = onStatsAsync ?? throw new ArgumentNullException(nameof(onStatsAsync));
    private readonly object _rendererStatsGate = new();
    private readonly object _statsGate = new();

    private ViewStats? _pendingRendererStats;
    private ViewStats? _pendingStats;
    private DateTimeOffset? _lastRendererStatsForwardedAt;
    private DateTimeOffset? _lastStatsEmittedAt;

    public Task InitializeAsync(ViewDisplayInfo displayInfo, CancellationToken cancellationToken = default) =>
        _innerRenderer?.InitializeAsync(displayInfo, cancellationToken) ?? Task.CompletedTask;

    public Task PresentAsync(ViewFrame frame, CancellationToken cancellationToken = default) =>
        _innerRenderer?.PresentAsync(frame, cancellationToken) ?? Task.CompletedTask;

    public async Task UpdateStatsAsync(ViewStats stats, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stats);

        var now = _timeProvider.GetUtcNow();
        var rendererStatsToForward = CaptureRendererStats(stats, now);
        if (rendererStatsToForward is not null && _innerRenderer is not null)
        {
            await _innerRenderer.UpdateStatsAsync(rendererStatsToForward, cancellationToken).ConfigureAwait(false);
        }

        var statsToEmit = CaptureJsonStats(stats, now);
        if (statsToEmit is not null)
        {
            await _onStatsAsync(statsToEmit).ConfigureAwait(false);
        }
    }

    public async Task FlushPendingStatsAsync()
    {
        var now = _timeProvider.GetUtcNow();
        var rendererStatsToForward = FlushPendingRendererStats(now);
        if (rendererStatsToForward is not null && _innerRenderer is not null)
        {
            await _innerRenderer.UpdateStatsAsync(rendererStatsToForward).ConfigureAwait(false);
        }

        var statsToEmit = FlushPendingJsonStats(now);
        if (statsToEmit is not null)
        {
            await _onStatsAsync(statsToEmit).ConfigureAwait(false);
        }
    }

    private ViewStats? CaptureRendererStats(ViewStats stats, DateTimeOffset now)
    {
        if (_innerRenderer is null)
        {
            return null;
        }

        if (_rendererStatsInterval == TimeSpan.Zero)
        {
            return stats;
        }

        ViewStats? statsToForward = null;
        lock (_rendererStatsGate)
        {
            _pendingRendererStats = stats;
            if (_lastRendererStatsForwardedAt is null || now - _lastRendererStatsForwardedAt.Value >= _rendererStatsInterval)
            {
                statsToForward = _pendingRendererStats;
                _pendingRendererStats = null;
                _lastRendererStatsForwardedAt = now;
            }
        }

        return statsToForward;
    }

    private ViewStats? CaptureJsonStats(ViewStats stats, DateTimeOffset now)
    {
        if (_statsEventInterval == TimeSpan.Zero)
        {
            return null;
        }

        ViewStats? statsToEmit = null;
        lock (_statsGate)
        {
            _pendingStats = stats;
            if (_lastStatsEmittedAt is null || now - _lastStatsEmittedAt.Value >= _statsEventInterval)
            {
                statsToEmit = _pendingStats;
                _pendingStats = null;
                _lastStatsEmittedAt = now;
            }
        }

        return statsToEmit;
    }

    private ViewStats? FlushPendingRendererStats(DateTimeOffset now)
    {
        if (_innerRenderer is null || _rendererStatsInterval == TimeSpan.Zero)
        {
            return null;
        }

        lock (_rendererStatsGate)
        {
            var statsToForward = _pendingRendererStats;
            if (statsToForward is null)
            {
                return null;
            }

            _pendingRendererStats = null;
            _lastRendererStatsForwardedAt = now;
            return statsToForward;
        }
    }

    private ViewStats? FlushPendingJsonStats(DateTimeOffset now)
    {
        if (_statsEventInterval == TimeSpan.Zero)
        {
            return null;
        }

        lock (_statsGate)
        {
            var statsToEmit = _pendingStats;
            if (statsToEmit is null)
            {
                return null;
            }

            _pendingStats = null;
            _lastStatsEmittedAt = now;
            return statsToEmit;
        }
    }

    public Task WaitForCloseAsync(CancellationToken cancellationToken = default) =>
        _innerRenderer?.WaitForCloseAsync(cancellationToken) ?? Task.Delay(Timeout.Infinite, cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class NullViewTransportBootstrap : IViewTransportBootstrap
{
    public Task<ViewConnectionInfo> StartAsync(ViewStartRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ViewConnectionInfo(Guid.NewGuid().ToString("N"), request.Codec, ViewPacketStreamReader.CurrentProtocolVersion, 0, 0, 0, "phase-1-stub", "stub"));

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class NullViewBackend : IViewBackend
{
    public string Name => "stub";

    public Task InitializeAsync(ViewConnectionInfo connectionInfo, IViewRenderer? renderer, IViewRecorder? recorder, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RunAsync(IAsyncEnumerable<ViewPacket> packets, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}