using Luotsi.Cli.Errors;
using Luotsi.Cli.Infrastructure.Contracts;

namespace Luotsi.Cli.Cli.Inspect;

internal sealed class InspectSessionCommandDispatcher(IDeviceHost deviceHost)
{
    private readonly IDeviceHost _deviceHost = deviceHost ?? throw new ArgumentNullException(nameof(deviceHost));

    public string Normalize(string command) => command.Trim().Replace('-', '_').ToLowerInvariant();

    public bool IsExit(string normalizedCommand) => normalizedCommand is "exit" or "quit";

    public bool ShouldCaptureScreenState(string normalizedCommand) => normalizedCommand is
        "refresh" or
        "screen_state" or
        "snapshot" or
        "tap" or
        "tap_text" or
        "wait_visible" or
        "type_text" or
        "keyevent";

    public async Task<object> ExecuteAsync(InspectCommandRequest request, string normalizedCommand)
    {
        ArgumentNullException.ThrowIfNull(request);

        return normalizedCommand switch
        {
            "refresh" or "screen_state" or "snapshot" => new { refreshed = true },
            "tap" => await _deviceHost.TapAsync(RequireInt(request.X, "x").ToString(System.Globalization.CultureInfo.InvariantCulture), RequireInt(request.Y, "y").ToString(System.Globalization.CultureInfo.InvariantCulture)).ConfigureAwait(false),
            "tap_text" => await _deviceHost.TapTextAsync(RequireText(request.Text, "text"), request.TimeoutSec ?? 15).ConfigureAwait(false),
            "wait_visible" => await _deviceHost.WaitVisibleAsync(RequireText(request.Text, "text"), request.TimeoutSec ?? 15).ConfigureAwait(false),
            "type_text" => await _deviceHost.TypeTextAsync(RequireText(request.Text, "text")).ConfigureAwait(false),
            "keyevent" => await _deviceHost.KeyEventAsync(RequireText(request.Code, "code")).ConfigureAwait(false),
            "telemetry_tail" => await _deviceHost.TelemetryTailAsync(request.Tail ?? 200).ConfigureAwait(false),
            "telemetry_watch" => await _deviceHost.TelemetryWatchAsync(request.TimeoutSec ?? 15).ConfigureAwait(false),
            _ => throw new UsageException($"Unknown inspect command '{request.Command}'.")
        };
    }

    private static string RequireText(string? value, string optionName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new UsageException($"Inspect command requires '{optionName}'.")
            : value;

    private static int RequireInt(int? value, string optionName) =>
        value ?? throw new UsageException($"Inspect command requires '{optionName}'.");
}