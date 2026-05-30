# Portable Physical Lab CI

This workflow pack lets a CI runner use real Android devices through Luotsi
without making GitHub Actions the only integration point. The reusable contract
is the script pair under `eng/ci/`; the GitHub Actions workflow is one adapter
over those scripts.

Use this when a runner can reach devices through adb, either because the devices
are attached to the runner host or because the runner can connect to a host adb
server or adb-over-TCP target.

## Entry scripts

The portable entry points are:

- `../eng/ci/run-lab-scenarios.sh`
- `../eng/ci/run-lab-scenarios.ps1`

Both scripts read the same environment variables:

| Variable | Default | Purpose |
|---|---|---|
| `LUOTSI_DEVICE_QUERY` | `state=online,type=physical,availability=available` | Lab selector passed to `luotsi lab` and `luotsi run`. |
| `LUOTSI_SCENARIO_PATH` | `examples/scenarios` | Scenario file or directory to validate and run. |
| `LUOTSI_OWNER` | CI-derived owner, or `ci-local` | Lease owner shown by `luotsi lab leases`. |
| `LUOTSI_TTL_SEC` | `1800` | Device claim lease lifetime. |
| `LUOTSI_ARTIFACTS_DIR` | `artifacts/luotsi-lab` | Run artifact root to upload from CI. |
| `LUOTSI_JUNIT_PATH` | `<artifacts>/junit.xml` | JUnit report path. |
| `LUOTSI_BIN` | `luotsi` | Luotsi executable on the runner path. |
| `LUOTSI_DRY_RUN` | `false` | Validate scripts and scenarios without claiming a device. |

Normal runs execute:

```bash
luotsi version
luotsi lab status --device-query "$LUOTSI_DEVICE_QUERY"
luotsi lab plan --device-query "$LUOTSI_DEVICE_QUERY"
luotsi scenario-validate --path "$LUOTSI_SCENARIO_PATH"
luotsi run --path "$LUOTSI_SCENARIO_PATH" --device-query "$LUOTSI_DEVICE_QUERY" --claim-device --owner "$LUOTSI_OWNER" --ttl-sec "$LUOTSI_TTL_SEC" --report-junit "$LUOTSI_JUNIT_PATH" --artifacts "$LUOTSI_ARTIFACTS_DIR"
luotsi replay summarize --artifacts "$LUOTSI_ARTIFACTS_DIR"
```

Dry runs execute `scenario-validate` and `run --dry-run`, then stop before lab
selection or device claiming.

## GitHub Actions adapter

The workflow adapter lives at `../.github/workflows/android-lab-scenarios.yml`.
It is manual by default and routes to a trusted self-hosted runner with:

```yaml
runs-on: [self-hosted, luotsi-lab, android-device]
```

Use runner labels or runner groups to restrict which repositories can reach the
lab host. Keep this workflow off untrusted pull requests unless the runner is
isolated enough to handle arbitrary code execution.

## Generic CI

Any CI can reuse the scripts if it can:

- run Bash or PowerShell
- install or expose a `luotsi` executable
- see adb devices from the job environment
- upload `LUOTSI_ARTIFACTS_DIR` as a job artifact

Example Bash job body:

```bash
export LUOTSI_DEVICE_QUERY="state=online,type=physical,availability=available,model=Pixel_9"
export LUOTSI_SCENARIO_PATH="examples/scenarios"
export LUOTSI_OWNER="${CI_JOB_ID:-local-lab}"
export LUOTSI_ARTIFACTS_DIR="artifacts/luotsi-lab"
export LUOTSI_JUNIT_PATH="artifacts/luotsi-lab/junit.xml"
bash ./eng/ci/run-lab-scenarios.sh
```

For a device-free check of the integration layer:

```bash
LUOTSI_DRY_RUN=true bash ./eng/ci/run-lab-scenarios.sh
```

## Docker

The recommended Docker model is host ADB, not privileged USB passthrough.
Run Luotsi inside the container, but connect it to a host adb server or to
devices already exposed over TCP.

The job should mount:

- the repository or scenario directory
- an artifact output directory
- any Luotsi install or cache paths your image does not bake in

The container should receive the same `LUOTSI_*` variables as a normal CI job.
If it talks to a host adb server, configure the adb client for that topology,
for example through `ADB_SERVER_SOCKET` or a host name such as
`host.docker.internal`.

Privileged USB passthrough is an advanced fallback because it couples the
container to host kernel, udev, and USB behavior. Keep the device lease and
ownership model in Luotsi either way.

## Related docs

- [Scenario playbooks](scenarios.md)
- [Replay and artifacts](view-session.md)
- [Command reference](commands.md)
