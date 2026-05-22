using Luotsi.Cli.View.Contracts;

namespace Luotsi.Cli.View.Rendering;

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
    private const int DeviceSlotWidth = 116;
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
                ViewChromeButtonKind.Back => new ViewChromeCommandHitTarget(ViewWindowCommand.Back),
                ViewChromeButtonKind.Home => new ViewChromeCommandHitTarget(ViewWindowCommand.Home),
                ViewChromeButtonKind.Recents => new ViewChromeCommandHitTarget(ViewWindowCommand.Recents),
                ViewChromeButtonKind.Rotate => new ViewChromeCommandHitTarget(ViewWindowCommand.Rotate),
                ViewChromeButtonKind.PauseStream => new ViewChromeCommandHitTarget(ViewWindowCommand.PauseStream),
                ViewChromeButtonKind.OpenArtifacts => new ViewChromeCommandHitTarget(ViewWindowCommand.OpenArtifacts),
                ViewChromeButtonKind.ScaleMode => new ViewChromeLocalHitTarget(ViewChromeLocalAction.ToggleScaleMode),
                ViewChromeButtonKind.Fullscreen => new ViewChromeLocalHitTarget(ViewChromeLocalAction.ToggleFullscreen),
                ViewChromeButtonKind.Help => new ViewChromeLocalHitTarget(ViewChromeLocalAction.ToggleHelpOverlay),
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

    internal static ViewChromeTooltip? ResolveTooltip(int clientWidth, int clientHeight, int x, int y, ViewChromeState? chrome, ViewScaleMode scaleMode, bool isFullscreen)
    {
        if (clientWidth <= 0 || clientHeight <= 0 || chrome is null)
        {
            return null;
        }

        var layout = BuildRenderLayout(clientWidth, clientHeight, chrome);
        foreach (var button in layout.Buttons)
        {
            if (!button.Bounds.Contains(x, y))
            {
                continue;
            }

            var text = DescribeTooltip(button, scaleMode, isFullscreen);
            return text is null ? null : new ViewChromeTooltip(button.Bounds, text);
        }

        var shareTooltip = layout.ShareBadge is not null && layout.ShareBadge.Bounds.Contains(x, y)
            ? new ViewChromeTooltip(layout.ShareBadge.Bounds, BuildShareBadgeTooltip(layout.ShareBadge))
            : null;
        if (shareTooltip is not null)
        {
            return shareTooltip;
        }

        return layout.DeviceSlots
            .Where(device => device.Bounds.Contains(x, y))
            .Select(device => new ViewChromeTooltip(device.Bounds, $"{device.Label} {device.StatusLabel} {device.DeviceSelector}".Trim()))
            .FirstOrDefault();
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
        AddButton(ViewChromeButtonKind.Back, !chrome.ReadOnly);
        AddButton(ViewChromeButtonKind.Home, !chrome.ReadOnly);
        AddButton(ViewChromeButtonKind.Recents, !chrome.ReadOnly);
        AddButton(ViewChromeButtonKind.Rotate, !chrome.ReadOnly);
        AddButton(ViewChromeButtonKind.PauseStream, true);
        AddButton(ViewChromeButtonKind.OpenArtifacts, true);
        AddButton(ViewChromeButtonKind.ScaleMode, true);
        AddButton(ViewChromeButtonKind.Fullscreen, true);
        AddButton(ViewChromeButtonKind.Help, true);

        var deviceSlots = new List<ViewChromeDeviceSlotLayout>();
        if (chrome is {CanSwitchDevices: true, Devices.Count: > 1})
        {
            var shelfTop = Math.Max(Padding, clientHeight - ShelfHeight + Padding / 2);
            var shelfLeft = Padding;
            foreach (var device in chrome.Devices)
            {
                deviceSlots.Add(new ViewChromeDeviceSlotLayout(
                    device.DeviceSelector,
                    device.Index,
                    BuildDeviceLabel(device),
                    BuildDeviceStatusLabel(device),
                    device.IsActive,
                    new ViewChromeRect(shelfLeft, shelfTop, DeviceSlotWidth, ButtonSize + 4),
                    !device.IsActive && string.Equals(device.Status, "device", StringComparison.OrdinalIgnoreCase)));
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
            : new ViewChromeBadgeLayout(new ViewChromeRect(Math.Max(Padding, clientWidth - ShareBadgeWidth - Padding), buttonTop, ShareBadgeWidth, ButtonSize), chrome.ObserverCount, chrome.ShareEndpoint!);

        return new ViewChromeRenderLayout(buttons, deviceSlots, toolbarBounds, shelfBounds, shareBadge);
    }

    private static string? DescribeTooltip(ViewChromeButtonLayout button, ViewScaleMode scaleMode, bool isFullscreen) =>
        button.Kind switch
        {
            ViewChromeButtonKind.Screenshot => WithShortcut("Screenshot", "F12"),
            ViewChromeButtonKind.Record => WithShortcut(button.Active ? "Stop Recording" : "Start Recording", "F9"),
            ViewChromeButtonKind.Reconnect => WithShortcut("Reconnect", "F5"),
            ViewChromeButtonKind.Back => WithShortcut("Back", "F1"),
            ViewChromeButtonKind.Home => WithShortcut("Home", "F2"),
            ViewChromeButtonKind.Recents => WithShortcut("Recents", "F3"),
            ViewChromeButtonKind.Rotate => WithShortcut("Rotate", "F4"),
            ViewChromeButtonKind.PauseStream => WithShortcut("Pause Stream", "F6"),
            ViewChromeButtonKind.OpenArtifacts => WithShortcut("Open Artifacts", "F7"),
            ViewChromeButtonKind.ScaleMode => WithShortcut(scaleMode == ViewScaleMode.Fill ? "Fit" : "Fill", "F8"),
            ViewChromeButtonKind.Fullscreen => WithShortcut(isFullscreen ? "Windowed" : "Fullscreen", "F11"),
            ViewChromeButtonKind.Help => WithShortcut("Help", "F10"),
            _ => null
        };

    private static string WithShortcut(string label, string shortcut) => $"{label} {shortcut}";

    private static string BuildDeviceLabel(ViewChromeDevice device)
    {
        var selector = device.DeviceSelector;
        var hostEnd = selector.LastIndexOf('.');
        var portStart = selector.LastIndexOf(':');
        var suffix = hostEnd >= 0 && portStart > hostEnd
            ? selector[(hostEnd + 1)..portStart]
            : selector.Length <= 8 ? selector : selector[^8..];
        return $"{device.Index} {suffix}";
    }

    private static string BuildDeviceStatusLabel(ViewChromeDevice device)
    {
        if (device.IsActive)
        {
            return "ACTIVE";
        }

        if (string.Equals(device.Status, "device", StringComparison.OrdinalIgnoreCase))
        {
            return "READY";
        }

        if (string.Equals(device.Status, "unauthorized", StringComparison.OrdinalIgnoreCase))
        {
            return "UNAUTH";
        }

        var status = string.IsNullOrWhiteSpace(device.Status) ? "UNKNOWN" : device.Status.ToUpperInvariant();
        return status.Length <= 7 ? status : status[..7];
    }

    private static string BuildShareBadgeTooltip(ViewChromeBadgeLayout shareBadge)
    {
        var observersLabel = shareBadge.ObserverCount == 1 ? "1 observer" : $"{shareBadge.ObserverCount} observers";
        return $"Share {observersLabel} {shareBadge.Endpoint}";
    }
}

/// <summary>
/// Local window action owned entirely by the renderer.
/// </summary>
public enum ViewChromeLocalAction
{
    ToggleScaleMode = 0,
    ToggleFullscreen = 1,
    ToggleHelpOverlay = 2
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
    Fullscreen = 4,
    Back = 5,
    Home = 6,
    Recents = 7,
    Rotate = 8,
    PauseStream = 9,
    OpenArtifacts = 10,
    Help = 11
}

internal sealed record ViewChromeRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

internal sealed record ViewChromeButtonLayout(ViewChromeButtonKind Kind, ViewChromeRect Bounds, bool Enabled, bool Active);

internal sealed record ViewChromeDeviceSlotLayout(string DeviceSelector, int Index, string Label, string StatusLabel, bool IsActive, ViewChromeRect Bounds, bool Enabled);

internal sealed record ViewChromeBadgeLayout(ViewChromeRect Bounds, int ObserverCount, string Endpoint);

internal sealed record ViewChromeTooltip(ViewChromeRect AnchorBounds, string Text);

internal sealed record ViewChromeRenderLayout(
    IReadOnlyList<ViewChromeButtonLayout> Buttons,
    IReadOnlyList<ViewChromeDeviceSlotLayout> DeviceSlots,
    ViewChromeRect? ToolbarBounds,
    ViewChromeRect? ShelfBounds,
    ViewChromeBadgeLayout? ShareBadge = null);
