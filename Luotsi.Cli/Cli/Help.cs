namespace Luotsi.Cli.Cli;

/// <summary>
/// Help text.
/// </summary>
public static class Help
{
    /// <summary>
    /// Gets command-line help.
    /// </summary>
    public const string Text = """
Luotsi

Usage:
  dotnet run --project Luotsi.Cli -- <command> [options]

Commands:
  devices
  preflight --package <app.id>
  screen-state
  inspect
  view (--device <adb serial> | --join-share <host:port> | --last) [--profile <name>] [--save-profile <name>] [--share-bind <host:port>] [--preset safe|balanced|high-quality|low-latency] [--defaults] [--read-only] [--always-on-top] [--codec h264|h265] [--decoder ffmpeg|wmf] [--headless] [--record <file>] [--stats-interval-ms <ms>] [--renderer-stats-interval-ms <ms>]
  view-doctor --device <adb serial> [--profile <name>] [--preset safe|balanced|high-quality|low-latency] [--defaults] [--read-only] [--decoder ffmpeg|wmf] [--record <file>]
  profile-list
  profile-delete --name <profile>
  wireless --device <usb serial> [--host <ip-or-host>] [--port 5555]
  telemetry-tail [--tail 200]
  telemetry-watch [--timeout-sec 15]
  wait-step --step <STEP_NAME> [--timeout-sec 15]
  wait-action-ready --action <name> [--step <STEP_NAME>] [--timeout-sec 15]
  wait-visible --text <label> [--timeout-sec 15]
  tap-text --text <label> [--timeout-sec 15]
  tap --x <px> --y <px>
  type-text --text <value>
  keyevent --code <code>
  logcat [--tail 200]
  wait-log --contains <text> [--timeout-sec 15]
  record --output <file.mp4> [--time-limit-sec 30]
  run --file <scenario.json>

Common options:
  --device <adb serial>
  --adb <adb executable>
  --platform <android>
  --artifacts <directory>
  --poll-artifacts <final|per-attempt|none>

Design:
  Luotsi is intentionally host-side and cross-platform. The v1
  implementation stays on boring ADB commands so it is easy for agents and CI
  to run.
""";
}
