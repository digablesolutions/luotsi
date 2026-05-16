using System.Text.Json;
using Luotsi.Cli;
using Luotsi.Cli.Artifacts;
using Luotsi.Cli.Cli;
using Luotsi.Cli.Errors;
using Luotsi.Cli.Hosts.Android;
using Luotsi.Cli.Hosts.Android.View;
using Luotsi.Cli.Infrastructure;
using Luotsi.Cli.Models;
using Luotsi.Cli.Scenarios;
using Luotsi.Cli.Telemetry;
using Luotsi.Cli.View;
using Xunit;

namespace Luotsi.Cli.Tests;

public sealed partial class AppTests
{
    private static readonly JsonSerializerOptions TestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static string CreateUiDump(string text) =>
        $"<hierarchy><node text=\"{text}\" content-desc=\"\" resource-id=\"id/{text}\" class=\"android.widget.TextView\" enabled=\"true\" clickable=\"false\" bounds=\"[0,0][100,100]\" /></hierarchy>";

    private static string CreateDeviceFingerprintShellOutput(string serial, string model, string androidRelease, string sdk, string fingerprint, string abi, string currentFocus) =>
        string.Join(
            "\n",
            "__LUOTSI_DEVICE_FINGERPRINT__SERIAL__",
            serial,
            "__LUOTSI_DEVICE_FINGERPRINT__MODEL__",
            model,
            "__LUOTSI_DEVICE_FINGERPRINT__ANDROID_RELEASE__",
            androidRelease,
            "__LUOTSI_DEVICE_FINGERPRINT__SDK__",
            sdk,
            "__LUOTSI_DEVICE_FINGERPRINT__FINGERPRINT__",
            fingerprint,
            "__LUOTSI_DEVICE_FINGERPRINT__ABI__",
            abi,
            "__LUOTSI_DEVICE_FINGERPRINT__CURRENT_FOCUS__",
            currentFocus);

    private static JsonElement SerializeToJsonElement<T>(T value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, TestJsonOptions));
        return document.RootElement.Clone();
    }

    private static string CreateUiDumpWithNodes(params string[] nodes) => $"<hierarchy>{string.Join(string.Empty, nodes)}</hierarchy>";

    private static string CreateUiNode(string text, string contentDescription, string className, bool clickable, int left, int top, int right, int bottom) =>
        $"<node text=\"{text}\" content-desc=\"{contentDescription}\" resource-id=\"\" class=\"{className}\" enabled=\"true\" clickable=\"{clickable.ToString().ToLowerInvariant()}\" bounds=\"[{left},{top}][{right},{bottom}]\" />";

    private static ScreenState CreateScreenState(DateTimeOffset capturedAt, string text) =>
        new(capturedAt, 1, [new ScreenElement(text, null, $"id/{text}", "android.widget.TextView", true, true, 0, 0, 100, 100)]);
}
