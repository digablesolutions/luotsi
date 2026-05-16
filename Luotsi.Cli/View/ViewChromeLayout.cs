namespace Luotsi.Cli.View;

/// <summary>
/// Hit-testing helper for the in-window toolbar and multi-device shelf.
/// </summary>
public static class ViewChromeLayout
{
    private const int Padding = 8;
    private const int ButtonSize = 32;
    private const int ButtonGap = 8;
    private const int ToolbarHeight = 48;
    private const int ShelfHeight = 52;
    private const int DeviceSlotWidth = 44;
    private const int DeviceSlotGap = 8;
    private const int ShareBadgeWidth = 52;

    /// <summary>
    /// Resolves the chrome action for a click in window coordinates.
    /// </summary>
    /// <param name="clientWidth">Logical client width.</param>
    /// <param name="clientHeight">Logical client height.</param>
    /// <param name="x">Logical click X.</param>
    /// <param name="y">Logical click Y.</param>
    /// <param name="chrome">Current chrome state.</param>
    /// <returns>The resolved hit target, or <see langword="null"/> when the click is outside chrome affordances.</returns>
    public static ViewChromeHitTarget? HitTest(int clientWidth, int clientHeight, int x, int y, ViewChromeState? chrome)
    {
        if (clientWidth <= 0 || clientHeight <= 0 || chrome is null)
        {
            return null;
        }

        var layout = BuildRenderLayout(clientWidth, clientHeight, chrome);
        foreach (var button in layout.Buttons)
        {
            if (!button.Bounds.Contains(x, y) || !button.Enabled)
            {
                continue;
            }

            return button.Kind switch
            {
                ViewChromeButtonKind.Screenshot => new ViewChromeCommandHitTarget(ViewWindowCommand.TakeScreenshot),
                ViewChromeButtonKind.Record => new ViewChromeCommandHitTarget(ViewWindowCommand.ToggleRecording),
                ViewChromeButtonKind.Reconnect => new ViewChromeCommandHitTarget(ViewWindowCommand.Reconnect),
                ViewChromeButtonKind.ScaleMode => new ViewChromeLocalHitTarget(ViewChromeLocalAction.ToggleScaleMode),
                ViewChromeButtonKind.Fullscreen => new ViewChromeLocalHitTarget(ViewChromeLocalAction.ToggleFullscreen),
                _ => null
            };
        }

        foreach (var device in layout.DeviceSlots)
        {
            if (device.Bounds.Contains(x, y) && device.Enabled)
            {
                return new ViewChromeSwitchDeviceHitTarget(device.DeviceSelector);
            }
        }

        return null;
    }

    internal static ViewChromeRenderLayout BuildRenderLayout(int clientWidth, int clientHeight, ViewChromeState? chrome)
    {
        if (clientWidth <= 0 || clientHeight <= 0 || chrome is null)
        {
            return new ViewChromeRenderLayout([], [], null, null);
        }

        var buttons = new List<ViewChromeButtonLayout>();
        var buttonTop = Padding;
        var buttonLeft = Padding;

        void AddButton(ViewChromeButtonKind kind, bool enabled, bool active = false)
        {
            buttons.Add(new ViewChromeButtonLayout(
                kind,
                new ViewChromeRect(buttonLeft, buttonTop, ButtonSize, ButtonSize),
                enabled,
                active));
            buttonLeft += ButtonSize + ButtonGap;
        }

        AddButton(ViewChromeButtonKind.Screenshot, chrome.CanTakeScreenshot);
        AddButton(ViewChromeButtonKind.Record, chrome.CanToggleRecording, chrome.IsRecording);
        AddButton(ViewChromeButtonKind.Reconnect, chrome.CanReconnect);
        AddButton(ViewChromeButtonKind.ScaleMode, true);
        AddButton(ViewChromeButtonKind.Fullscreen, true);

        var deviceSlots = new List<ViewChromeDeviceSlotLayout>();
        if (chrome.CanSwitchDevices && chrome.Devices.Count > 1)
        {
            var shelfTop = Math.Max(Padding, clientHeight - ShelfHeight + Padding / 2);
            var shelfLeft = Padding;
            foreach (var device in chrome.Devices)
            {
                deviceSlots.Add(new ViewChromeDeviceSlotLayout(
                    device.DeviceSelector,
                    device.Index,
                    new ViewChromeRect(shelfLeft, shelfTop, DeviceSlotWidth, ButtonSize + 4),
                    !device.IsActive));
                shelfLeft += DeviceSlotWidth + DeviceSlotGap;
            }
        }

        ViewChromeRect? toolbarBounds = buttons.Count == 0
            ? null
            : new ViewChromeRect(Padding / 2, Padding / 2, Math.Min(clientWidth - Padding, buttonLeft), ToolbarHeight - Padding);

        ViewChromeRect? shelfBounds = deviceSlots.Count == 0
            ? null
            : new ViewChromeRect(
                Padding / 2,
                Math.Max(Padding / 2, clientHeight - ShelfHeight),
                Math.Min(clientWidth - Padding, deviceSlots[^1].Bounds.Right + Padding / 2),
                ShelfHeight - Padding / 2);

        ViewChromeBadgeLayout? shareBadge = string.IsNullOrWhiteSpace(chrome.ShareEndpoint)
            ? null
            : new ViewChromeBadgeLayout(new ViewChromeRect(Math.Max(Padding, clientWidth - ShareBadgeWidth - Padding), buttonTop, ShareBadgeWidth, ButtonSize), chrome.ObserverCount);

        return new ViewChromeRenderLayout(buttons, deviceSlots, toolbarBounds, shelfBounds, shareBadge);
    }
}

/// <summary>
/// Local window action owned entirely by the renderer.
/// </summary>
public enum ViewChromeLocalAction
{
    ToggleScaleMode = 0,
    ToggleFullscreen = 1
}

/// <summary>
/// Hit target returned by <see cref="ViewChromeLayout.HitTest"/>.
/// </summary>
public abstract record ViewChromeHitTarget;

/// <summary>
/// Toolbar hit target that maps to a session-owned command.
/// </summary>
/// <param name="Command">Requested session command.</param>
public sealed record ViewChromeCommandHitTarget(ViewWindowCommand Command) : ViewChromeHitTarget;

/// <summary>
/// Toolbar hit target that maps to a local renderer action.
/// </summary>
/// <param name="Action">Requested local action.</param>
public sealed record ViewChromeLocalHitTarget(ViewChromeLocalAction Action) : ViewChromeHitTarget;

/// <summary>
/// Shelf hit target that requests a device switch.
/// </summary>
/// <param name="DeviceSelector">ADB device selector to activate.</param>
public sealed record ViewChromeSwitchDeviceHitTarget(string DeviceSelector) : ViewChromeHitTarget;

internal enum ViewChromeButtonKind
{
    Screenshot = 0,
    Record = 1,
    Reconnect = 2,
    ScaleMode = 3,
    Fullscreen = 4
}

internal sealed record ViewChromeRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

internal sealed record ViewChromeButtonLayout(ViewChromeButtonKind Kind, ViewChromeRect Bounds, bool Enabled, bool Active);

internal sealed record ViewChromeDeviceSlotLayout(string DeviceSelector, int Index, ViewChromeRect Bounds, bool Enabled);

internal sealed record ViewChromeBadgeLayout(ViewChromeRect Bounds, int ObserverCount);

internal sealed record ViewChromeRenderLayout(
    IReadOnlyList<ViewChromeButtonLayout> Buttons,
    IReadOnlyList<ViewChromeDeviceSlotLayout> DeviceSlots,
    ViewChromeRect? ToolbarBounds,
    ViewChromeRect? ShelfBounds,
    ViewChromeBadgeLayout? ShareBadge = null);