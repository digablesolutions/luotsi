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
  luotsi <command> [options]

From source:
  .\scripts\luotsi.ps1 <command> [options]
  ./scripts/luotsi.sh <command> [options]
  dotnet run --project Luotsi.Cli -- <command> [options]

Commands:
  devices
  device-status [--device <adb serial> | --device-query <query>]
  adb server-status
  adb version
  adb features
  adb mdns check
  adb reconnect [offline|device]
  wait-for-device [--timeout-sec 15]
  device-wait [--timeout-sec 15]
  preflight [--package <app.id>]
  screen-state
  inspect
  view (--device <adb serial> | --join-share <host:port> | --last) [--profile <name>] [--save-profile <name>] [--share-bind <host:port>] [--preset safe|balanced|high-quality|low-latency] [--defaults] [--read-only] [--always-on-top] [--codec h264|h265] [--decoder ffmpeg|wmf] [--capture-backend auto|screenrecord|mediaprojection] [--headless] [--record <file>] [--stats-interval-ms <ms>] [--renderer-stats-interval-ms <ms>]
  view setup --device <adb serial> [--profile <name>] [--preset safe|balanced|high-quality|low-latency] [--defaults] [--decoder ffmpeg|wmf] [--capture-backend auto|screenrecord|mediaprojection] [--dry-run]
  view-setup --device <adb serial> [--profile <name>] [--preset safe|balanced|high-quality|low-latency] [--defaults] [--decoder ffmpeg|wmf] [--capture-backend auto|screenrecord|mediaprojection] [--dry-run]
  reconnect [--profile <name>] [--device <adb serial> | --join-share <host:port>] [--save-profile <name>] [--share-bind <host:port>] [--preset safe|balanced|high-quality|low-latency] [--defaults] [--read-only] [--always-on-top] [--codec h264|h265] [--decoder ffmpeg|wmf] [--capture-backend auto|screenrecord|mediaprojection] [--headless] [--record <file>] [--stats-interval-ms <ms>] [--renderer-stats-interval-ms <ms>]
  view-doctor --device <adb serial> [--profile <name>] [--preset safe|balanced|high-quality|low-latency] [--defaults] [--read-only] [--decoder ffmpeg|wmf] [--capture-backend auto|screenrecord|mediaprojection] [--record <file>] [--fix]
  profile-list
  profile-delete --name <profile>
  wireless --device <usb serial> [--host <ip-or-host>] [--port 5555]
  wireless-scan
  wireless-pair (--endpoint <host:port> | --service <mdns-service>) --code <pairing-code>
  wireless-connect [--endpoint <host:port> | --service <mdns-service>] [--save-profile <name>]
  forward-list
  forward --local <adb-endpoint> --remote <adb-endpoint> [--no-rebind]
  forward-remove --local <adb-endpoint>
  reverse-list
  reverse --remote <adb-endpoint> --local <adb-endpoint> [--no-rebind]
  reverse-remove --remote <adb-endpoint>
  start-app --package <app.id> [--activity <activity>] [--wait]
  start-uri --uri <uri> [--package <app.id>] [--activity <activity>] [--action android.intent.action.VIEW] [--wait]
  force-stop --package <app.id>
  clear --package <app.id> (alias: clear-app)
  wait-for-activity --activity <activity-or-pattern> [--timeout-sec 15]
  wait-for-not-activity --activity <activity-or-pattern> [--timeout-sec 15]
  is-app-installed --package <app.id>
  list-installed-packages [--third-party]
  grant-permission --package <app.id> --permission <permission>
  revoke-permission --package <app.id> --permission <permission>
  scenario-list --path <scenario-file-or-directory-or-glob> [--include-tag <tag>] [--exclude-tag <tag>] [--name <text>] [--action <action>]
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
  run --file <scenario.json> [--validate-only] [--no-require-device-ready] [--device-ready-timeout-sec 15] [--events-jsonl <file>] [--report-json <file>] [--report-junit <file>] [--capture-on failure|never] [--attach-artifacts never|on-failure|always]
  run --path <scenario-file-or-directory-or-glob> [--dry-run|--validate-only] [--no-require-device-ready] [--device-ready-timeout-sec 15] [--events-jsonl <file>] [--report-json <file>] [--report-junit <file>] [--capture-on failure|never] [--attach-artifacts never|on-failure|always] [--include-tag <tag>] [--exclude-tag <tag>] [--name <text>] [--action <action>] [--shard-count <n> --shard-index <zero-based>] [--shard-strategy index|hash]

Common options:
  --device <adb serial>
  --device-query <query>       exact-match clauses: state=online,type=physical,model=Pixel_9
  --adb <adb executable>
  --platform <android>
  --adb-timeout-sec <seconds>  (default 120, 0 disables; env LUOTSI_ADB_TIMEOUT_SEC)
  --artifacts <directory>
  --poll-artifacts <final|per-attempt|none>

Design:
  Luotsi is intentionally host-side and cross-platform. The v1
  implementation stays on boring ADB commands so it is easy for agents and CI
  to run.
""";
}
