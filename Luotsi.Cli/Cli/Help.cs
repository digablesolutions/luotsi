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
      __                 __        _
     / /   __  __ ____  / /_ _____(_)
    / /   / / / // __ \/ __// ___/ /
   / /___/ /_/ // /_/ / /_ (__  ) /
  /_____/\__,_/ \____/\__//____/_/
          /\        host-driven device automation
     ____/  \____   inspect | drive | record | report

Usage:
  luotsi <command> [options]

Fast paths:
  luotsi devices
  luotsi view --device <adb serial>
  luotsi screen-state --device <adb serial>
  luotsi run --path scenarios --device <adb serial> --report-junit junit.xml

From source:
  .\scripts\luotsi.ps1 <command> [options]
  ./scripts/luotsi.sh <command> [options]
  dotnet run --project Luotsi.Cli -- <command> [options]

Command groups:

  Device inventory and readiness
    devices
    lab status [--device-query <query>]
    lab doctor [--device-query <query>]
    device-status [--device <adb serial> | --device-query <query>]
    wait-for-device [--timeout-sec 15]
    device-wait [--timeout-sec 15]
    preflight [--package <app.id>]
    doctor --device <adb serial> [--package <app.id>] [--profile <name>] [--preset safe|balanced|high-quality|low-latency] [--defaults] [--read-only] [--decoder ffmpeg|wmf] [--capture-backend auto|screenrecord|mediaprojection] [--record <file>] [--fix]

  ADB server and wireless
    adb server-status
    adb version
    adb features
    adb mdns check
    adb reconnect [offline|device]
    wireless --device <usb serial> [--host <ip-or-host>] [--port 5555]
    wireless-scan
    wireless-pair (--endpoint <host:port> | --service <mdns-service>) --code <pairing-code>
    wireless-connect [--endpoint <host:port> | --service <mdns-service>] [--save-profile <name>]

  Live view and profiles
    view (--device <adb serial> | --join-share <host:port> | --last) [--profile <name>] [--save-profile <name>] [--share-bind <host:port>] [--preset safe|balanced|high-quality|low-latency] [--defaults] [--read-only] [--always-on-top] [--codec h264|h265] [--decoder ffmpeg|wmf] [--capture-backend auto|screenrecord|mediaprojection] [--headless] [--record <file>] [--stats-interval-ms <ms>] [--renderer-stats-interval-ms <ms>]
    reconnect [--profile <name>] [--device <adb serial> | --join-share <host:port>] [--save-profile <name>] [--share-bind <host:port>] [--preset safe|balanced|high-quality|low-latency] [--defaults] [--read-only] [--always-on-top] [--codec h264|h265] [--decoder ffmpeg|wmf] [--capture-backend auto|screenrecord|mediaprojection] [--headless] [--record <file>] [--stats-interval-ms <ms>] [--renderer-stats-interval-ms <ms>]
    view setup --device <adb serial> [--profile <name>] [--preset safe|balanced|high-quality|low-latency] [--defaults] [--decoder ffmpeg|wmf] [--capture-backend auto|screenrecord|mediaprojection] [--dry-run]
    view-setup --device <adb serial> [--profile <name>] [--preset safe|balanced|high-quality|low-latency] [--defaults] [--decoder ffmpeg|wmf] [--capture-backend auto|screenrecord|mediaprojection] [--dry-run]
    view-doctor --device <adb serial> [--profile <name>] [--preset safe|balanced|high-quality|low-latency] [--defaults] [--read-only] [--decoder ffmpeg|wmf] [--capture-backend auto|screenrecord|mediaprojection] [--record <file>] [--fix]
    profile-list
    profile-delete --name <profile>

  App and port plumbing
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
    is-app-installed --package <app.id>
    list-installed-packages [--third-party]
    grant-permission --package <app.id> --permission <permission>
    revoke-permission --package <app.id> --permission <permission>

  Inspect, interact, and capture
    screen-state
    inspect
    telemetry-tail [--tail 200]
    telemetry-watch [--timeout-sec 15]
    wait-step --step <STEP_NAME> [--timeout-sec 15]
    wait-action-ready --action <name> [--step <STEP_NAME>] [--timeout-sec 15]
    wait-visible --text <label> [--timeout-sec 15]
    wait-for-activity --activity <activity-or-pattern> [--timeout-sec 15]
    wait-for-not-activity --activity <activity-or-pattern> [--timeout-sec 15]
    tap-text --text <label> [--timeout-sec 15]
    tap --x <px> --y <px>
    type-text --text <value>
    keyevent --code <code>
    logcat [--tail 200]
    wait-log --contains <text> [--timeout-sec 15]
    record --output <file.mp4> [--time-limit-sec 30]

  Scenarios and CI reports
    scenario-init [--file <scenario.json>] [--name <name>] [--package <app.id>] [--activity <activity>] [--width <px>] [--height <px>] [--orientation <name>] [--force]
    scenario-list --path <scenario-file-or-directory-or-glob> [--include-tag <tag>] [--exclude-tag <tag>] [--name <text>] [--action <action>]
    scenario-validate (--file <scenario.json> | --path <scenario-file-or-directory-or-glob>) [--events-jsonl <file>] [--report-json <file>] [--report-junit <file>]
    scenario-explain --file <scenario.json>
    run --file <scenario.json> [--validate-only] [--no-require-device-ready] [--device-ready-timeout-sec 15] [--package <app.id>] [--events-jsonl <file>] [--report-json <file>] [--report-junit <file>] [--capture-on failure|never] [--attach-artifacts never|on-failure|always]
    run --path <scenario-file-or-directory-or-glob> [--dry-run|--validate-only] [--no-require-device-ready] [--device-ready-timeout-sec 15] [--package <app.id>] [--events-jsonl <file>] [--report-json <file>] [--report-junit <file>] [--capture-on failure|never] [--attach-artifacts never|on-failure|always] [--include-tag <tag>] [--exclude-tag <tag>] [--name <text>] [--action <action>] [--shard-count <n> --shard-index <zero-based>] [--shard-strategy index|hash]

Common options:
  --device <adb serial>
  --device-query <query>       exact-match clauses: state=online,type=physical,model=Pixel_9
  --adb <adb executable>
  --platform <android>
  --adb-timeout-sec <seconds>  default 120, 0 disables; env LUOTSI_ADB_TIMEOUT_SEC
  --artifacts <directory>
  --poll-artifacts <final|per-attempt|none>

Design:
  Luotsi is intentionally host-side and cross-platform. The v1 implementation
  stays on boring ADB commands so it is easy for agents and CI to run.
""";
}
