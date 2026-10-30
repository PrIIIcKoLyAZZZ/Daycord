#!/bin/bash
set -euo pipefail

if [[ "$#" -ne 3 ]]; then
    echo "Usage: create-windows-installer.sh <publish-dir> <version> <output.exe>" >&2
    exit 2
fi

PUBLISH_DIR="$1"
APP_VERSION="$2"
OUTPUT_FILE="$3"

if [[ "$(uname -s)" != "Darwin" ]]; then
    echo "ERROR: This release script requires macOS." >&2
    exit 1
fi

if [[ ! -d "$PUBLISH_DIR" ]]; then
    echo "ERROR: Windows publish directory not found: $PUBLISH_DIR" >&2
    exit 1
fi

PUBLISH_DIR="$(cd "$PUBLISH_DIR" && pwd -P)"

if [[ ! "$APP_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "ERROR: Expected semantic version such as 0.1.0." >&2
    exit 1
fi

# Resolve Homebrew's NSIS compiler even when Rider
# does not inherit the shell's PATH.
if command -v makensis >/dev/null 2>&1; then
    MAKENSIS="$(command -v makensis)"
elif [[ -x /opt/homebrew/bin/makensis ]]; then
    MAKENSIS="/opt/homebrew/bin/makensis"
elif [[ -x /usr/local/bin/makensis ]]; then
    MAKENSIS="/usr/local/bin/makensis"
else
    echo "ERROR: NSIS compiler not found." >&2
    echo "Install it with: brew install makensis" >&2
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd -P)"
NSIS_SCRIPT="$SCRIPT_DIR/windows/daycord-installer.nsi"

if [[ ! -f "$NSIS_SCRIPT" ]]; then
    echo "ERROR: Missing NSIS script: $NSIS_SCRIPT" >&2
    exit 1
fi

# Verify essential .NET publication files.
for required_file in \
    "Daycord.Desktop.exe" \
    "Daycord.Desktop.dll" \
    "Daycord.Desktop.runtimeconfig.json" \
    "Daycord.Core.dll" \
    "Daycord.Infrastructure.dll"
do
    if [[ ! -f "$PUBLISH_DIR/$required_file" ]]; then
        echo "ERROR: Missing published file: $required_file" >&2
        exit 1
    fi
done

# Verify that the host belongs to the target OS.
if ! file "$PUBLISH_DIR/Daycord.Desktop.exe" | grep -q "PE32"; then
    echo "ERROR: Daycord.Desktop.exe is not a Windows PE executable." >&2
    exit 1
fi

# Reject suspicious content and accidental user data.
UNEXPECTED_FILE="$(find "$PUBLISH_DIR" -type f \( \
    -iname '*.db' -o \
    -iname '*.db-wal' -o \
    -iname '*.db-shm' -o \
    -iname '*.sqlite' -o \
    -iname '*.sqlite3' -o \
    -iname '*.daycordbak' -o \
    -iname '*.xlsx' -o \
    -iname '*.pfx' -o \
    -iname '*.p12' \
    \) -print -quit)"

if [[ -n "$UNEXPECTED_FILE" ]]; then
    echo "ERROR: Unexpected file in Windows publish output:" >&2
    echo "$UNEXPECTED_FILE" >&2
    exit 1
fi

# Do not silently include symlinks in the installer.
SYMLINK="$(find "$PUBLISH_DIR" -type l -print -quit)"

if [[ -n "$SYMLINK" ]]; then
    echo "ERROR: Symbolic links are not expected in Windows publish output:" >&2
    echo "$SYMLINK" >&2
    exit 1
fi

OUTPUT_DIR="$(dirname "$OUTPUT_FILE")"
mkdir -p "$OUTPUT_DIR"

OUTPUT_FILE="$(cd "$OUTPUT_DIR" && pwd -P)/$(basename "$OUTPUT_FILE")"

TEMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/daycord-nsis.XXXXXX")"

cleanup() {
    rm -rf "$TEMP_DIR"
}

trap cleanup EXIT

UNINSTALL_MANIFEST="$TEMP_DIR/uninstall-files.nsh"
TEMP_INSTALLER="$TEMP_DIR/Daycord-Setup.exe"

: > "$UNINSTALL_MANIFEST"

# Convert source-relative paths into Windows paths
# and reject characters that would break NSIS syntax.
validate_path() {
    local value="$1"

    case "$value" in
        *'!'*|*'$'*|*'"'*|*$'\n'*|*$'\r'*)
            echo "ERROR: Unsupported filename for NSIS: $value" >&2
            exit 1
            ;;
    esac
}

# Generate an explicit uninstall list.
# It deletes only files included in this publication.
while IFS= read -r -d '' source_file; do
    relative="${source_file#"$PUBLISH_DIR"/}"

    validate_path "$relative"

    windows_path="${relative//\//\\}"

    printf 'Delete "$INSTDIR\\%s"\n' \
        "$windows_path" >> "$UNINSTALL_MANIFEST"

done < <(find "$PUBLISH_DIR" -type f -print0)

# Remove empty subdirectories, deepest first.
# The installation root is handled by the NSIS script.
while IFS= read -r -d '' source_dir; do
    relative="${source_dir#"$PUBLISH_DIR"/}"

    validate_path "$relative"

    windows_path="${relative//\//\\}"

    printf 'RMDir "$INSTDIR\\%s"\n' \
        "$windows_path" >> "$UNINSTALL_MANIFEST"

done < <(find "$PUBLISH_DIR" -mindepth 1 -depth -type d -print0)

echo "Creating Windows installer..."
echo "Version: $APP_VERSION"
echo "Source: $PUBLISH_DIR"

"$MAKENSIS" -V3 \
    "-DAPP_VERSION=$APP_VERSION" \
    "-DPUBLISH_DIR=$PUBLISH_DIR" \
    "-DOUTPUT_FILE=$TEMP_INSTALLER" \
    "-DUNINSTALL_MANIFEST=$UNINSTALL_MANIFEST" \
    "$NSIS_SCRIPT"

if [[ ! -s "$TEMP_INSTALLER" ]]; then
    echo "ERROR: NSIS did not produce an installer." >&2
    exit 1
fi

# Only replace an existing installer after compilation succeeds.
mv -f "$TEMP_INSTALLER" "$OUTPUT_FILE"

echo "Windows installer created:"
echo "$OUTPUT_FILE"

echo "Signing status: unsigned (technical build)"

if command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$OUTPUT_FILE"
fi