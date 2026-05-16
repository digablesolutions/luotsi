namespace Luotsi.Cli.View;

/// <summary>
/// Session-owned interaction request emitted by the local view surface.
/// </summary>
public abstract record ViewInteractionRequest;

/// <summary>
/// Relative tap request from the local mirror surface.
/// </summary>
/// <param name="XRatio">Normalized horizontal coordinate.</param>
/// <param name="YRatio">Normalized vertical coordinate.</param>
public sealed record ViewTapRequest(double XRatio, double YRatio) : ViewInteractionRequest;

/// <summary>
/// Session-level window command emitted by local hotkeys or future toolbar actions.
/// </summary>
/// <param name="Command">Requested command.</param>
public sealed record ViewWindowCommandRequest(ViewWindowCommand Command) : ViewInteractionRequest;

/// <summary>
/// Text input request emitted by the local window surface.
/// </summary>
/// <param name="Text">UTF-8 text to send to the device.</param>
public sealed record ViewTextInputRequest(string Text) : ViewInteractionRequest;

/// <summary>
/// Android keyevent request emitted by the local window surface.
/// </summary>
/// <param name="Code">Android keyevent code or symbolic name.</param>
public sealed record ViewKeyInputRequest(string Code) : ViewInteractionRequest;

/// <summary>
/// Scroll request emitted by the local window surface.
/// </summary>
/// <param name="HorizontalTicks">Horizontal wheel ticks.</param>
/// <param name="VerticalTicks">Vertical wheel ticks.</param>
public sealed record ViewScrollRequest(int HorizontalTicks, int VerticalTicks) : ViewInteractionRequest;

/// <summary>
/// Clipboard paste request emitted by the local window surface.
/// </summary>
/// <param name="Text">Host clipboard text.</param>
public sealed record ViewClipboardPasteRequest(string Text) : ViewInteractionRequest;

/// <summary>
/// File-drop request emitted by the local window surface.
/// </summary>
/// <param name="FilePath">Dropped host-local file path.</param>
public sealed record ViewFileDropRequest(string FilePath) : ViewInteractionRequest;

/// <summary>
/// Device-switch request emitted by the local window shelf.
/// </summary>
/// <param name="DeviceSelector">ADB device selector to make active for the next connection iteration.</param>
public sealed record ViewSwitchDeviceRequest(string DeviceSelector) : ViewInteractionRequest;

/// <summary>
/// Window command raised by local UI affordances such as hotkeys.
/// </summary>
public enum ViewWindowCommand
{
    TakeScreenshot = 0,
    ToggleRecording = 1,
    Reconnect = 2
}