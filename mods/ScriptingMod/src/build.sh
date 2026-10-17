#!/usr/bin/env bash
# Compiles ScriptingMod.dll (the mod code) via Docker (mono:6.12).
# The trimmed MoonSharp DLL is built by tools/moonsharp-build.sh.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/../../.." && pwd)"
MOD_DIR="$REPO_DIR/mods/ScriptingMod/ScriptingMod"

[ -f "$MOD_DIR/MoonSharp.Interpreter.dll" ] || {
    echo "ERROR: MoonSharp.Interpreter.dll missing - run tools/moonsharp-build.sh first." >&2
    exit 1
}

echo ">> Compiling ScriptingMod.dll (Docker mono:6.12)..."
sg docker -c "docker run --rm --user $(id -u):$(id -g) -e HOME=/tmp \
    -v \"$REPO_DIR\":/work -w /work mono:6.12 bash -euc '
GAME=/work/Besiege/Besiege_Data/Managed
mcs -nostdlib -noconfig -target:library -optimize+ \
    -out:/work/mods/ScriptingMod/ScriptingMod/ScriptingMod.dll \
    -r:\$GAME/mscorlib.dll -r:\$GAME/System.dll -r:\$GAME/System.Core.dll \
    -r:\$GAME/Assembly-CSharp.dll -r:\$GAME/UnityEngine.dll \
    -r:/work/mods/ScriptingMod/ScriptingMod/MoonSharp.Interpreter.dll \
    /work/mods/ScriptingMod/src/WatchdogDebugger.cs \
    /work/mods/ScriptingMod/src/BlockKeyActuator.cs \
    /work/mods/ScriptingMod/src/BlockAngleRamps.cs \
    /work/mods/ScriptingMod/src/LuaApi.cs \
    /work/mods/ScriptingMod/src/LuaActuators.cs \
    /work/mods/ScriptingMod/src/ScriptingModEntryPoint.cs \
    /work/mods/ScriptingMod/src/MicrocontrollerScript.cs

echo \">> BUILD OK\"
'"

echo ">> OK: $MOD_DIR/ScriptingMod.dll"
