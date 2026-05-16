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
    IViewRecorderFactory? viewRecorderFactory = null,
    IArtifactFolderOpener? artifactFolderOpener = null) : IViewSession
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
    private readonly object _writeGate = new();

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
            var interactionRouter = new ViewSessionInteractionRouter(
                _deviceHost,
                _artifacts,
                options,
                recorder,
                _timeProvider,
                sessionId,
                WriteJsonLine,
                artifactFolderOpener);
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
                        type = "view_stats",
                        session_id = sessionId,
                        observed_at = _timeProvider.GetUtcNow(),
                        stats
                    });

                    return Task.CompletedTask;
                });
                interactionRouter.AttachStreamPauseUpdater(sessionRenderer.SetPaused);
                interactionRouter.AttachChromeUpdater(chrome => sessionRenderer.UpdateChromeAsync(chrome));
                var firstConnection = true;

                while (true)
                {
                    await using var viewBackend = _viewBackendFactory.Create(options);
                    using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    interactionRouter.BeginIteration(activeDeviceSelector, sessionCancellation);
                    await interactionRouter.PublishChromeAsync().ConfigureAwait(false);
                    var connectionInfo = usesSharedTransport
                        ? BuildSharedConnectionInfo(options.JoinShareEndpoint!)
                        : await _transportBootstrap.StartAsync(
                            new ViewStartRequest(
                                options.AdbExecutable,
                                activeDeviceSelector,
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
                                    type = observerEvent.EventType == "connected" ? "view_share_client_connected" : "view_share_client_disconnected",
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
                                type = "view_share_started",
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
                            type = "view_started",
                            session_id = sessionId,
                            started_at = _timeProvider.GetUtcNow(),
                            device = activeDeviceSelector,
                            preset = options.PresetName,
                            decoder = options.Decoder,
                            codec = negotiatedConnection.Codec,
                            backend = viewBackend.Name,
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
                            type = "view_reconnected",
                            session_id = sessionId,
                            reconnected_at = _timeProvider.GetUtcNow(),
                            device = activeDeviceSelector,
                            connection = negotiatedConnection
                        });
                        await interactionRouter.EmitDeviceShelfSnapshotIfNeededAsync().ConfigureAwait(false);
                    }

                    var sourcePackets = _packetStreamReader.ReadPacketsAsync(streamConnection.Stream, sessionCancellation.Token);
                    var sharedPackets = shareServer is null
                        ? sourcePackets
                        : RelayPacketsAsync(sourcePackets, shareServer, sessionCancellation.Token);
                    var viewTask = viewBackend.RunAsync(sharedPackets, sessionCancellation.Token);
                    var reconnectTask = interactionRouter.WaitForReconnectAsync();
                    var windowCloseTask = renderer is not null
                        ? renderer.WaitForCloseAsync()
                        : Task.Delay(Timeout.Infinite, cancellationToken);

                    var completedTask = reconnectTask.IsCompleted
                        ? reconnectTask
                        : await Task.WhenAny(viewTask, windowCloseTask, reconnectTask).ConfigureAwait(false);
                    if (completedTask == viewTask && reconnectTask.IsCompleted)
                    {
                        completedTask = reconnectTask;
                    }
                    if (completedTask == reconnectTask)
                    {
                        await interactionRouter.StopRecordingForReconnectAsync().ConfigureAwait(false);
                        sessionCancellation.Cancel();
                        if (!usesSharedTransport)
                        {
                            await _transportBootstrap.StopAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                        try
                        {
                            await viewTask.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested)
                        {
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
                        sessionCancellation.Cancel();
                        if (!usesSharedTransport)
                        {
                            await _transportBootstrap.StopAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                        try
                        {
                            await viewTask.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested)
                        {
                        }

                        break;
                    }

                    await viewTask.ConfigureAwait(false);
                    break;
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

    private static ViewConnectionInfo BuildSharedConnectionInfo(string joinShareEndpoint)
    {
        var (host, port) = ViewShareEndpointParser.ParseConnect(joinShareEndpoint);
        return new ViewConnectionInfo($"share-{Guid.NewGuid():N}", "unknown", ViewTransportConstants.CurrentProtocolVersion, 0, 0, port, "share-relay", "shared-tcp", host);
    }

    private async IAsyncEnumerable<ViewPacket> RelayPacketsAsync(
        IAsyncEnumerable<ViewPacket> packets,
        TcpViewShareServer shareServer,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var packet in packets.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            await shareServer.PublishPacketAsync(packet, cancellationToken).ConfigureAwait(false);
            yield return packet;
        }
    }

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

