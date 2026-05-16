using VisitLab.Cli.Infrastructure;

namespace VisitLab.Cli.View;

/// <summary>
/// Creates native window surfaces for the built-in renderer.
/// </summary>
public interface IViewWindowSurfaceFactory
{
    /// <summary>
    /// Creates a native window surface instance.
    /// </summary>
    /// <returns>Window surface instance.</returns>
    IViewWindowSurface Create();
}

/// <summary>
/// Represents a native window surface that can present decoded frames.
/// </summary>
public interface IViewWindowSurface : IAsyncDisposable
{
    /// <summary>
    /// Initializes the native window surface.
    /// </summary>
    /// <param name="title">Window title.</param>
    /// <param name="displayInfo">Initial display info.</param>
    /// <param name="pointerHandler">Pointer callback for click routing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completion task.</returns>
    Task InitializeAsync(string title, ViewDisplayInfo displayInfo, Func<ViewPointerEvent, Task> pointerHandler, CancellationToken cancellationToken = default);

    /// <summary>
    /// Presents a frame to the native window surface.
    /// </summary>
    /// <param name="frame">Decoded frame.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completion task.</returns>
    Task PresentAsync(ViewFrame frame, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits until the native window is closed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completion task.</returns>
    Task WaitForCloseAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Pointer event raised by the native window surface.
/// </summary>
/// <param name="ClientX">Pointer X within the client area.</param>
/// <param name="ClientY">Pointer Y within the client area.</param>
/// <param name="ClientWidth">Current client width.</param>
/// <param name="ClientHeight">Current client height.</param>
public sealed record ViewPointerEvent(int ClientX, int ClientY, int ClientWidth, int ClientHeight);

/// <summary>
/// In-process native window renderer that presents decoded frames and routes clicks through the existing tap-point host path.
/// </summary>
public sealed class NativeWindowViewRenderer(IViewWindowSurfaceFactory windowSurfaceFactory, IDeviceHost deviceHost) : IViewRenderer
{
    private readonly IViewWindowSurface _windowSurface = (windowSurfaceFactory ?? throw new ArgumentNullException(nameof(windowSurfaceFactory))).Create();
    private readonly IDeviceHost _deviceHost = deviceHost ?? throw new ArgumentNullException(nameof(deviceHost));
    private ViewDisplayInfo? _displayInfo;

    /// <inheritdoc />
    public async Task InitializeAsync(ViewDisplayInfo displayInfo, CancellationToken cancellationToken = default)
    {
        _displayInfo = displayInfo ?? throw new ArgumentNullException(nameof(displayInfo));
        await _windowSurface.InitializeAsync("VisitLab View", displayInfo, HandlePointerAsync, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PresentAsync(ViewFrame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (_displayInfo is null)
        {
            throw new InvalidOperationException("Native window renderer was not initialized.");
        }

        _displayInfo = _displayInfo with
        {
            Width = frame.Width,
            Height = frame.Height,
            PixelFormat = frame.PixelFormat
        };

        await _windowSurface.PresentAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task UpdateStatsAsync(ViewStats stats, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task WaitForCloseAsync(CancellationToken cancellationToken = default) => _windowSurface.WaitForCloseAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _windowSurface.DisposeAsync();

    private async Task HandlePointerAsync(ViewPointerEvent pointerEvent)
    {
        var displayInfo = _displayInfo;
        if (displayInfo is null)
        {
            return;
        }

        if (!ViewPointerMapper.TryMapToRelativePoint(
                pointerEvent,
                displayInfo.Width,
                displayInfo.Height,
                out var xRatio,
                out var yRatio))
        {
            return;
        }

        await _deviceHost.TapPointAsync("view-window", null, null, xRatio, yRatio, 0).ConfigureAwait(false);
    }
}

internal static class ViewPointerMapper
{
    public static bool TryMapToRelativePoint(
        ViewPointerEvent pointerEvent,
        int sourceWidth,
        int sourceHeight,
        out double xRatio,
        out double yRatio)
    {
        var layout = ComputeLayout(pointerEvent.ClientWidth, pointerEvent.ClientHeight, sourceWidth, sourceHeight);
        if (layout.Width <= 0 || layout.Height <= 0)
        {
            xRatio = 0;
            yRatio = 0;
            return false;
        }

        if (pointerEvent.ClientX < layout.Left ||
            pointerEvent.ClientY < layout.Top ||
            pointerEvent.ClientX >= layout.Left + layout.Width ||
            pointerEvent.ClientY >= layout.Top + layout.Height)
        {
            xRatio = 0;
            yRatio = 0;
            return false;
        }

        xRatio = Math.Clamp((pointerEvent.ClientX - layout.Left) / (double)layout.Width, 0d, 1d);
        yRatio = Math.Clamp((pointerEvent.ClientY - layout.Top) / (double)layout.Height, 0d, 1d);
        return true;
    }

    public static ViewContentLayout ComputeLayout(int clientWidth, int clientHeight, int sourceWidth, int sourceHeight)
    {
        if (clientWidth <= 0 || clientHeight <= 0 || sourceWidth <= 0 || sourceHeight <= 0)
        {
            return default;
        }

        var scale = Math.Min(1d, Math.Min(clientWidth / (double)sourceWidth, clientHeight / (double)sourceHeight));
        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale, MidpointRounding.AwayFromZero));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale, MidpointRounding.AwayFromZero));
        var left = (clientWidth - width) / 2;
        var top = (clientHeight - height) / 2;
        return new ViewContentLayout(left, top, width, height);
    }
}

internal readonly record struct ViewContentLayout(int Left, int Top, int Width, int Height);
