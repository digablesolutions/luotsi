# Android CLI Journey Opportunity Brief

Date: 2026-06-07

Status: exploration brief for a 60-90 minute product sprint

## Sprint goal

Decide whether Android CLI 1.0, Android skills, and Journeys create a near-term
Luotsi opportunity, then pick the smallest useful product or documentation slice
that sharpens Luotsi's position without reimplementing Google's agent tooling.

Success criteria:

- State what Android CLI is standardizing for agent-first Android workflows.
- State where Luotsi is meaningfully different.
- Identify concrete synergy ideas that fit Luotsi's existing surfaces:
  `inspect`, `discover`, `run`, `replay`, and `replay scenario-draft`.
- Recommend one first move that can be implemented without changing Luotsi's
  device-control model.

Source anchors:

- [Android CLI Now Stable 1.0: Accelerate developing for Android using any agent](https://android-developers.googleblog.com/2026/05/android-cli-stable-1-0-agent-development.html)
- [Overview of Android CLI](https://developer.android.com/tools/agents/android-cli)
- [Android CLI support for Journeys](https://developer.android.com/tools/agents/android-cli/journeys)
- Referenced X post summary supplied in the planning prompt: Codex `/goal` mode
  is most useful when the target is concrete, bounded, and measurable.

## What Android CLI is standardizing

Android CLI is becoming the official Android command-line surface for
agent-first development. It gives agents and scripts a shared entry point for
Android project work, including project creation, app build and run commands,
virtual device management, Android documentation lookup, and Android-specific
skills.

The important concepts for Luotsi are:

- `android init` installs the base `android-cli` skill so agents can understand
  the official Android CLI surface.
- `android skills add`, `skills find`, and `skills list` make Android skills a
  first-class distribution channel for agent instructions.
- `android studio ...` commands connect an agent to a running Android Studio
  instance for project intelligence such as file analysis, symbol lookup, usage
  search, Compose preview rendering, preview semantics, and dependency version
  lookup.
- Journeys let an agent turn natural-language app-flow instructions into device
  interactions and assertions. The docs explicitly frame Journeys as terminal
  agent workflows that can also be deployed in CI/CD.

That combination matters because it normalizes a new expectation: Android teams
will increasingly ask agents to understand an app, write or run user-flow tests,
and use official Android knowledge without leaving the terminal.

## Where Luotsi differs

Luotsi should not compete by cloning Android CLI. Android CLI is strongest near
the Android project, official Android guidance, Android Studio, SDK-managed
emulators, and agent skills.

Luotsi's niche is the host-side evidence and control layer for real-device
workflows:

- Real Android device state over ADB, exposed as JSON envelopes and JSONL
  sessions.
- `inspect` for interactive agent control with screen snapshots, deltas, command
  results, and replayable session timelines.
- `discover` for bounded exploration of unknown app surfaces plus starter
  scenario candidates.
- JSON scenario playbooks for repeatable runs with validation, metadata,
  assertions, screenshots, logcat, telemetry, and reports.
- Shared-lab concerns such as device readiness, claims, queues, quarantine,
  device health, and JUnit/CI policy signals.
- `replay` commands that let agents and humans debug after the device session is
  gone: open, summarize, capsule, timeline, scrub, graph, cluster, search, and
  scenario-draft.

The product distinction can be crisp:

Android CLI helps an agent work inside the Android development ecosystem.
Luotsi helps an agent prove, preserve, and replay what happened on real devices
and shared labs.

## Synergy ideas

### 1. Evidence-backed Journeys

Position Luotsi as the replay and evidence layer around Journey-like app flows.
The pitch: natural-language journeys are useful, but teams still need durable
proof when the run fails or when a CI result needs review.

Useful first slice:

- Document a workflow where an agent uses Android CLI/Journeys to define a core
  app experience, then uses Luotsi to run a reviewed scenario or inspect session
  against a real device.
- Make the handoff artifact-driven: screenshots, hierarchy captures, logcat,
  telemetry, JSON/JUnit reports, replay timeline, and replay graph become the
  stable review material.
- Use `luotsi replay open`, `replay capsule`, `replay graph`, and
  `replay scenario-draft` as the recovery path when the flow fails.

Why it fits: Luotsi already treats artifacts as the boundary between live device
control and post-run reasoning. That is the missing confidence layer for
Journey-style workflows in CI or a physical device lab.

### 2. Journey-to-Scenario Bridge

Treat Journey text as intent, not as a new Luotsi execution language. Luotsi can
eventually help convert a natural-language journey into a review-required JSON
scenario candidate, but the near-term framing should stay conservative.

Useful first slice:

- Add a Journey-style scenario authoring template that captures:
  app package/activity, calibrated device metadata, user goal, critical asserts,
  unsafe actions to avoid, and artifact expectations.
- Show an agent path from live exploration to a checked-in scenario:
  `inspect` or `discover` -> replay artifacts -> `replay scenario-draft` ->
  `scenario-validate` -> `run --dry-run` -> `run`.
- Keep generated scenario files review-required. Do not imply arbitrary natural
  language should execute unattended in Luotsi.

Why it fits: the current Luotsi scenario model is explicit JSON with validation,
metadata warnings, artifact capture, and CI reporting. That is a better
downstream target for a Journey than a second natural-language executor.

### 3. Android CLI Companion Skill

Create or document a Luotsi-focused agent skill that teaches agents when to use
Android CLI and when to use Luotsi.

Useful first slice:

- A skill or docs page can tell agents:
  use Android CLI for official Android docs, project scaffolding, build/run,
  Android Studio-backed analysis, Android skills, and Journey authoring.
- Use Luotsi for real-device ADB state, JSONL inspection, shared physical labs,
  scenario replay, failure capsules, artifact packaging, and CI evidence.
- Include combined playbooks such as:
  "Use `android studio analyze-file` to inspect the app code, then use
  `luotsi inspect` to observe the real device, then promote the result to a
  Luotsi scenario and replay bundle."

Why it fits: Android CLI's skill model creates a distribution shape that users
will recognize. Luotsi can meet that shape without moving policy or orchestration
onto the Android helper.

## Recommendation

Pursue "Evidence-backed Journeys" first, with "Journey-to-Scenario Bridge" as
the adjacent implementation story.

This is the best first move because it uses Luotsi's existing strengths instead
of creating a parallel Android CLI. It also gives Luotsi a sharper claim:
official Android agent tools can help create and run flows, while Luotsi makes
those flows inspectable, repeatable, replayable, and trustworthy on real devices
and shared labs.

Concrete next deliverable:

- Add a public docs page or website section named "Evidence-backed Android
  Journeys" or "Android CLI Journeys with Luotsi evidence".
- Include one workflow:
  `doctor` -> Android CLI/Journey intent -> `inspect` or `discover` ->
  `replay scenario-draft` -> `scenario-validate` -> `run` with artifacts ->
  `replay open`.
- Include one comparison table:
  Android CLI owns official Android project intelligence, skills, and Journey
  authoring; Luotsi owns real-device evidence, physical-lab governance, replay,
  and CI-grade artifact trails.

Do not start with a parser, import command, or new execution format. The fastest
validated move is product positioning plus a repeatable workflow that existing
Luotsi commands already support.

## Acceptance check for this sprint

- The brief exists at `docs/android-cli-journey-opportunity-brief.md`.
- It is source-linked to the Android CLI blog, Android CLI docs, and Journeys
  docs.
- It names the first recommended opportunity and the adjacent follow-up.
- It does not require product code changes.
- It keeps Android CLI complementary: Android CLI owns official Android tooling
  and agent skills; Luotsi owns real-device evidence, replay, lab governance,
  and CI-grade artifact trails.
