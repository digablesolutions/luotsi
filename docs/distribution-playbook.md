# Luotsi Distribution Playbook

This file turns the discoverability plan into a concrete release and distribution checklist. The goal is not generic marketing volume. The goal is to make it easy for AI agent builders, mobile engineers, and technical evaluators to find the right Luotsi surfaces quickly.

## GitHub repository surfaces

Use the repository itself as the first distribution asset.

- Description: keep it aligned with the homepage and docs hub language around host-driven Android automation, AI agents, JSONL, live view, and replay.
- Homepage: point to the public docs site at `https://digablesolutions.github.io/luotsi/`.
- Topics: keep `adb`, `android`, `cli`, `device-automation`, `dotnet`, `e2e-testing`, and `live-view`, then add targeted discovery terms such as `ai-agents`, `android-automation`, `device-lab`, `jsonl`, `mobile-testing`, and `replay`.
- Social preview image: GitHub repository social preview is still a manual upload surface. Use `website/public/images/buggy-commands.png` as the current candidate asset until a dedicated branded preview image is designed. Upload it in GitHub repository settings under General > Social preview.

## Release-note shaping

GitHub-generated release notes are now categorized through `.github/release.yml`.
The release workflow prepends optional handwritten intros only from active intro files: `.github/release-notes/stable.md`, `.github/release-notes/prerelease.md`, or `.github/release-notes/<tag>.md`.
Template files live alongside them at `.github/release-notes/stable.template.md` and `.github/release-notes/prerelease.template.md`.

For each public release, add a short hand-written introduction above the generated notes with these three blocks when relevant:

1. What changed for agent builders.
2. What changed for engineers running real-device workflows.
3. Which docs page or tutorial to open first.
4. How to reason from the first command output into the next replay command.

Use wording that points readers to stable public entry points:

- Docs hub: `https://digablesolutions.github.io/luotsi/docs/`
- First five minutes: `https://digablesolutions.github.io/luotsi/docs/getting-started/first-five-minutes/`
- AI workflows: `https://digablesolutions.github.io/luotsi/docs/core-workflows/ai-agent-workflows/`
- Installation: `https://digablesolutions.github.io/luotsi/docs/getting-started/installation/`
- Replay and artifacts: `https://digablesolutions.github.io/luotsi/docs/core-workflows/replay-and-artifacts/`

For release validation and community posts, prefer the same first-output loop used by the CLI: `command -> structured output -> artifact root -> replay command -> next action`. Mention `luotsi help output` for source-tree users. When the message talks about failed CI runs, agent handoffs, or artifact packets, make the first concrete handoff `luotsi replay packet --artifacts <artifact-root>` followed by `luotsi replay packet --artifacts <artifact-root> --check` so evaluators see the durable `run-summary.json`, `run-summary.md`, At a Glance summary, primary failure, recommended next action, and 60-second checklist. When the message is aimed at humans browsing a failed run, use `luotsi replay open --artifacts <artifact-root> --dry-run` before the generic artifact browser.

## Directory and package listings

Track submissions in a lightweight table instead of relying on memory.

Skip curated awesome-lists for Luotsi. Focus on directories and indexes where maintainers expect a concise product entry, install link, and ongoing updates.

| Surface | Audience fit | Owner | Status | Notes |
| --- | --- | --- | --- | --- |
| Package and installer indexes | General developer discovery |  | Backlog | Prefer surfaces that can point to the docs site and GitHub releases cleanly. |
| CLI and developer-tool directories | General engineering discovery |  | Backlog | Use the docs site URL as the primary landing page. |
| Testing and QA directories | Device lab and CI evaluators |  | Backlog | Emphasize real-device adb workflows, not browser automation. |

When submitting, reuse the same compact description so listings do not drift.

> Luotsi is a host-driven Android automation CLI for AI agents, engineers, and CI. It runs against real devices over adb, exposes structured JSON, optional JSONL session streams, live view, scenario playbooks, and replay artifacts for later triage.

## Community and channel syndication

Use channels that can plausibly send technical evaluators to the docs hub or releases page.

- GitHub Releases: publish the release first and treat the release page as the canonical changelog target.
- LinkedIn company or engineering posts: use for credibility and broad technical reach.
- X or Mastodon: use short launch notes that link straight to the docs hub or release page.
- Hacker News or similar engineering communities: reserve for significant launches, major workflow additions, or polished demos rather than every patch release.
- Android, testing, and automation communities: post only when the release contains a clear angle for real-device debugging, CI, or agent-driven workflows.

## Reusable message angles

### Agent-builder angle

Luotsi gives agent loops a real Android device surface without inventing another control plane: `inspect` emits JSONL, `view -o jsonl` or `view --json` exposes the live-view event stream when automation needs it, `run` writes artifact-rich scenario outputs, and `replay` lets you triage after the device session ends.

### Mobile engineering angle

Luotsi keeps orchestration on the host machine, talks to Android over adb, mirrors the device when needed, and leaves behind artifacts that make failures debuggable after the run.

### CI and lab angle

The same Luotsi binary works for operator-driven sessions, repeatable scenarios, and replay-friendly CI reporting, which reduces the gap between debugging locally and running in a shared device lab.

## Release-day checklist

- Confirm the repo description, homepage, topics, and social preview are current.
- Confirm the docs hub, AI workflow guide, installation page, and replay page match the release.
- Publish the GitHub release with a short human-written introduction above generated notes.
- Post one primary announcement that links to either the release page or docs hub.
- Log any directory submissions and community posts in the table above so follow-up is visible.
