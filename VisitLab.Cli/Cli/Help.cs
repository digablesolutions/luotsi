namespace VisitLab.Cli;

/// <summary>
/// Help text.
/// </summary>
public static class Help
{
    /// <summary>
    /// Gets command-line help.
    /// </summary>
    public const string Text = """
VisitLab.Cli

Usage:
  dotnet run --project VisitLab.Cli -- <command> [options]

Commands:
  devices
  preflight --package <app.id>
  screen-state
  inspect
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

Design:
  The CLI is intentionally host-side and cross-platform. The v1
  implementation stays on boring ADB commands so it is easy for agents and CI
  to run.
""";
}