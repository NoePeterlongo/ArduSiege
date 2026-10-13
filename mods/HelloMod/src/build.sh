#!/usr/bin/env bash
# Compile HelloMod.dll avec le compilateur Mono (mcs) dans Docker,
# en referencant les DLL du jeu lui-meme (runtime CLR 2.0 parfaitement fidele).
# Aucune installation necessaire sur la machine hote.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"          # .../mods/HelloMod
REPO_DIR="$(cd "$PROJECT_DIR/../.." && pwd)"          # racine du depot
IMAGE="mono:6.12"

[ -f "$REPO_DIR/Besiege/Besiege_Data/Managed/Assembly-CSharp.dll" ] || {
    echo "ERREUR : DLL du jeu introuvables dans $REPO_DIR/Besiege/Besiege_Data/Managed" >&2
    exit 1
}

echo ">> Compilation de src/HelloMod.cs (image $IMAGE)"
docker run --rm \
    --user "$(id -u):$(id -g)" \
    -e HOME=/tmp \
    -v "$REPO_DIR":/work \
    -w /work \
    "$IMAGE" \
    mcs -target:library -nostdlib -noconfig \
        -out:mods/HelloMod/HelloMod/HelloMod.dll \
        -r:Besiege/Besiege_Data/Managed/mscorlib.dll \
        -r:Besiege/Besiege_Data/Managed/System.dll \
        -r:Besiege/Besiege_Data/Managed/System.Core.dll \
        -r:Besiege/Besiege_Data/Managed/Assembly-CSharp.dll \
        -r:Besiege/Besiege_Data/Managed/UnityEngine.dll \
        mods/HelloMod/src/HelloMod.cs

echo ">> OK : $PROJECT_DIR/HelloMod/HelloMod.dll"
