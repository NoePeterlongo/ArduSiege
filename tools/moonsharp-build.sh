#!/usr/bin/env bash
# Build de la DLL MoonSharp elaguee pour le mod ScriptingMod (Besiege).
#
#   bash tools/moonsharp-build.sh           → compile third-party/moonsharp-trimmed vers
#                                             mods/ScriptingMod/ScriptingMod/MoonSharp.Interpreter.dll
#   bash tools/moonsharp-build.sh --regen   → en plus : regenere ModuleRegister.g.cs depuis
#                                             les sources MoonSharp amont (telechargees
#                                             dans third-party/.upstream/, gitignore)
#
# Tout se passe dans Docker (mono:6.12) : la reflexion du generateur est legale HORS du
# jeu (c'est l'IL charge PAR le jeu qui est filtre par l'AssemblyScanner).
# Le fichier response de mcs contient un chemin par ligne (find brut, sans prefix).
# Voir docs/journal-m0.md et third-party/README.md.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
MOD_OUT_DIR="$REPO_DIR/mods/ScriptingMod/ScriptingMod"
NET35_LIST="$REPO_DIR/third-party/moonsharp-net35-files.txt"
UPSTREAM_DIR="$REPO_DIR/third-party/.upstream"
TAG="v2.0.0.0"

REGEN=0
[ "${1:-}" = "--regen" ] && REGEN=1

[ -f "$REPO_DIR/Besiege/Besiege_Data/Managed/mscorlib.dll" ] || {
    echo "ERREUR : DLL du jeu introuvables (Besiege/Besiege_Data/Managed)" >&2
    exit 1
}

# ---- Sources amont (mode --regen uniquement) ------------------------------------
if [ "$REGEN" = "1" ]; then
    SRC="$UPSTREAM_DIR/moonsharp-2.0.0.0/src/MoonSharp.Interpreter"
    if [ ! -d "$SRC" ]; then
        echo ">> Telechargement des sources MoonSharp $TAG..."
        mkdir -p "$UPSTREAM_DIR"
        # NB : l'archive GitHub du tag v2.0.0.0 s'extrait dans moonsharp-2.0.0.0 (le 'v' est retire)
        curl -sSL "https://github.com/moonsharp-devs/moonsharp/archive/refs/tags/$TAG.tar.gz" \
            | tar xz -C "$UPSTREAM_DIR"
    fi
    STAGE="$UPSTREAM_DIR/stage-full"
    rm -rf "$STAGE"
    mkdir -p "$STAGE"
    ( cd "$SRC" && while read -r f; do
        [ -f "$f" ] || { echo "ERREUR : fichier net35 manquant dans l'amont : $f" >&2; exit 1; }
        mkdir -p "$STAGE/$(dirname "$f")"
        cp "$f" "$STAGE/$f"
      done < "$NET35_LIST" )
    echo ">> Arbre net35 complet stage : $(find "$STAGE" -name '*.cs' | wc -l) fichiers"
fi

# ---- Conteneur ------------------------------------------------------------------
echo ">> Build Docker (mono:6.12), REGEN=$REGEN..."
sg docker -c "docker run --rm --user $(id -u):$(id -g) -e HOME=/tmp \
    -e REGEN=$REGEN \
    -v \"$REPO_DIR\":/work -w /work mono:6.12 bash -euc '
GAME=/work/Besiege/Besiege_Data/Managed
REFS=\"-r:\$GAME/mscorlib.dll -r:\$GAME/System.dll -r:\$GAME/System.Core.dll\"

if [ \"\$REGEN\" = \"1\" ]; then
    echo \">> [regen] Compilation DLL MoonSharp complete (input reflexion du generateur)...\"
    find /work/third-party/.upstream/stage-full -name \"*.cs\" > /tmp/full.rsp
    mcs -nostdlib -noconfig -target:library -out:/tmp/MoonSharp.Full.dll \$REFS @/tmp/full.rsp

    echo \">> [regen] Compilation du generateur...\"
    mcs -nostdlib -noconfig -target:exe -out:/tmp/GenModuleRegister.exe \$REFS \
        -r:/tmp/MoonSharp.Full.dll /work/tools/moonsharp-gen/GenModuleRegister.cs

    echo \">> [regen] Execution du generateur (reflexion hors-jeu)...\"
    mono /tmp/GenModuleRegister.exe /tmp/MoonSharp.Full.dll \
        /work/third-party/moonsharp-trimmed/Modules/ModuleRegister.g.cs
fi

echo \">> Compilation de la DLL elaguee...\"
find /work/third-party/moonsharp-trimmed -name \"*.cs\" > /tmp/trim.rsp
mkdir -p /work/mods/ScriptingMod/ScriptingMod
mcs -nostdlib -noconfig -target:library -optimize+ \
    -out:/work/mods/ScriptingMod/ScriptingMod/MoonSharp.Interpreter.dll \$REFS @/tmp/trim.rsp

echo \">> BUILD OK\"
'"

echo ">> OK : $MOD_OUT_DIR/MoonSharp.Interpreter.dll"
