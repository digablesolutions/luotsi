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
  if [[ -z "${GITHUB_STEP_SUMMARY:-}" ]]; then
    return 0
  fi

  {
    printf '\n## Luotsi Run Summary\n\n'
    if [[ -f "$summary_path" ]]; then
      cat "$summary_path"
    else
      printf 'The durable packet was not available in `%s`.\n\n' "$summary_path"
      printf 'Run these commands against the uploaded artifact root:\n\n'
      printf '```bash\n'
      printf 'luotsi replay packet --artifacts %q\n' "$artifacts_dir"
      printf 'luotsi replay packet --artifacts %q --check\n' "$artifacts_dir"
      printf '```\n'
    fi
    printf '\n'
  } >> "$GITHUB_STEP_SUMMARY"
}

write_and_check_run_summary_packet() {
  if [[ ! -d "$artifacts_dir" ]]; then
    return 0
  fi

  local packet_exit_code=0
  set +e
  run_luotsi replay packet --artifacts "$artifacts_dir"
  packet_exit_code=$?
  if [[ "$packet_exit_code" -eq 0 ]]; then
    run_luotsi replay packet --artifacts "$artifacts_dir" --check
    packet_exit_code=$?
  fi
  set -e
  append_run_summary_to_github_step_summary
  return "$packet_exit_code"
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
run_exit_code=0
set +e
run_luotsi run \
  --path "$scenario_path" \
  --device-query "$device_query" \
  --claim-device \
  --owner "$owner" \
  --ttl-sec "$ttl_sec" \
  --report-junit "$junit_path" \
  --artifacts "$artifacts_dir"
run_exit_code=$?
set -e

packet_exit_code=0
write_and_check_run_summary_packet || packet_exit_code=$?
if [[ "$run_exit_code" -ne 0 ]]; then
  exit "$run_exit_code"
fi

exit "$packet_exit_code"
