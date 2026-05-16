using System.Text;
using SDL;
using static SDL.SDL3;

namespace VisitLab.Cli.View;

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

    private readonly TaskCompletionSource _readySource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _closedSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _frameLock = new();

    private Thread? _windowThread;
    private string _title = "VisitLab View";
    private Func<ViewPointerEvent, Task>? _pointerHandler;
    private ViewFrameSnapshot? _frame;
    private bool _frameDirty;
    private bool _disposeRequested;
    private bool _disposed;

    private unsafe SDL_Window* _window;
    private unsafe SDL_Renderer* _renderer;
    private unsafe SDL_Texture* _texture;
    private int _textureWidth;
    private int _textureHeight;

    public async Task InitializeAsync(string title, ViewDisplayInfo displayInfo, Func<ViewPointerEvent, Task> pointerHandler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(displayInfo);
        ArgumentNullException.ThrowIfNull(pointerHandler);

        _title = string.IsNullOrWhiteSpace(title) ? "VisitLab View" : title;
        _pointerHandler = pointerHandler;
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
            Name = "VisitLabSdlViewWindow"
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

            var titleBytes = Encoding.UTF8.GetBytes(_title + '\0');
            fixed (byte* title = titleBytes)
            {
                _window = SDL_CreateWindow(
                    title,
                    Math.Max(displayInfo.Width, 320),
                    Math.Max(displayInfo.Height, 240),
                    SDL_WindowFlags.SDL_WINDOW_RESIZABLE | SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY);
            }

            if (_window is null)
            {
                throw new InvalidOperationException($"Failed to create SDL3 view window: {SDL_GetError()}");
            }

            _renderer = SDL_CreateRenderer(_window, (byte*)null);
            if (_renderer is null)
            {
                throw new InvalidOperationException($"Failed to create SDL3 renderer: {SDL_GetError()}");
            }

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

        if (frame is not null && _texture is not null && TryGetWindowPixelSize(out var pixelWidth, out var pixelHeight))
        {
            var layout = ViewPointerMapper.ComputeLayout(pixelWidth, pixelHeight, frame.Width, frame.Height);
            if (layout.Width > 0 && layout.Height > 0)
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
            (int)Math.Round(mouseButtonEvent.x, MidpointRounding.AwayFromZero),
            (int)Math.Round(mouseButtonEvent.y, MidpointRounding.AwayFromZero),
            clientWidth,
            clientHeight);

        _ = Task.Run(
            async () =>
            {
                try
                {
                    await pointerHandler(pointerEvent).ConfigureAwait(false);
                }
                catch
                {
                }
            });
    }

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

    private sealed record ViewFrameSnapshot(int Width, int Height, int RowStride, byte[] PixelData)
    {
        public static ViewFrameSnapshot From(ViewFrame frame)
        {
            var bytesPerPixel = 4;
            var tightStride = frame.Width * bytesPerPixel;
            var sourceRowStride = frame.RowStride <= 0 ? tightStride : frame.RowStride;
            var pixelData = frame.PixelData.ToArray();
            if (pixelData.Length == 0)
            {
                return new ViewFrameSnapshot(frame.Width, frame.Height, tightStride, pixelData);
            }

            if (sourceRowStride == tightStride)
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