namespace Luotsi.Cli.Telemetry;

/// <summary>
/// Shared telemetry logcat and payload contract literals.
/// </summary>
public static class TelemetryContracts
{
    /// <summary>
    /// Current Luotsi logcat marker prefix.
    /// </summary>
    public const string CurrentMarker = "LUOTSI_DEVICE_TELEMETRY";

    /// <summary>
    /// Temporary legacy logcat marker prefix retained for backward compatibility.
    /// </summary>
    public const string LegacyMarker = "DEVICE_TEST_TELEMETRY";

    /// <summary>
    /// Current telemetry payload schema.
    /// </summary>
    public const string CurrentSchema = "luotsi-device-telemetry.v1";

    /// <summary>
    /// Legacy telemetry payload schema.
    /// </summary>
    public const string LegacySchema = "device-test-telemetry.v1";

    /// <summary>
    /// Accepted logcat markers in parse order.
    /// </summary>
    public static IReadOnlyList<string> AcceptedMarkers { get; } = [CurrentMarker, LegacyMarker];
}