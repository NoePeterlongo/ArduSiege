# Journal M2 — API capteurs

> Objectif M2 (étude §7) : exposer à Lua la lecture de l'état du jeu — `self` (le bloc
> contrôleur), `machine` (les blocs de la machine), `input` (le clavier réel du joueur).
> Suite de `docs/journal-m1.md` (bloc validé).

## Étape 6 — Retours joueur #1 (corrigés) : console `lua` + touche de démo

**Bug** : `lua "return machine.block_count"` → nil. **Cause** : la commande console
s'exécutait sur l'instance POC M0 (sans les globals M2 — ils ne sont installés que sur le
runtime du bloc). **Fix (architecture)** :
- registre statique `ScriptingModEntryPoint.ActiveRuntime/ActiveWatchdog/ActiveScriptName`,
  renseigné par `MicrocontrollerScript` au montage/au démontage ;
- `lua "..."` **route vers le runtime du microcontrôleur pendant la simulation**
  (contexte complet : self/machine/input — permet aussi d'inspecter/modifier l'état du
  script en cours, usage debug) ;
- hors simulation : POC avec `input` (installé au chargement) et `machine` résolu à la
  volée via `Modding.Common.Player.GetLocalPlayer().Machine` (rafraîchi à chaque appel,
  nil si pas de joueur) ;
- `RunLua` refactoré pour prendre (script, watchdog) explicites.

**UX** : la touche de démo passe de `Space` (touche de simulation — bien vu) à `P`.

**Démos versionnées (D8 renforcé)** : les scripts de démo portent un tag `SMDEMO vN` ;
`WriteDemoIfOutdated` les (ré)installe si absentes ou si la version embarquée est plus
récente ; un fichier local **sans tag** (personnalisé par le joueur) n'est jamais
touché. `capteurs.lua` → v2 (touche P), `hello.lua` → v1 (tag ajouté).

Version du mod : 0.2.0. Build OK ; contrôle statique identique (Path whitelisté seul,
`MemberInfo::` = 0).

## État : EN COURS (retest joueur attendu)

---

## Étape 1 — Vérifications API (code décompilé, avant d'écrire quoi que ce soit)

- `Modding.Blocks.PlayerMachine` (wrapper officiel) : `Position`, `Rotation` (Quaternion),
  `Size`, `Mass`, `Health`, `BlockCount`, **`BuildingBlocks`/`SimulationBlocks`**
  (`ReadOnlyCollection<Block>`) — choisir selon `Modding.Game.IsSimulating` ✓ ;
- `Modding.Blocks.Block` (wrapper officiel) : `Health` (0 si pas de barre de vie),
  `IsBurning`, `GameObject`, `InternalObject` (BlockBehaviour), et la fabrique
  **`Block.From(BlockScript)`** (prend le script directement — le handler sim) ;
- `BlockBehaviour` : `Prefab.Type` (enum BlockType) + `Prefab.name` (champs publics),
  `Guid` (champ), `IsDestroyed`, `Rigidbody` (champ public hérité de `BasicInfo` —
  attention : pas une propriété) ;
- `BlockScript.Machine` → `Modding.Blocks.PlayerMachine` (via
  `ModBlockBehaviourHandler.Machine`, ligne 51) ✓ ;
- `UnityEngine.Input` autorisé par le scanner (seuls WWW/MasterServer sont bannis dans
  UnityEngine) → lecture du clavier réel sans passer par le KeyInputController (interne) ;
- `Enum.Parse(typeof(KeyCode), name, true)` : `System.Enum` (namespace `System`) —
  autorisé (≠ System.Reflection) ;
- MoonSharp : `Preset_HardSandbox` inclut `TableIterators` → `ipairs`/`pairs` dispo ✓ ;
- pièges évités d'office (règles M0/M1) : `Type.Name`→`FullName`, `DataType` aliasé,
  `Modding.ModIO` qualifié, `NewTable(null)` ambiguïté (passer le `Script`).

## Étape 2 — Implémentation (`src/LuaApi.cs`, ~330 lignes)

Trois wrappers `IUserDataType` (pattern prouvé au M0 : servis par
`AutoDescribingUserDataDescriptor`, zéro réflexion — chaque accès = un `switch` C#) :

| Global | Accès Lua | Source |
|---|---|---|
| `self` | `.type` `.prefab_name` `.guid` `.position` `.rotation` (euler, table {x,y,z}) `.velocity` `.angular_velocity` `.health` `.is_burning` `.is_destroyed` | `Modding.Blocks.Block.From(controller)` |
| `machine` | `.position` `.rotation` `.mass` `.health` `.block_count` `.size` + `machine:blocks([filtre])` | `controller.Machine` ; blocs = Simulation/Building selon `Game.IsSimulating` ; filtre = sous-chaîne insensible à la casse sur le nom d'enum OU de prefab |
| `input` | `input:is_held("Space")` `.is_pressed` `.is_released` (noms `UnityEngine.KeyCode` ; deux syntaxes acceptées : `input.meth(k)` et `input:meth(k)`) | `UnityEngine.Input` |

- Branchement : `LuaApi.Install(m_Lua, this)` dans `OnSimulateStart` **avant** `DoString`
  (les globals sont disponibles dès `on_sim_start`) ;
- toutes les valeurs Lua = primitives/tables fraîches (`Vec3` → `{x=,y=,z=}`) —
  aucun objet CLR brut exposé (règle D-M0-2) ;
- erreurs de touche inconnue → `ScriptRuntimeException` avec message explicite.

## Étape 3 — Seed démo `capteurs.lua` (installé si absent, jamais écrasé)

Inventaire des types de blocs au démarrage (`machine:blocks()` + `ipairs`), puis toutes
les secondes : position du contrôleur, norme de vitesse, état de la barre Espace —
la vitrine des capteurs M2.

## Étape 4 — Build + contrôle statique (RÉUSSI)

- BUILD OK (LuaApi.cs ajouté au build.sh) ;
- ikdasm sur ScriptingMod.dll : `MemberInfo::` = 0, `System.IO` = `Path`×2 (whitelisté),
  aucun `Microsoft.*` — conforme scanner.

## Étape 5 — Test joueur (attendu)

Relancer le jeu → bloc Microcontroller → mapper : choisir **capteurs** dans le menu
script → simuler :
- attendu : inventaire des blocs (`- CogMotor... x2`, etc. selon le véhicule),
- puis chaque seconde : `t=1s pos=(…) |v|=… espace=false`,
- en bougeant le véhicule : position/vitesse changent ; barre espace → `true`.

*(à compléter : résultats)*
