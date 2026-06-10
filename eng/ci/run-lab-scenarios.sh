#!/usr/bin/env bash
set -euo pipefail

luotsi_bin="${LUOTSI_BIN:-luotsi}"
device_query="${LUOTSI_DEVICE_QUERY:-state=online,type=physical,availability=available}"
scenario_path="${LUOTSI_SCENARIO_PATH:-examples/scenarios}"
ttl_sec="${LUOTSI_TTL_SEC:-1800}"
artifacts_dir="${LUOTSI_ARTIFACTS_DIR:-artifacts/luotsi-lab}"
junit_path="${LUOTSI_JUNIT_PATH:-${artifacts_dir%/}/junit.xml}"
dry_run="${LUOTSI_DRY_RUN:-false}"

default_owner="ci-local"
if [[ -n "${GITHUB_RUN_ID:-}" ]]; then
  default_owner="gh-actions-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT:-1}"
elif [[ -n "${BUILD_BUILDID:-}" ]]; then
  default_owner="azure-pipelines-${BUILD_BUILDID}"
elif [[ -n "${CI_PIPELINE_ID:-}" ]]; then
  default_owner="ci-pipeline-${CI_PIPELINE_ID}"
fi
owner="${LUOTSI_OWNER:-$default_owner}"

run_luotsi() {
  printf '+ %q' "$luotsi_bin"
  printf ' %q' "$@"
  printf '\n'
  "$luotsi_bin" "$@"
}

append_run_summary_to_github_step_summary() {
  local summary_path="${artifacts_dir%/}/run-summary.md"
  if [[ -z "${GITHUB_STEP_SUMMARY:-}" || ! -f "$summary_path" ]]; then
    return 0
  fi

  {
    printf '\n## Luotsi Run Summary\n\n'
    cat "$summary_path"
    printf '\n'
  } >> "$GITHUB_STEP_SUMMARY"
}

is_true() {
  case "$(printf '%s' "$1" | tr '[:upper:]' '[:lower:]')" in
    1|true|yes|y|on) return 0 ;;
    *) return 1 ;;
  esac
}

mkdir -p "$artifacts_dir"
mkdir -p "$(dirname "$junit_path")"

run_luotsi version

if is_true "$dry_run"; then
  run_luotsi scenario-validate --path "$scenario_path"
  run_luotsi run --path "$scenario_path" --dry-run --artifacts "$artifacts_dir"
  exit 0
fi

run_luotsi lab status --device-query "$device_query"
run_luotsi lab plan --device-query "$device_query"
run_luotsi scenario-validate --path "$scenario_path"
run_luotsi run \
  --path "$scenario_path" \
  --device-query "$device_query" \
  --claim-device \
  --owner "$owner" \
  --ttl-sec "$ttl_sec" \
  --report-junit "$junit_path" \
  --artifacts "$artifacts_dir"
run_luotsi replay open --artifacts "$artifacts_dir" --dry-run --write-json --write-markdown
append_run_summary_to_github_step_summary
