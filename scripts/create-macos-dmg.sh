#!/bin/bash
set -euo pipefail

if [[ "$#" -ne 2 ]]; then
    echo "Usage: create-macos-dmg.sh <Daycord.app> <output.dmg>" >&2
    exit 2
fi

APP_PATH="$1"
DMG_PATH="$2"

if [[ "$(uname -s)" != "Darwin" ]]; then
    echo "ERROR: macOS is required." >&2
    exit 1
fi

for tool in ditto diskutil hdiutil mktemp du awk osascript swift chflags; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        echo "ERROR: Missing tool: $tool" >&2
        exit 1
    fi
done

PLIST_BUDDY="/usr/libexec/PlistBuddy"

if [[ ! -x "$PLIST_BUDDY" ]]; then
    echo "ERROR: PlistBuddy is missing." >&2
    exit 1
fi

if [[ ! -d "$APP_PATH/Contents" ]]; then
    echo "ERROR: Daycord.app not found: $APP_PATH" >&2
    exit 1
fi

if [[ ! -x "$APP_PATH/Contents/MacOS/Daycord.Desktop" ]]; then
    echo "ERROR: Daycord executable is missing or not executable." >&2
    exit 1
fi

if [[ ! -f "$APP_PATH/Contents/Info.plist" ||
      ! -f "$APP_PATH/Contents/Resources/icon.icns" ]]; then
    echo "ERROR: Invalid macOS bundle resources." >&2
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd -P)"
BACKGROUND_SWIFT="$SCRIPT_DIR/create-macos-dmg-background.swift"

if [[ ! -f "$BACKGROUND_SWIFT" ]]; then
    echo "ERROR: Missing Swift background generator." >&2
    exit 1
fi

UNEXPECTED_FILE="$(find "$APP_PATH" -type f \( \
    -name '*.db' -o -name '*.db-wal' -o -name '*.db-shm' -o \
    -name '*.sqlite' -o -name '*.sqlite3' -o \
    -name '*.daycordbak' -o -name '*.xlsx' \
    \) -print -quit)"

if [[ -n "$UNEXPECTED_FILE" ]]; then
    echo "ERROR: Unexpected data in bundle: $UNEXPECTED_FILE" >&2
    exit 1
fi

APP_VERSION="$("$PLIST_BUDDY" \
    -c 'Print :CFBundleShortVersionString' \
    "$APP_PATH/Contents/Info.plist")"

OUTPUT_DIR="$(dirname "$DMG_PATH")"
mkdir -p "$OUTPUT_DIR"

DMG_PATH="$(cd "$OUTPUT_DIR" && pwd -P)/$(basename "$DMG_PATH")"

TEMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/daycord-dmg.XXXXXX")"

MOUNT_POINT="$TEMP_DIR/mount"
RW_DMG="$TEMP_DIR/Daycord-temp.dmg"
FINAL_DMG="$TEMP_DIR/Daycord-final.dmg"

BACKGROUND_DIR="$MOUNT_POINT/.background"
BACKGROUND_FILE="$BACKGROUND_DIR/background.png"

MOUNTED=0

unmount_image() {
    if [[ "$MOUNTED" != "1" ]]; then
        return 0
    fi

    for attempt in 1 2 3; do
        if diskutil eject "$MOUNT_POINT" >/dev/null 2>&1; then
            MOUNTED=0
            return 0
        fi

        sleep 2
    done

    return 1
}

cleanup() {
    if [[ "$MOUNTED" == "1" ]] && ! unmount_image; then
        echo "WARNING: Temporary DMG remains mounted at $MOUNT_POINT" >&2
        return
    fi

    rm -rf "$TEMP_DIR"
}

trap cleanup EXIT

mkdir -p "$MOUNT_POINT"

APP_SIZE_KB="$(du -sk "$APP_PATH" | awk '{print $1}')"
DMG_SIZE_MB=$(( (APP_SIZE_KB + 1024 * 90 + 1023) / 1024 ))

# Create temporary read-write APFS image using modern diskutil.
diskutil image create blank \
    --format RAW \
    --fs APFS \
    --volumeName "Daycord" \
    --size "${DMG_SIZE_MB}MiB" \
    "$RW_DMG" >/dev/null

# Attach the image at our private temporary mount point.
diskutil image attach \
    --nobrowse \
    --mountPoint "$MOUNT_POINT" \
    "$RW_DMG" >/dev/null

MOUNTED=1

ditto "$APP_PATH" "$MOUNT_POINT/Daycord.app"

ln -s /Applications "$MOUNT_POINT/Applications"

mkdir -p "$BACKGROUND_DIR"

swift "$BACKGROUND_SWIFT" \
    "$BACKGROUND_FILE" \
    "$APP_VERSION"

chflags hidden "$BACKGROUND_DIR" || true

if [[ -d "$MOUNT_POINT/.fseventsd" ]]; then
    chflags hidden "$MOUNT_POINT/.fseventsd" || true
fi

# Preserve the previously accepted Daycord DMG design.
if ! osascript - "$MOUNT_POINT" "$BACKGROUND_FILE" <<'APPLESCRIPT'
on run argv
    set mountedPath to item 1 of argv
    set backgroundPath to item 2 of argv

    set mountedFolder to (POSIX file mountedPath) as alias
    set backgroundAlias to (POSIX file backgroundPath) as alias

    tell application "Finder"
        tell folder mountedFolder
            open
            delay 1

            set current view of container window to icon view
            set toolbar visible of container window to false
            set statusbar visible of container window to false
            set pathbar visible of container window to false

            set bounds of container window to {140, 120, 820, 560}

            set viewOptions to the icon view options of container window
            set arrangement of viewOptions to not arranged
            set icon size of viewOptions to 112
            set text size of viewOptions to 14
            set label position of viewOptions to bottom
            set background picture of viewOptions to backgroundAlias

            repeat with hiddenName in {".background", ".fseventsd", ".Trashes"}
                try
                    set position of item (contents of hiddenName) of container window to {1400, 1100}
                end try
            end repeat

            set position of item "Daycord.app" of container window to {172, 238}
            set position of item "Applications" of container window to {508, 238}

            update without registering applications
            delay 2

            close
        end tell
    end tell
end run
APPLESCRIPT
then
    echo "ERROR: Finder could not save the DMG layout." >&2
    exit 1
fi

sync
sleep 1

if ! unmount_image; then
    echo "ERROR: Cannot detach the temporary DMG." >&2
    exit 1
fi

# Convert the finished image to a read-only compressed DMG.
diskutil image create from \
    --format UDZO \
    "$RW_DMG" \
    "$FINAL_DMG" >/dev/null

# Validate the compressed image before publishing.
hdiutil verify "$FINAL_DMG" >/dev/null

mv -f "$FINAL_DMG" "$DMG_PATH"

echo "DMG created: $DMG_PATH"
echo "Signing status: unsigned (technical build)"