# Luotsi Project Map

Use this when changing Luotsi itself.

## Source Ownership

- CLI help and visible command reference: `Luotsi.Cli/Cli/Help.cs`, `docs/commands.md`, website docs.
- Routing and options: `Luotsi.Cli/Cli/CliOptions.cs`, `Luotsi.Cli/Cli/Routing/`.
- Envelopes and failure responders: `Luotsi.Cli/Cli/Envelope/`.
- Public result models: `Luotsi.Cli/Models/`.
- Device, ADB, and host interaction: `Luotsi.Cli/Hosts/Android/`, `Luotsi.Cli/Infrastructure/`.
- Scenarios: `Luotsi.Cli/Scenarios/`.
- Artifacts and replay: `Luotsi.Cli/Artifacts/`, `Luotsi.Cli/Cli/Replay/`.
- View session and sharing: `Luotsi.Cli/View/`, `Luotsi.ViewServer.Android/`.
- Agent examples: `examples/agents/`.
- Public docs: `README.md`, `AGENTS.md`, `docs/`, `website/src/content/docs/docs/`.

## Contract Rules

- When output changes, update implementation, tests, help text, docs, and examples together.
- Keep JSON envelope field names snake_case in command output unless an existing contract says otherwise. Persisted summary files often use camelCase.
- Prefer adding typed next-action fields over forcing agents to scrape Markdown.
- Preserve artifact roots and replay commands in human and JSON output.
- Do not make generated scenario drafts look production-ready without validation/review.

## Validation Commands

Run focused checks first, then broader checks when the touched surface is shared.

```bash
dotnet.exe test Luotsi.Cli.Tests/Luotsi.Cli.Tests.csproj --no-restore --filter "<test filter>" -v minimal
dotnet.exe test Luotsi.Cli.Tests/Luotsi.Cli.Tests.csproj --no-restore -v minimal
```

Website docs:

```bash
cd website
npm run check
npm run build
```

Android helper:

```bash
./gradlew -p Luotsi.ViewServer.Android :app:assembleDebug
```

Before publishing, run `git diff --check` and scan for conflict markers.
