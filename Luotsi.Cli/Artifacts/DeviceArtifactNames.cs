namespace Luotsi.Cli.Artifacts;

/// <summary>
/// Stable artifact file names emitted by the Android device host.
/// </summary>
internal static class DeviceArtifactNames
{
    public const string HierarchyXml = "hierarchy.xml";
    public const string InvalidHierarchyXml = "hierarchy-invalid.xml";
    public const string ScreenStateJson = "screen-state.json";
    public const string WaitLogBaseName = "wait-log";
    public const string LogcatText = "logcat.txt";
    public const string TelemetryTailBaseName = "telemetry-tail";
    public const string TelemetryWatchBaseName = "telemetry-watch";
    public const string WaitStepBaseName = "wait-step";
    public const string WaitActionReadyBaseName = "wait-action-ready";
    public const string AssertEventBaseName = "assert-event";
    public const string DeviceFingerprintJson = "device-fingerprint.json";

    public static string TextFromBase(string artifactBaseName) => $"{artifactBaseName}.txt";

    public static string JsonFromBase(string artifactBaseName) => $"{artifactBaseName}.json";

    public static string ScreenshotForLabel(string label) => $"{label}-screenshot.png";

    public static string LogcatForLabel(string label) => $"{label}-logcat.txt";

    public static string ScreenStateForLabel(string label) => $"{label}-screen-state.json";

    public static string HierarchyForLabel(string label) => $"{label}-hierarchy.xml";

    public static string InvalidHierarchyForLabel(string label) => $"{label}-hierarchy-invalid.xml";

    public static string FailureMetadataForLabel(string label) => $"{label}-failure.json";
}