namespace Luotsi.Cli.Cli;

/// <summary>
/// Help text.
/// </summary>
public static class Help
{
    private static readonly IReadOnlyDictionary<string, string> TopicTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["adb"] = """
Luotsi help: adb

Usage:
  luotsi adb server-status
  luotsi adb version
  luotsi adb features
  luotsi adb mdns check
  luotsi adb reconnect [offline|device]

Examples:
  luotsi adb server-status
  luotsi adb mdns check
  luotsi adb reconnect offline

Notes:
  These commands are host-side ADB readiness checks. Use them when a device
  disappears, reports offline, or wireless discovery is unreliable.
""",
        ["app"] = """
Luotsi help: app controls

Usage:
  luotsi start-app --package <app.id> [--activity <activity>] [--wait]
  luotsi start-uri --uri <uri> [--package <app.id>] [--activity <activity>] [--action android.intent.action.VIEW] [--wait]
  luotsi force-stop --package <app.id>
  luotsi clear --package <app.id>
  luotsi is-app-installed --package <app.id>
  luotsi list-installed-packages [--third-party]
  luotsi grant-permission --package <app.id> --permission <permission>
  luotsi revoke-permission --package <app.id> --permission <permission>

Examples:
  luotsi force-stop --package com.example.app --device emulator-5554
  luotsi start-app --package com.example.app --activity .MainActivity --wait
  luotsi clear --package com.example.app

Failure modes:
  Missing package names return usage errors. Device-side package manager
  failures are reported in the command envelope with stderr attached.
""",
        ["artifacts"] = """
Luotsi help: artifacts

Usage:
  luotsi <command> --artifacts <directory>
  luotsi artifacts list [--artifacts <directory>] [--limit 20]
  luotsi artifacts info (<artifact-root-or-run-id> | --last [--artifacts <directory>])
  luotsi artifacts open (<artifact-root-or-run-id> | --last [--artifacts <directory>]) [--dry-run]
  luotsi artifacts pack <artifact-root-or-run-id> [--output <file.zip>] [--force] [--dry-run]
  luotsi artifacts unpack <artifact.zip> [--output <directory>] [--force] [--dry-run]
  luotsi run --path scenarios --report-json results.json --report-junit junit.xml
  luotsi run --path scenarios --events-jsonl events.jsonl

Artifacts:
  Luotsi writes index.md and index.html in each artifact session. The index
  groups screenshots, recordings, reports, logs, screen-state dumps, and UI
  hierarchy files so CI uploads are easier to browse.
  artifacts list shows recent artifact roots and their run ids. artifacts open
  refreshes the index if needed and opens the local browser or file manager.
  artifacts info summarizes one artifact root without opening or changing it.
  artifacts pack writes a zip suitable for sharing or CI upload, embeds
  luotsi-artifact-package.json, and reports SHA-256 for handoff verification.
  Use --dry-run to preview the output path, manifest, and entry count without
  writing.
  artifacts unpack extracts a zip into a local artifact root with zip-slip
  protection, requires luotsi-artifact-package.json, refreshes index.html for
  non-dry-run restores, and reports the manifest plus SHA-256 before you open
  or replay it.
  artifacts info/open also accept --last so you can jump straight back to the
  latest run artifact root under the default Luotsi run-artifact home or
  --artifacts <directory>.
  When the target is a run id rather than a full path, Luotsi searches the
  default Luotsi run-artifact home or --artifacts <directory>.

Examples:
  luotsi screen-state --device emulator-5554 --artifacts artifacts
  luotsi run --path scenarios --device emulator-5554 --artifacts artifacts --report-junit junit.xml
  luotsi artifacts list --artifacts artifacts
  luotsi artifacts info 20260518-100000-run --artifacts artifacts
  luotsi artifacts open artifacts/20260518-100000-run
  luotsi artifacts open 20260518-100000-run --artifacts artifacts
  luotsi artifacts open --last --artifacts artifacts
  luotsi artifacts pack artifacts/20260518-100000-run --output replay.zip --dry-run
  luotsi artifacts pack artifacts/20260518-100000-run --output replay.zip
  luotsi artifacts unpack replay.zip --output artifacts/replay --dry-run
""",
        ["inspect"] = """
Luotsi help: inspect

Usage:
  luotsi inspect --device <adb serial>
  luotsi screen-state --device <adb serial>
  luotsi telemetry-tail --device <adb serial> [--tail 200]
  luotsi logcat --device <adb serial> [--tail 200]
  luotsi record --device <adb serial> --output <file.mp4> [--time-limit-sec 30]

Examples:
  luotsi screen-state --device emulator-5554
  luotsi inspect --device emulator-5554

Failure modes:
  Old devices can fail UI hierarchy capture. Inspect keeps non-hierarchy
  commands useful where possible; screen-state includes attempted strategies
  and raw output in artifacts when hierarchy capture fails.
""",
        ["quickstart"] = """
Luotsi help: quickstart

Goal:
  Get from "device is attached" to a useful developer or CI workflow with the
  fewest Luotsi commands.

First run:
  1. Confirm device visibility
     luotsi devices

  2. Run guided readiness checks and fixes
     luotsi doctor --device <adb serial>
     luotsi doctor --device <adb serial> --fix

  3. Open a live mirror when you need operator feedback
     luotsi view --device <adb serial>

Common workflows:
  Inspect a screen and gather artifacts
    luotsi screen-state --device <adb serial>
    luotsi inspect --device <adb serial>

  Resume the latest local triage bundle
    luotsi artifacts open --last --artifacts artifacts
    luotsi replay open --last --artifacts artifacts --dry-run

  Prepare live view prerequisites without opening a stream
    luotsi view setup --device <adb serial>
    luotsi view-doctor --device <adb serial>

  Start authoring a scenario
    luotsi scenario-init --file scenarios/smoke.json --name "smoke"
    luotsi scenario-validate --path scenarios

  Run scenarios for CI or local verification
    luotsi run --path scenarios --device <adb serial> --report-junit junit.xml

Tips:
  Scenario runs write artifacts into the default Luotsi run-artifact home
  unless you override it with --artifacts <directory>. --output-dir <directory>
  is a clearer alias for the same root.
  Use luotsi help view, luotsi help scenario, and luotsi help lab when you want
  a deeper command family reference.
""",
        ["lab"] = """
Luotsi help: lab

Usage:
  luotsi devices
  luotsi device-status [--device <adb serial> | --device-query <query>]
  luotsi wait-for-device [--timeout-sec 15]
  luotsi device-wait [--timeout-sec 15]
  luotsi preflight [--package <app.id>]
  luotsi doctor --device <adb serial> [--package <app.id>] [--fix]
  luotsi lab status [--device-query <query>]
  luotsi lab doctor [--device-query <query>] [--fix]
  luotsi lab plan [--device-query <query>]
  luotsi lab claim [--device-query <query>] [--owner <name>] [--ttl-sec 3600]
  luotsi lab leases
  luotsi lab release (--lease <lease-id> | --serial <adb serial>)
  luotsi lab extend (--lease <lease-id> | --serial <adb serial>) [--ttl-sec 3600]
  luotsi lab quarantine [--device-query <query>] --reason <text> [--owner <name>]
  luotsi lab quarantines
  luotsi lab unquarantine --serial <adb serial>

Examples:
  luotsi devices
  luotsi device-status --device emulator-5554
  luotsi lab status
  luotsi lab status --device-query state=device,type=physical
  luotsi lab plan --device-query model=Pixel_9
  luotsi lab claim --device-query model=Pixel_9 --owner ci-job-1 --ttl-sec 1800
  luotsi lab leases
  luotsi lab extend --serial emulator-5554 --ttl-sec 7200
  luotsi lab quarantine --device-query serial=emulator-5554 --reason "flaky touchscreen"
  luotsi lab quarantines
  luotsi lab doctor --fix

Output:
  Lab status explains which attached devices match a query and includes ADB
  probe attempt/retry counts for transient host readiness failures. Lab doctor
  reports ambiguous selection, offline devices, stale devices, and recommended
  repair commands. With --fix, Luotsi may run safe host-side recovery actions.
  Lab claim creates a host-side lease token so CI and agents can avoid selecting
  a device already claimed by another workflow. Active leases are honored by
  --device-query selection. Lab quarantine marks unhealthy devices unavailable
  until they are explicitly unquarantined. Lab plan is a dry-run allocator that
  explains which device would be selected or why selection is blocked, and
  returns recommended_commands for the next operator or agent action.
""",
        ["ports"] = """
Luotsi help: ports

Usage:
  luotsi forward-list
  luotsi forward --local <adb-endpoint> --remote <adb-endpoint> [--no-rebind]
  luotsi forward-remove --local <adb-endpoint>
  luotsi reverse-list
  luotsi reverse --remote <adb-endpoint> --local <adb-endpoint> [--no-rebind]
  luotsi reverse-remove --remote <adb-endpoint>

Examples:
  luotsi reverse --remote tcp:8080 --local tcp:8080 --device emulator-5554
  luotsi forward --local tcp:9222 --remote localabstract:chrome_devtools_remote

Notes:
  Use reverse when an Android app needs to call a host-local dev server. Use
  forward when the host needs to reach a device-local service.
""",
        ["replay"] = """
Luotsi help: replay

Usage:
  luotsi replay open --artifacts <artifact-root> [--dry-run] [--write-json] [--write-markdown]
  luotsi replay open --last [--artifacts <directory>] [--dry-run] [--write-json] [--write-markdown]
  luotsi replay summarize --artifacts <artifact-root> [--format json|jsonl]
  luotsi replay capsule --artifacts <artifact-root> [--write-readme] [--write-json]
  luotsi replay timeline --artifacts <artifact-root> [--failures] [--type <event-type>] [--contains <text>] [--source-path <timeline-path>] [--sequence <n>] [--since <timestamp>] [--until <timestamp>] [--context <n>] [--limit 200] [--format json|jsonl] [--write-json] [--write-jsonl] [--write-markdown]
  luotsi replay scrub --artifacts <artifact-root> [--failures] [--type <event-type>] [--contains <text>] [--source-path <timeline-path>] [--sequence <n>] [--context <n>] [--limit 200] [--write-json] [--write-markdown]
  luotsi replay graph --artifacts <artifact-root> [--failed] [--node-kind <kind>] [--edge-kind <kind>] [--action <text>] [--selector <text>] [--contains <text>] [--insight <kind>] [--severity info|warning|error] [--evidence <kind>] [--fact <text>] [--node <id> --depth 1] [--limit 200] [--format json|jsonl] [--write-json] [--write-jsonl] [--write-markdown]
  luotsi replay cluster --artifacts <artifact-root> [--min-count <n>] [--similarity same_failure_shape|likely_same_cause|same_bucket] [--contains <text>] [--write-json] [--write-markdown]
  luotsi replay scenario-draft --artifacts <artifact-root> --output <scenario.json> [--name <name>] [--write-json] [--write-markdown]
  luotsi replay search --artifacts <artifact-root> --contains <text> [--limit 50]

Examples:
  luotsi replay open --artifacts artifacts/20260518-100000-view --write-json --write-markdown
  luotsi replay open --last --artifacts artifacts --dry-run
  luotsi replay summarize --artifacts artifacts/20260518-100000-view
  luotsi replay summarize --artifacts artifacts/20260518-100000-view --format json
  luotsi replay summarize --artifacts artifacts/20260518-100000-view --format jsonl
  luotsi replay capsule --artifacts artifacts/20260518-100000-run --write-readme --write-json
  luotsi replay timeline --artifacts artifacts/20260518-100000-run --failures --format jsonl --write-jsonl --write-markdown
  luotsi replay timeline --artifacts artifacts/20260518-100000-run --source-path session-timeline.jsonl --sequence 1
  luotsi replay scrub --artifacts artifacts/20260518-100000-run --failures --context 3 --write-markdown
  luotsi replay graph --artifacts artifacts/20260518-100000-run --write-json --write-jsonl --write-markdown
  luotsi replay graph --artifacts artifacts/20260518-100000-run --format jsonl
  luotsi replay graph --artifacts artifacts/20260518-100000-run --failed --node-kind failure --write-markdown
  luotsi replay graph --artifacts artifacts/20260518-100000-run --contains "not visible" --write-markdown
  luotsi replay graph --artifacts artifacts/20260518-100000-run --evidence artifact --format jsonl
  luotsi replay graph --artifacts artifacts/20260518-100000-run --fact action_to_failure --format jsonl
  luotsi replay graph --artifacts artifacts/20260518-100000-run --severity warning --write-markdown
  luotsi replay graph --artifacts artifacts/20260518-100000-run --node-kind selector --write-markdown
  luotsi replay graph --artifacts artifacts/20260518-100000-run --node failure:session-timeline.jsonl:3 --depth 2
  luotsi replay cluster --artifacts artifacts/ci-runs --write-json --write-markdown
  luotsi replay cluster --artifacts artifacts/ci-runs --min-count 2 --similarity same_failure_shape --contains waitVisible
  luotsi replay scenario-draft --artifacts artifacts/20260518-100000-inspect --output scenarios/draft.json --write-markdown
  luotsi replay search --artifacts artifacts/20260518-100000-run --contains "not visible"

Output:
  Replay commands normally return the standard command envelope. replay
  summarize, timeline, and graph can use --format json|jsonl for raw machine
  exports. Raw --format modes cannot be combined with --human, --quiet, --json,
  or --console-output because those flags control envelope presentation.

Notes:
  Replay open is the canonical replay front door: it refreshes
  index.html/index.md, opens the artifact browser, and returns session counts,
  primary failure, recommended next action, and commands into capsule, timeline,
  scrub, graph, search, scenario draft, and clustering. With --write-json and
  --write-markdown, it writes replay-open-summary.json and replay-open.md. Use
  --last to resume the latest artifact root under the default temp root or
  --artifacts <directory> without re-copying a path.
  Replay summarize reads session-replay.json and session-timeline.jsonl from an
  existing artifact root. By default it returns the condensed failure timeline
  as a normal JSON command envelope. `--format json` writes the bare summary
  object, and `--format jsonl` writes one summary header line followed by one
  session line per replay session. The summary includes commands that point
  into open, capsule, scrub, graph, and cluster follow-ups. Failed scenario runs also expose
  failure_capsule_path and an embedded failure_capsule summary with linked
  reports and failure artifacts. Replay scenario-draft turns inspect/replay action events into a conservative draft
  scenario with warnings and suggestions for cleanup. The result includes
  source_summaries, step_origins, and normalizations so reviewers can see
  which steps came from inspect commands, screen deltas, view events,
  telemetry, or existing scenario events. With --write-json and
  --write-markdown, it writes review artifacts into the replay root. Suggested
  commands route back to open, capsule, scrub, graph provenance, validation, and
  source timeline events. Replay search scans
  text-like replay artifacts, reports, logcat, hierarchies, screen-state JSON,
  and timelines for a case-insensitive string, then returns commands back into
  open, capsule, scrub, and graph when the matches support those next steps. Replay capsule returns a compact
  bundle manifest with artifact counts, an artifact_manifest, primary failure,
  existing scenario draft artifact paths, and suggested next commands. With --write-readme, replay capsule writes replay-capsule.md into
  the artifact root. With --write-json, it writes replay-capsule-summary.json.
  Both options refresh the artifact index. Replay timeline returns ordered
  session-timeline.jsonl events with stable detail text for CI and agents.
  Use --contains to filter normalized event type/detail text. Use --since and
  --until with ISO-8601 timestamps to narrow by event time. Use --source-path
  and --sequence to reopen a specific event referenced by scenario-draft
  provenance. Use --context to include neighboring events around filtered matches.
  With --format json or --format jsonl, replay timeline writes raw machine
  output instead of the normal command envelope. With --write-json or
  --write-jsonl, it persists normalized timeline artifacts. With
  --write-markdown, it writes replay-timeline.md for artifact browsing. Timeline
  results include commands back into open, capsule, scrub, and graph when the
  selected events support those follow-ups. These
  write options refresh the artifact index. Replay scrub uses the same timeline
  filters but returns a local previous/focused/next event view with exact
  commands to reopen the focused event, move to adjacent events, search the
  focused detail, open the replay front door, or open graph context. With --write-json and
  --write-markdown, it writes replay-scrub.json and replay-scrub.md into the
  artifact root. Replay graph emits a stable node
  and edge model over sessions, timeline events, failures, scenarios,
  artifacts, actions, text selectors, screen observations, telemetry
  signals, and generated scenario draft provenance. Its result includes query,
  taxonomy, agent_summary, total_node_count, total_edge_count, node_kinds,
  edge_kinds, insights, actions, and failure_paths so agents can quickly
  understand what failed and what to do next. With --format json or --format
  jsonl, replay graph writes raw machine output instead of the command envelope.
  With --write-jsonl, it persists replay-graph.jsonl for CI and agent consumers.
  Graph actions start with replay open so semantic context can
  route back to the canonical front door.
  Use --failed, --node-kind, --edge-kind, --action, --selector, --contains,
  --insight, --evidence, --fact,
  --severity, --node,
  --depth, and --limit to return a focused subgraph with local context. Replay cluster groups failed replay sessions by normalized failure
  shape and returns cross-run intelligence: similarity, likely cause, stable
  versus variable signals, and capsule/graph/scrub/search commands for the latest
  matching bundle. Use --min-count, --similarity, and --contains to focus on
  repeated high-signal clusters.
  Failures still use the normal error envelope.
""",
        ["update"] = """
Luotsi help: update

Usage:
  luotsi version
  luotsi update [--version <tag>] [--channel stable|prerelease] [--dry-run] [--detach]

Examples:
  luotsi version
  luotsi update --dry-run
  luotsi update --detach
  luotsi update --version v0.1.0-rc.4 --dry-run
  luotsi update --version v0.1.0-rc.4 --detach

Notes:
  Luotsi does not auto-update during normal commands. The update command uses
  the existing installer manifest to reinstall the correct runtime archive for
  this host. Stable updates can use the latest stable release. Prerelease
  updates currently require an explicit --version tag. On Windows, non-dry-run
  update requires --detach and returns `update_started` because the installer
  continues after the current luotsi.exe process exits.
""",
        ["run"] = """
Luotsi help: run

Usage:
  luotsi run --file <scenario.json> [--validate-only] [--claim-device] [--owner <name>] [--ttl-sec 3600]
  luotsi run --path <scenario-file-or-directory-or-glob> [--dry-run|--validate-only]
             [--include-tag <tag>] [--exclude-tag <tag>] [--name <text>] [--action <action>]
             [--shard-count <n> --shard-index <zero-based>] [--shard-strategy index|hash]
             [--claim-device] [--owner <name>] [--ttl-sec 3600]
             [--events-jsonl <file>] [--report-json <file>] [--report-junit <file>]
             [--capture-on failure|never] [--attach-artifacts never|on-failure|always]
             [--progress auto|line|plain|quiet|jsonl] [--output-dir <directory>]

Examples:
  luotsi run --path scenarios --device emulator-5554 --report-junit junit.xml
  luotsi run --path scenarios --device-query model=Pixel_9 --claim-device --owner ci-job-1
  luotsi run --path scenarios --include-tag smoke --dry-run
  luotsi run --path scenarios --shard-count 4 --shard-index 0 --events-jsonl events.jsonl

Artifacts:
  Runs can emit JSONL lifecycle events, JSON summaries, JUnit XML, failure
  bundles, screenshots, recordings, and a browsable artifact index. Successful
  run results also include artifact_commands with exact artifacts open,
  artifacts pack, and replay open commands for the run artifact root. With
  --human or --console-output human, failed runs are rendered as a compact
  triage capsule that surfaces the primary failure, evidence counts, and next
  command.

Progress:
  Run prints progress to stderr by default and keeps the final command envelope
  on stdout. Use --progress quiet for log-heavy CI, --progress line for compact
  one-line events, --progress plain for human text, or --progress jsonl for a
  typed JSONL progress stream on stderr. --quiet also selects quiet progress
  unless --progress quiet is supplied explicitly. --events-jsonl remains the
  durable artifact event stream.

Failure modes:
  scenarioRunEnded is emitted even when parsing, ADB readiness, install, or a
  step fails. Use --validate-only for null-device CI validation. --claim-device
  releases its lab lease from a finally path after execution.
""",
        ["scenario"] = """
Luotsi help: scenarios

Usage:
  luotsi scenario-init [--file <scenario.json>] [--name <name>] [--package <app.id>] [--activity <activity>] [--force]
  luotsi scenario-list --path <scenario-file-or-directory-or-glob> [filters]
  luotsi scenario-validate (--file <scenario.json> | --path <scenario-file-or-directory-or-glob>)
                           [--events-jsonl <file>] [--report-json <file>] [--report-junit <file>]
                           [--progress auto|line|plain|quiet|jsonl]
  luotsi scenario-explain --file <scenario.json>

Examples:
  luotsi scenario-init --file scenarios/login.json --name "login smoke"
  luotsi scenario-validate --path scenarios
  luotsi scenario-explain --file scenarios/login.json

Notes:
  Scenario metadata can declare tags, expected package/activity, screen size,
  orientation, and notes so Luotsi can warn when a device does not match the
  scenario authoring target. scenario-validate uses the same stderr progress
  modes and report writers as run --validate-only, while stdout remains the
  final command envelope.
""",
        ["view"] = """
Luotsi help: view

Usage:
  luotsi view --device <adb serial> [--profile <name>] [--preset safe|balanced|high-quality|low-latency]
              [--capture-backend auto|screenrecord|mediaprojection] [--decoder ffmpeg|wmf]
              [--read-only] [--headless] [--record <file>]
              [-o|--output human|json|jsonl] [--json] [--quiet]
  luotsi view setup --device <adb serial> [--dry-run]
  luotsi view-doctor --device <adb serial> [--fix]
  luotsi reconnect [--profile <name>] [--device <adb serial> | --join-share <host:port>]

Examples:
  luotsi view --device emulator-5554
  luotsi view setup --device emulator-5554 --capture-backend mediaprojection
  luotsi view-doctor --device emulator-5554 --fix

Artifacts:
  View can record MP4 output, capture screenshots from the window, and write
  runtime diagnostic events when decoder, helper, transport, or projection
  startup fails.

Output:
  View prints a human startup checklist by default. Use -o jsonl, --output
  jsonl, or --json to stream the raw event bus to stdout for automation. View
  treats -o json as JSONL because the live event stream is line-oriented. Use
  --quiet to print only diagnostics and errors. Session events are still
  written to the artifact timeline either way.

Failure modes:
  If startup fails, Luotsi tries to print one actionable next command such as
  view setup, view-doctor --fix, or choosing another decoder/capture backend.
""",
        ["wireless"] = """
Luotsi help: wireless

Usage:
  luotsi wireless --device <usb serial> [--host <ip-or-host>] [--port 5555]
  luotsi wireless-scan
  luotsi wireless-pair (--endpoint <host:port> | --service <mdns-service>) --code <pairing-code>
  luotsi wireless-connect [--endpoint <host:port> | --service <mdns-service>] [--save-profile <name>]

Examples:
  luotsi wireless-scan
  luotsi wireless-pair --service adb-1234._adb-tls-pairing._tcp --code 123456
  luotsi wireless-connect --service adb-1234._adb-tls-connect._tcp --save-profile pixel-wifi

Notes:
  Prefer TLS pairing and mDNS connect services on modern Android. The legacy
  adb tcpip path is available for older devices but is not encrypted.
"""
    };

    /// <summary>
    /// Gets available command help topics.
    /// </summary>
    public static IReadOnlyList<string> Topics { get; } = TopicTexts.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>
    /// Gets help topics ordered for user-facing suggestions.
    /// </summary>
    public static IReadOnlyList<string> SuggestedTopics { get; } = BuildSuggestedTopics();

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
  luotsi help <topic>
  luotsi --version

Start here:
  luotsi help quickstart

Workflow index:

  First-time setup and repair
    luotsi devices
    luotsi doctor --device <adb serial>
    luotsi view setup --device <adb serial>
    luotsi help lab

  Manual live debugging
    luotsi view --device <adb serial>
    luotsi screen-state --device <adb serial>
    luotsi inspect --device <adb serial>
    luotsi help view

  Scenario authoring
    luotsi scenario-init --file scenarios/smoke.json --name "smoke"
    luotsi scenario-validate --path scenarios
    luotsi help scenario

  CI execution and reports
    luotsi run --path scenarios --device <adb serial> --report-junit junit.xml
    luotsi help run

Help topics:
  quickstart | lab | view | inspect | scenario | run | artifacts | replay | adb
  wireless | ports | app | update

From source:
  .\scripts\luotsi.ps1 <command> [options]
  ./scripts/luotsi.sh <command> [options]
  dotnet run --project Luotsi.Cli -- <command> [options]

Command groups:

  Device inventory and readiness
    devices
    lab status [--device-query <query>]
    lab doctor [--device-query <query>] [--fix]
    lab plan [--device-query <query>]
    lab claim [--device-query <query>] [--owner <name>] [--ttl-sec 3600]
    lab leases
    lab release (--lease <lease-id> | --serial <adb serial>)
    lab extend (--lease <lease-id> | --serial <adb serial>) [--ttl-sec 3600]
    lab quarantine [--device-query <query>] --reason <text> [--owner <name>]
    lab quarantines
    lab unquarantine --serial <adb serial>
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
    view (--device <adb serial> | --join-share <host:port> | --last) [--profile <name>] [--save-profile <name>] [--share-bind <host:port>] [--preset safe|balanced|high-quality|low-latency] [--defaults] [--read-only] [--always-on-top] [--codec h264|h265] [--decoder ffmpeg|wmf] [--capture-backend auto|screenrecord|mediaprojection] [--headless] [--record <file>] [--stats-interval-ms <ms>] [--renderer-stats-interval-ms <ms>] [-o|--output human|json|jsonl] [--json] [--quiet]
    reconnect [--profile <name>] [--device <adb serial> | --join-share <host:port>] [--save-profile <name>] [--share-bind <host:port>] [--preset safe|balanced|high-quality|low-latency] [--defaults] [--read-only] [--always-on-top] [--codec h264|h265] [--decoder ffmpeg|wmf] [--capture-backend auto|screenrecord|mediaprojection] [--headless] [--record <file>] [--stats-interval-ms <ms>] [--renderer-stats-interval-ms <ms>] [-o|--output human|json|jsonl] [--json] [--quiet]
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

  Artifact replay and triage
    artifacts list [--artifacts <directory>] [--limit 20]
    artifacts info (<artifact-root-or-run-id> | --last [--artifacts <directory>])
    artifacts open (<artifact-root-or-run-id> | --last [--artifacts <directory>]) [--dry-run]
    artifacts pack <artifact-root-or-run-id> [--output <file.zip>] [--force] [--dry-run]
    artifacts unpack <artifact.zip> [--output <directory>] [--force] [--dry-run]
    replay summarize --artifacts <artifact-root> [--format json|jsonl]
    replay capsule --artifacts <artifact-root> [--write-readme] [--write-json]
    replay timeline --artifacts <artifact-root> [--failures] [--type <event-type>] [--contains <text>] [--since <timestamp>] [--until <timestamp>] [--context <n>] [--limit 200] [--format json|jsonl] [--write-json] [--write-jsonl] [--write-markdown]
    replay scrub --artifacts <artifact-root> [--failures] [--source-path <timeline-path>] [--sequence <n>] [--context <n>] [--write-json] [--write-markdown]
    replay graph --artifacts <artifact-root> [--failed] [--node-kind <kind>] [--edge-kind <kind>] [--action <text>] [--selector <text>] [--contains <text>] [--insight <kind>] [--severity info|warning|error] [--evidence <kind>] [--fact <text>] [--node <id> --depth 1] [--limit 200] [--format json|jsonl] [--write-json] [--write-jsonl] [--write-markdown]
    replay cluster --artifacts <artifact-root> [--write-json] [--write-markdown]
    replay open (--artifacts <artifact-root> | --last [--artifacts <directory>]) [--dry-run] [--write-json] [--write-markdown]
    replay scenario-draft --artifacts <artifact-root> --output <scenario.json> [--name <name>] [--write-json] [--write-markdown]
    replay search --artifacts <artifact-root> --contains <text> [--limit 50]

  Install and update
    version
    update [--version <tag>] [--channel stable|prerelease] [--dry-run] [--detach]

  Scenarios and CI reports
    scenario-init [--file <scenario.json>] [--name <name>] [--package <app.id>] [--activity <activity>] [--width <px>] [--height <px>] [--orientation <name>] [--force]
    scenario-list --path <scenario-file-or-directory-or-glob> [--include-tag <tag>] [--exclude-tag <tag>] [--name <text>] [--action <action>]
    scenario-validate (--file <scenario.json> | --path <scenario-file-or-directory-or-glob>) [--events-jsonl <file>] [--report-json <file>] [--report-junit <file>]
    scenario-explain --file <scenario.json>
    run --file <scenario.json> [--validate-only] [--claim-device] [--owner <name>] [--ttl-sec 3600] [--no-require-device-ready] [--device-ready-timeout-sec 15] [--package <app.id>] [--events-jsonl <file>] [--report-json <file>] [--report-junit <file>] [--capture-on failure|never] [--attach-artifacts never|on-failure|always] [--progress auto|line|plain|quiet|jsonl] [--output-dir <directory>]
    run --path <scenario-file-or-directory-or-glob> [--dry-run|--validate-only] [--claim-device] [--owner <name>] [--ttl-sec 3600] [--no-require-device-ready] [--device-ready-timeout-sec 15] [--package <app.id>] [--events-jsonl <file>] [--report-json <file>] [--report-junit <file>] [--capture-on failure|never] [--attach-artifacts never|on-failure|always] [--progress auto|line|plain|quiet|jsonl] [--output-dir <directory>] [--include-tag <tag>] [--exclude-tag <tag>] [--name <text>] [--action <action>] [--shard-count <n> --shard-index <zero-based>] [--shard-strategy index|hash]

Common options:
  --device <adb serial>
  --device-query <query>       exact-match clauses: state=online,type=physical,model=Pixel_9
  --adb <adb executable>
  --platform <android>
  --adb-timeout-sec <seconds>  default 120, 0 disables; env LUOTSI_ADB_TIMEOUT_SEC
  --artifacts <directory>
  --output-dir <directory>     run alias for --artifacts
  --poll-artifacts <final|per-attempt|none>
  -o, --output <mode>          view: human|json|jsonl
  --human                      one-shot commands: print a concise text envelope;
                               run/replay triage uses capsule-style summaries
  --console-output <human|json|quiet>
                               one-shot commands: choose terminal envelope mode
  --json                       view: stream JSONL events; one-shot commands: JSON envelope
  --quiet                      view: print only diagnostics/errors; one-shot: suppress success output
  --version                    print the Luotsi version and exit

Design:
  Luotsi is intentionally host-side and cross-platform. The v1 implementation
  stays on boring ADB commands so it is easy for agents and CI to run.
""";

    /// <summary>
    /// Gets a command help topic, or root help when the topic is unknown.
    /// </summary>
    /// <param name="topic">Topic or command name.</param>
    /// <returns>Topic help text.</returns>
    public static string GetTopic(string topic) => TryGetTopic(topic, out var text) ? text : Text;

    /// <summary>
    /// Tries to get command-specific help.
    /// </summary>
    /// <param name="topic">Topic or command name.</param>
    /// <param name="text">Topic help text.</param>
    /// <returns>True when the topic is known.</returns>
    public static bool TryGetTopic(string topic, out string text)
    {
        if (TopicTexts.TryGetValue(topic, out text!))
        {
            return true;
        }

        return TryNormalizeTopic(topic, out var normalized) && TopicTexts.TryGetValue(normalized, out text!);
    }

    private static bool TryNormalizeTopic(string topic, out string normalized)
    {
        normalized = topic.ToLowerInvariant() switch
        {
          "workflow" or "workflows" or "start" or "getting-started" or "gettingstarted" => "quickstart",
            "replay-summarize" => "replay",
            "version" => "update",
            "view-setup" or "view-doctor" or "reconnect" or "profile-list" or "profile-delete" => "view",
            "scenario-init" or "scenario-list" or "scenario-validate" or "scenario-explain" => "scenario",
            "wireless-scan" or "wireless-pair" or "wireless-connect" => "wireless",
            "forward" or "forward-list" or "forward-remove" or "reverse" or "reverse-list" or "reverse-remove" => "ports",
            "start-app" or "start-uri" or "force-stop" or "clear" or "clear-app" or "is-app-installed" or "list-installed-packages" or "grant-permission" or "revoke-permission" => "app",
            "screen-state" or "telemetry-tail" or "telemetry-watch" or "wait-step" or "wait-action-ready" or "wait-visible" or "wait-for-activity" or "wait-for-not-activity" or "tap" or "tap-text" or "type-text" or "keyevent" or "logcat" or "wait-log" or "record" => "inspect",
            "devices" or "device-status" or "wait-for-device" or "device-wait" or "preflight" or "doctor" => "lab",
            _ => string.Empty
        };

        return normalized.Length > 0;
    }

  private static IReadOnlyList<string> BuildSuggestedTopics()
  {
    var topics = Topics.Where(static topic => !string.Equals(topic, "quickstart", StringComparison.OrdinalIgnoreCase)).ToList();
    topics.Insert(0, "quickstart");
    return topics;
  }
}
