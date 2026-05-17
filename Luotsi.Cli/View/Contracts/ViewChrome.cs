namespace Luotsi.Cli.View.Contracts;

/// <summary>
/// Session-owned chrome state rendered by the local view window.
/// </summary>
/// <param name="ActiveDevice">Current active device selector.</param>
/// <param name="Devices">Known device shelf entries for the current session.</param>
/// <param name="ReadOnly">Whether the current session is read-only.</param>
/// <param name="IsObserverSession">Whether the current session is a share-transport observer client.</param>
/// <param name="IsRecording">Whether operator-driven recording is currently active.</param>
/// <param name="CanTakeScreenshot">Whether the screenshot action is currently available.</param>
/// <param name="CanToggleRecording">Whether operator recording control is available.</param>
/// <param name="CanReconnect">Whether reconnect is available.</param>
/// <param name="CanSwitchDevices">Whether the device shelf is interactive.</param>
/// <param name="ShareEndpoint">Optional share endpoint exposed by the source session.</param>
/// <param name="ObserverCount">Current connected observer count for the share endpoint.</param>
public sealed record ViewChromeState(
    string ActiveDevice,
    IReadOnlyList<ViewChromeDevice> Devices,
    bool ReadOnly,
    bool IsObserverSession,
    bool IsRecording,
    bool CanTakeScreenshot,
    bool CanToggleRecording,
    bool CanReconnect,
    bool CanSwitchDevices,
    string? ShareEndpoint = null,
    int ObserverCount = 0);

/// <summary>
/// Device entry displayed on the in-window shelf.
/// </summary>
/// <param name="Index">One-based slot index shown in the shelf.</param>
/// <param name="DeviceSelector">ADB device selector represented by the entry.</param>
/// <param name="Status">ADB status for the device.</param>
/// <param name="Details">Optional device details text from the device listing.</param>
/// <param name="IsActive">Whether the entry matches the active session device.</param>
public sealed record ViewChromeDevice(int Index, string DeviceSelector, string? Status, string Details, bool IsActive);