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
5. Mod.xml, le code source des mods et TOUT leur contenu : ASCII pur (le parser XML
   du jeu est en us-ascii ; évite tout risque inutile).
6. **Jamais `.Name` sur un `Type`** — `Type.Name` compile en
   `System.Reflection.MemberInfo::get_Name()` et le scanner le rejette (le type
   déclarant est dans `System.Reflection`). Toujours `.FullName` (déclaré sur
   `System.Type`, autorisé). Vérifié empiriquement au M0 (journal, étape 7).
7. **La whitelist System.IO est une liste de types EXACTS** (`Stream`, `TextReader`,
   `TextWriter`, `BinaryReader`, `BinaryWriter`, `MemoryStream`, `Path`, `SeekOrigin`) :
   `StreamReader`/`StreamWriter` sont interdits (vérifié en jeu au M0). Ne jamais
   supposer qu'un type est whitelisté parce que son nom ressemble — contrôler
   `AssemblyScanner.cs` ou l'IL compilé (ikdasm), ou laisser le scanner parler.
8. **Ne jamais éditer `Mod.xml` (ni les fichiers du mod) jeu ouvert** : le loader
   (mode Debug) surveille `*.xml` et recharge à chaud — une réécriture sans `<ID>` fait
   **régénérer le GUID** du mod (perte d'identité = machines sauvegardées orphelines).
   Éditer les fichiers du mod jeu fermé.
9. **Toujours écrire `Modding.ModIO.Xxx`** (qualifié) : le jeu a un namespace global
   `ModIO` (IO interne, blacklisté) qui masque la classe `Modding.ModIO` dans la
   résolution de noms C# — `ModIO.Xxx` nu ne compile pas dans les mods.
10. **Jamais de nombre décimal dans les `.obj` des mods** : le chargeur de meshes
    (`Besiege/AssetImporter.LoadMeshData`) parse avec `Convert.ToSingle` **sans culture
    invariante** → sur locale française, tout `.` lève `FormatException` (vérifié en jeu
    au M1). Géométrie en entiers dans l'OBJ + mise à l'échelle via `<Mesh><Scale>` du
    XML du bloc (désérialisé par XmlSerializer, invariant).
11. **Géométrie d'attache codée en dur — ajuster avec le workflow Debug** : la géométrie
    du point de base est codée en dur dans `BlockPrefabCreator.cs` (trigger à `z=0.5`,
    attend un bloc vanillesque ~1×1) ; un collider mal proportionné par rapport à ce
    point d'attache provoque l'erreur « intersection » permanente. Workflow validé au
    M1 : `<Debug>True</Debug>` sur le bloc (visualisation des colliders/points en jeu) +
    édition du XML à chaud + `Ctrl+H` (reload des valeurs). Un bloc plat (collider
    0.9×0.9×0.25 décalé z+0.15) fonctionne — le collider doit être cohérent avec le
    point d'attache, pas nécessairement cubique. L'icône rend le mesh **brut** (sans
    `<Mesh><Scale>`) : compenser via `<Icon><Scale>` (ici ×0.25 pour un mesh ±1).

12. **TOUT EN ANGLAIS** : le code du mod, les commentaires, les chaînes (console,
    messages d'erreur Lua), les démos embarquées, la doc joueur et les commits sont
    en ANGLAIS (ASCII). Les journaux historiques (docs/journal-*.md, étude, workflow)
    restent en français — ce sont des archives ; tout nouveau document est en anglais.

## Documentation

- Source de vérité officielle : `Besiege/Besiege_Data/Documentation/Modding.pdf`
  (extrait texte : `docs/modding-doc.txt` — cherchable avec grep).
- Code du jeu décompilé : `decomp/` (regénérable, gitignoré) — la seule source de vérité
  quand la doc est silencieuse (ex. mécanisme de la liste noire, émulation de touches).
- Étude d'architecture du mod de scripting : `docs/etude-faisabilite-scripting.md`
  (faisabilité, canaux capteurs/actionneurs, adressage des blocs, jalons M0-M5).
- Workflow d'écriture des scripts et décisions D1-D8 : `docs/workflow-scripting.md`
  (parcours joueur complet, persistance `.bsg`, watchdog, points reportés).

## Conventions de travail

- **TOUT EN ANGLAIS** (règle 12 ci-dessus) : commits en anglais, à l'impératif ;
  une tâche = un commit si possible.
- Test systématique en jeu après build (console `Ctrl+Shift+K`, `show_logs true`,
  entrer dans un niveau — les mods se chargent à l'entrée en partie, pas au lancement).
- Après premier chargement d'un nouveau mod : commiter le GUID inséré dans son `Mod.xml`.
- Ne jamais commiter : `Besiege/`, `decomp/`, `*.dll`, logs.
- Documenter précisément, pour aider à la reprise du travail par un autre agent IA.
