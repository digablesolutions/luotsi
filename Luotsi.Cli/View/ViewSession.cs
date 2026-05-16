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

        return new NativeWindowViewRenderer(new Sdl3ViewWindowSurfaceFactory(), interactionHandler);
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
                WriteJsonLine);
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

internal sealed class NullViewRendererFactory : IViewRendererFactory
{
    public IViewRenderer? Create(ViewOptions options, Func<ViewInteractionRequest, Task> interactionHandler) => null;
}

internal sealed class NullViewRecorderFactory : IViewRecorderFactory
{
    public IViewRecorder? Create(ViewOptions options) => null;
}

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

internal sealed class ViewSessionInteractionRouter(
    IDeviceHost deviceHost,
    ArtifactSession artifacts,
    ViewOptions options,
    SessionControlledViewRecorder recorder,
    TimeProvider timeProvider,
    string sessionId,
    Action<object> writeJsonLine)
{
    private readonly IDeviceHost _deviceHost = deviceHost ?? throw new ArgumentNullException(nameof(deviceHost));
    private readonly ArtifactSession _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
    private readonly ViewOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly SessionControlledViewRecorder _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly string _sessionId = string.IsNullOrWhiteSpace(sessionId) ? throw new ArgumentException("Session id is required.", nameof(sessionId)) : sessionId;
    private readonly Action<object> _writeJsonLine = writeJsonLine ?? throw new ArgumentNullException(nameof(writeJsonLine));

    private CancellationTokenSource? _iterationCancellation;
    private TaskCompletionSource _reconnectRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _initialRecordingStarted;
    private int _screenshotSequence;
    private int _recordingSequence;
    private Func<ViewChromeState, Task>? _chromeUpdater;
    private IReadOnlyList<ViewChromeDevice> _devices = [];
    private string? _shareEndpoint = options.JoinShareEndpoint;
    private int _observerCount;

    public string ActiveDeviceSelector { get; private set; } = options.DeviceSelector;

    public void BeginIteration(string deviceSelector, CancellationTokenSource iterationCancellation)
    {
        ActiveDeviceSelector = string.IsNullOrWhiteSpace(deviceSelector) ? _options.DeviceSelector : deviceSelector;
        _iterationCancellation = iterationCancellation;
        UpdateActiveDeviceFlags();
    }

    public void AttachConnection(ViewConnectionInfo connectionInfo) => _ = _recorder.InitializeAsync(connectionInfo);

    public void AttachChromeUpdater(Func<ViewChromeState, Task> chromeUpdater) => _chromeUpdater = chromeUpdater;

    public Task WaitForReconnectAsync() => _reconnectRequested.Task;

    public void ResetReconnectSignal() => _reconnectRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task StartInitialRecordingIfNeededAsync()
    {
        if (_initialRecordingStarted || string.IsNullOrWhiteSpace(_options.RecordPath))
        {
            return;
        }

        _initialRecordingStarted = true;
        await _recorder.StartAsync(_options.RecordPath).ConfigureAwait(false);
        await PublishChromeAsync().ConfigureAwait(false);
        WriteEvent(new
        {
            type = "view_recording_started",
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            record_path = _options.RecordPath,
            source = "startup"
        });
    }

    public async Task StopRecordingForReconnectAsync()
    {
        if (!_recorder.IsRecording)
        {
            return;
        }

        var recordPath = _recorder.ActiveRecordPath;
        await _recorder.StopAsync().ConfigureAwait(false);
        await PublishChromeAsync().ConfigureAwait(false);
        WriteEvent(new
        {
            type = "view_recording_stopped",
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            record_path = recordPath,
            reason = "reconnect"
        });
    }

    public async Task HandleAsync(ViewInteractionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (request)
        {
            case ViewTapRequest tapRequest:
                if (TryBlockReadOnly("tap"))
                {
                    return;
                }

                await _deviceHost.TapPointAsync("view-window", null, null, tapRequest.XRatio, tapRequest.YRatio, 0).ConfigureAwait(false);
                break;

            case ViewWindowCommandRequest windowCommandRequest:
                await HandleCommandAsync(windowCommandRequest.Command).ConfigureAwait(false);
                break;

            case ViewTextInputRequest textInputRequest:
                if (TryBlockReadOnly("text_input"))
                {
                    return;
                }

                await _deviceHost.TypeTextAsync(textInputRequest.Text).ConfigureAwait(false);
                break;

            case ViewKeyInputRequest keyInputRequest:
                if (TryBlockReadOnly("key_input"))
                {
                    return;
                }

                await _deviceHost.KeyEventAsync(keyInputRequest.Code).ConfigureAwait(false);
                break;

            case ViewScrollRequest scrollRequest:
                if (TryBlockReadOnly("scroll"))
                {
                    return;
                }

                await _deviceHost.ScrollAsync(scrollRequest.HorizontalTicks, scrollRequest.VerticalTicks).ConfigureAwait(false);
                break;

            case ViewClipboardPasteRequest clipboardPasteRequest:
                if (TryBlockReadOnly("clipboard"))
                {
                    return;
                }

                await _deviceHost.TypeTextAsync(clipboardPasteRequest.Text).ConfigureAwait(false);
                WriteEvent(new
                {
                    type = "view_clipboard_pasted",
                    session_id = _sessionId,
                    occurred_at = _timeProvider.GetUtcNow(),
                    length = clipboardPasteRequest.Text.Length
                });
                break;

            case ViewFileDropRequest fileDropRequest:
                if (TryBlockReadOnly("file_drop"))
                {
                    return;
                }

                await HandleFileDropAsync(fileDropRequest.FilePath).ConfigureAwait(false);
                break;

            case ViewSwitchDeviceRequest switchDeviceRequest:
                await HandleDeviceSwitchAsync(switchDeviceRequest).ConfigureAwait(false);
                break;

            default:
                throw new InvalidOperationException($"Unsupported view interaction request '{request.GetType().Name}'.");
        }
    }

    private async Task HandleDeviceSwitchAsync(ViewSwitchDeviceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceSelector))
        {
            throw new UsageException("device switch requires a non-empty device selector.");
        }

        if (string.Equals(request.DeviceSelector, ActiveDeviceSelector, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        WriteEvent(new
        {
            type = "view_device_switch_requested",
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            from_device = ActiveDeviceSelector,
            to_device = request.DeviceSelector
        });
        ActiveDeviceSelector = request.DeviceSelector;
        UpdateActiveDeviceFlags();
        await PublishChromeAsync().ConfigureAwait(false);
        _reconnectRequested.TrySetResult();
        _iterationCancellation?.Cancel();
    }

    private async Task HandleCommandAsync(ViewWindowCommand command)
    {
        switch (command)
        {
            case ViewWindowCommand.TakeScreenshot:
                if (TryBlockUnsupported("screenshot", "observer_session", !string.IsNullOrWhiteSpace(_options.JoinShareEndpoint)))
                {
                    break;
                }

            {
                var label = $"view-window-{Interlocked.Increment(ref _screenshotSequence):000}";
                var result = await _deviceHost.TakeScreenshotAsync(label).ConfigureAwait(false);
                WriteEvent(new
                {
                    type = "view_screenshot_captured",
                    session_id = _sessionId,
                    occurred_at = _timeProvider.GetUtcNow(),
                    label = result.Label,
                    file = result.File
                });
                break;
            }

            case ViewWindowCommand.ToggleRecording:
                await ToggleRecordingAsync().ConfigureAwait(false);
                break;

            case ViewWindowCommand.Reconnect:
                WriteEvent(new
                {
                    type = "view_reconnect_requested",
                    session_id = _sessionId,
                    occurred_at = _timeProvider.GetUtcNow(),
                    device = ActiveDeviceSelector
                });
                _reconnectRequested.TrySetResult();
                _iterationCancellation?.Cancel();
                break;

            default:
                break;
        }
    }

    private async Task ToggleRecordingAsync()
    {
        if (_recorder.IsRecording)
        {
            var recordPath = _recorder.ActiveRecordPath;
            await _recorder.StopAsync().ConfigureAwait(false);
            await PublishChromeAsync().ConfigureAwait(false);
            WriteEvent(new
            {
                type = "view_recording_stopped",
                session_id = _sessionId,
                occurred_at = _timeProvider.GetUtcNow(),
                record_path = recordPath,
                reason = "operator"
            });
            return;
        }

        var nextPath = BuildNextRecordingPath();
        await _recorder.StartAsync(nextPath).ConfigureAwait(false);
        await PublishChromeAsync().ConfigureAwait(false);
        WriteEvent(new
        {
            type = "view_recording_started",
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            record_path = nextPath,
            source = "operator"
        });
    }

    private string BuildNextRecordingPath()
    {
        var sequence = Interlocked.Increment(ref _recordingSequence);
        if (sequence == 1 && !string.IsNullOrWhiteSpace(_options.RecordPath) && !_initialRecordingStarted)
        {
            return _options.RecordPath;
        }

        var preferredPath = _options.RecordPath;
        var extension = string.IsNullOrWhiteSpace(preferredPath) ? ".h264" : Path.GetExtension(preferredPath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".h264";
        }

        var directory = string.IsNullOrWhiteSpace(preferredPath)
            ? _artifacts.Root
            : Path.GetDirectoryName(Path.GetFullPath(preferredPath)) ?? _artifacts.Root;
        var fileBaseName = string.IsNullOrWhiteSpace(preferredPath)
            ? "view-window-record"
            : Path.GetFileNameWithoutExtension(preferredPath);
        return Path.Combine(directory, $"{fileBaseName}-{sequence:000}{extension}");
    }

    private async Task HandleFileDropAsync(string filePath)
    {
        if (string.Equals(Path.GetExtension(filePath), ".apk", StringComparison.OrdinalIgnoreCase))
        {
            var installResult = await _deviceHost.InstallPackageAsync(filePath).ConfigureAwait(false);
            WriteEvent(new
            {
                type = "view_package_installed",
                session_id = _sessionId,
                occurred_at = _timeProvider.GetUtcNow(),
                package_path = installResult.PackagePath
            });
            return;
        }

        var pushResult = await _deviceHost.PushFileAsync(filePath).ConfigureAwait(false);
        WriteEvent(new
        {
            type = "view_file_pushed",
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            local_path = pushResult.LocalPath,
            remote_path = pushResult.RemotePath
        });
    }

    public async Task EmitDeviceShelfSnapshotIfNeededAsync()
    {
        if (!string.IsNullOrWhiteSpace(_options.JoinShareEndpoint))
        {
            _devices = [];
            await PublishChromeAsync().ConfigureAwait(false);
            return;
        }

        try
        {
            var devices = await _deviceHost.GetDevicesAsync().ConfigureAwait(false);
            _devices = devices.Devices
                .Select((device, index) => new ViewChromeDevice(
                    index + 1,
                    device.Serial ?? $"device-{index + 1}",
                    device.Status,
                    device.Details,
                    string.Equals(device.Serial, ActiveDeviceSelector, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            UpdateActiveDeviceFlags();
            await PublishChromeAsync().ConfigureAwait(false);
            if (devices.Devices.Count <= 1)
            {
                return;
            }

            WriteEvent(new
            {
                type = "view_device_shelf",
                session_id = _sessionId,
                observed_at = _timeProvider.GetUtcNow(),
                active_device = ActiveDeviceSelector,
                devices = devices.Devices
            });
        }
        catch
        {
        }
    }

    public Task PublishChromeAsync()
    {
        var chromeUpdater = _chromeUpdater;
        return chromeUpdater is null ? Task.CompletedTask : chromeUpdater(BuildChromeState());
    }

    public async Task UpdateShareStateAsync(string? shareEndpoint, int observerCount)
    {
        _shareEndpoint = shareEndpoint;
        _observerCount = observerCount;
        await PublishChromeAsync().ConfigureAwait(false);
    }

    private ViewChromeState BuildChromeState() => new(
        ActiveDeviceSelector,
        _devices,
        _options.ReadOnly,
        !string.IsNullOrWhiteSpace(_options.JoinShareEndpoint),
        _recorder.IsRecording,
        string.IsNullOrWhiteSpace(_options.JoinShareEndpoint),
        true,
        true,
        _devices.Count > 1 && string.IsNullOrWhiteSpace(_options.JoinShareEndpoint),
        _shareEndpoint,
        _observerCount);

    private void UpdateActiveDeviceFlags()
    {
        if (_devices.Count == 0)
        {
            return;
        }

        _devices = _devices
            .Select(device => device with { IsActive = string.Equals(device.DeviceSelector, ActiveDeviceSelector, StringComparison.OrdinalIgnoreCase) })
            .ToArray();
    }

    private bool TryBlockReadOnly(string requestType)
    {
        if (!_options.ReadOnly)
        {
            return false;
        }

        WriteEvent(new
        {
            type = "view_input_blocked",
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            request_type = requestType,
            reason = "read_only"
        });
        return true;
    }

    private bool TryBlockUnsupported(string requestType, string reason, bool unsupported)
    {
        if (!unsupported)
        {
            return false;
        }

        WriteEvent(new
        {
            type = "view_input_blocked",
            session_id = _sessionId,
            occurred_at = _timeProvider.GetUtcNow(),
            request_type = requestType,
            reason
        });
        return true;
    }

    private void WriteEvent(object value) => _writeJsonLine(value);
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

    public Task UpdateChromeAsync(ViewChromeState chrome, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chrome);
        return _innerRenderer?.UpdateChromeAsync(chrome, cancellationToken) ?? Task.CompletedTask;
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