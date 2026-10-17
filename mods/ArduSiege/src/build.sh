#!/usr/bin/env bash
# Compiles ArduSiege.dll (the mod code) via Docker (mono:6.12).
# The trimmed MoonSharp DLL is built by tools/moonsharp-build.sh.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/../../.." && pwd)"
MOD_DIR="$REPO_DIR/mods/ArduSiege/ArduSiege"

[ -f "$MOD_DIR/MoonSharp.Interpreter.dll" ] || {
    echo "ERROR: MoonSharp.Interpreter.dll missing - run tools/moonsharp-build.sh first." >&2
    exit 1
}

echo ">> Compiling ArduSiege.dll (Docker mono:6.12)..."
sg docker -c "docker run --rm --user $(id -u):$(id -g) -e HOME=/tmp \
    -v \"$REPO_DIR\":/work -w /work mono:6.12 bash -euc '
GAME=/work/Besiege/Besiege_Data/Managed
mcs -nostdlib -noconfig -target:library -optimize+ \
    -out:/work/mods/ArduSiege/ArduSiege/ArduSiege.dll \
    -r:\$GAME/mscorlib.dll -r:\$GAME/System.dll -r:\$GAME/System.Core.dll \
    -r:\$GAME/Assembly-CSharp.dll -r:\$GAME/UnityEngine.dll \
    -r:/work/mods/ArduSiege/ArduSiege/MoonSharp.Interpreter.dll \
    /work/mods/ArduSiege/src/WatchdogDebugger.cs \
    /work/mods/ArduSiege/src/BlockKeyActuator.cs \
    /work/mods/ArduSiege/src/BlockAngleRamps.cs \
    /work/mods/ArduSiege/src/SimLogger.cs \
    /work/mods/ArduSiege/src/LuaApi.cs \
    /work/mods/ArduSiege/src/LuaActuators.cs \
    /work/mods/ArduSiege/src/ArduSiegeEntryPoint.cs \
    /work/mods/ArduSiege/src/MicrocontrollerScript.cs

echo \">> BUILD OK\"
'"

echo ">> OK: $MOD_DIR/ArduSiege.dll"
