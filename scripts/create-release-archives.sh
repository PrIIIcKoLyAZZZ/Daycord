#!/bin/bash
set -euo pipefail

if [[ "$#" -ne 4 ]]; then
    echo "Usage: create-release-archives.sh <osx-arm64|win-x64> <version> <publish-dir> <artifacts-dir>" >&2
    exit 2
fi

RID="$1"
VERSION="$2"
PUBLISH_DIR="$3"
ARTIFACT_DIR="$4"

if [[ "$(uname -s)" != "Darwin" ]]; then
    echo "ERROR: This release-packaging script must run on macOS." >&2
    exit 1
fi

case "$RID" in
    osx-arm64|win-x64) ;;
    *)
        echo "ERROR: Unsupported RuntimeIdentifier: $RID" >&2
        exit 2
        ;;
esac

if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "ERROR: Expected a version like 0.1.0, got: $VERSION" >&2
    exit 2
fi

for tool in ditto unzip shasum mktemp find awk; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        echo "ERROR: Required tool not found: $tool" >&2
        exit 1
    fi
done

if [[ ! -d "$PUBLISH_DIR" ]]; then
    echo "ERROR: Publish directory does not exist: $PUBLISH_DIR" >&2
    exit 1
fi

PUBLISH_DIR="$(cd "$PUBLISH_DIR" && pwd -P)"

mkdir -p "$ARTIFACT_DIR"
ARTIFACT_DIR="$(cd "$ARTIFACT_DIR" && pwd -P)"

# Never place release archives inside their source directory.
case "$ARTIFACT_DIR/" in
    "$PUBLISH_DIR/"*)
        echo "ERROR: Artifact directory must not be inside the publish directory." >&2
        exit 1
        ;;
esac

MAC_ZIP="Daycord-${VERSION}-osx-arm64.zip"
MAC_DMG="Daycord-${VERSION}-osx-arm64.dmg"

WIN_ZIP="Daycord-${VERSION}-win-x64.zip"
WIN_SETUP="Daycord-Setup-${VERSION}-win-x64.exe"

if [[ "$RID" == "osx-arm64" ]]; then
    SOURCE="$PUBLISH_DIR/Daycord.app"
    ARCHIVE_NAME="$MAC_ZIP"

    if [[ ! -x "$SOURCE/Contents/MacOS/Daycord.Desktop" ||
          ! -f "$SOURCE/Contents/Info.plist" ||
          ! -f "$SOURCE/Contents/Resources/icon.icns" ]]; then
        echo "ERROR: Daycord.app is missing its executable or bundle resources." >&2
        exit 1
    fi

    if [[ ! -s "$ARTIFACT_DIR/$MAC_DMG" ]]; then
        echo "ERROR: The macOS DMG is missing; publish it before creating ZIP." >&2
        exit 1
    fi

    EXPECTED_ENTRY="Daycord.app/Contents/MacOS/Daycord.Desktop"

else
    SOURCE="$PUBLISH_DIR"
    ARCHIVE_NAME="$WIN_ZIP"

    for required_file in \
        Daycord.Desktop.exe \
        Daycord.Desktop.dll \
        Daycord.Desktop.runtimeconfig.json
    do
        if [[ ! -s "$SOURCE/$required_file" ]]; then
            echo "ERROR: Missing Windows publish file: $required_file" >&2
            exit 1
        fi
    done

    if [[ ! -s "$ARTIFACT_DIR/$WIN_SETUP" ]]; then
        echo "ERROR: Windows Setup.exe is missing; build it before creating ZIP." >&2
        exit 1
    fi

    if [[ -n "$(find "$SOURCE" -type l -print -quit)" ]]; then
        echo "ERROR: Windows publish folder contains a symbolic link." >&2
        exit 1
    fi

    EXPECTED_ENTRY="Daycord.Desktop.exe"
fi

# Reject unexpected metadata directories in published content.
UNEXPECTED_DIR="$(find "$SOURCE" -type d -name '__MACOSX' -print -quit)"

if [[ -n "$UNEXPECTED_DIR" ]]; then
    echo "ERROR: Unexpected __MACOSX directory in publish output:" >&2
    echo "  $UNEXPECTED_DIR" >&2
    exit 1
fi

# Reject private data, test output and unwanted metadata.
UNEXPECTED_FILE="$(find "$SOURCE" -type f \( \
    -iname '*.db' -o \
    -iname '*.db-shm' -o \
    -iname '*.db-wal' -o \
    -iname '*.sqlite' -o \
    -iname '*.sqlite3' -o \
    -iname '*.sqlite-shm' -o \
    -iname '*.sqlite-wal' -o \
    -iname '*.bak' -o \
    -iname '*.backup' -o \
    -iname '*.daycordbak' -o \
    -iname '*.xlsx' -o \
    -iname '*.pfx' -o \
    -iname '*.p12' -o \
    -iname '*.key' -o \
    -iname '*.log' -o \
    -iname '*.zip' -o \
    -iname '*.dmg' -o \
    -name '.DS_Store' -o \
    -name '._*' \
    \) -print -quit)"

if [[ -n "$UNEXPECTED_FILE" ]]; then
    echo "ERROR: Unexpected test, private, or packaging file in publish output:" >&2
    echo "  $UNEXPECTED_FILE" >&2
    exit 1
fi

# Build in a temporary directory to protect existing artifacts.
TEMP_DIR="$(mktemp -d "$ARTIFACT_DIR/.daycord-release.XXXXXX")"

cleanup() {
    rm -rf "$TEMP_DIR"
}

trap cleanup EXIT

TEMP_ZIP="$TEMP_DIR/$ARCHIVE_NAME"

# Disable macOS resource forks, extended attributes,
# ACL metadata and quarantine metadata.
#
# Do not use --sequesterRsrc.
# Preserve ordinary file permissions and symbolic links.

if [[ "$RID" == "osx-arm64" ]]; then

    COPYFILE_DISABLE=1 ditto -c -k \
        --norsrc \
        --noextattr \
        --noacl \
        --noqtn \
        --keepParent \
        "$SOURCE" \
        "$TEMP_ZIP"

else

    COPYFILE_DISABLE=1 ditto -c -k \
        --norsrc \
        --noextattr \
        --noacl \
        --noqtn \
        "$SOURCE" \
        "$TEMP_ZIP"

fi

# Verify archive integrity.
unzip -tqq "$TEMP_ZIP" >/dev/null

# Verify the required executable is included.
if ! unzip -Z -1 "$TEMP_ZIP" | awk -v wanted="$EXPECTED_ENTRY" '
    $0 == wanted { found = 1 }
    END { exit !found }
'; then
    echo "ERROR: Archive does not contain the expected entry: $EXPECTED_ENTRY" >&2
    exit 1
fi

# Check archive contents for macOS service metadata.
#
# Separate awk rules avoid multiline expression parsing issues.
# Read the complete listing to avoid broken-pipe errors.
BAD_ENTRY="$(unzip -Z -1 "$TEMP_ZIP" | awk '
    /(^|\/)__MACOSX(\/|$)/ {
        if (!found) {
            print
            found = 1
        }
    }

    /(^|\/)\._[^\/]*$/ {
        if (!found) {
            print
            found = 1
        }
    }

    /(^|\/)\.DS_Store$/ {
        if (!found) {
            print
            found = 1
        }
    }
')"

if [[ -n "$BAD_ENTRY" ]]; then
    echo "ERROR: macOS service metadata found inside ZIP:" >&2
    echo "  $BAD_ENTRY" >&2
    exit 1
fi

# Publish only after successful validation.
mv -f "$TEMP_ZIP" "$ARTIFACT_DIR/$ARCHIVE_NAME"

# Generate checksums from existing release artifacts.
SUMS_TEMP="$TEMP_DIR/SHA256SUMS.txt"
: > "$SUMS_TEMP"

for name in \
    "$MAC_DMG" \
    "$MAC_ZIP" \
    "$WIN_SETUP" \
    "$WIN_ZIP"
do
    if [[ -f "$ARTIFACT_DIR/$name" ]]; then
        (
            cd "$ARTIFACT_DIR"
            shasum -a 256 "$name"
        ) >> "$SUMS_TEMP"
    fi
done

if [[ ! -s "$SUMS_TEMP" ]]; then
    echo "ERROR: SHA256 manifest is empty." >&2
    exit 1
fi

mv -f "$SUMS_TEMP" "$ARTIFACT_DIR/SHA256SUMS.txt"

# Verify recorded checksums.
(
    cd "$ARTIFACT_DIR"
    shasum -a 256 -c SHA256SUMS.txt
)

echo "Created: $ARTIFACT_DIR/$ARCHIVE_NAME"
echo "Checksums: $ARTIFACT_DIR/SHA256SUMS.txt"
echo "Archive metadata check: OK (no __MACOSX or AppleDouble)"