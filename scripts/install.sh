#!/usr/bin/env sh
set -eu

VERSION="latest"
INSTALL_ROOT=""
OWNER="digablesolutions"
REPOSITORY="luotsi"
SKIP_PATH_UPDATE=0
DRY_RUN=0

usage() {
    cat <<'EOF'
Luotsi installer

Usage:
    curl -fsSL https://github.com/digablesolutions/luotsi/releases/latest/download/luotsi-install.sh | sh
    curl -fsSL https://github.com/digablesolutions/luotsi/releases/latest/download/luotsi-install.sh | sh -s -- --version v1.2.3 --dry-run

Options:
  --version <tag>       Install a specific release tag. Defaults to the latest published release.
  --install-root <dir>  Override the install root. Defaults to ~/.local/share/luotsi.
  --owner <name>        GitHub owner. Defaults to digablesolutions.
  --repo <name>         GitHub repository. Defaults to luotsi.
  --skip-path-update    Do not modify shell profile files.
  --dry-run             Print the resolved install plan without downloading or writing files.
  --help                Show this help.
EOF
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --version)
            VERSION=${2:?missing value for --version}
            shift 2
            ;;
        --install-root)
            INSTALL_ROOT=${2:?missing value for --install-root}
            shift 2
            ;;
        --owner)
            OWNER=${2:?missing value for --owner}
            shift 2
            ;;
        --repo)
            REPOSITORY=${2:?missing value for --repo}
            shift 2
            ;;
        --skip-path-update)
            SKIP_PATH_UPDATE=1
            shift
            ;;
        --dry-run)
            DRY_RUN=1
            shift
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            printf 'Unknown argument: %s\n\n' "$1" >&2
            usage >&2
            exit 1
            ;;
    esac
done

require_command() {
    if ! command -v "$1" >/dev/null 2>&1; then
        printf 'Required command not found: %s\n' "$1" >&2
        exit 1
    fi
}

normalize_version() {
    case "$1" in
        ""|latest)
            printf ''
            ;;
        v*)
            printf '%s' "$1"
            ;;
        *)
            printf 'v%s' "$1"
            ;;
    esac
}

github_api() {
    url=$1
    if command -v curl >/dev/null 2>&1; then
        curl -fsSL -H 'Accept: application/vnd.github+json' -H 'User-Agent: luotsi-installer' -H 'X-GitHub-Api-Version: 2022-11-28' "$url"
        return
    fi

    if command -v wget >/dev/null 2>&1; then
        wget -qO- --header='Accept: application/vnd.github+json' --header='User-Agent: luotsi-installer' --header='X-GitHub-Api-Version: 2022-11-28' "$url"
        return
    fi

    printf 'Either curl or wget is required to contact GitHub Releases.\n' >&2
    exit 1
}

json_get_string() {
    key=$1
    json=$2

    if command -v jq >/dev/null 2>&1; then
        printf '%s' "$json" | jq -r --arg key "$key" '.[$key] // empty'
        return
    fi

    printf '%s' "$json" | tr '\n' ' ' | sed -n "s/.*\"$key\"[[:space:]]*:[[:space:]]*\"\([^\"]*\)\".*/\1/p" | head -n 1
}

download_file() {
    url=$1
    destination=$2
    if command -v curl >/dev/null 2>&1; then
        curl -fsSL "$url" -o "$destination"
        return
    fi

    wget -qO "$destination" "$url"
}

compute_sha256() {
    file=$1
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$file" | awk '{print tolower($1)}'
        return
    fi

    if command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "$file" | awk '{print tolower($1)}'
        return
    fi

    printf 'Either sha256sum or shasum is required to verify release checksums.\n' >&2
    exit 1
}

get_expected_hash() {
    checksum_file=$1
    asset_name=$2
    hash=$(awk -v target="$asset_name" '
        {
            name = $2
            sub(/^\*/, "", name)
            sub(/^\.\//, "", name)
            if (name == target) {
                print tolower($1)
                exit
            }
        }
    ' "$checksum_file")
    if [ -z "$hash" ]; then
        printf 'Could not find a SHA-256 entry for %s in %s\n' "$asset_name" "$checksum_file" >&2
        exit 1
    fi

    printf '%s' "$hash"
}

get_platform_rid() {
    os_name=$(uname -s)
    architecture=$(uname -m)

    case "$os_name" in
        Darwin)
            case "$architecture" in
                x86_64|amd64)
                    printf 'osx-x64'
                    ;;
                arm64|aarch64)
                    printf 'osx-arm64'
                    ;;
                *)
                    printf 'Unsupported macOS architecture: %s\n' "$architecture" >&2
                    exit 1
                    ;;
            esac
            ;;
        Linux)
            case "$architecture" in
                x86_64|amd64)
                    printf 'linux-x64'
                    ;;
                *)
                    printf 'Unsupported Linux architecture for published Luotsi releases: %s\n' "$architecture" >&2
                    exit 1
                    ;;
            esac
            ;;
        *)
            printf 'Unsupported host OS: %s\n' "$os_name" >&2
            exit 1
            ;;
    esac
}

resolve_release_tag() {
    requested_tag=$1
    if [ -n "$requested_tag" ]; then
        printf '%s' "$requested_tag"
        return
    fi

    if ! json=$(github_api "https://api.github.com/repos/$OWNER/$REPOSITORY/releases/latest"); then
        printf 'No published stable GitHub Releases were found for %s/%s.\n' "$OWNER" "$REPOSITORY" >&2
        exit 1
    fi

    tag=$(json_get_string tag_name "$json")
    if [ -z "$tag" ]; then
        printf 'No published stable GitHub Releases were found for %s/%s.\n' "$OWNER" "$REPOSITORY" >&2
        exit 1
    fi

    printf '%s' "$tag"
}

resolve_install_root() {
    if [ -n "$INSTALL_ROOT" ]; then
        printf '%s' "$INSTALL_ROOT"
        return
    fi

    if [ -z "${HOME:-}" ]; then
        printf 'HOME is not set. Pass --install-root explicitly.\n' >&2
        exit 1
    fi

    printf '%s/.local/share/luotsi' "$HOME"
}

path_contains_dir() {
    target=$1
    old_ifs=$IFS
    IFS=:
    for entry in ${PATH:-}; do
        if [ "$entry" = "$target" ]; then
            IFS=$old_ifs
            return 0
        fi
    done
    IFS=$old_ifs
    return 1
}

resolve_profile_file() {
    shell_name=$(basename "${SHELL:-sh}")
    case "$shell_name" in
        bash)
            if [ -f "$HOME/.bashrc" ]; then
                printf '%s/.bashrc' "$HOME"
            elif [ -f "$HOME/.bash_profile" ]; then
                printf '%s/.bash_profile' "$HOME"
            else
                printf '%s/.bashrc' "$HOME"
            fi
            ;;
        zsh)
            if [ -f "$HOME/.zshrc" ]; then
                printf '%s/.zshrc' "$HOME"
            else
                printf '%s/.zshrc' "$HOME"
            fi
            ;;
        *)
            printf '%s/.profile' "$HOME"
            ;;
    esac
}

ensure_profile_path() {
    profile_file=$1
    bin_dir=$2
    marker_start='# >>> luotsi >>>'

    if [ -f "$profile_file" ] && grep -F "$marker_start" "$profile_file" >/dev/null 2>&1; then
        return 1
    fi

    {
        printf '\n%s\n' "$marker_start"
        printf 'export PATH="%s:$PATH"\n' "$bin_dir"
        printf '# <<< luotsi <<<\n'
    } >> "$profile_file"

    return 0
}

write_command_shim() {
    shim_path=$1
    cat > "$shim_path" <<'EOF'
#!/usr/bin/env sh
set -eu
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec "$script_dir/../current/luotsi" "$@"
EOF
    chmod +x "$shim_path"
}

write_manifest() {
    manifest_path=$1
    install_dir=$2
    bin_dir=$3
    command_path=$4
    tag=$5
    rid=$6
    archive_name=$7
    archive_url=$8
    checksum_url=$9
    installed_at_utc=$(date -u +'%Y-%m-%dT%H:%M:%SZ')

    cat > "$manifest_path" <<EOF
{
  "schema": "luotsi-install.v1",
  "tool": "luotsi",
  "tag": "$tag",
  "version": "${tag#v}",
  "rid": "$rid",
  "install_root": "$install_dir",
  "current_root": "$install_dir/current",
  "bin_directory": "$bin_dir",
  "command_path": "$command_path",
  "archive_name": "$archive_name",
  "archive_url": "$archive_url",
  "checksum_url": "$checksum_url",
  "installed_at_utc": "$installed_at_utc"
}
EOF
}

require_command tar

REQUESTED_TAG=$(normalize_version "$VERSION")
RESOLVED_TAG=$(resolve_release_tag "$REQUESTED_TAG")
RID=$(get_platform_rid)
RESOLVED_VERSION=${RESOLVED_TAG#v}
RESOLVED_INSTALL_ROOT=$(resolve_install_root)
if [ "$SKIP_PATH_UPDATE" -eq 0 ] && [ -z "${HOME:-}" ]; then
    printf 'HOME is not set; skipping PATH update.\n'
    SKIP_PATH_UPDATE=1
fi

BIN_DIR="$RESOLVED_INSTALL_ROOT/bin"
CURRENT_DIR="$RESOLVED_INSTALL_ROOT/current"
PREVIOUS_DIR="$RESOLVED_INSTALL_ROOT/previous"
COMMAND_PATH="$BIN_DIR/luotsi"
MANIFEST_PATH="$RESOLVED_INSTALL_ROOT/install.json"
ARCHIVE_NAME="luotsi-cli-$RESOLVED_VERSION-$RID.tar.gz"
ARCHIVE_URL="https://github.com/$OWNER/$REPOSITORY/releases/download/$RESOLVED_TAG/$ARCHIVE_NAME"
CHECKSUM_URL="https://github.com/$OWNER/$REPOSITORY/releases/download/$RESOLVED_TAG/SHA256SUMS"

printf 'Luotsi installer\n'
printf '  Release:      %s\n' "$RESOLVED_TAG"
printf '  Runtime:      %s\n' "$RID"
printf '  Install root: %s\n' "$RESOLVED_INSTALL_ROOT"
printf '  Command dir:  %s\n' "$BIN_DIR"
printf '  Asset:        %s\n' "$ARCHIVE_NAME"

if [ "$DRY_RUN" -eq 1 ]; then
    printf 'Dry run only. No files were downloaded or changed.\n'
    exit 0
fi

temp_root=$(mktemp -d "${TMPDIR:-/tmp}/luotsi-install.XXXXXX")
archive_path="$temp_root/$ARCHIVE_NAME"
checksum_path="$temp_root/SHA256SUMS"
extract_dir="$temp_root/payload"
payload_dir="$temp_root/current"

cleanup() {
    rm -rf "$temp_root"
}
trap cleanup EXIT INT TERM

mkdir -p "$RESOLVED_INSTALL_ROOT" "$BIN_DIR" "$extract_dir"

printf 'Downloading release archive...\n'
download_file "$ARCHIVE_URL" "$archive_path"
download_file "$CHECKSUM_URL" "$checksum_path"

expected_hash=$(get_expected_hash "$checksum_path" "$ARCHIVE_NAME")
actual_hash=$(compute_sha256 "$archive_path")
if [ "$expected_hash" != "$actual_hash" ]; then
    printf 'SHA-256 mismatch for %s. Expected %s but got %s\n' "$ARCHIVE_NAME" "$expected_hash" "$actual_hash" >&2
    exit 1
fi

tar -xzf "$archive_path" -C "$extract_dir"

payload_source="$extract_dir"
if [ ! -x "$payload_source/luotsi" ]; then
    first_dir=$(find "$extract_dir" -mindepth 1 -maxdepth 1 -type d | head -n 1)
    if [ -n "$first_dir" ] && [ -x "$first_dir/luotsi" ]; then
        payload_source=$first_dir
    else
        printf 'The release archive did not contain a luotsi executable at its root.\n' >&2
        exit 1
    fi
fi

mv "$payload_source" "$payload_dir"

if [ -e "$PREVIOUS_DIR" ]; then
    rm -rf "$PREVIOUS_DIR"
fi

if [ -e "$CURRENT_DIR" ]; then
    mv "$CURRENT_DIR" "$PREVIOUS_DIR"
fi

if ! mv "$payload_dir" "$CURRENT_DIR"; then
    if [ ! -e "$CURRENT_DIR" ] && [ -e "$PREVIOUS_DIR" ]; then
        mv "$PREVIOUS_DIR" "$CURRENT_DIR"
    fi
    printf 'Failed to install Luotsi into %s\n' "$CURRENT_DIR" >&2
    exit 1
fi

if [ -e "$PREVIOUS_DIR" ]; then
    rm -rf "$PREVIOUS_DIR"
fi

write_command_shim "$COMMAND_PATH"
write_manifest "$MANIFEST_PATH" "$RESOLVED_INSTALL_ROOT" "$BIN_DIR" "$COMMAND_PATH" "$RESOLVED_TAG" "$RID" "$ARCHIVE_NAME" "$ARCHIVE_URL" "$CHECKSUM_URL"

path_updated=0
profile_file=''
if [ "$SKIP_PATH_UPDATE" -eq 0 ]; then
    if path_contains_dir "$BIN_DIR"; then
        path_updated=0
    else
        profile_file=$(resolve_profile_file)
        if ensure_profile_path "$profile_file" "$BIN_DIR"; then
            path_updated=1
        fi
    fi
fi

printf 'Install complete.\n'
printf '  Command: %s\n' "$COMMAND_PATH"
if [ "$SKIP_PATH_UPDATE" -eq 1 ]; then
    printf '  PATH was not modified. Add %s to your shell PATH to run luotsi.\n' "$BIN_DIR"
elif [ "$path_updated" -eq 1 ]; then
    printf '  Updated %s. Open a new terminal before running luotsi.\n' "$profile_file"
else
    printf '  PATH already contains %s.\n' "$BIN_DIR"
fi