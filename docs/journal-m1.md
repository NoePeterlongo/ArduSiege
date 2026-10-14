# Journal M1 — Bloc Microcontrôleur

> Objectif M1 (étude §7) : un bloc moddé « Microcontroller » posable sur la machine,
> mapper vanilla (menu script + toggle actif), runtime Lua monté à chaque simulation,
> `on_sim_start` / `on_tick` (50 Hz) / `on_sim_stop` appelés, script `hello.lua` seedé
> dans la bibliothèque du dossier data. Suite de `docs/journal-m0.md` (moteur validé).

## Étape 6 — Bug joueur #1 : le chargeur OBJ est sensible à la locale (corrigé)

Symptôme : bloc absent de la barre ; `Player.log` :
`[Mods] Error loading resource microcontroller-mesh: System.FormatException:
Unknown char: .` dans `AssetImporter.LoadMeshData`.

**Cause racine** : le chargeur OBJ des mods parse les floats avec `Convert.ToSingle` **sans
culture invariante** (décompilé : `Besiege/AssetImporter.cs:776`). Sur une locale française,
`0.25` → `FormatException` : **tout point décimal dans un `.obj` de mod casse sur les
systèmes français** (et réciproquement : des virgules casseraient les systèmes anglais —
aucun format ne marche partout tant qu'il y a des décimaux).

**Correctif robuste (toute locale)** : **zéro décimal dans l'OBJ** — cube à sommets ±1,
normales 0/±1, UV 0/1. La taille réelle (×0.25 → cube 0.5) passe par `<Mesh><Scale
x="0.25" .../>` dans le XML du bloc, désérialisé par **XmlSerializer (.NET)** qui, lui,
parse en invariant — vérifié dans `Modding.Serialization/TransformValues.cs` (attributs
`[XmlElement]`). NB : les faces négatives des sommets sont OK (pas de point), seuls les
indices de faces devaient être positifs (chargeur).

**Leçon M1 (→ AGENTS.md règle 10)** : jamais de décimaux dans les assets OBJ des mods ;
tout ce qui est décimal passe par le XML (invariant) ou par la géométrie entière.

Au passage : le message « M0 prêt » était un résidu de libellé (corrigé en M1) — le DLL
était bien le nouveau.

## Étape 7 — Bug joueur #2 : bloc impossible à placer + icône trop grande (corrigé)

Symptômes : icône 2-3× trop grande dans la barre ; placement impossible (« intersection »,
ghost rouge), y compris sur du vide.

**Cause racine (les deux symptômes)** : la géométrie des points d'attache des blocs
moddés est **codée en dur pour des blocs 1×1** dans `BlockPrefabCreator.cs` :
- le trigger du point de base est à `localPosition (0,0,0.5)` + centre `(0,-0.58,0)`
  (lignes ~336) : le ghost se positionne donc avec le **centre à 0.5 de la surface**.
  Avec un collider 0.5 (mon M1 initial), le bloc **pénètre de 0.25** dans le parent →
  `GhostTrigger.touchingCount > 0` → erreur « intersection » (`GhostPinTrigger.cs:15`,
  `!StatMaster.Mode.allowIntersection`) ;
- l'icône (`BlockButtonCreator.cs`) rend le mesh **brut** (sans le `<Mesh><Scale>`),
  compensée par `<Icon><Scale>` — mon mesh brut = taille 2 → icône trop grande.

**Correctif — dimensionner le bloc en 1×1 standard** (géométrie attendue par le loader) :
mesh ±1 × Scale 0.5 → visuel 1.0 ; collider 1.0 ; Icon Scale 0.5 → icône 1.0 ;
AddingPoint y=0.5.

**Leçon M1 (→ AGENTS.md règle 11)** : un bloc moddé doit être de taille ~1×1
(ou un multiple) — la géométrie d'attache est codée en dur ; un collider plus petit
que 1 pénètre le parent au placement. Les « petits blocs » vanilla sont des prefabs
nativement configurés, ils ne passent pas par ce chemin.

## Étape 8 — RÉUSSITE M1 (ajusté en jeu par le joueur)

**Résultat** : bloc posable, plaçable sur le véhicule, mapper fonctionnel, **script Lua
opérationnel pendant la simulation** (`on_sim_start` / `on_tick` 50 Hz / `on_sim_stop`).

Le dernier ajustement de géométrie a été fait **en jeu par le joueur** avec le workflow
officiel (visualisation `<Debug>` + édition à chaud + `Ctrl+H`) — validé comme méthode :

```xml
<Mesh><Scale x="0.5" y="0.5" z="0.25" /></Mesh>      <!-- bloc PLAT (panneau 1x0.25x1) -->
<BoxCollider>
    <Position x="0.0" y="0.0" z="0.15" />            <!-- décalé vers le point de base -->
    <Scale x="0.9" y="0.9" z="0.25" />               <!-- collider fin, pas cubique -->
</BoxCollider>
```

**Enseignement final M1** : le collider n'a pas besoin d'être un cube 1×1 — il doit être
**cohérent avec le point de base codé en dur** (z=0.5). La nuance est passée dans
AGENTS.md (règle 11 révisée). L'icône convergera avec `<Icon><Scale 0.25>`.

### Historique des itérations (4 sessions de test joueur)

1. mesh 0.5 avec décimaux → **FormatException** (locale, règle 10) → OBJ en entiers ;
2. collider 0.5 centré → **« intersection »** (géométrie d'attache codée en dur) ;
3. collider 1.0 centré → icône encore 2× trop grande, placement KO ;
4. **bloc plat ajusté en jeu (joueur) → tout fonctionne, script inclus** ✅.

## État : TERMINÉ (M1 validé en jeu — 2026-10-14)

---

## Étape 1 — Assets générés sans Unity (2026-10-14)

- `tools/gen_assets.py` (Python stdlib) → `Resources/microcontroller.{obj,png}` :
  - **Cube 0.5** en OBJ **écrit à la main** — conventions vérifiées dans le chargeur
    du jeu (`Besiege/AssetImporter.cs::LoadMeshData`, décompilé) : indices **1-based
    positifs uniquement** (le parseur `ConvertStringToInt` casse sur les nombres
    négatifs !), quads OK (triangulation en éventail `list[0], list[i], list[i+1]`),
    axes lus tels quels (convention OBJ droite/Blender = correcte sous Unity),
    `v`, `vt`, `vn`, `f v/vt/vn` supportés ;
  - **Texture 32×32 RGBA** PNG écrit à la main (struct+zlib) : corps PCB sombre,
    substrat, die bleu, broches dorées.
- Régénération : `python3 tools/gen_assets.py` (depuis `Resources/`).

## Étape 2 — Définition du bloc

- `MicrocontrollerBlock.xml` (gabarit = le fichier d'exemple de la doc officielle,
  `docs/modding-doc.txt` l.~780-920) : Name, ID **1** (⚠️ ne jamais changer un ID
  publié), Mass 0.5, SearchKeywords (script, lua), `<Script>ScriptingMod.MicrocontrollerScript</Script>`,
  Mesh/Texture (ressources manifest), Icon, BoxCollider 0.5, BasePoint collant,
  1 AddingPoint au-dessus.
- Manifeste : + `<Resources>` (mesh, texture) + `<Blocks>` ; version 0.1.0.

## Étape 3 — BlockScript (M1)

`MicrocontrollerScript.cs` :
- `SafeAwake` : `MMenu` « script » (liste = fichiers `.lua` du dossier data `Scripts/`)
  + `MToggle` « actif » ;
- `OnSimulateStart` : monte `Script(Preset_SoftSandbox)` + watchdog, `DoString` du
  script choisi, appelle `on_sim_start` ;
- `SimulateFixedUpdateHost` : `watchdog.Reset()` **par tick** puis `on_tick` ;
- erreurs Lua/watchdog → log + **script désactivé** (jamais de crash du jeu) ;
- `OnSimulateStop` : appelle `on_sim_stop` puis démonte (état frais au run suivant).
- Budget watchdog M1 : **1 000 000 instr/tick** (M0 = 50 M ≈ 17 s de gel → 1 M ≈ 0,35 s).

EntryPoint : seed de la bibliothèque — `Scripts/hello.lua` installé au premier
chargement si le dossier data est vide (D8 : les exemples sont la documentation vivante).

## Étape 4 — Pièges du jour (2 leçons durables → AGENTS.md)

1. **Ne JAMAIS éditer `Mod.xml` jeu ouvert** : le mod loader (mode Debug) surveille
   `*.xml` (`ModReloading.cs`, FileSystemWatcher) et recharge immédiatement. J'ai
   réécrit le manifeste pendant que le jeu tournait → il a constaté l'absence d'ID et
   **régénéré un GUID** (`64dffd8d…` remplacé par `a1d11e61…`). Sans conséquence ici
   (rien de publié), mais un mod publié perdrait l'identité de ses machines sauvegardées.
   → Éditer les fichiers du mod jeu fermé, ou n'éditer que des fichiers non-`Mod.xml`.
2. **Toujours qualifier `Modding.ModIO` en entier** : le jeu a un namespace **global
   `ModIO`** (son IO interne, justement blacklisté par le scanner). En résolution de
   noms C#, le namespace englobant (global) **gagne** sur les types des `using` →
   `ModIO.ExistsDirectory` ne compile pas ; `Modding.ModIO.ExistsDirectory` oui.
   (C'est aussi pour ça que le scanner bannit le namespace `ModIO` : les mods ne
   doivent toucher ni le voir.)

Build : OK (ScriptingMod.dll ~ M0 + block script).

## Étape 5 — Test joueur (attendu)

1. Relancer le jeu, entrer dans un niveau.
2. Barre de blocs → chercher **« Microcontroller »** (ou « script » / « lua ») → poser.
3. Mapper du bloc : menu **script** (hello) + toggle **actif**.
4. Lancer la simulation (`Espace`) → attendu dans la console :
   `[MC:hello] script demarre : boucle on_tick a 50 Hz` puis chaque seconde
   `[MC:hello] 50 ticks (1 s de simulation)`, … ; à l'arrêt :
   `script arrete apres N ticks`.
5. Points à vérifier : mesh pas inversé (winding), texture lisible, bloc plaçable
   (colliders/points), toggle actif marche.

*(à compléter : résultats, correctifs éventuels)*
