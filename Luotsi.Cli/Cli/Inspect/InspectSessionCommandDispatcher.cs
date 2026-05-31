using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;
using Luotsi.Cli.Models;

namespace Luotsi.Cli.Cli.Inspect;

internal sealed class InspectSessionCommandDispatcher(IDeviceHost deviceHost)
{
    private readonly IDeviceHost _deviceHost = deviceHost ?? throw new ArgumentNullException(nameof(deviceHost));

    public static string Normalize(string command) => command.Trim().Replace('-', '_').ToLowerInvariant();

    public static bool IsExit(string normalizedCommand) => normalizedCommand is "exit" or "quit";

    public static bool ShouldCaptureScreenState(string normalizedCommand) => normalizedCommand is
        "refresh" or
        "screen_state" or
        "snapshot" or
        "tap" or
        "tap_element" or
        "tap_selector" or
        "tap_text" or
        "wait_element" or
        "wait_selector" or
        "wait_visible" or
        "type_text" or
        "keyevent";

    public static ScreenElementSelector? TryCreateResultSelector(InspectCommandRequest request, string normalizedCommand)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!UsesSelector(request, normalizedCommand))
        {
            return null;
        }

        try
        {
            var selector = request.ToSelector();
            return selector.HasCriteria ? selector : null;
        }
        catch (UsageException)
        {
            return null;
        }
    }

    public async Task<object> ExecuteAsync(InspectCommandRequest request, string normalizedCommand)
    {
        ArgumentNullException.ThrowIfNull(request);

        return normalizedCommand switch
        {
            "refresh" or "screen_state" or "snapshot" => new { refreshed = true },
            "tap" => await _deviceHost.TapAsync(RequireInt(request.X, "x").ToString(System.Globalization.CultureInfo.InvariantCulture), RequireInt(request.Y, "y").ToString(System.Globalization.CultureInfo.InvariantCulture)).ConfigureAwait(false),
            "tap_element" or "tap_selector" => await _deviceHost.TapElementAsync(RequireSelector(request, normalizedCommand), request.TimeoutSec ?? 15).ConfigureAwait(false),
            "tap_text" => await TapTextAsync(request).ConfigureAwait(false),
            "wait_element" or "wait_selector" => await _deviceHost.WaitVisibleAsync(RequireSelector(request, normalizedCommand), request.TimeoutSec ?? 15).ConfigureAwait(false),
            "wait_visible" => await WaitVisibleAsync(request).ConfigureAwait(false),
            "type_text" => await _deviceHost.TypeTextAsync(RequireText(request.Text, "text")).ConfigureAwait(false),
            "keyevent" => await _deviceHost.KeyEventAsync(RequireText(request.Code, "code")).ConfigureAwait(false),
            "logcat" => await _deviceHost.LogcatAsync(request.Tail ?? 200).ConfigureAwait(false),
            "telemetry_tail" => await _deviceHost.TelemetryTailAsync(request.Tail ?? 200).ConfigureAwait(false),
            "telemetry_watch" => await _deviceHost.TelemetryWatchAsync(request.TimeoutSec ?? 15).ConfigureAwait(false),
            "screenshot" or "take_screenshot" => await _deviceHost.TakeScreenshotAsync(request.Label ?? request.Text ?? "inspect").ConfigureAwait(false),
            "capture_artifacts" => await _deviceHost.CaptureArtifactsAsync(request.Label ?? request.Text ?? "inspect").ConfigureAwait(false),
            "record" => await _deviceHost.RecordAsync(RequireText(request.Output, "output"), request.TimeLimitSec ?? 30).ConfigureAwait(false),
            _ => throw new UsageException($"Unknown inspect command '{request.Command}'.")
        };
    }

    private static string RequireText(string? value, string optionName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new UsageException($"Inspect command requires '{optionName}'.")
            : value;

    private static int RequireInt(int? value, string optionName) =>
        value ?? throw new UsageException($"Inspect command requires '{optionName}'.");

    private async Task<object> WaitVisibleAsync(InspectCommandRequest request)
    {
        if (request.HasSelectorOptions)
        {
            return await _deviceHost.WaitVisibleAsync(RequireSelector(request, "wait_visible"), request.TimeoutSec ?? 15).ConfigureAwait(false);
        }

        return await _deviceHost.WaitVisibleAsync(RequireText(request.Text, "text"), request.TimeoutSec ?? 15).ConfigureAwait(false);
    }

    private async Task<object> TapTextAsync(InspectCommandRequest request)
    {
        if (request.HasSelectorOptions)
        {
            return await _deviceHost.TapElementAsync(RequireSelector(request, "tap_text"), request.TimeoutSec ?? 15).ConfigureAwait(false);
        }

        return await _deviceHost.TapTextAsync(RequireText(request.Text, "text"), request.TimeoutSec ?? 15).ConfigureAwait(false);
    }

    private static ScreenElementSelector RequireSelector(InspectCommandRequest request, string command)
    {
        var selector = request.ToSelector();
        return selector.HasCriteria
            ? selector
            : throw new UsageException($"Inspect command '{command}' requires at least one selector field: text, content_description, resource_id, class_name, or region.");
    }

    private static bool UsesSelector(InspectCommandRequest request, string normalizedCommand) =>
        normalizedCommand is "tap_element" or "tap_selector" or "wait_element" or "wait_selector" ||
        (normalizedCommand is "tap_text" or "wait_visible" && request.HasSelectorOptions);
}
