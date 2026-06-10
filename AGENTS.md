# Luotsi Agent Guide

Use this file as the first stop for AI agents working in this repository.

## What Luotsi Is

Luotsi is a host-driven Android device automation and replay CLI. It runs on the engineer, agent, or CI machine, talks to real Android devices over ADB, and preserves structured output plus replayable artifacts.

The core DX loop is:

```text
command -> structured output -> artifact root -> replay command -> next action
```

Start by reading:

- `website/src/content/docs/docs/getting-started/first-five-minutes.mdx`
- `website/src/content/docs/docs/reference/output-envelopes.mdx`
- `docs/commands.md`
- `Luotsi.Cli/Cli/Help.cs`

From the CLI, the fastest orientation commands are:

```bash
luotsi help quickstart
luotsi help output
```

## Output Contracts

Preserve these contracts unless the task explicitly changes them:

- Normal commands return one JSON envelope with `schema`, `ok`, `command`, `started_at`, `ended_at`, `data`, `artifacts`, `provenance`, and `error`.
- `inspect` is a JSONL session: one JSON event per line out, one JSON command per line in.
- `view` is human-readable by default and can stream JSONL with `-o jsonl`, `--output jsonl`, or `--json`.
- Scenario runs, inspect sessions, view sessions, and failure captures should leave durable artifact roots.
- Replay commands should help humans and agents answer what failed, what evidence proves it, and what command to run next.

When reading a one-shot envelope, check `ok` and the process exit code first. Then look for the next command in `data.recommended_next_action.command`, ordered handoffs such as `data.recommended_next_steps`, `data.next_actions`, and `data.suggested_commands`, command arrays such as `data.commands`, `data.artifact_commands`, and `data.recommended_commands`, and finally `artifacts.artifact_root` as the fallback target for `luotsi replay packet --artifacts <artifact-root>`. Use `luotsi replay open --artifacts <artifact-root> --dry-run` when a human needs the replay front door response.

When changing output, update both the implementation and the reader surface: help text, docs, tests, and representative examples where applicable.

## Development Workflow

Keep changes small and tied to one behavior or docs slice. Prefer the existing implementation surface over inventing a parallel contract.

Useful ownership map:

- CLI help and command list: `Luotsi.Cli/Cli/Help.cs`
- CLI routing/options: `Luotsi.Cli/Cli/`, especially `CliOptions.cs` and routing classes
- Output envelopes: `Luotsi.Cli/Cli/Envelope/`
- Shared result contracts: `Luotsi.Cli/Models/`
- Scenarios: `Luotsi.Cli/Scenarios/`
- Artifacts and replay: `Luotsi.Cli/Artifacts/` and `Luotsi.Cli/Cli/Replay/`
- Public docs: `docs/` and `website/src/content/docs/docs/`
- Android helper: `Luotsi.ViewServer.Android/`

## Validation

Use the pinned SDK from `global.json`. In WSL, `dotnet` and `node` may need explicit Windows paths:

```bash
'/mnt/c/Program Files/dotnet/dotnet.exe' test Luotsi.Cli.Tests/Luotsi.Cli.Tests.csproj
cmd.exe /c npm.cmd run build
```

Run focused checks first, then broader checks when the touched area justifies it.

Common targeted checks:

```bash
# Help, docs, and command-reference slices
'/mnt/c/Program Files/dotnet/dotnet.exe' test Luotsi.Cli.Tests/Luotsi.Cli.Tests.csproj --filter "FullyQualifiedName~Command_Reference_Documents_Known_Command_And_Help_Surfaces|FullyQualifiedName~Website_Documentation_Links_Resolve|FullyQualifiedName~Website_Documentation_Sidebar_Slugs_Resolve"

# Website docs build
cd website
cmd.exe /c npm.cmd ci
cmd.exe /c npm.cmd run build
```

For code changes, prefer focused unit tests around the owning class plus `dotnet test Luotsi.sln` before merge when feasible.

## Android Helper Work

For changes under `Luotsi.ViewServer.Android/`, also read:

- `.github/instructions/luotsi-viewserver-android-kotlin.instructions.md`
- `.github/instructions/luotsi-viewserver-android-config.instructions.md`
- `docs/android-agent-bundle.md`

Keep the Android helper thin. Policy, orchestration, diagnostics, and artifact handling belong on the host side unless there is a concrete device-side reason.

## Review Posture

Before claiming a task is done, report:

- changed files
- the behavior or docs surface changed
- validation commands run and their results
- any output contract, artifact, or UX impact
- any checks you could not run

Do not treat model confidence as validation. Use tests, builds, static checks, rendered docs, command output, or replay artifacts as evidence.
