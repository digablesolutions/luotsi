# Copilot instructions for Luotsi

## Build and test commands

Use the solution and test project from the repository root.

```powershell
dotnet build .\Luotsi.sln
dotnet test .\Luotsi.sln
dotnet test .\Luotsi.Cli.Tests\Luotsi.Cli.Tests.csproj --filter "FullyQualifiedName~Luotsi.Cli.Tests.AppTests.RunAsync_Invalid_Tap_Coordinates_Return_Usage_Error_Envelope"
```

The repo is pinned to **.NET SDK 10.0.300** in `global.json`.

There is no dedicated lint or formatting command checked into the repo. The normal safety net here is `dotnet build` plus `dotnet test`.

## First-five-minute output loop

When you are reasoning from Luotsi output, start with the CLI-native primer:

```powershell
luotsi help output
```

The core handoff is `command -> structured output -> artifact root -> replay command -> next action`. Normal commands return one JSON envelope, while `inspect` is a JSONL session. After checking `ok` and the process exit code, choose the next command from `data.recommended_next_action_command` / `data.recommendedNextActionCommand`, `data.recommended_next_action.command`, focused packet evidence such as `data.primary_failure.source_command` or `data.primaryFailure.sourceCommand`, packet checklist commands such as `data.triage_checklist[].command` or `data.triageChecklist[].command`, ordered handoff arrays such as `data.recommended_next_steps`, `data.next_actions`, and `data.suggested_commands`, then `artifacts.artifact_root` as the fallback target for `luotsi replay packet --artifacts <artifact-root>`. Use command arrays such as `data.commands`, `data.artifact_commands`, and `data.recommended_commands` only when there is no artifact root to packetize.

Use `luotsi replay open --artifacts <artifact-root> --dry-run` when a human needs the primary failure, recommended next action, and follow-up commands without launching a browser. Use `luotsi artifacts open <artifact-root>` only when you specifically need the generic artifact browser.

## High-level architecture

- `Luotsi.Cli\Cli\` is the command layer. `Program` creates `App`, `CliOptions` finds the first known command anywhere in the argv list, and `App` dispatches that command to a device host or to the long-lived `InspectSession`.
- `Luotsi.Cli\Infrastructure\` defines the seams used throughout the app: `IFileSystem`, `IProcessRunner`, `IAdbClient`, `IDeviceHost`, `IConsoleIo`, `IDelay`, and the default factory implementations. Keep orchestration code depending on these abstractions rather than directly on `File`, `Process`, or `Console`.
- `Luotsi.Cli\Hosts\Android\DeviceRunner.cs` is the concrete runtime for almost all device behavior today. It wraps `adb`, captures UI hierarchy/logcat/telemetry artifacts, normalizes screen state, and implements both direct CLI commands and scenario actions.
- `Luotsi.Cli\Scenarios\ScenarioExecutor.cs` loads JSON scenario files from `scenarios\`, resolves `${env:...}`, `${var:...}`, and `${now:...}` templates, validates step arguments, and runs steps through `IScenarioActionHost`. Failures are expected to carry structured artifact bundles, not just plain exceptions.
- `Luotsi.Cli\Telemetry\` parses semantic telemetry from logcat (`LUOTSI_DEVICE_TELEMETRY`) and feeds both direct telemetry commands and semantic waits such as `wait-step` / `wait-action-ready`.
- `Luotsi.Cli\Artifacts\ArtifactSession.cs` gives every command/scenario run its own artifact root. Runtime commands are expected to leave behind useful artifacts, and failure paths should capture screenshots, hierarchy, screen-state, logcat, and metadata instead of failing silently.
- The platform seam is already present (`IDeviceHostFactory`, `DeviceHostConfiguration`), but the shipped implementation is still Android-only. If you add another platform, keep `App` and `ScenarioExecutor` host-agnostic.

## Key conventions

- **Two JSON conventions are in use on purpose.** External command envelopes and inspect-mode events are serialized as compact `snake_case` JSON in `App` and `InspectSession`. Scenario files and internal artifact JSON written through `AppJson.Options` stay `camelCase` and indented. Do not collapse these into one serializer without checking both protocols.
- **Normal command mode returns exactly one final JSON envelope.** `inspect` is the exception: it is a JSONL session that emits `session_started`, `screen_snapshot`, `command_result`, `screen_delta`, and `session_ended` events over stdin/stdout.
- **Scenario action names are camelCase strings**, while CLI commands are hyphenated (`waitVisible` vs `wait-visible`). Preserve the existing names when adding actions or docs because `ScenarioExecutor` switches on the exact action strings.
- **Artifact capture is part of the contract.** `DeviceRunner` writes `hierarchy.xml`, `screen-state.json`, telemetry/log artifacts, and failure bundles. When changing waits, polling, or failure behavior, keep artifact generation intact and respect `--poll-artifacts` (`final`, `per-attempt`, `none`).
- **Tests prefer injected fakes over real processes/devices.** `Luotsi.Cli.Tests\AppTests.cs` exercises `App`, `DeviceRunner`, and `ScenarioExecutor` directly with `FakeConsole`, `FakeFileSystem`, `FakeAdbClient`, `FakeDelay`, and manual time. Follow that pattern instead of adding tests that depend on a real device or real `adb`.
- **Error classification is user-visible API.** `ErrorInfo.Classify` maps failures into categories such as `usage_error`, `selector_or_screen_state`, `oracle_timeout`, and `configuration_error`. If you add new failure paths, make sure the category and envelope shape still match the CLI contract.
- **The CLI is intentionally “boring ADB first.”** Prefer extending the current host-side ADB-driven flow and telemetry/artifact model before introducing heavier transport or protocol layers.
