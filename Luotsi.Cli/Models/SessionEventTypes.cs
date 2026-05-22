namespace Luotsi.Cli.Models;

/// <summary>
/// Shared JSONL event type names for inspect and view session protocols.
/// </summary>
public static class SessionEventTypes
{
    /// <summary>
    /// Inspect session JSONL event types.
    /// </summary>
    public static class Inspect
    {
        public const string SessionStarted = "session_started";
        public const string SessionEnded = "session_ended";
        public const string ProtocolError = "protocol_error";
        public const string CommandResult = "command_result";
        public const string SessionError = "session_error";
        public const string ScreenSnapshot = "screen_snapshot";
        public const string ScreenDelta = "screen_delta";
    }

    /// <summary>
    /// View session JSONL event types.
    /// </summary>
    public static class View
    {
        public const string Started = "view_started";
        public const string StartupPhase = "view_startup_phase";
        public const string Diagnostic = "view_diagnostic";
        public const string Stats = "view_stats";
        public const string ShareStarted = "view_share_started";
        public const string ShareClientConnected = "view_share_client_connected";
        public const string ShareClientDisconnected = "view_share_client_disconnected";
        public const string Reconnected = "view_reconnected";
        public const string CaptureBackendFallback = "view_capture_backend_fallback";
        public const string Ended = "view_ended";
        public const string Error = "view_error";
        public const string RecordingStarted = "view_recording_started";
        public const string RecordingStopped = "view_recording_stopped";
        public const string ClipboardPasted = "view_clipboard_pasted";
        public const string InteractionFailed = "view_interaction_failed";
        public const string DeviceSwitchRequested = "view_device_switch_requested";
        public const string ScreenshotCaptured = "view_screenshot_captured";
        public const string ReconnectRequested = "view_reconnect_requested";
        public const string ArtifactsOpened = "view_artifacts_opened";
        public const string StreamPaused = "view_stream_paused";
        public const string StreamResumed = "view_stream_resumed";
        public const string PackageInstalled = "view_package_installed";
        public const string FilePushed = "view_file_pushed";
        public const string FilePulled = "view_file_pulled";
        public const string KeyCommandSent = "view_key_command_sent";
        public const string DeviceShelf = "view_device_shelf";
        public const string InputBlocked = "view_input_blocked";
    }
}
