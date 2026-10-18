#!/usr/bin/env bash
# Build the ArduSiege release zip (for GitHub Releases).
#
#   bash tools/make-release.sh [--skip-build]
#
# Steps:
#   1. (unless --skip-build) build MoonSharp.Interpreter.dll and ArduSiege.dll
#      via the existing Docker build scripts;
#   2. zip the mod folder (the one the game loads) into dist/ArduSiege-v<version>.zip,
#      with ArduSiege/ at the zip root so players extract it straight into
#      Besiege/Besiege_Data/Mods/.
#
# The zip contains the DLLs (never committed to git). dist/ is gitignored:
# attach the zip to a GitHub release, do not commit it.
# Version is read from mods/ArduSiege/ArduSiege/Mod.xml.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
MOD_DIR="$REPO_DIR/mods/ArduSiege/ArduSiege"
DIST_DIR="$REPO_DIR/dist"

VERSION="$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' "$MOD_DIR/Mod.xml" | head -1)"
[ -n "$VERSION" ] || { echo "ERROR: cannot read version from Mod.xml" >&2; exit 1; }

if [ "${1:-}" != "--skip-build" ]; then
    echo ">> Building DLLs (Docker)..."
    bash "$REPO_DIR/tools/moonsharp-build.sh"
    bash "$REPO_DIR/mods/ArduSiege/src/build.sh"
fi

for dll in ArduSiege.dll MoonSharp.Interpreter.dll; do
    [ -f "$MOD_DIR/$dll" ] || { echo "ERROR: $MOD_DIR/$dll missing (run without --skip-build)" >&2; exit 1; }
done

ZIP_PATH="$DIST_DIR/ArduSiege-v$VERSION.zip"
echo ">> Packaging ArduSiege v$VERSION -> $ZIP_PATH"
mkdir -p "$DIST_DIR"
rm -f "$ZIP_PATH"
# Stage the mod folder + the MIT license (must ship with the binaries).
STAGE="$DIST_DIR/.stage"
rm -rf "$STAGE"
mkdir -p "$STAGE"
cp -r "$MOD_DIR" "$STAGE/ArduSiege"
cp "$REPO_DIR/mods/ArduSiege/LICENSE.txt" "$STAGE/ArduSiege/LICENSE.txt"
(cd "$STAGE" && zip -qr "$ZIP_PATH" ArduSiege)
rm -rf "$STAGE"

echo ">> Zip contents:"
unzip -l "$ZIP_PATH"
echo ">> OK: $ZIP_PATH"
echo ">> Upload it to the GitHub release (do not commit; dist/ is gitignored)."
