using System.Runtime.InteropServices;
using System.Text;
using Luotsi.Cli.View.Contracts;
using SDL;
using static SDL.SDL3;

namespace Luotsi.Cli.View.Rendering;

/// <summary>
/// Creates the SDL3-backed window surface used by the built-in renderer.
/// </summary>
public sealed class Sdl3ViewWindowSurfaceFactory : IViewWindowSurfaceFactory
{
    /// <inheritdoc />
    public IViewWindowSurface Create() => new Sdl3ViewWindowSurface();
}

internal sealed class Sdl3ViewWindowSurface : IViewWindowSurface
{
    private const byte MouseButtonLeft = 1;
    private const int EventWaitTimeoutMs = 16;
    private const int TooltipGlyphCellSize = 2;
    private const int TooltipGlyphGap = 2;
    private const int TooltipPaddingX = 8;
    private const int TooltipPaddingY = 6;
    private const int TooltipOffsetY = 6;

    private readonly TaskCompletionSource _readySource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _closedSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _frameLock = new();
    private readonly Lock _statsLock = new();
    private readonly Lock _chromeLock = new();
    private readonly IViewWindowIconProvider _iconProvider;

    private Thread? _windowThread;
    private string _title = "Luotsi View";
    private ViewWindowOptions _windowOptions = new();
    private Func<ViewPointerEvent, Task>? _pointerHandler;
    private Func<ViewInteractionRequest, Task>? _interactionHandler;
    private ViewFrameSnapshot? _frame;
    private ViewStats? _stats;
    private ViewChromeState? _chrome;
    private bool _frameDirty;
    private bool _statsDirty;
    private bool _chromeDirty;
    private bool _disposeRequested;
    private bool _disposed;
    private bool _isFullscreen;
    private bool _showHelpOverlay;
    private ViewScaleMode _scaleMode = ViewScaleMode.Fit;
    private ViewChromeTooltip? _hoverTooltip;
    private int _lastMouseClientX;
    private int _lastMouseClientY;
    private bool _hasMousePosition;

    private unsafe SDL_Window* _window;
    private unsafe SDL_Renderer* _renderer;
    private unsafe SDL_Texture* _texture;
    private int _textureWidth;
    private int _textureHeight;

    public Sdl3ViewWindowSurface()
        : this(new LuotsiWindowIconProvider())
    {
    }

    internal Sdl3ViewWindowSurface(IViewWindowIconProvider iconProvider)
    {
        _iconProvider = iconProvider ?? throw new ArgumentNullException(nameof(iconProvider));
    }

    public async Task InitializeAsync(
        string title,
        ViewDisplayInfo displayInfo,
        Func<ViewPointerEvent, Task> pointerHandler,
        Func<ViewInteractionRequest, Task>? interactionHandler = null,
        ViewWindowOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(displayInfo);
        ArgumentNullException.ThrowIfNull(pointerHandler);

        _title = string.IsNullOrWhiteSpace(title) ? "Luotsi View" : title;
        _windowOptions = options ?? new ViewWindowOptions();
        _scaleMode = _windowOptions.InitialScaleMode;
        _pointerHandler = pointerHandler;
        _interactionHandler = interactionHandler;
        EnsureWindowThread(displayInfo);
        await _readySource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task PresentAsync(ViewFrame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Sdl3ViewWindowSurface));
        }

        await _readySource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_frameLock)
        {
            _frame = ViewFrameSnapshot.From(frame);
            _frameDirty = true;
        }
    }

    public async Task UpdateStatsAsync(ViewStats stats, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stats);

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Sdl3ViewWindowSurface));
        }

        await _readySource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_statsLock)
        {
            _stats = stats;
            _statsDirty = true;
        }
    }

    public async Task UpdateChromeAsync(ViewChromeState chrome, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chrome);

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Sdl3ViewWindowSurface));
        }

        await _readySource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_chromeLock)
        {
            _chrome = chrome;
            _chromeDirty = true;
        }
    }

    public Task WaitForCloseAsync(CancellationToken cancellationToken = default) => _closedSource.Task.WaitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposeRequested = true;

        if (_windowThread is null)
        {
            return;
        }

        try
        {
            await _closedSource.Task.ConfigureAwait(false);
        }
        catch
        {
            // ignored
        }
    }

    private void EnsureWindowThread(ViewDisplayInfo displayInfo)
    {
        if (_windowThread is not null)
        {
            return;
        }

        _windowThread = new Thread(() => WindowThreadStart(displayInfo))
        {
            IsBackground = true,
            Name = "LuotsiSdlViewWindow"
        };
        _windowThread.Start();
    }

    private unsafe void WindowThreadStart(ViewDisplayInfo displayInfo)
    {
        try
        {
            if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO | SDL_InitFlags.SDL_INIT_EVENTS))
            {
                throw new InvalidOperationException($"Failed to initialize SDL3 video subsystem: {SDL_GetError()}");
            }

            var hasUsableBounds = NativeSdlWindow.TryGetPrimaryDisplayUsableBounds(out var usableWidth, out var usableHeight);
            var (windowWidth, windowHeight) = ResolveInitialWindowSize(
                displayInfo.Width,
                displayInfo.Height,
                hasUsableBounds ? usableWidth : 0,
                hasUsableBounds ? usableHeight : 0);

            var titleBytes = Encoding.UTF8.GetBytes(_title + '\0');
            fixed (byte* title = titleBytes)
            {
                _window = SDL_CreateWindow(
                    title,
                    windowWidth,
                    windowHeight,
                    SDL_WindowFlags.SDL_WINDOW_RESIZABLE | SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY);
            }

            if (_window is null)
            {
                throw new InvalidOperationException($"Failed to create SDL3 view window: {SDL_GetError()}");
            }

            TryApplyWindowIcon();

            if (_windowOptions.AlwaysOnTop && !NativeSdlWindow.SetAlwaysOnTop(_window, true))
            {
                throw new InvalidOperationException($"Failed to set SDL3 view window always-on-top: {SDL_GetError()}");
            }

            _renderer = SDL_CreateRenderer(_window, (byte*)null);
            if (_renderer is null)
            {
                throw new InvalidOperationException($"Failed to create SDL3 renderer: {SDL_GetError()}");
            }

            _ = SDL_SetRenderDrawBlendMode(_renderer, SDL_BlendMode.SDL_BLENDMODE_BLEND);

            _ = SDL_StartTextInput(_window);

            _readySource.TrySetResult();
            RunEventLoop();
        }
        catch (Exception ex)
        {
            _readySource.TrySetException(ex);
            _closedSource.TrySetException(ex);
        }
        finally
        {
            DestroyTexture();

            if (_renderer is not null)
            {
                SDL_DestroyRenderer(_renderer);
                _renderer = null;
            }

            if (_window is not null)
            {
                _ = SDL_StopTextInput(_window);
                SDL_DestroyWindow(_window);
                _window = null;
            }

            SDL_Quit();
            _closedSource.TrySetResult();
        }
    }

    private unsafe void RunEventLoop()
    {
        var shouldRender = true;
        while (!_disposeRequested)
        {
            SDL_Event sdlEvent;
            if (SDL_WaitEventTimeout(&sdlEvent, EventWaitTimeoutMs))
            {
                shouldRender |= HandleEvent(sdlEvent);
                while (SDL_PollEvent(&sdlEvent))
                {
                    shouldRender |= HandleEvent(sdlEvent);
                }
            }

            var statsDirty = ConsumeStatsDirty();
            var chromeDirty = ConsumeChromeDirty();
            if (statsDirty || chromeDirty)
            {
                UpdateWindowTitle();
                if (chromeDirty)
                {
                    shouldRender |= RefreshHoverTooltip();
                }
            }

            if (ConsumeFrameDirty() || shouldRender)
            {
                RenderFrame();
                shouldRender = false;
            }
        }
    }

    private bool HandleEvent(SDL_Event sdlEvent)
    {
        switch (sdlEvent.type)
        {
            case (uint)SDL_EventType.SDL_EVENT_QUIT:
            case (uint)SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED:
            case (uint)SDL_EventType.SDL_EVENT_WINDOW_DESTROYED:
                _disposeRequested = true;
                return false;

            case (uint)SDL_EventType.SDL_EVENT_WINDOW_EXPOSED:
            case (uint)SDL_EventType.SDL_EVENT_WINDOW_RESIZED:
            case (uint)SDL_EventType.SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
            case (uint)SDL_EventType.SDL_EVENT_WINDOW_DISPLAY_SCALE_CHANGED:
                return true;

            case (uint)SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
                HandlePointer(sdlEvent.button);
                return false;

            case (uint)SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                return HandleMouseMotion(sdlEvent.motion);

            case (uint)SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                HandleMouseWheel(sdlEvent.wheel);
                return false;

            case (uint)SDL_EventType.SDL_EVENT_KEY_DOWN:
                return HandleKey(sdlEvent.key);

            case (uint)SDL_EventType.SDL_EVENT_TEXT_INPUT:
                HandleTextInput(sdlEvent.text);
                return false;

            case (uint)SDL_EventType.SDL_EVENT_DROP_FILE:
                HandleDropFile(sdlEvent.drop);
                return false;

            default:
                return false;
        }
    }

    private bool ConsumeFrameDirty()
    {
        lock (_frameLock)
        {
            var frameDirty = _frameDirty;
            _frameDirty = false;
            return frameDirty;
        }
    }

    private bool ConsumeStatsDirty()
    {
        lock (_statsLock)
        {
            var statsDirty = _statsDirty;
            _statsDirty = false;
            return statsDirty;
        }
    }

    private bool ConsumeChromeDirty()
    {
        lock (_chromeLock)
        {
            var chromeDirty = _chromeDirty;
            _chromeDirty = false;
            return chromeDirty;
        }
    }

    private unsafe void UpdateWindowTitle()
    {
        if (_window is null)
        {
            return;
        }

        var titleBytes = Encoding.UTF8.GetBytes(BuildWindowTitle() + '\0');
        fixed (byte* title = titleBytes)
        {
            SDL_SetWindowTitle(_window, title);
        }
    }

    private string BuildWindowTitle()
    {
        ViewStats? stats;
        lock (_statsLock)
        {
            stats = _stats;
        }

        ViewChromeState? chrome;
        lock (_chromeLock)
        {
            chrome = _chrome;
        }

        var titleParts = new List<string> { BuildWindowTitlePrefix() };
        if (chrome is not null)
        {
            titleParts.Add(chrome.IsObserverSession ? "observer" : chrome.ReadOnly ? "read-only" : "interactive");
            if (!string.IsNullOrWhiteSpace(chrome.ActiveDevice))
            {
                titleParts.Add($"active {chrome.ActiveDevice}");
            }

            if (chrome.Devices.Count > 1)
            {
                titleParts.Add($"shelf {string.Join(", ", chrome.Devices.Select(device => $"{device.Index}:{device.DeviceSelector}"))}");
            }

            if (!string.IsNullOrWhiteSpace(chrome.ShareEndpoint))
            {
                titleParts.Add($"share {chrome.ShareEndpoint} obs {chrome.ObserverCount}");
            }
        }

        if (stats is null)
        {
            return string.Join(" | ", titleParts);
        }

        titleParts.Add($"decode {stats.DecodeFps:0.0} fps");
        titleParts.Add($"present {stats.PresentFps:0.0} fps");
        titleParts.Add($"latency {stats.EndToEndLatencyMs} ms");
        return string.Join(" | ", titleParts);
    }

    private string BuildWindowTitlePrefix()
    {
        var modeLabel = _scaleMode == ViewScaleMode.Fill ? "fill" : "fit";
        return _isFullscreen ? $"{_title} | {modeLabel} | fullscreen" : $"{_title} | {modeLabel}";
    }

    private unsafe void RenderFrame()
    {
        if (_renderer is null)
        {
            return;
        }

        ViewFrameSnapshot? frame;
        lock (_frameLock)
        {
            frame = _frame;
        }

        if (frame is not null)
        {
            EnsureTexture(frame);
            UpdateTexture(frame);
        }

        _ = SDL_SetRenderDrawColor(_renderer, 0, 0, 0, 255);
        _ = SDL_RenderClear(_renderer);

        if (TryGetWindowPixelSize(out var pixelWidth, out var pixelHeight))
        {
            if (frame is not null && _texture is not null)
            {
                var layout = ViewPointerMapper.ComputeLayout(pixelWidth, pixelHeight, frame.Width, frame.Height, _scaleMode);
                if (layout is {Width: > 0, Height: > 0})
                {
                    var destinationRect = new SDL_FRect
                    {
                        x = layout.Left,
                        y = layout.Top,
                        w = layout.Width,
                        h = layout.Height
                    };

                    _ = SDL_RenderTexture(_renderer, _texture, null, &destinationRect);
                }
            }

            RenderChrome(pixelWidth, pixelHeight);
        }

        _ = SDL_RenderPresent(_renderer);
    }

    private unsafe void EnsureTexture(ViewFrameSnapshot frame)
    {
        if (_renderer is null)
        {
            return;
        }

        if (_texture is not null && _textureWidth == frame.Width && _textureHeight == frame.Height)
        {
            return;
        }

        DestroyTexture();
        _texture = SDL_CreateTexture(
            _renderer,
            // Libav emits BGRA byte-order pixels; on little-endian desktop hosts
            // SDL's ARGB8888 texture format matches that memory layout.
            SDL_PixelFormat.SDL_PIXELFORMAT_ARGB8888,
            SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING,
            frame.Width,
            frame.Height);
        if (_texture is null)
        {
            throw new InvalidOperationException($"Failed to create SDL3 streaming texture: {SDL_GetError()}");
        }

        _textureWidth = frame.Width;
        _textureHeight = frame.Height;
    }

    private unsafe void UpdateTexture(ViewFrameSnapshot frame)
    {
        if (_texture is null)
        {
            return;
        }

        fixed (byte* pixelData = frame.PixelData)
        {
            if (!SDL_UpdateTexture(_texture, null, (IntPtr)pixelData, frame.RowStride))
            {
                throw new InvalidOperationException($"Failed to update SDL3 texture: {SDL_GetError()}");
            }
        }
    }

    private unsafe bool TryGetWindowPixelSize(out int width, out int height)
    {
        width = 0;
        height = 0;
        if (_window is null)
        {
            return false;
        }

        var localWidth = 0;
        var localHeight = 0;

        if (SDL_GetWindowSizeInPixels(_window, &localWidth, &localHeight))
        {
            width = localWidth;
            height = localHeight;
            return true;
        }

        if (!SDL_GetWindowSize(_window, &localWidth, &localHeight))
        {
            return false;
        }

        width = localWidth;
        height = localHeight;
        return true;
    }

    private unsafe bool TryGetLogicalWindowSize(out int width, out int height)
    {
        width = 0;
        height = 0;
        if (_window is null)
        {
            return false;
        }

        var localWidth = 0;
        var localHeight = 0;
        if (!SDL_GetWindowSize(_window, &localWidth, &localHeight))
        {
            return false;
        }

        width = localWidth;
        height = localHeight;
        return true;
    }

    private void HandlePointer(SDL_MouseButtonEvent mouseButtonEvent)
    {
        if (mouseButtonEvent.button != MouseButtonLeft)
        {
            return;
        }

        var clientX = (int)Math.Round(mouseButtonEvent.x, MidpointRounding.AwayFromZero);
        var clientY = (int)Math.Round(mouseButtonEvent.y, MidpointRounding.AwayFromZero);
        if (TryHandleChromeInteraction(clientX, clientY))
        {
            return;
        }

        var pointerHandler = _pointerHandler;
        if (pointerHandler is null)
        {
            return;
        }

        if (!TryGetLogicalWindowSize(out var clientWidth, out var clientHeight))
        {
            return;
        }

        var pointerEvent = new ViewPointerEvent(
            clientX,
            clientY,
            clientWidth,
            clientHeight,
            _scaleMode);

        QueueInteraction(
            () => pointerHandler(pointerEvent),
            ex => ReportInteractionFailureAsync(
                new ViewPointerInteractionRequest(
                    pointerEvent.ClientX,
                    pointerEvent.ClientY,
                    pointerEvent.ClientWidth,
                    pointerEvent.ClientHeight,
                    pointerEvent.ScaleMode),
                ex));
    }

    private bool HandleMouseMotion(SDL_MouseMotionEvent mouseMotionEvent)
    {
        _lastMouseClientX = (int)Math.Round(mouseMotionEvent.x, MidpointRounding.AwayFromZero);
        _lastMouseClientY = (int)Math.Round(mouseMotionEvent.y, MidpointRounding.AwayFromZero);
        _hasMousePosition = true;
        return RefreshHoverTooltip();
    }

    private bool RefreshHoverTooltip()
    {
        ViewChromeTooltip? nextTooltip = null;
        if (_hasMousePosition && TryGetLogicalWindowSize(out var clientWidth, out var clientHeight))
        {
            ViewChromeState? chrome;
            lock (_chromeLock)
            {
                chrome = _chrome;
            }

            nextTooltip = ViewChromeLayout.ResolveTooltip(clientWidth, clientHeight, _lastMouseClientX, _lastMouseClientY, chrome, _scaleMode, _isFullscreen);
        }

        if (Equals(_hoverTooltip, nextTooltip))
        {
            return false;
        }

        _hoverTooltip = nextTooltip;
        return true;
    }

    private bool HandleKey(SDL_KeyboardEvent keyboardEvent)
    {
        if (keyboardEvent.repeat)
        {
            return false;
        }

        if (_isFullscreen && keyboardEvent.key == SDL_Keycode.SDLK_ESCAPE || IsAltPressed(keyboardEvent.mod) &&
            (keyboardEvent.key == SDL_Keycode.SDLK_RETURN || keyboardEvent.key == SDL_Keycode.SDLK_RETURN2))
        {
            ToggleFullscreen();
            return true;
        }

        if (IsCtrlPressed(keyboardEvent.mod) && keyboardEvent.key == SDL_Keycode.SDLK_V)
        {
            HandleClipboardPaste();
            return false;
        }

        switch (keyboardEvent.key)
        {
            case SDL_Keycode.SDLK_F1:
                DispatchInteraction(new ViewWindowCommandRequest(ViewWindowCommand.Back));
                return false;

            case SDL_Keycode.SDLK_F2:
                DispatchInteraction(new ViewWindowCommandRequest(ViewWindowCommand.Home));
                return false;

            case SDL_Keycode.SDLK_F3:
                DispatchInteraction(new ViewWindowCommandRequest(ViewWindowCommand.Recents));
                return false;

            case SDL_Keycode.SDLK_F4:
                DispatchInteraction(new ViewWindowCommandRequest(ViewWindowCommand.Rotate));
                return false;

            case SDL_Keycode.SDLK_F5:
                DispatchInteraction(new ViewWindowCommandRequest(ViewWindowCommand.Reconnect));
                return false;

            case SDL_Keycode.SDLK_F6:
                DispatchInteraction(new ViewWindowCommandRequest(ViewWindowCommand.PauseStream));
                return false;

            case SDL_Keycode.SDLK_F7:
                DispatchInteraction(new ViewWindowCommandRequest(ViewWindowCommand.OpenArtifacts));
                return false;

            case SDL_Keycode.SDLK_F8:
                _scaleMode = _scaleMode == ViewScaleMode.Fit ? ViewScaleMode.Fill : ViewScaleMode.Fit;
                _ = RefreshHoverTooltip();
                UpdateWindowTitle();
                return true;

            case SDL_Keycode.SDLK_F9:
                DispatchInteraction(new ViewWindowCommandRequest(ViewWindowCommand.ToggleRecording));
                return false;

            case SDL_Keycode.SDLK_F10:
                ToggleHelpOverlay();
                return true;

            case SDL_Keycode.SDLK_F11:
                ToggleFullscreen();
                return true;

            case SDL_Keycode.SDLK_F12:
                DispatchInteraction(new ViewWindowCommandRequest(ViewWindowCommand.TakeScreenshot));
                return false;

            case SDL_Keycode.SDLK_RETURN:
            case SDL_Keycode.SDLK_RETURN2:
                DispatchInteraction(new ViewKeyInputRequest("KEYCODE_ENTER"));
                return false;

            case SDL_Keycode.SDLK_TAB:
                DispatchInteraction(new ViewKeyInputRequest("KEYCODE_TAB"));
                return false;

            case SDL_Keycode.SDLK_BACKSPACE:
                DispatchInteraction(new ViewKeyInputRequest("KEYCODE_DEL"));
                return false;

            case SDL_Keycode.SDLK_DELETE:
                DispatchInteraction(new ViewKeyInputRequest("KEYCODE_FORWARD_DEL"));
                return false;

            case SDL_Keycode.SDLK_ESCAPE:
                DispatchInteraction(new ViewKeyInputRequest("KEYCODE_ESCAPE"));
                return false;

            case SDL_Keycode.SDLK_LEFT:
                DispatchInteraction(new ViewKeyInputRequest("KEYCODE_DPAD_LEFT"));
                return false;

            case SDL_Keycode.SDLK_RIGHT:
                DispatchInteraction(new ViewKeyInputRequest("KEYCODE_DPAD_RIGHT"));
                return false;

            case SDL_Keycode.SDLK_UP:
                DispatchInteraction(new ViewKeyInputRequest("KEYCODE_DPAD_UP"));
                return false;

            case SDL_Keycode.SDLK_DOWN:
                DispatchInteraction(new ViewKeyInputRequest("KEYCODE_DPAD_DOWN"));
                return false;

            case SDL_Keycode.SDLK_HOME:
                DispatchInteraction(new ViewKeyInputRequest("KEYCODE_MOVE_HOME"));
                return false;

            case SDL_Keycode.SDLK_END:
                DispatchInteraction(new ViewKeyInputRequest("KEYCODE_MOVE_END"));
                return false;

            case SDL_Keycode.SDLK_PAGEUP:
                DispatchInteraction(new ViewKeyInputRequest("KEYCODE_PAGE_UP"));
                return false;

            case SDL_Keycode.SDLK_PAGEDOWN:
                DispatchInteraction(new ViewKeyInputRequest("KEYCODE_PAGE_DOWN"));
                return false;

            default:
                return false;
        }
    }

    private unsafe void HandleTextInput(SDL_TextInputEvent textInputEvent)
    {
        var text = Marshal.PtrToStringUTF8((nint)textInputEvent.text);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        DispatchInteraction(new ViewTextInputRequest(text));
    }

    private void HandleMouseWheel(SDL_MouseWheelEvent mouseWheelEvent)
    {
        var horizontalTicks = mouseWheelEvent.integer_x != 0
            ? mouseWheelEvent.integer_x
            : (int)Math.Round(mouseWheelEvent.x, MidpointRounding.AwayFromZero);
        var verticalTicks = mouseWheelEvent.integer_y != 0
            ? mouseWheelEvent.integer_y
            : (int)Math.Round(mouseWheelEvent.y, MidpointRounding.AwayFromZero);

        if (horizontalTicks == 0 && verticalTicks == 0)
        {
            return;
        }

        DispatchInteraction(new ViewScrollRequest(horizontalTicks, verticalTicks));
    }

    private void HandleClipboardPaste()
    {
        if (!SDL_HasClipboardText())
        {
            return;
        }

        var text = SDL_GetClipboardText();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        DispatchInteraction(new ViewClipboardPasteRequest(text));
    }

    private unsafe void HandleDropFile(SDL_DropEvent dropEvent)
    {
        var filePath = Marshal.PtrToStringUTF8((nint)dropEvent.data);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        if (TryParseRemotePullPath(filePath, out var remotePath))
        {
            DispatchInteraction(new ViewFilePullRequest(remotePath));
            return;
        }

        DispatchInteraction(new ViewFileDropRequest(filePath));
    }

    internal static bool TryParseRemotePullPath(string path, out string remotePath)
    {
        remotePath = string.Empty;
        var value = path.Trim();
        var prefixLength = value.StartsWith("device:", StringComparison.OrdinalIgnoreCase)
            ? "device:".Length
            : value.StartsWith("adb:", StringComparison.OrdinalIgnoreCase)
                ? "adb:".Length
                : 0;
        if (prefixLength == 0)
        {
            return false;
        }

        remotePath = value[prefixLength..].Trim();
        return remotePath.StartsWith("/", StringComparison.Ordinal) && remotePath.Length > 1;
    }

    private static bool IsCtrlPressed(SDL_Keymod modifiers) => (modifiers & SDL_Keymod.SDL_KMOD_CTRL) != 0;

    private static bool IsAltPressed(SDL_Keymod modifiers) => (modifiers & SDL_Keymod.SDL_KMOD_ALT) != 0;

    internal static (int Width, int Height) ResolveInitialWindowSize(int contentWidth, int contentHeight, int usableWidth = 0, int usableHeight = 0)
    {
        var width = Math.Max(contentWidth, 320);
        var height = Math.Max(contentHeight, 240);
        if (usableWidth <= 0 || usableHeight <= 0)
        {
            return (width, height);
        }

        var maxWidth = Math.Max(320, (int)Math.Floor(usableWidth * 0.9d));
        var maxHeight = Math.Max(240, (int)Math.Floor(usableHeight * 0.9d));
        if (width <= maxWidth && height <= maxHeight)
        {
            return (width, height);
        }

        var scale = Math.Min((double)maxWidth / width, (double)maxHeight / height);
        return (
            Math.Max(320, (int)Math.Floor(width * scale)),
            Math.Max(240, (int)Math.Floor(height * scale)));
    }

    private unsafe void ToggleFullscreen()
    {
        if (_window is null)
        {
            return;
        }

        var nextState = !_isFullscreen;
        if (SDL_SetWindowFullscreen(_window, nextState))
        {
            _isFullscreen = !_isFullscreen;
            _ = RefreshHoverTooltip();
            UpdateWindowTitle();
        }
    }

    private void ToggleHelpOverlay()
    {
        _showHelpOverlay = !_showHelpOverlay;
    }

    private bool TryHandleChromeInteraction(int clientX, int clientY)
    {
        if (!TryGetLogicalWindowSize(out var clientWidth, out var clientHeight))
        {
            return false;
        }

        ViewChromeState? chrome;
        lock (_chromeLock)
        {
            chrome = _chrome;
        }

        if (chrome is null)
        {
            return false;
        }

        var layout = ViewChromeLayout.BuildRenderLayout(clientWidth, clientHeight, chrome);
        var withinChrome = (layout.ToolbarBounds?.Contains(clientX, clientY) ?? false)
            || (layout.ShelfBounds?.Contains(clientX, clientY) ?? false)
            || (layout.ShareBadge?.Bounds.Contains(clientX, clientY) ?? false);
        var hit = ViewChromeLayout.HitTest(clientWidth, clientHeight, clientX, clientY, chrome);
        switch (hit)
        {
            case ViewChromeCommandHitTarget commandHit:
                DispatchInteraction(new ViewWindowCommandRequest(commandHit.Command));
                return true;

            case ViewChromeSwitchDeviceHitTarget switchHit:
                DispatchInteraction(new ViewSwitchDeviceRequest(switchHit.DeviceSelector));
                return true;

            case ViewChromeLocalHitTarget localHit:
                switch (localHit.Action)
                {
                    case ViewChromeLocalAction.ToggleScaleMode:
                        _scaleMode = _scaleMode == ViewScaleMode.Fit ? ViewScaleMode.Fill : ViewScaleMode.Fit;
                        _ = RefreshHoverTooltip();
                        UpdateWindowTitle();
                        return true;

                    case ViewChromeLocalAction.ToggleFullscreen:
                        ToggleFullscreen();
                        return true;

                    case ViewChromeLocalAction.ToggleHelpOverlay:
                        ToggleHelpOverlay();
                        return true;
                }

                break;
        }

        return withinChrome;
    }

    private void DispatchInteraction(ViewInteractionRequest request)
    {
        var interactionHandler = _interactionHandler;
        if (interactionHandler is null)
        {
            return;
        }

        QueueInteraction(() => interactionHandler(request), ex => ReportInteractionFailureAsync(request, ex));
    }

    private static void QueueInteraction(Func<Task> interactionAsync, Func<Exception, Task> reportFailureAsync)
    {
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await interactionAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await reportFailureAsync(ex).ConfigureAwait(false);
                }
            });
    }

    private async Task ReportInteractionFailureAsync(ViewInteractionRequest request, Exception exception)
    {
        var interactionHandler = _interactionHandler;
        if (interactionHandler is null || request is ViewInteractionFailedRequest)
        {
            return;
        }

        try
        {
            await interactionHandler(new ViewInteractionFailedRequest(
                request.GetType().Name,
                exception.GetType().FullName ?? exception.GetType().Name,
                exception.Message)).ConfigureAwait(false);
        }
        catch
        {
            // ignored
        }
    }

    private unsafe void RenderChrome(int pixelWidth, int pixelHeight)
    {
        if (_renderer is null)
        {
            return;
        }

        ViewChromeState? chrome;
        lock (_chromeLock)
        {
            chrome = _chrome;
        }

        if (chrome is null || !TryGetLogicalWindowSize(out var logicalWidth, out var logicalHeight))
        {
            return;
        }

        var layout = ViewChromeLayout.BuildRenderLayout(logicalWidth, logicalHeight, chrome);
        var scaleX = logicalWidth <= 0 ? 1f : pixelWidth / (float)logicalWidth;
        var scaleY = logicalHeight <= 0 ? 1f : pixelHeight / (float)logicalHeight;

        if (layout.ToolbarBounds is not null)
        {
            FillRect(layout.ToolbarBounds, scaleX, scaleY, 12, 12, 12, 208);
            foreach (var button in layout.Buttons)
            {
                DrawToolbarButton(button, scaleX, scaleY);
            }
        }

        if (layout.ShareBadge is not null)
        {
            FillRect(layout.ShareBadge.Bounds, scaleX, scaleY, 16, 82, 82, 220);
            OutlineRect(layout.ShareBadge.Bounds, scaleX, scaleY, 140, 220, 220, 255);
            DrawNumber(layout.ShareBadge.ObserverCount, layout.ShareBadge.Bounds, scaleX, scaleY, 240, 250, 250, 255);
        }

        if (layout.ShelfBounds is not null)
        {
            FillRect(layout.ShelfBounds, scaleX, scaleY, 12, 12, 12, 208);
            foreach (var slot in layout.DeviceSlots)
            {
                DrawDeviceSlot(slot, scaleX, scaleY);
            }
        }

        if (_showHelpOverlay)
        {
            DrawHelpOverlay(logicalWidth, logicalHeight, scaleX, scaleY);
        }

        if (_hoverTooltip is not null)
        {
            DrawTooltip(_hoverTooltip, logicalWidth, logicalHeight, scaleX, scaleY);
        }
    }

    private void DrawToolbarButton(ViewChromeButtonLayout button, float scaleX, float scaleY)
    {
        var fill = button.Enabled
            ? button.Active ? (R: (byte)136, G: (byte)20, B: (byte)20, A: (byte)232) : (R: (byte)32, G: (byte)32, B: (byte)32, A: (byte)228)
            : (R: (byte)28, G: (byte)28, B: (byte)28, A: (byte)180);
        var stroke = button.Enabled
            ? button.Active ? (R: (byte)248, G: (byte)140, B: (byte)140, A: (byte)255) : (R: (byte)210, G: (byte)210, B: (byte)210, A: (byte)255)
            : (R: (byte)90, G: (byte)90, B: (byte)90, A: (byte)255);
        FillRect(button.Bounds, scaleX, scaleY, fill.R, fill.G, fill.B, fill.A);
        OutlineRect(button.Bounds, scaleX, scaleY, stroke.R, stroke.G, stroke.B, stroke.A);

        switch (button.Kind)
        {
            case ViewChromeButtonKind.Screenshot:
                DrawScreenshotIcon(button.Bounds, scaleX, scaleY, stroke.R, stroke.G, stroke.B, stroke.A);
                break;

            case ViewChromeButtonKind.Record:
                DrawRecordIcon(button.Bounds, scaleX, scaleY, stroke.R, stroke.G, stroke.B, stroke.A, button.Active);
                break;

            case ViewChromeButtonKind.Reconnect:
                DrawReconnectIcon(button.Bounds, scaleX, scaleY, stroke.R, stroke.G, stroke.B, stroke.A);
                break;

            case ViewChromeButtonKind.Back:
                DrawBackIcon(button.Bounds, scaleX, scaleY, stroke.R, stroke.G, stroke.B, stroke.A);
                break;

            case ViewChromeButtonKind.Home:
                DrawHomeIcon(button.Bounds, scaleX, scaleY, stroke.R, stroke.G, stroke.B, stroke.A);
                break;

            case ViewChromeButtonKind.Recents:
                DrawRecentsIcon(button.Bounds, scaleX, scaleY, stroke.R, stroke.G, stroke.B, stroke.A);
                break;

            case ViewChromeButtonKind.Rotate:
                DrawRotateIcon(button.Bounds, scaleX, scaleY, stroke.R, stroke.G, stroke.B, stroke.A);
                break;

            case ViewChromeButtonKind.PauseStream:
                DrawPauseIcon(button.Bounds, scaleX, scaleY, stroke.R, stroke.G, stroke.B, stroke.A);
                break;

            case ViewChromeButtonKind.OpenArtifacts:
                DrawFolderIcon(button.Bounds, scaleX, scaleY, stroke.R, stroke.G, stroke.B, stroke.A);
                break;

            case ViewChromeButtonKind.ScaleMode:
                DrawScaleIcon(button.Bounds, scaleX, scaleY, stroke.R, stroke.G, stroke.B, stroke.A);
                break;

            case ViewChromeButtonKind.Fullscreen:
                DrawFullscreenIcon(button.Bounds, scaleX, scaleY, stroke.R, stroke.G, stroke.B, stroke.A);
                break;

            case ViewChromeButtonKind.Help:
                DrawHelpIcon(button.Bounds, scaleX, scaleY, stroke.R, stroke.G, stroke.B, stroke.A);
                break;
        }
    }

    private void DrawDeviceSlot(ViewChromeDeviceSlotLayout slot, float scaleX, float scaleY)
    {
        var isBlocked = slot is {Enabled: false, IsActive: false};
        FillRect(slot.Bounds, scaleX, scaleY,
            slot.IsActive ? (byte)26 : isBlocked ? (byte)70 : (byte)24,
            slot.IsActive ? (byte)86 : isBlocked ? (byte)24 : (byte)50,
            slot.IsActive ? (byte)52 : isBlocked ? (byte)24 : (byte)70,
            230);
        OutlineRect(slot.Bounds, scaleX, scaleY,
            slot.IsActive ? (byte)120 : isBlocked ? (byte)230 : (byte)140,
            slot.IsActive ? (byte)220 : isBlocked ? (byte)100 : (byte)190,
            slot.IsActive ? (byte)160 : isBlocked ? (byte)100 : (byte)230,
            255);
        DrawTinyText(slot.Label, slot.Bounds.Left + 8, slot.Bounds.Top + 7, scaleX, scaleY, 245, 245, 245, 255, 2);
        DrawTinyText(slot.StatusLabel, slot.Bounds.Left + 8, slot.Bounds.Top + 22, scaleX, scaleY,
            slot.IsActive ? (byte)170 : isBlocked ? (byte)255 : (byte)180,
            slot.IsActive ? (byte)255 : isBlocked ? (byte)160 : (byte)225,
            slot.IsActive ? (byte)195 : isBlocked ? (byte)160 : (byte)255,
            255,
            1);
    }

    private void DrawScreenshotIcon(ViewChromeRect bounds, float scaleX, float scaleY, byte r, byte g, byte b, byte a)
    {
        var body = Inset(bounds, 7, 9, 7, 9);
        OutlineRect(body, scaleX, scaleY, r, g, b, a);
        OutlineRect(new ViewChromeRect(body.Left + 6, body.Top + 6, Math.Max(6, body.Width - 12), Math.Max(6, body.Height - 12)), scaleX, scaleY, r, g, b, a);
        FillRect(new ViewChromeRect(body.Left + 4, body.Top - 3, Math.Max(8, body.Width / 3), 4), scaleX, scaleY, r, g, b, a);
    }

    private void DrawRecordIcon(ViewChromeRect bounds, float scaleX, float scaleY, byte r, byte g, byte b, byte a, bool active)
    {
        var inner = Inset(bounds, 10, 10, 10, 10);
        if (active)
        {
            FillRect(inner, scaleX, scaleY, r, g, b, a);
        }
        else
        {
            OutlineRect(inner, scaleX, scaleY, r, g, b, a);
        }
    }

    private void DrawReconnectIcon(ViewChromeRect bounds, float scaleX, float scaleY, byte r, byte g, byte b, byte a)
    {
        DrawLine(bounds.Left + 9, bounds.Top + 16, bounds.Right - 10, bounds.Top + 16, scaleX, scaleY, r, g, b, a);
        DrawLine(bounds.Right - 10, bounds.Top + 16, bounds.Right - 15, bounds.Top + 11, scaleX, scaleY, r, g, b, a);
        DrawLine(bounds.Right - 10, bounds.Top + 16, bounds.Right - 15, bounds.Top + 21, scaleX, scaleY, r, g, b, a);
        DrawLine(bounds.Right - 9, bounds.Bottom - 16, bounds.Left + 10, bounds.Bottom - 16, scaleX, scaleY, r, g, b, a);
        DrawLine(bounds.Left + 10, bounds.Bottom - 16, bounds.Left + 15, bounds.Bottom - 11, scaleX, scaleY, r, g, b, a);
        DrawLine(bounds.Left + 10, bounds.Bottom - 16, bounds.Left + 15, bounds.Bottom - 21, scaleX, scaleY, r, g, b, a);
    }

    private void DrawBackIcon(ViewChromeRect bounds, float scaleX, float scaleY, byte r, byte g, byte b, byte a)
    {
        DrawLine(bounds.Left + 12, bounds.Top + bounds.Height / 2, bounds.Right - 10, bounds.Top + bounds.Height / 2, scaleX, scaleY, r, g, b, a);
        DrawLine(bounds.Left + 12, bounds.Top + bounds.Height / 2, bounds.Left + 20, bounds.Top + 12, scaleX, scaleY, r, g, b, a);
        DrawLine(bounds.Left + 12, bounds.Top + bounds.Height / 2, bounds.Left + 20, bounds.Bottom - 12, scaleX, scaleY, r, g, b, a);
    }

    private void DrawHomeIcon(ViewChromeRect bounds, float scaleX, float scaleY, byte r, byte g, byte b, byte a)
    {
        DrawLine(bounds.Left + 10, bounds.Top + 21, bounds.Left + bounds.Width / 2, bounds.Top + 10, scaleX, scaleY, r, g, b, a);
        DrawLine(bounds.Left + bounds.Width / 2, bounds.Top + 10, bounds.Right - 10, bounds.Top + 21, scaleX, scaleY, r, g, b, a);
        OutlineRect(new ViewChromeRect(bounds.Left + 14, bounds.Top + 21, bounds.Width - 28, bounds.Height - 31), scaleX, scaleY, r, g, b, a);
    }

    private void DrawRecentsIcon(ViewChromeRect bounds, float scaleX, float scaleY, byte r, byte g, byte b, byte a)
    {
        OutlineRect(new ViewChromeRect(bounds.Left + 12, bounds.Top + 11, bounds.Width - 26, bounds.Height - 26), scaleX, scaleY, r, g, b, a);
        OutlineRect(new ViewChromeRect(bounds.Left + 17, bounds.Top + 17, bounds.Width - 26, bounds.Height - 26), scaleX, scaleY, r, g, b, a);
    }

    private void DrawRotateIcon(ViewChromeRect bounds, float scaleX, float scaleY, byte r, byte g, byte b, byte a)
    {
        var centerX = bounds.Left + bounds.Width / 2;
        var centerY = bounds.Top + bounds.Height / 2;
        DrawLine(centerX - 9, centerY - 7, centerX + 7, centerY - 7, scaleX, scaleY, r, g, b, a);
        DrawLine(centerX + 7, centerY - 7, centerX + 7, centerY + 8, scaleX, scaleY, r, g, b, a);
        DrawLine(centerX + 7, centerY + 8, centerX + 1, centerY + 2, scaleX, scaleY, r, g, b, a);
        DrawLine(centerX + 7, centerY + 8, centerX + 13, centerY + 2, scaleX, scaleY, r, g, b, a);
        DrawLine(centerX - 9, centerY - 7, centerX - 4, centerY - 12, scaleX, scaleY, r, g, b, a);
        DrawLine(centerX - 9, centerY - 7, centerX - 4, centerY - 2, scaleX, scaleY, r, g, b, a);
    }

    private void DrawPauseIcon(ViewChromeRect bounds, float scaleX, float scaleY, byte r, byte g, byte b, byte a)
    {
        FillRect(new ViewChromeRect(bounds.Left + 13, bounds.Top + 11, 5, bounds.Height - 22), scaleX, scaleY, r, g, b, a);
        FillRect(new ViewChromeRect(bounds.Right - 18, bounds.Top + 11, 5, bounds.Height - 22), scaleX, scaleY, r, g, b, a);
    }

    private void DrawFolderIcon(ViewChromeRect bounds, float scaleX, float scaleY, byte r, byte g, byte b, byte a)
    {
        DrawLine(bounds.Left + 9, bounds.Top + 17, bounds.Left + 18, bounds.Top + 17, scaleX, scaleY, r, g, b, a);
        DrawLine(bounds.Left + 18, bounds.Top + 17, bounds.Left + 22, bounds.Top + 21, scaleX, scaleY, r, g, b, a);
        OutlineRect(new ViewChromeRect(bounds.Left + 9, bounds.Top + 21, bounds.Width - 18, bounds.Height - 31), scaleX, scaleY, r, g, b, a);
    }

    private void DrawScaleIcon(ViewChromeRect bounds, float scaleX, float scaleY, byte r, byte g, byte b, byte a)
    {
        var outer = Inset(bounds, 7, 7, 7, 7);
        OutlineRect(outer, scaleX, scaleY, r, g, b, a);
        var inner = _scaleMode == ViewScaleMode.Fill
            ? Inset(bounds, 9, 9, 9, 9)
            : Inset(bounds, 13, 13, 13, 13);
        OutlineRect(inner, scaleX, scaleY, r, g, b, a);
    }

    private void DrawFullscreenIcon(ViewChromeRect bounds, float scaleX, float scaleY, byte r, byte g, byte b, byte a)
    {
        DrawLine(bounds.Left + 9, bounds.Top + 15, bounds.Left + 9, bounds.Top + 9, scaleX, scaleY, r, g, b, a);
        DrawLine(bounds.Left + 9, bounds.Top + 9, bounds.Left + 15, bounds.Top + 9, scaleX, scaleY, r, g, b, a);
        DrawLine(bounds.Right - 9, bounds.Top + 15, bounds.Right - 9, bounds.Top + 9, scaleX, scaleY, r, g, b, a);
        DrawLine(bounds.Right - 15, bounds.Top + 9, bounds.Right - 9, bounds.Top + 9, scaleX, scaleY, r, g, b, a);
        DrawLine(bounds.Left + 9, bounds.Bottom - 15, bounds.Left + 9, bounds.Bottom - 9, scaleX, scaleY, r, g, b, a);
        DrawLine(bounds.Left + 9, bounds.Bottom - 9, bounds.Left + 15, bounds.Bottom - 9, scaleX, scaleY, r, g, b, a);
        DrawLine(bounds.Right - 9, bounds.Bottom - 15, bounds.Right - 9, bounds.Bottom - 9, scaleX, scaleY, r, g, b, a);
        DrawLine(bounds.Right - 15, bounds.Bottom - 9, bounds.Right - 9, bounds.Bottom - 9, scaleX, scaleY, r, g, b, a);
    }

    private void DrawHelpIcon(ViewChromeRect bounds, float scaleX, float scaleY, byte r, byte g, byte b, byte a)
    {
        var middleX = bounds.Left + bounds.Width / 2;
        DrawLine(middleX - 5, bounds.Top + 11, middleX + 5, bounds.Top + 11, scaleX, scaleY, r, g, b, a);
        DrawLine(middleX + 5, bounds.Top + 11, middleX + 5, bounds.Top + 17, scaleX, scaleY, r, g, b, a);
        DrawLine(middleX + 5, bounds.Top + 17, middleX, bounds.Top + 17, scaleX, scaleY, r, g, b, a);
        DrawLine(middleX, bounds.Top + 17, middleX, bounds.Top + 22, scaleX, scaleY, r, g, b, a);
        FillRect(new ViewChromeRect(middleX - 1, bounds.Top + 25, 3, 3), scaleX, scaleY, r, g, b, a);
    }

    private void DrawHelpOverlay(int logicalWidth, int logicalHeight, float scaleX, float scaleY)
    {
        string[] lines =
        [
            "LUOTSI HELP",
            "CLICK TOOLBAR",
            "TOGGLE F10",
            "F1 BACK",
            "F2 HOME",
            "F3 RECENTS",
            "F4 ROTATE",
            "F5 RECONNECT",
            "F6 PAUSE",
            "F7 ARTIFACTS",
            "F8 FIT OR FILL",
            "F9 RECORD",
            "F11 FULLSCREEN",
            "F12 SCREENSHOT"
        ];

        const int glyphWidth = 5 * TooltipGlyphCellSize;
        const int glyphHeight = 7 * TooltipGlyphCellSize;
        const int lineGap = 4;
        const int overlayPadding = 10;
        const int overlayTop = 56;
        var maxTextWidth = lines.Max(line => line.Length * glyphWidth + Math.Max(0, line.Length - 1) * TooltipGlyphGap);
        var overlayWidth = maxTextWidth + overlayPadding * 2;
        var overlayHeight = lines.Length * glyphHeight + Math.Max(0, lines.Length - 1) * lineGap + overlayPadding * 2;
        var overlayLeft = Math.Max(8, logicalWidth - overlayWidth - 8);
        var overlayTopClamped = Math.Clamp(overlayTop, 8, Math.Max(8, logicalHeight - overlayHeight - 8));
        var bounds = new ViewChromeRect(overlayLeft, overlayTopClamped, overlayWidth, overlayHeight);

        FillRect(bounds, scaleX, scaleY, 10, 10, 10, 232);
        OutlineRect(bounds, scaleX, scaleY, 232, 232, 232, 255);

        var textTop = bounds.Top + overlayPadding;
        foreach (var line in lines)
        {
            DrawTooltipText(line, bounds.Left + overlayPadding, textTop, scaleX, scaleY, 244, 244, 244, 255);
            textTop += glyphHeight + lineGap;
        }
    }

    private void DrawNumber(int value, ViewChromeRect bounds, float scaleX, float scaleY, byte r, byte g, byte b, byte a)
    {
        var text = Math.Max(0, value).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var digitWidth = Math.Max(6f, bounds.Width / (float)Math.Max(2, text.Length * 2));
        var digitHeight = Math.Max(12f, bounds.Height - 12f);
        var totalWidth = text.Length * digitWidth + Math.Max(0, text.Length - 1) * 4f;
        var startX = bounds.Left + (bounds.Width - totalWidth) / 2f;
        var startY = bounds.Top + (bounds.Height - digitHeight) / 2f;

        for (var index = 0; index < text.Length; index++)
        {
            DrawDigit(text[index], startX + index * (digitWidth + 4f), startY, digitWidth, digitHeight, scaleX, scaleY, r, g, b, a);
        }
    }

    private void DrawTinyText(string text, int left, int top, float scaleX, float scaleY, byte r, byte g, byte b, byte a, int pixelSize)
    {
        var cursor = left;
        foreach (var rawCharacter in text.ToUpperInvariant())
        {
            if (rawCharacter == ' ')
            {
                cursor += 4 * pixelSize;
                continue;
            }

            var glyph = GetTinyGlyph(rawCharacter);
            if (glyph is null)
            {
                cursor += 4 * pixelSize;
                continue;
            }

            for (var row = 0; row < glyph.Length; row++)
            {
                for (var column = 0; column < glyph[row].Length; column++)
                {
                    if (glyph[row][column] == '1')
                    {
                        FillRect(new ViewChromeRect(cursor + column * pixelSize, top + row * pixelSize, pixelSize, pixelSize), scaleX, scaleY, r, g, b, a);
                    }
                }
            }

            cursor += 6 * pixelSize;
        }
    }

    private static string[]? GetTinyGlyph(char character) => character switch
    {
        '0' => ["111", "101", "101", "101", "111"],
        '1' => ["010", "110", "010", "010", "111"],
        '2' => ["111", "001", "111", "100", "111"],
        '3' => ["111", "001", "111", "001", "111"],
        '4' => ["101", "101", "111", "001", "001"],
        '5' => ["111", "100", "111", "001", "111"],
        '6' => ["111", "100", "111", "101", "111"],
        '7' => ["111", "001", "010", "010", "010"],
        '8' => ["111", "101", "111", "101", "111"],
        '9' => ["111", "101", "111", "001", "111"],
        'A' => ["111", "101", "111", "101", "101"],
        'C' => ["111", "100", "100", "100", "111"],
        'D' => ["110", "101", "101", "101", "110"],
        'E' => ["111", "100", "110", "100", "111"],
        'F' => ["111", "100", "110", "100", "100"],
        'H' => ["101", "101", "111", "101", "101"],
        'I' => ["111", "010", "010", "010", "111"],
        'K' => ["101", "101", "110", "101", "101"],
        'N' => ["101", "111", "111", "111", "101"],
        'O' => ["111", "101", "101", "101", "111"],
        'R' => ["110", "101", "110", "101", "101"],
        'T' => ["111", "010", "010", "010", "010"],
        'U' => ["101", "101", "101", "101", "111"],
        'V' => ["101", "101", "101", "101", "010"],
        'W' => ["101", "101", "111", "111", "101"],
        'Y' => ["101", "101", "010", "010", "010"],
        '-' => ["000", "000", "111", "000", "000"],
        '_' => ["000", "000", "000", "000", "111"],
        ':' => ["000", "010", "000", "010", "000"],
        '.' => ["000", "000", "000", "000", "010"],
        _ => null
    };

    private void DrawDigit(char digit, float left, float top, float width, float height, float scaleX, float scaleY, byte r, byte g, byte b, byte a)
    {
        const int segmentA = 1 << 0;
        const int segmentB = 1 << 1;
        const int segmentC = 1 << 2;
        const int segmentD = 1 << 3;
        const int segmentE = 1 << 4;
        const int segmentF = 1 << 5;
        const int segmentG = 1 << 6;

        var mask = digit switch
        {
            '0' => segmentA | segmentB | segmentC | segmentD | segmentE | segmentF,
            '1' => segmentB | segmentC,
            '2' => segmentA | segmentB | segmentD | segmentE | segmentG,
            '3' => segmentA | segmentB | segmentC | segmentD | segmentG,
            '4' => segmentB | segmentC | segmentF | segmentG,
            '5' => segmentA | segmentC | segmentD | segmentF | segmentG,
            '6' => segmentA | segmentC | segmentD | segmentE | segmentF | segmentG,
            '7' => segmentA | segmentB | segmentC,
            '8' => segmentA | segmentB | segmentC | segmentD | segmentE | segmentF | segmentG,
            '9' => segmentA | segmentB | segmentC | segmentD | segmentF | segmentG,
            _ => segmentA | segmentD | segmentG
        };

        var thickness = Math.Max(2f, width / 4f);
        var middleY = top + height / 2f - thickness / 2f;
        var bottomY = top + height - thickness;
        var rightX = left + width - thickness;

        void Segment(bool enabled, float x, float y, float w, float h)
        {
            if (!enabled)
            {
                return;
            }

            FillRect(new ViewChromeRect((int)MathF.Round(x), (int)MathF.Round(y), (int)MathF.Round(w), (int)MathF.Round(h)), scaleX, scaleY, r, g, b, a);
        }

        Segment((mask & segmentA) != 0, left, top, width, thickness);
        Segment((mask & segmentB) != 0, rightX, top, thickness, height / 2f);
        Segment((mask & segmentC) != 0, rightX, middleY, thickness, height / 2f);
        Segment((mask & segmentD) != 0, left, bottomY, width, thickness);
        Segment((mask & segmentE) != 0, left, middleY, thickness, height / 2f);
        Segment((mask & segmentF) != 0, left, top, thickness, height / 2f);
        Segment((mask & segmentG) != 0, left, middleY, width, thickness);
    }

    private void DrawTooltip(ViewChromeTooltip tooltip, int logicalWidth, int logicalHeight, float scaleX, float scaleY)
    {
        var text = tooltip.Text.ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var glyphWidth = 5 * TooltipGlyphCellSize;
        var glyphHeight = 7 * TooltipGlyphCellSize;
        var textWidth = text.Length * glyphWidth + Math.Max(0, text.Length - 1) * TooltipGlyphGap;
        var tooltipWidth = textWidth + TooltipPaddingX * 2;
        var tooltipHeight = glyphHeight + TooltipPaddingY * 2;
        var left = tooltip.AnchorBounds.Left + (tooltip.AnchorBounds.Width - tooltipWidth) / 2;
        left = Math.Clamp(left, 8, Math.Max(8, logicalWidth - tooltipWidth - 8));

        var top = tooltip.AnchorBounds.Bottom + TooltipOffsetY;
        if (top + tooltipHeight > logicalHeight - 8)
        {
            top = Math.Max(8, tooltip.AnchorBounds.Top - tooltipHeight - TooltipOffsetY);
        }

        var bounds = new ViewChromeRect(left, top, tooltipWidth, tooltipHeight);
        FillRect(bounds, scaleX, scaleY, 10, 10, 10, 236);
        OutlineRect(bounds, scaleX, scaleY, 232, 232, 232, 255);
        DrawTooltipText(text, bounds.Left + TooltipPaddingX, bounds.Top + TooltipPaddingY, scaleX, scaleY, 244, 244, 244, 255);
    }

    private void DrawTooltipText(string text, int left, int top, float scaleX, float scaleY, byte r, byte g, byte b, byte a)
    {
        const int glyphWidth = 5 * TooltipGlyphCellSize;
        var cursorLeft = left;
        foreach (var character in text)
        {
            DrawTooltipGlyph(character, cursorLeft, top, scaleX, scaleY, r, g, b, a);
            cursorLeft += glyphWidth + TooltipGlyphGap;
        }
    }

    private void DrawTooltipGlyph(char character, int left, int top, float scaleX, float scaleY, byte r, byte g, byte b, byte a)
    {
        var glyphRows = GetTooltipGlyphRows(character);
        if (glyphRows == 0)
        {
            return;
        }

        for (var row = 0; row < 7; row++)
        {
            var rowBits = (int)((glyphRows >> ((6 - row) * 5)) & 0x1F);
            for (var column = 0; column < 5; column++)
            {
                if ((rowBits & (1 << (4 - column))) == 0)
                {
                    continue;
                }

                FillRect(
                    new ViewChromeRect(
                        left + column * TooltipGlyphCellSize,
                        top + row * TooltipGlyphCellSize,
                        TooltipGlyphCellSize,
                        TooltipGlyphCellSize),
                    scaleX,
                    scaleY,
                    r,
                    g,
                    b,
                    a);
            }
        }
    }

    private static long GetTooltipGlyphRows(char character) => char.ToUpperInvariant(character) switch
    {
        'A' => 0b01110_10001_10001_11111_10001_10001_10001,
        'B' => 0b11110_10001_10001_11110_10001_10001_11110,
        'C' => 0b01110_10001_10000_10000_10000_10001_01110,
        'D' => 0b11110_10001_10001_10001_10001_10001_11110,
        'E' => 0b11111_10000_10000_11110_10000_10000_11111,
        'F' => 0b11111_10000_10000_11110_10000_10000_10000,
        'G' => 0b01110_10001_10000_10111_10001_10001_01110,
        'H' => 0b10001_10001_10001_11111_10001_10001_10001,
        'I' => 0b11111_00100_00100_00100_00100_00100_11111,
        'K' => 0b10001_10010_10100_11000_10100_10010_10001,
        'L' => 0b10000_10000_10000_10000_10000_10000_11111,
        'M' => 0b10001_11011_10101_10101_10001_10001_10001,
        'N' => 0b10001_11001_10101_10011_10001_10001_10001,
        'O' => 0b01110_10001_10001_10001_10001_10001_01110,
        'P' => 0b11110_10001_10001_11110_10000_10000_10000,
        'R' => 0b11110_10001_10001_11110_10100_10010_10001,
        'S' => 0b01111_10000_10000_01110_00001_00001_11110,
        'T' => 0b11111_00100_00100_00100_00100_00100_00100,
        'U' => 0b10001_10001_10001_10001_10001_10001_01110,
        'W' => 0b10001_10001_10001_10101_10101_11011_10001,
        'Y' => 0b10001_10001_01010_00100_00100_00100_00100,
        '0' => 0b01110_10001_10011_10101_11001_10001_01110,
        '1' => 0b00100_01100_00100_00100_00100_00100_01110,
        '2' => 0b01110_10001_00001_00010_00100_01000_11111,
        '3' => 0b11110_00001_00001_01110_00001_00001_11110,
        '4' => 0b00010_00110_01010_10010_11111_00010_00010,
        '5' => 0b11111_10000_10000_11110_00001_00001_11110,
        '6' => 0b01110_10000_10000_11110_10001_10001_01110,
        '7' => 0b11111_00001_00010_00100_01000_01000_01000,
        '8' => 0b01110_10001_10001_01110_10001_10001_01110,
        '9' => 0b01110_10001_10001_01111_00001_00001_01110,
        ' ' => 0,
        _ => 0b11111_00001_00110_00100_00000_00100_00000
    };

    private unsafe void FillRect(ViewChromeRect rect, float scaleX, float scaleY, byte r, byte g, byte b, byte a)
    {
        if (_renderer is null)
        {
            return;
        }

        _ = SDL_SetRenderDrawColor(_renderer, r, g, b, a);
        var scaled = ScaleRect(rect, scaleX, scaleY);
        _ = SDL_RenderFillRect(_renderer, &scaled);
    }

    private unsafe void OutlineRect(ViewChromeRect rect, float scaleX, float scaleY, byte r, byte g, byte b, byte a)
    {
        if (_renderer is null)
        {
            return;
        }

        _ = SDL_SetRenderDrawColor(_renderer, r, g, b, a);
        var scaled = ScaleRect(rect, scaleX, scaleY);
        _ = SDL_RenderRect(_renderer, &scaled);
    }

    private unsafe void DrawLine(float x1, float y1, float x2, float y2, float scaleX, float scaleY, byte r, byte g, byte b, byte a)
    {
        if (_renderer is null)
        {
            return;
        }

        _ = SDL_SetRenderDrawColor(_renderer, r, g, b, a);
        _ = SDL_RenderLine(_renderer, x1 * scaleX, y1 * scaleY, x2 * scaleX, y2 * scaleY);
    }

    private static ViewChromeRect Inset(ViewChromeRect rect, int left, int top, int right, int bottom) =>
        new(
            rect.Left + left,
            rect.Top + top,
            Math.Max(1, rect.Width - left - right),
            Math.Max(1, rect.Height - top - bottom));

    private static SDL_FRect ScaleRect(ViewChromeRect rect, float scaleX, float scaleY) => new()
    {
        x = rect.Left * scaleX,
        y = rect.Top * scaleY,
        w = Math.Max(1f, rect.Width * scaleX),
        h = Math.Max(1f, rect.Height * scaleY)
    };

    private unsafe void DestroyTexture()
    {
        if (_texture is null)
        {
            return;
        }

        SDL_DestroyTexture(_texture);
        _texture = null;
        _textureWidth = 0;
        _textureHeight = 0;
    }

    private unsafe void TryApplyWindowIcon()
    {
        if (_window is null)
        {
            return;
        }

        try
        {
            var icon = _iconProvider.GetDefaultIcon();
            if (icon is null)
            {
                return;
            }

            fixed (byte* pixels = icon.ArgbPixels)
            {
                var surface = NativeSdlWindow.CreateArgbSurface(icon.Width, icon.Height, pixels, icon.Pitch);
                if (surface is null)
                {
                    return;
                }

                try
                {
                    NativeSdlWindow.SetWindowIcon(_window, surface);
                }
                finally
                {
                    NativeSdlWindow.DestroySurface(surface);
                }
            }
        }
        catch
        {
            // ignored
        }
    }

    private sealed record ViewFrameSnapshot(int Width, int Height, int RowStride, byte[] PixelData)
    {
        public static ViewFrameSnapshot From(ViewFrame frame)
        {
            const int bytesPerPixel = 4;
            var tightStride = frame.Width * bytesPerPixel;
            var sourceRowStride = frame.RowStride <= 0 ? tightStride : frame.RowStride;
            var pixelData = frame.PixelData.ToArray();
            if (pixelData.Length == 0 || sourceRowStride == tightStride)
            {
                return new ViewFrameSnapshot(frame.Width, frame.Height, tightStride, pixelData);
            }

            var normalized = new byte[tightStride * frame.Height];
            var source = frame.PixelData.Span;
            for (var row = 0; row < frame.Height; row++)
            {
                source.Slice(row * sourceRowStride, tightStride).CopyTo(normalized.AsSpan(row * tightStride, tightStride));
            }

            return new ViewFrameSnapshot(frame.Width, frame.Height, tightStride, normalized);
        }
    }
}

internal static unsafe partial class NativeSdlWindow
{
    public static bool SetAlwaysOnTop(SDL_Window* window, bool alwaysOnTop) =>
        SDL_SetWindowAlwaysOnTop(window, alwaysOnTop);

    public static bool TryGetPrimaryDisplayUsableBounds(out int width, out int height)
    {
        width = 0;
        height = 0;

        var displayId = SDL_GetPrimaryDisplay();
        if (displayId == 0)
        {
            return false;
        }

        SDL_Rect rect;
        if (!SDL_GetDisplayUsableBounds(displayId, &rect) || rect.w <= 0 || rect.h <= 0)
        {
            return false;
        }

        width = rect.w;
        height = rect.h;
        return true;
    }

    public static SDL_Surface* CreateArgbSurface(int width, int height, byte* pixels, int pitch) =>
        SDL_CreateSurfaceFrom(width, height, SDL_PixelFormat.SDL_PIXELFORMAT_ARGB8888, pixels, pitch);

    public static void SetWindowIcon(SDL_Window* window, SDL_Surface* icon) =>
        SDL_SetWindowIcon(window, icon);

    public static void DestroySurface(SDL_Surface* surface) =>
        SDL_DestroySurface(surface);

    [LibraryImport("SDL3")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool SDL_SetWindowAlwaysOnTop(SDL_Window* window, [MarshalAs(UnmanagedType.I1)] bool onTop);

    [LibraryImport("SDL3")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial uint SDL_GetPrimaryDisplay();

    [LibraryImport("SDL3")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool SDL_GetDisplayUsableBounds(uint displayId, SDL_Rect* rect);

    [LibraryImport("SDL3")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial SDL_Surface* SDL_CreateSurfaceFrom(
        int width,
        int height,
        SDL_PixelFormat format,
        void* pixels,
        int pitch);

    [LibraryImport("SDL3")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial void SDL_SetWindowIcon(SDL_Window* window, SDL_Surface* icon);

    [LibraryImport("SDL3")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial void SDL_DestroySurface(SDL_Surface* surface);
}
