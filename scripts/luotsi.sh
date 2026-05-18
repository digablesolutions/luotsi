#!/usr/bin/env sh
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_root=$(dirname -- "$script_dir")

exec dotnet run --project "$repo_root/Luotsi.Cli" -- "$@"
