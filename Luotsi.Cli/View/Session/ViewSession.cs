using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Hosts.Android.View;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;
using Luotsi.Cli.View.Backends.Ffmpeg;
using Luotsi.Cli.View.Contracts;
using Luotsi.Cli.View.Recording;
using Luotsi.Cli.View.Rendering;
using Luotsi.Cli.View.Transport;

namespace Luotsi.Cli.View.Session;

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
            new ViewSessionRuntime
            {
                Console = _console,
                TimeProvider = _timeProvider,
                TransportBootstrap = new AndroidViewBootstrap(
                    _adbClientFactory,
                    _processRunner,
                    new AndroidViewHelperPackageLocator(_environment, _fileSystem),
                    _idGenerator),
                ViewBackendFactory = new DefaultViewBackendFactory(_environment),
                StreamConnector = new LocalhostViewStreamConnector(),
                PacketStreamReader = new ViewPacketStreamReader(),
                ViewRendererFactory = new DefaultViewRendererFactory(),
                ViewRecorderFactory = new DefaultViewRecorderFactory(_fileSystem, _processRunner, _environment)
            });
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
    public IViewRenderer? Create(ViewOptions options, Func<ViewInteractionRequest, Task> interactionHandler)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(interactionHandler);

        if (options.Headless)
        {
            return null;
        }

        return new NativeWindowViewRenderer(
            new Sdl3ViewWindowSurfaceFactory(),
            interactionHandler,
            new ViewWindowOptions(options.AlwaysOnTop, ParseScaleMode(options.ScaleMode)));
    }

    private static ViewScaleMode ParseScaleMode(string value) =>
        string.Equals(value, "fill", StringComparison.OrdinalIgnoreCase) ? ViewScaleMode.Fill : ViewScaleMode.Fit;
}

/// <summary>
/// Built-in device mirror session.
/// </summary>
public sealed class ViewSession : IViewSession
{
    private static readonly JsonSerializerOptions OutputJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private const int InitialStreamAttempts = 600;
    private static readonly TimeSpan InitialStreamRetryDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan DefaultAutoReconnectAfter = TimeSpan.FromSeconds(170);
    private readonly IDeviceHost _deviceHost;
    private readonly ArtifactSession _artifacts;
    private readonly IConsoleIo _console;
    private readonly TimeProvider _timeProvider;
    private readonly IViewTransportBootstrap _transportBootstrap;
    private readonly IViewBackendFactory _viewBackendFactory;
    private readonly IViewStreamConnector _streamConnector;
    private readonly IViewPacketStreamReader _packetStreamReader;
    private readonly IViewRendererFactory _viewRendererFactory;
    private readonly IViewRecorderFactory _viewRecorderFactory;
    private readonly IArtifactFolderOpener _artifactFolderOpener;
    private readonly TimeSpan _autoReconnectAfter;
    private readonly Lock _writeGate = new();

    public ViewSession(IDeviceHost deviceHost, ArtifactSession artifacts, ViewSessionRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(deviceHost);
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(runtime);

        _deviceHost = deviceHost;
        _artifacts = artifacts;
        _console = runtime.Console ?? throw new ArgumentNullException(nameof(ViewSessionRuntime.Console));
        _timeProvider = runtime.TimeProvider ?? throw new ArgumentNullException(nameof(ViewSessionRuntime.TimeProvider));
        _transportBootstrap = runtime.TransportBootstrap ?? throw new ArgumentNullException(nameof(ViewSessionRuntime.TransportBootstrap));
        _viewBackendFactory = runtime.ViewBackendFactory ?? throw new ArgumentNullException(nameof(ViewSessionRuntime.ViewBackendFactory));
        _streamConnector = runtime.StreamConnector ?? throw new ArgumentNullException(nameof(ViewSessionRuntime.StreamConnector));
        _packetStreamReader = runtime.PacketStreamReader ?? throw new ArgumentNullException(nameof(ViewSessionRuntime.PacketStreamReader));
        _viewRendererFactory = runtime.ViewRendererFactory ?? new NullViewRendererFactory();
        _viewRecorderFactory = runtime.ViewRecorderFactory ?? new NullViewRecorderFactory();
        _artifactFolderOpener = runtime.ArtifactFolderOpener ?? new SystemArtifactFolderOpener();
        _autoReconnectAfter = runtime.AutoReconnectAfter ?? DefaultAutoReconnectAfter;
    }

    /// <inheritdoc />
    public async Task<int> RunAsync(ViewOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var sessionId = Guid.NewGuid().ToString("N");
        var activeDeviceSelector = options.DeviceSelector;
        var usesSharedTransport = !string.IsNullOrWhiteSpace(options.JoinShareEndpoint);
        var emittedShareStarted = false;
        TcpViewShareServer? shareServer = null;

        try
        {
            await using var recorder = new SessionControlledViewRecorder(_viewRecorderFactory, options);
            IViewRenderer? renderer = null;
            SessionViewRenderer? sessionRenderer = null;
            var interactionRouter = new ViewSessionInteractionRouter(new ViewSessionInteractionContext(
                _deviceHost,
                _artifacts,
                options,
                recorder,
                _timeProvider,
                sessionId,
                WriteJsonLine,
                _artifactFolderOpener));
            string endReason = "stream_ended";
            try
            {
                renderer = _viewRendererFactory.Create(options, interactionRouter.HandleAsync);
                sessionRenderer = new SessionViewRenderer(
                    renderer,
                    _timeProvider,
                    TimeSpan.FromMilliseconds(options.RendererStatsIntervalMs),
                    TimeSpan.FromMilliseconds(options.StatsIntervalMs),
                    stats =>
                {
                    WriteJsonLine(new
                    {
                        type = SessionEventTypes.View.Stats,
                        session_id = sessionId,
                        observed_at = _timeProvider.GetUtcNow(),
                        stats
                    });

                    return Task.CompletedTask;
                });
                interactionRouter.AttachStreamPauseUpdater(sessionRenderer.SetPaused);
                interactionRouter.AttachChromeUpdater(chrome => sessionRenderer.UpdateChromeAsync(chrome, cancellationToken));
                var firstConnection = true;

                while (true)
                {
                    await using var viewBackend = _viewBackendFactory.Create(options);
                    using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    interactionRouter.BeginIteration(activeDeviceSelector, sessionCancellation);
                    await interactionRouter.PublishChromeAsync().ConfigureAwait(false);
                    var streamStart = usesSharedTransport
                        ? await ConnectAndReadHeaderAsync(BuildSharedConnectionInfo(options.JoinShareEndpoint!), cancellationToken).ConfigureAwait(false)
                        : await StartTransportAndReadHeaderAsync(options, activeDeviceSelector, sessionId, cancellationToken).ConfigureAwait(false);
                    var connectionInfo = streamStart.ConnectionInfo;
                    var streamConnection = streamStart.Connection;
                    try
                    {
                        var packetSource = _packetStreamReader.ReadPacketsAsync(streamConnection.Stream, sessionCancellation.Token);
                        if (!usesSharedTransport)
                        {
                            var startupFallback = await TryFallbackOnStartupServerErrorAsync(
                                options,
                                activeDeviceSelector,
                                sessionId,
                                connectionInfo,
                                streamStart.Header,
                                streamConnection,
                                packetSource,
                                cancellationToken).ConfigureAwait(false);
                            connectionInfo = startupFallback.ConnectionInfo;
                            streamConnection = startupFallback.Connection;
                            packetSource = startupFallback.Packets;
                            streamStart = (startupFallback.ConnectionInfo, startupFallback.Connection, startupFallback.Header);
                        }

                        var header = streamStart.Header;
                        var negotiatedConnection = connectionInfo with
                        {
                            Codec = header.Codec,
                            ProtocolVersion = header.ProtocolVersion,
                            Width = header.Width,
                            Height = header.Height
                        };

                        if (!string.IsNullOrWhiteSpace(options.ShareBindEndpoint))
                        {
                            if (shareServer is null)
                            {
                                shareServer = new TcpViewShareServer(options.ShareBindEndpoint);
                                shareServer.ObserverChanged += observerEvent =>
                                {
                                    _ = interactionRouter.UpdateShareStateAsync(shareServer.BoundEndpoint, observerEvent.ObserverCount);
                                    WriteJsonLine(new
                                    {
                                        type = observerEvent.Kind == ViewShareObserverEventKind.Connected
                                            ? SessionEventTypes.View.ShareClientConnected
                                            : SessionEventTypes.View.ShareClientDisconnected,
                                        session_id = sessionId,
                                        occurred_at = _timeProvider.GetUtcNow(),
                                        endpoint = shareServer.BoundEndpoint,
                                        remote_endpoint = observerEvent.RemoteEndpoint,
                                        observer_count = observerEvent.ObserverCount,
                                        reason = observerEvent.Reason
                                    });
                                };
                            }

                            var shareEndpoint = await shareServer.StartAsync(cancellationToken).ConfigureAwait(false);
                            await shareServer.BeginStreamAsync(header, cancellationToken).ConfigureAwait(false);
                            await interactionRouter.UpdateShareStateAsync(shareEndpoint, shareServer.ObserverCount).ConfigureAwait(false);
                            if (!emittedShareStarted)
                            {
                                WriteJsonLine(new
                                {
                                    type = SessionEventTypes.View.ShareStarted,
                                    session_id = sessionId,
                                    occurred_at = _timeProvider.GetUtcNow(),
                                    endpoint = shareEndpoint,
                                    observer_count = shareServer.ObserverCount
                                });
                                emittedShareStarted = true;
                            }
                        }

                        interactionRouter.AttachConnection(negotiatedConnection);
                        await viewBackend.InitializeAsync(negotiatedConnection, sessionRenderer, recorder, sessionCancellation.Token).ConfigureAwait(false);

                        if (firstConnection)
                        {
                            WriteJsonLine(new
                            {
                                type = SessionEventTypes.View.Started,
                                session_id = sessionId,
                                started_at = _timeProvider.GetUtcNow(),
                                device = activeDeviceSelector,
                                preset = options.PresetName,
                                decoder = options.Decoder,
                                codec = negotiatedConnection.Codec,
                                backend = viewBackend.Name,
                                capture_backend = negotiatedConnection.CaptureBackend,
                                requested_capture_backend = options.CaptureBackend,
                                headless = options.Headless,
                                record_path = options.RecordPath,
                                max_size = options.MaxSize,
                                max_fps = options.MaxFps,
                                video_bit_rate = options.VideoBitRate,
                                read_only = options.ReadOnly,
                                stats_interval_ms = options.StatsIntervalMs,
                                renderer_stats_interval_ms = options.RendererStatsIntervalMs,
                                overlay_screen_state = options.OverlayScreenState,
                                overlay_telemetry = options.OverlayTelemetry,
                                connection = negotiatedConnection,
                                artifacts = _artifacts.ToData()
                            });
                            await interactionRouter.EmitDeviceShelfSnapshotIfNeededAsync().ConfigureAwait(false);
                            firstConnection = false;
                            await interactionRouter.StartInitialRecordingIfNeededAsync().ConfigureAwait(false);
                        }
                        else
                        {
                            WriteJsonLine(new
                            {
                                type = SessionEventTypes.View.Reconnected,
                                session_id = sessionId,
                                reconnected_at = _timeProvider.GetUtcNow(),
                                device = activeDeviceSelector,
                                capture_backend = negotiatedConnection.CaptureBackend,
                                requested_capture_backend = options.CaptureBackend,
                                connection = negotiatedConnection
                            });
                            await interactionRouter.EmitDeviceShelfSnapshotIfNeededAsync().ConfigureAwait(false);
                        }

                        var sourcePackets = GuardReconnectBudgetAsync(
                            packetSource,
                            interactionRouter,
                            usesSharedTransport || !string.Equals(negotiatedConnection.CaptureBackend, ViewCaptureBackends.Screenrecord, StringComparison.OrdinalIgnoreCase)
                                ? null
                                : _timeProvider.GetUtcNow().Add(_autoReconnectAfter),
                            sessionCancellation.Token);
                        var sharedPackets = shareServer is null
                            ? sourcePackets
                            : RelayPacketsAsync(sourcePackets, shareServer, sessionCancellation.Token);
                        var viewTask = viewBackend.RunAsync(sharedPackets, sessionCancellation.Token);
                        var reconnectTask = interactionRouter.WaitForReconnectAsync();
                        var windowCloseTask = renderer is not null
                            ? renderer.WaitForCloseAsync(sessionCancellation.Token)
                            : Task.Delay(Timeout.Infinite, cancellationToken);

                        var completedTask = reconnectTask.IsCompleted
                            ? reconnectTask
                            : await Task.WhenAny(viewTask, windowCloseTask, reconnectTask).ConfigureAwait(false);
                        if (reconnectTask.IsCompleted)
                        {
                            completedTask = reconnectTask;
                        }
                        if (completedTask == reconnectTask)
                        {
                            await interactionRouter.StopRecordingForReconnectAsync().ConfigureAwait(false);
                            await sessionCancellation.CancelAsync();
                            if (!usesSharedTransport)
                            {
                                await _transportBootstrap.StopAsync(CancellationToken.None).ConfigureAwait(false);
                            }
                            try
                            {
                                await viewTask.ConfigureAwait(false);
                            }
                            catch (OperationCanceledException ex) when (sessionCancellation.IsCancellationRequested)
                            {
                                Debug.WriteLine($"View backend stopped for reconnect cancellation: {ex.Message}");
                            }

                            if (sessionRenderer is not null)
                            {
                                await sessionRenderer.FlushPendingStatsAsync().ConfigureAwait(false);
                            }

                            interactionRouter.ResetReconnectSignal();
                            activeDeviceSelector = interactionRouter.ActiveDeviceSelector;
                            continue;
                        }

                        if (completedTask == windowCloseTask)
                        {
                            endReason = "window_closed";
                            await sessionCancellation.CancelAsync();
                            if (!usesSharedTransport)
                            {
                                await _transportBootstrap.StopAsync(CancellationToken.None).ConfigureAwait(false);
                            }
                            try
                            {
                                await viewTask.ConfigureAwait(false);
                            }
                            catch (OperationCanceledException ex) when (sessionCancellation.IsCancellationRequested)
                            {
                                Debug.WriteLine($"View backend stopped for window-close cancellation: {ex.Message}");
                            }

                            break;
                        }

                        await viewTask.ConfigureAwait(false);
                        break;
                    }
                    finally
                    {
                        await streamConnection.DisposeAsync().ConfigureAwait(false);
                    }
                }

                if (sessionRenderer is not null)
                {
                    await sessionRenderer.FlushPendingStatsAsync().ConfigureAwait(false);
                }

                WriteJsonLine(new
                {
                    type = SessionEventTypes.View.Ended,
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
                type = SessionEventTypes.View.Error,
                session_id = sessionId,
                occurred_at = _timeProvider.GetUtcNow(),
                error = ErrorInfo.From(ex, ex is UsageException ? "usage_error" : ErrorInfo.Classify(ex.Message))
            });

            WriteJsonLine(new
            {
                type = SessionEventTypes.View.Ended,
                session_id = sessionId,
                ended_at = _timeProvider.GetUtcNow(),
                reason = "error"
            });

            return 1;
        }
        finally
        {
            if (!usesSharedTransport)
            {
                await _transportBootstrap.StopAsync(cancellationToken).ConfigureAwait(false);
            }

            if (shareServer is not null)
            {
                await shareServer.DisposeAsync().ConfigureAwait(false);
            }

            _ = _deviceHost;
        }
    }

    private void WriteJsonLine(object value)
    {
        lock (_writeGate)
        {
            _console.WriteLine(JsonSerializer.Serialize(value, OutputJsonOptions));
        }
    }

    private async IAsyncEnumerable<ViewPacket> GuardReconnectBudgetAsync(
        IAsyncEnumerable<ViewPacket> sourcePackets,
        ViewSessionInteractionRouter interactionRouter,
        DateTimeOffset? reconnectAt,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var reconnectRequested = false;

        await foreach (var packet in sourcePackets.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (!reconnectRequested &&
                reconnectAt.HasValue &&
                packet.PacketType != ViewPacketType.StreamEnd &&
                _timeProvider.GetUtcNow() >= reconnectAt.Value)
            {
                reconnectRequested = interactionRouter.RequestReconnect("stream_duration_guard", "screenrecord_time_limit");
            }

            yield return packet;
        }
    }

    private static ViewConnectionInfo BuildSharedConnectionInfo(string joinShareEndpoint)
    {
        var (host, port) = ViewShareEndpointParser.ParseConnect(joinShareEndpoint);
        return new ViewConnectionInfo($"share-{Guid.NewGuid():N}", "unknown", ViewTransportConstants.CurrentProtocolVersion, 0, 0, port, "share-relay", "shared-tcp", host);
    }

    private async IAsyncEnumerable<ViewPacket> RelayPacketsAsync(
        IAsyncEnumerable<ViewPacket> packets,
        TcpViewShareServer shareServer,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var packet in packets.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            await shareServer.PublishPacketAsync(packet, cancellationToken).ConfigureAwait(false);
            yield return packet;
        }
    }

    private async Task<(ViewConnectionInfo ConnectionInfo, IViewStreamConnection Connection, ViewStreamHeader Header, IAsyncEnumerable<ViewPacket> Packets)> TryFallbackOnStartupServerErrorAsync(
        ViewOptions options,
        string activeDeviceSelector,
        string sessionId,
        ViewConnectionInfo connectionInfo,
        ViewStreamHeader header,
        IViewStreamConnection connection,
        IAsyncEnumerable<ViewPacket> packets,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(options.CaptureBackend, ViewCaptureBackends.Auto, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(connectionInfo.CaptureBackend, ViewCaptureBackends.MediaProjection, StringComparison.OrdinalIgnoreCase))
        {
            return (connectionInfo, connection, header, packets);
        }

        var enumerator = packets.GetAsyncEnumerator(cancellationToken);
        if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
            return (connectionInfo, connection, header, EmptyPackets());
        }

        var firstPacket = enumerator.Current;
        if (firstPacket.PacketType != ViewPacketType.ServerError)
        {
            return (connectionInfo, connection, header, PrependPacket(firstPacket, enumerator, cancellationToken));
        }

        await enumerator.DisposeAsync().ConfigureAwait(false);
        await connection.DisposeAsync().ConfigureAwait(false);
        await _transportBootstrap.StopAsync(CancellationToken.None).ConfigureAwait(false);

        var reason = firstPacket.Payload.IsEmpty
            ? "MediaProjection helper reported a startup error."
            : System.Text.Encoding.UTF8.GetString(firstPacket.Payload.Span);
        WriteJsonLine(new
        {
            type = SessionEventTypes.View.CaptureBackendFallback,
            session_id = sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            requested_capture_backend = options.CaptureBackend,
            failed_capture_backend = ViewCaptureBackends.MediaProjection,
            fallback_capture_backend = ViewCaptureBackends.Screenrecord,
            reason
        });

        var fallback = await StartTransportWithBackendAndReadHeaderAsync(options, activeDeviceSelector, ViewCaptureBackends.Screenrecord, sessionId, cancellationToken).ConfigureAwait(false);
        return (fallback.ConnectionInfo, fallback.Connection, fallback.Header, _packetStreamReader.ReadPacketsAsync(fallback.Connection.Stream, cancellationToken));
    }

    private static async IAsyncEnumerable<ViewPacket> EmptyPackets()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    private static async IAsyncEnumerable<ViewPacket> PrependPacket(
        ViewPacket firstPacket,
        IAsyncEnumerator<ViewPacket> remainingPackets,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (remainingPackets.ConfigureAwait(false))
        {
            yield return firstPacket;
            while (await remainingPackets.MoveNextAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return remainingPackets.Current;
            }
        }
    }

    private async Task<(ViewConnectionInfo ConnectionInfo, IViewStreamConnection Connection, ViewStreamHeader Header)> StartTransportAndReadHeaderAsync(
        ViewOptions options,
        string activeDeviceSelector,
        string sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await StartTransportWithBackendAndReadHeaderAsync(options, activeDeviceSelector, options.CaptureBackend, sessionId, cancellationToken).ConfigureAwait(false);
        }
        catch (MediaProjectionConsentException ex) when (IsExplicitMediaProjectionRequest(options))
        {
            throw new UsageException($"{ex.Message} Use --capture-backend auto or --capture-backend screenrecord.");
        }
        catch (Exception ex) when (ShouldFallbackToScreenrecord(options, ex))
        {
            await _transportBootstrap.StopAsync(CancellationToken.None).ConfigureAwait(false);
            WriteJsonLine(new
            {
                type = SessionEventTypes.View.CaptureBackendFallback,
                session_id = sessionId,
                occurred_at = _timeProvider.GetUtcNow(),
                requested_capture_backend = options.CaptureBackend,
                failed_capture_backend = ViewCaptureBackends.MediaProjection,
                fallback_capture_backend = ViewCaptureBackends.Screenrecord,
                reason = ex.Message
            });

            return await StartTransportWithBackendAndReadHeaderAsync(options, activeDeviceSelector, ViewCaptureBackends.Screenrecord, sessionId, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsExplicitMediaProjectionRequest(ViewOptions options) =>
        string.Equals(options.CaptureBackend, ViewCaptureBackends.MediaProjection, StringComparison.OrdinalIgnoreCase);

    private static bool ShouldFallbackToScreenrecord(ViewOptions options, Exception exception) =>
        string.Equals(options.CaptureBackend, ViewCaptureBackends.Auto, StringComparison.OrdinalIgnoreCase) &&
        exception is not UsageException &&
        !IsMissingViewHelperPackage(exception.Message);

    private static bool IsMissingViewHelperPackage(string message) =>
        message.Contains("Android view helper package was not found", StringComparison.OrdinalIgnoreCase);

    private async Task<(ViewConnectionInfo ConnectionInfo, IViewStreamConnection Connection, ViewStreamHeader Header)> StartTransportWithBackendAndReadHeaderAsync(
        ViewOptions options,
        string activeDeviceSelector,
        string captureBackend,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var connectionInfo = await _transportBootstrap.StartAsync(
            new ViewStartRequest(
                options.AdbExecutable,
                activeDeviceSelector,
                options.MaxSize,
                options.MaxFps,
                options.VideoBitRate,
                options.Codec,
                captureBackend,
                options.CommandTimeout),
            phase => WriteStartupPhase(sessionId, phase),
            cancellationToken).ConfigureAwait(false);

        return await ConnectAndReadHeaderAsync(connectionInfo, cancellationToken).ConfigureAwait(false);
    }

    private void WriteStartupPhase(string sessionId, ViewStartupPhase phase)
    {
        WriteJsonLine(new
        {
            type = SessionEventTypes.View.StartupPhase,
            session_id = sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            phase = phase.Phase,
            status = phase.Status,
            summary = phase.Summary,
            detail = phase.Detail,
            recommendation = phase.Recommendation
        });
    }

    private async Task<(ViewConnectionInfo ConnectionInfo, IViewStreamConnection Connection, ViewStreamHeader Header)> ConnectAndReadHeaderAsync(
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
                return (connectionInfo, connection, header);
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

