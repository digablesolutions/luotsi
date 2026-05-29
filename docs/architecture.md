# Architecture Overview

Luotsi is a .NET CLI for driving a real Android device through `adb`,
capturing artifacts by default, and optionally opening a live mirrored view of
the device through the built-in `view` session.

The current architecture is split across three long-lived concerns:

- command execution for one-shot CLI commands
- inspect/view sessions for line-oriented interactive flows
- device-specific Android runtime work behind host abstractions

## Top-level flow

```mermaid
flowchart LR
  User[Operator or Agent] --> CLI[Luotsi]
    CLI --> App[App / CliOptions / Help]
    App --> Commands[One-shot commands]
    App --> Inspect[InspectSession JSONL]
    App --> View[ViewSession human or JSONL]

    Commands --> Host[IDeviceHost]
    Inspect --> Host
    View --> Bootstrap[Android view bootstrap]
    View --> Renderer[SDL3 renderer]
    View --> Backend[Libav decode backend]
    View --> Share[TCP share relay optional]

    Host --> Adb[adb / device shell]
    Bootstrap --> Adb
    Bootstrap --> Helper[Android helper process]
    Helper --> Stream[H.264 packet stream]
    Stream --> Backend
    Backend --> Renderer
    Backend --> Share
    Host --> Artifacts[ArtifactSession]
    View --> Artifacts
    Commands --> Artifacts
    Inspect --> Artifacts
```

## High-level responsibilities

### Command layer

`Luotsi.Cli/Cli/` owns argument parsing, command dispatch, help text, JSON
envelope formatting for one-shot commands, and console/output formatting for
long-lived sessions.

### Device host layer

`Luotsi.Cli/Infrastructure/` and `Luotsi.Cli/Hosts/Android/` own host-side
device semantics. The CLI keeps using typed host actions such as tap, text
entry, log reads, screen-state capture, and telemetry collection rather than
inventing a second device-control path.

### Scenario and telemetry layers

`Luotsi.Cli/Scenarios/` executes scenario JSON files through the same host
abstractions. `Luotsi.Cli/Telemetry/` parses semantic telemetry from logcat so
runtime commands and scenarios share the same higher-value oracle path.

### View layer

`Luotsi.Cli/View/` owns the built-in live mirror. The host bootstraps an
Android helper over `adb`, selects the requested capture backend, reads the
private packet stream from a localhost tunnel, decodes H.264 through native
libav, and presents decoded BGRA frames through SDL3. With
`--capture-backend auto`, the host prefers MediaProjection and falls back to
`screenrecord` if helper startup or consent fails during bring-up.

The same view session can optionally relay packets to observers via
`--share-bind`. Observer sessions (`--join-share`) are intentionally
read-only at the command/input layer.

## View runtime data path

```mermaid
sequenceDiagram
    participant User as Operator
    participant CLI as ViewSession
    participant Bootstrap as AndroidViewBootstrap
    participant Device as Android helper
    participant Stream as Localhost socket
    participant Share as Optional share relay
    participant Backend as LibavViewBackend
    participant Window as SDL3 window
    participant Host as IDeviceHost

    User->>CLI: view --device <serial> --decoder ffmpeg --codec h264
    CLI->>Bootstrap: StartAsync(...)
    Bootstrap->>Device: install helper app or push helper / forward tunnel / start consent or helper process
    Device-->>CLI: startup header (codec, size, session)
    Device-->>Stream: config + frame packets
    CLI->>Backend: RunAsync(packet stream)
    CLI->>Share: Publish stream packets (optional)
    Backend->>Window: PresentAsync(decoded BGRA frame)
    User->>Window: click
    Window->>CLI: pointer event
    CLI->>Host: TapPointAsync(relative coords)
    CLI-->>User: human progress or view_started / view_ended JSONL events
    Note over Share,Backend: Late-joining observers receive cached bootstrap packets\n(config plus latest keyframe) so decoding can start immediately.
```

## Native dependency story

- The current live decoder is native libav via `FFmpeg.AutoGen`.
- `LUOTSI_FFMPEG_ROOT` can point at a directory containing native FFmpeg
  shared libraries.
- If that environment variable is absent, the runtime probes bundled `ffmpeg`
  directories relative to the repo or published app, then the app base
  directory, and finally the process path.
- `ffmpeg/download-ffmpeg.ps1` is the DX helper for staging host-native shared
  libraries into `ffmpeg/bin`.
- The SDL3 runtime is already included in current macOS publishes; the FFmpeg
  shared libraries are still external and must be staged separately.

## Current platform shape

- Windows: fully active local development path, including live SDL3 rendering.
- macOS: `osx-arm64` and `osx-x64` cross-publishes now succeed and include
  `libSDL3.dylib`, but live runtime validation on a real macOS host is still an
  active slice rather than a fully confirmed path.
- Linux: packaging and probing are in place for the same libav + SDL3 shape,
  but live operator validation still needs dedicated coverage.

## Source map

- `Luotsi.Cli/Cli/`
- `Luotsi.Cli/Hosts/Android/`
- `Luotsi.Cli/Hosts/Android/View/`
- `Luotsi.Cli/Scenarios/`
- `Luotsi.Cli/Telemetry/`
- `Luotsi.Cli/View/`
- `Luotsi.Cli/Artifacts/`
