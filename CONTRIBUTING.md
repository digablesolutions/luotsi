# Contributing

Luotsi is intended to stay boring to operate and boring to automate. Keep
changes small, explicit, and easy to validate.

## Workflow

- Branch from `main` for each change.
- Open a pull request instead of pushing straight to `main`.
- Keep commits and PRs scoped to one change or one tightly related slice.
- Update docs when command behavior, flags, artifacts, or operator UX change.
- Update `docs/` first for maintained prose and reference content.
- If the public website mirrors that topic under `website/src/content/docs/docs/`, update the matching page in the same change.

## First-five-minute DX

When a change affects command output, artifacts, replay, or agent-facing
behavior, keep the first output handoff obvious for both humans and agents.
The core Luotsi loop is:

```text
command -> structured output -> artifact root -> replay command -> next action
```

Use `luotsi help output` as the CLI-native source for that model. Normal
commands return one JSON envelope, while `inspect` emits JSONL. After checking
`ok` and the process exit code, command readers should look for
`data.recommended_next_action.command`, focused packet evidence such as
`data.primary_failure.source_command` or `data.primaryFailure.sourceCommand`,
ordered handoff arrays such as `data.recommended_next_steps`,
`data.next_actions`, and `data.suggested_commands`, command arrays such as
`data.commands`, `data.artifact_commands`, and `data.recommended_commands`,
then
`artifacts.artifact_root` as the fallback target for
`luotsi replay packet --artifacts <artifact-root>`.

Prefer `luotsi replay open --artifacts <artifact-root> --dry-run` when a human
needs the primary failure, recommended next action, and follow-up commands
without launching a browser. Use
`luotsi artifacts open <artifact-root>` only when the generic artifact browser
is the specific goal.

## Validation

Use the pinned .NET SDK from `global.json` (`10.0.300`) for source builds and
tests. Release archives are self-contained and expose the installed binary as
`luotsi`, but the commands below are the contributor/source-tree workflow.

Run the normal .NET safety net from the repository root:

```powershell
dotnet build .\Luotsi.sln
dotnet test .\Luotsi.sln
```

If you only touched a narrow slice, run the focused test or command that proves
that slice first, then fall back to the full solution checks before merge.

If you changed the Android helper, also validate the helper build from
`Luotsi.ViewServer.Android`:

```powershell
.\gradlew.bat assembleDebug
```

## Design expectations

- Preserve the single JSON envelope contract for normal command mode.
- Preserve JSONL streaming semantics for `inspect` and `view` sessions.
- Prefer host-side orchestration over device-side complexity.
- Keep Android support ADB-first unless there is a concrete reason not to.
- Maintain artifact capture on device-facing failures.
- Prefer injected fakes in tests over real devices or real `adb`.

## Docs Maintenance

When documentation needs a source of truth, prefer the owning implementation surface instead of copying behavior from older docs:

- `docs/` for maintained repository prose and reference pages
- `website/src/content/docs/docs/` for the published public mirror of overlapping docs content
- `Luotsi.Cli/Cli/Help.cs` for the public CLI command list and flags
- `Luotsi.Cli/Scenarios/ScenarioExecutor.cs` and `Luotsi.Cli/Scenarios/ScenarioValidator.cs` for supported scenario actions and validation rules
- `Luotsi.ViewServer.Android/app/src/main/AndroidManifest.xml` plus the helper Kotlin sources for Android helper behavior, permissions, and entry points

Keep `Luotsi.Cli.Tests/TutorialDocumentationTests.cs` green when you touch docs. It validates local links in `docs/`, local links in the website docs content, and the Starlight sidebar slugs in `website/astro.config.mjs`.

## Pull requests

PRs should explain:

- what changed
- how it was validated
- any contract, artifact, or UX impact

If the change affects command output, attach representative JSON or artifact
examples in the PR description.
