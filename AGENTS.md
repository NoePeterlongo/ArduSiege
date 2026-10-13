# AGENTS.md — guide du dépôt pour agents IA

## Ce qu'est ce dépôt

Workspace de modding du jeu **Besiege** (Steam, Linux). But à terme : un mod de
**scripting embarqué** (capteurs/actionneurs/boucles de contrôle pour les machines du
joueur). Le premier mod (`mods/HelloMod`) valide la chaîne complète — il sert de
**référence fonctionnelle** pour tout nouveau mod.

- Jeu : Unity 5.4.0f3, Mono, **CLR 2.0** (mscorlib v2.0.50727 ≈ .NET 3.5), builds x86 + x86_64.
- **Mod loader officiel intégré** au jeu depuis v1.4 — aucune injection/hook nécessaire.
- Copie jouable du jeu dans `Besiege/` (ignorée par git, jamais commitée) ;
  install Steam d'origine : `~/.steam/debian-installation/steamapps/common/Besiege`.

## Commandes essentielles

```bash
# Compiler un mod (Docker obligatoire — ni mono ni dotnet sur l'hôte)
bash mods/<Nom>/src/build.sh
# Docker nécessite le groupe docker ; depuis une session non reloggée :
sg docker -c "bash mods/<Nom>/src/build.sh"

# Lancer le jeu (depuis la copie)
cd Besiege && ./Besiege.x86_64
# Console en jeu : Ctrl+Shift+K   |   logs : show_logs true   |   reload mod : Ctrl+H

# Regénérer la décompilation du jeu (référence de travail, gitignorée)
sg docker -c "docker run --rm --user \$(id -u):\$(id -g) -e HOME=/tmp -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  -v $PWD:/work -w /work mcr.microsoft.com/dotnet/sdk:8.0 sh -c \
  'dotnet tool install ilspycmd --tool-path /tmp/tools --version \"9.*\" 2>/dev/null | tail -1; \
   /tmp/tools/ilspycmd Besiege/Besiege_Data/Managed/Assembly-CSharp.dll -p -o decomp/Assembly-CSharp'"
```

## Environnement

- Pop!_OS 24.04 ; Docker (groupe `docker` configuré via usermod) ; images utilisées :
  `mono:6.12` (compil `mcs`) et `mcr.microsoft.com/dotnet/sdk:8.0` (ILSpy).
- Pas de mono/dotnet sur l'hôte — **toutes** les compilations passent par Docker.
- Pas besoin de l'éditeur Unity pour des mods de code ; les meshes `.obj` sont chargés
  nativement par le mod loader pour les blocs.

## Structure

```
Besiege/                          jeu (gitignoré)
docs/
  modding-doc.txt                 doc officielle extraite du PDF inclus au jeu (référence)
  etude-faisabilite-scripting.md  étude avant-projet du mod de scripting
mods/<NomProjet>/                 chaque mod : structure officielle « projet »
  <NomProjet>/                   ← ce dossier est chargé par le jeu (Mod.xml + DLL ici)
  src/                           code source + build.sh
decomp/                           jeu décompilé via ILSpy (gitignoré, à regénérer au besoin)
```

Le jeu ne scanne que les dossiers enregistrés via `addmodsdir` (commande console, persistant) —
`mods/HelloMod` est déjà enregistré.

## Contraintes techniques critiques (à respecter)

1. **Runtime CLR 2.0** : compiler avec `mcs -nostdlib -noconfig` en référençant les DLL de
   `Besiege/Besiege_Data/Managed/` (voir `mods/HelloMod/src/build.sh`). Jamais de netstandard/net5+.
2. **Liste noire du mod loader** (vérifiée au chargement d'un mod) : pas de `System.IO`,
   `System.Reflection`, `System.Runtime.InteropServices`, `Mono.CSharp`, `Mono.Cecil`,
   `System.CodeDom.Compiler`, IKVM, `UnityEngine.WWW` → pas de compilation C# à runtime,
   pas d'interop réflexion. IO via `Modding.ModIO` uniquement.
3. Les ID (GUID du mod inséré dans `Mod.xml`, IDs de blocs/entités) ne changent **jamais**
   après publication — ils lient les machines/niveaux sauvegardés.
4. Multiplayer : émulation de touches = host uniquement ; une incompatibilité MP se déclare
   via `<MultiplayerCompatible>false</MultiplayerCompatible>`.
5. Mod.xml et le code source des mods : ASCII pur (le parser XML du jeu écrit en us-ascii ;
   évite tout risque inutile).

## Documentation

- Source de vérité officielle : `Besiege/Besiege_Data/Documentation/Modding.pdf`
  (extrait texte : `docs/modding-doc.txt` — cherchable avec grep).
- Code du jeu décompilé : `decomp/` (regénérable, gitignoré) — la seule source de vérité
  quand la doc est silencieuse (ex. mécanisme de la liste noire, émulation de touches).
- Étude d'architecture du mod de scripting : `docs/etude-faisabilite-scripting.md`.

## Conventions de travail

- Commits en français, à l'impératif ; une tâche = un commit si possible.
- Test systématique en jeu après build (console `Ctrl+Shift+K`, `show_logs true`,
  entrer dans un niveau — les mods se chargent à l'entrée en partie, pas au lancement).
- Après premier chargement d'un nouveau mod : commiter le GUID inséré dans son `Mod.xml`.
- Ne jamais commiter : `Besiege/`, `decomp/`, `*.dll`, logs.
