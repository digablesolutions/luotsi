# Luotsi Workflows

## Install Or Find Luotsi

Use the installed binary first:

```bash
luotsi version
luotsi --version
luotsi help quickstart
luotsi help output
```

If missing, use the repository README or published docs for installer commands. Current public install paths:

```powershell
iex (irm https://github.com/digablesolutions/luotsi/releases/latest/download/luotsi-install.ps1)
```

```bash
curl -fsSL https://github.com/digablesolutions/luotsi/releases/latest/download/luotsi-install.sh | sh
```

Source checkout fallback:

```bash
dotnet run --project Luotsi.Cli -- version
dotnet run --project Luotsi.Cli -- help quickstart
```

## First Five Minutes

Use this sequence when evaluating Luotsi or preparing an agent/lab host:

```bash
luotsi quickstart --human
luotsi quickstart --device <serial> --package <app.id> --artifacts artifacts/first-run --write-json --write-markdown
luotsi quickstart-verify --device <serial> --package <app.id> --artifacts artifacts/first-run
luotsi doctor
luotsi doctor --device <serial> --package <app.id>
luotsi preflight --device <serial> --package <app.id>
```

`doctor` is the onboarding decision command. Without a selected device it guides device selection. With a selected device and package it reports readiness, blockers, next command, and recommended commands. When ready, package-aware doctor can point to `discover` so a real app becomes review-required scenario candidates.

## Command Selection

Use `screen-state` for a one-shot UI dump. Use `inspect` when an agent needs a live JSONL control loop. Use `view` when a human needs a mirror, screenshots, recordings, hotkeys, or share/observer mode. Use scenarios when the flow should be reviewed, versioned, repeated, reported, or run in CI.

Common commands:

```bash
luotsi devices
luotsi screen-state --device <serial>
luotsi inspect --device <serial> --artifacts artifacts/inspect
luotsi view setup --device <serial>
luotsi view --device <serial> --artifacts artifacts/view
luotsi discover --device <serial> --package <app.id> --budget 5m
```

Always capture an artifact root for workflows that another human or agent must continue.
