# Étude de faisabilité — Mod « scripting embarqué » pour Besiege

> Objectif du mod final : permettre au joueur de **scripter ses machines** — lire des
> capteurs (positions, vitesses, état des blocs, touches), commander des actionneurs
> (moteurs, pistons, canons…) et écrire ses propres **boucles de contrôle** (PID,
> automates, robotique), avec un langage de script sûr et sans recompilation.

Date : 2026-10-13 · Jeu : Besiege v1.4+ (Unity 5.4.0f3, Mono CLR 2.0, Steam Linux) ·
Mod loader : **officiel, intégré** (doc : `Besiege_Data/Documentation/Modding.pdf`, extrait dans `docs/modding-doc.txt`)

---

## 1. Vision du mod

Le joueur écrit des scripts (ex. Lua) qui s'exécutent pendant la simulation :

```lua
-- exemple : maintenance d'altitude à PID
local gyro = machine:find("Gyroscope")
local balloon = machine:find("Ballon")

function on_tick()
    local alt = machine.position.y
    local cmd = pid(50 - alt)            -- consigne : altitude 50
    balloon:set_input(" Monter", cmd > 0)
end
```

Architecture cible :

```
┌────────────── Jeu Besiege ──────────────┐
│  Mod loader officiel                    │
│   └─ Mod Scripting (Assembly-CSharp API) │
│       ├─ Moteur de script embarqué      │
│       │   (interpréteur tiers sandboxé) │
│       ├─ Pont « capteurs »  ← doc API   │
│       ├─ Pont « actionneurs »           │
│       │   (émulation de touches native) │
│       └─ Persistance scripts (ModIO)     │
└─────────────────────────────────────────┘
```

## 2. Constats (doc officielle — confirmés)

### 2.1 Ce que le mod loader offre déjà

| Besoin | Réponse vanilla | Détail |
|---|---|---|
| Savoir quand la sim démarre/s'arrête | `Events.OnMachineSimulationToggle` | classe `Modding.Events`, hooks statiques |
| Réagir aux blocs | `Events.OnBlockPlaced/Removed/Init` | + `OnLevelLoaded`, `OnMachineLoaded`… |
| **Lire** l'état d'un bloc | `Modding.Blocks.Block` | `GameObject` (→ transform, rigidbody, composants Unity), `Health`, `IsBurning`, `InWind`, `Machine`, `Guid` |
| État global | `Modding.Game` | `IsSimulating`, `IsSimulatingLocal/Global`, raycast souris |
| **Commander** des blocs vanilla | **émulation de touches** | système natif du jeu : un bloc moddé peut « presser » une touche, tous les blocs qui y répondent réagissent ; tick `FixedUpdate` dédié (`SendKeyEmulationUpdateHost`), host uniquement |
| Boucle par-frame | classe `BlockScript` (bloc moddé) | `BuildingUpdate` / `SimulateUpdateHost` (+ variants Fixed/Late) |
| Fichiers (scripts utilisateur) | `Modding.ModIO` | sandboxé au dossier du mod + dossier « data » persistant (survit aux MAJ workshop) |
| Console / commandes | `ModConsole.Log`, `RegisterCommand` | commande par mod, préfixée `modname:` si conflit |
| Intégration logique vanilla | triggers/events moddés | `ModEvents.RegisterCallback`, propriétés UI (TextInput, NumberInput, Choice, Picker…) |
| Multiplayer | émulation = host-only ; API `ModNetworking` | le simulateur (host en global sim) exécute le script → cohérent |

### 2.2 Contraintes (liste noire du mod loader, doc « Security »)

Interdits aux mods (vérifiés au chargement) : `System.IO` (remplacé par ModIO), `System.Net`,
`System.Xml` (sauf flux), **`System.Reflection`**, `System.Runtime.InteropServices`,
`System.Diagnostics` (sauf Stopwatch/Crypto), `Mono.CSharp`, `Mono.Cecil`,
`System.CodeDom.Compiler`, CSharpCompiler, IKVM, `UnityEngine.WWW`, `InternalModding`.

Conséquences directes pour le choix du moteur de script :
- ❌ pas de compilation C# à runtime par le mod ;
- ❌ pas d'interop par réflexion pour exposer des objets au langage de script ;
- ✅ il faut un interpréteur **pur** (parse + arbre syntaxique exécuté), sandboxable.

### 2.3 Cycle de vie d'un mod

- Charge à l'entrée dans un niveau/multiverse (ou au lancement si `<LoadInTitleScreen />`).
- `<Debug>true</Debug>` : reload à chaud de valeurs XML (colliders, transforms) au changement de fichier.
- GUID inséré dans `Mod.xml` au premier chargement (à commiter).
- Build : `mcs` (Docker `mono:6.12`) contre les DLL du jeu — chaîne déjà validée par HelloMod.

## 3. Réponses aux questions ouvertes (décompilation + doc)

### Q1 — Mécanisme exact de la liste noire ✅

`InternalModding.Assemblies.AssemblyScanner` (`decomp/Assembly-CSharp/InternalModding.Assemblies/AssemblyScanner.cs`) :
- scan **statique de l'IL via Mono.Cecil au chargement** du mod ;
- **récursif** : toute DLL référencée non déjà chargée dans le domaine est chargée en
  ReflectionOnly et **scannée à son tour** (donc un moteur de script embarqué dans le mod
  subit le même filtrage) ;
- détection au niveau **instruction** : champs, variables locales, `ldfld`, `call/callvirt/newobj`,
  tableaux (`newarr`), `ldtoken`, arguments génériques, types imbriqués, **P/Invoke interdit** ;
- liste noire par **préfixe de namespace** : `System.IO`, `System.Net`, `System.Xml`,
  `System.Reflection`, `System.Runtime.InteropServices`, `System.Diagnostics`,
  `System.Security`, `Mono.CSharp`, `Mono.Cecil`, `System.CodeDom.Compiler`,
  `CSharpCompiler`, `IKVM`, **`Microsoft`**, `Mono.CompilerServices`, `UnityEngine.WWW`,
  `UnityEngine.MasterServer`, `ModIO`, `PlayFab`, `Steamworks`, `GameGrind`,
  `InternalModding`, `BesiegeDlc` ;
- exceptions whitelist : `Stream`, `Text{Writer,Reader}`, `Binary{Writer,Reader}`,
  `MemoryStream`, `Path`, `SeekOrigin`, `Stopwatch`, `Cryptography` ;
- méthodes interdites nommément : `AssetBundle.LoadFromFile`, `Application.OpenURL/Quit/…`.

**Conséquence** : le moteur de script embarqué ne doit contenir **aucune** référence IL à ces
namespaces (ni reflection, ni CodeDom, ni `Microsoft.*`). Un moteur à interop réflexion est
inutilisable en l'état ; il faut un interpréteur pur, ou une compilation amputée de sa couche
reflection (à évaluer en Q3).

### Q2 — Émulation de touches sans bloc moddé ✅ → non, bloc « microcontrôleur » retenu

- `BlockBehaviour.EmulateKeys(MKey[] activationKeys, MKey emulateKey, bool emulate)` :
  `protected`, exige `SimPhysics` (le bloc simule) et un `KeyInputController`
  (composant attaché au GameObject **machine**, cf. `Machine.cs:1055`).
- `KeyInputController.Emulate(BlockBehaviour source, …)` : l'émulation est toujours
  attribuée à une instance de bloc de la machine — pattern identique chez les blocs vanilla
  (`LogicGate.cs` : `EmulateKeys(activationKeys, EmulateKey, emulate)`, LED d'état, etc.).
- Côté mod, l'API officielle est complète (`Modding/ModBlockBehaviour.cs`) :
  - `AddEmulatorKey(displayName, key, defaultKey)` → touche « émulable » dans le mapper ;
  - `EmulateKeys(MKey[] activationKeys, MKey emulateKey, bool emulate)` (wrapper public,
    exige `EmulatesAnyKeys => true`) ;
  - hooks de timing : `KeyEmulationUpdate()` (tick fixe, toutes instances) et
    `SendKeyEmulationUpdateHost()` (host uniquement, appel recommandé pour émuler) ;
  - cycle de vie complet : `Building/Simulate{Update,FixedUpdate,LateUpdate}{Always,Host,Client}`,
    `OnSimulateStart/Stop`, collisions/triggers pendant la sim.

**Décision d'architecture** : le mod expose un **bloc « Microcontrôleur »** à poser sur la
machine. Le script est lié à la machine (bon cycle de vie), le mapper natif sert d'UI
(choix du script, paramètres), et le pattern est celui des blocs vanilla. Contrainte
acceptée : pas de script sans bloc contrôleur posé (comme un Arduino dans la machine).

### Q5 — Lecture des entrées joueur ✅

- `KeyInputController` (public) : `IsPressed/IsHeld/IsReleased(KeyCode)` — état des touches
  **au niveau machine**, agrégé depuis tous les blocs ;
- par bloc : `MKey.IsPressed/IsHeld/IsReleased`, `MKey.EmulationPressed/Held/Released()`
  (pour distinguer saisie réelle vs émulée) ;
- `UnityEngine.Input` n'est pas dans la liste noire (seuls `UnityEngine.WWW` et
  `MasterServer` le sont) — accès direct au clavier possible côté mod.

### Q6 — Multiplayer ✅ (partiellement, suffisant pour l'étude)

- l'émulation est exécutée côté **host/simulateur** (`SendKeyEmulationUpdateHost`) —
  cohérent en global sim (le serveur simule) et local sim ;
- le mod déclare `<MultiplayerCompatible>` ; si le script n'utilise que l'émulation et la
  lecture d'état, il peut être déclaré compatible ;
- l'API `ModNetworking` (messages typés entre clients) existe si un jour le script doit
  synchroniser de l'état custom.

### Bonus découverts en décompilant

- **Commande directe des actionneurs sans touches** : `SaveableDataHolder.MapperTypes`
  (public, sur tout `BlockBehaviour`) expose les `MSlider`/`MKey`/`MMenu`… et
  `MSlider.Value` a un setter public qui notifie le bloc (`InvokeValueChanged`).
  Le script peut donc lire/écrire à chaud les paramètres de n'importe quel bloc
  (ex. vitesse moteur, limite d'angle). ⚠️ non networké en MP — l'émulation de touches
  reste le canal MP-safe ;
- **Persistance par bloc dans la machine** : `OnSave/OnLoad(XDataHolder)` sur
  `ModBlockBehaviour` → le script/paramètres du microcontrôleur sont sauvés **dans le
  fichier machine** (pas besoin de fichiers externes pour les données par machine) ;
- **Capteurs géométriques** : `Rigidbody` public par bloc, `PlayerMachine` accessible,
  callbacks `OnSimulateCollision{Enter,Stay,Exit}` / `OnSimulateTrigger*` → contacts
  physiques, capteurs de collision ;
- `ModIO` (public) : lecture/écriture de fichiers dans le dossier du mod + dossier
  « data » persistant → stockage des scripts utilisateur ;
- `ModConsole.RegisterCommand(name, handler(string[]), help)` : commandes console
  (préfixées automatiquement si conflit) — idéal pour `script reload`/`script list`.

### Q3 — Moteur de script compatible CLR 2.0 ET le filtre du scanner ✅

**Éliminés d'office** : NLua/KeraLua/LuaInterface (P/Invoke natif → le scanner rejette tout
P/Invoke), Jurassic (compile en IL via `Reflection.Emit`), IronPython/DLR (Emit), Jint
(2.x min net40 ; 3.x/4.x net462+), ClearScript (V8 natif), toute compilation C# runtime
(bannie explicitement), `Microsoft.*` (banni par préfixe).

**Recommandation : MoonSharp v2.0.0.0 (à pinner — la branche 3.0 beta abandonne .NET <4.5)** :
- Lua 5.2, interpréteur **bytecode pur** : cible `net35` documentée + zéro `Reflection.Emit`,
  zéro `DllImport`, zéro `System.Net`/`Xml` (audité sur le tag v2.0.0.0) ;
- la réflexion est **confinée à la couche d'interop CLR optionnelle** → **build élaguée**
  requise (~15 fichiers retirés : `IoModule`, `LoadModule`, `CoreLib/IO/*`,
  `Loaders/FileSystem|UnityAssets|…`, `Interop/StandardDescriptors`, `Interop/UserDataRegistries`,
  `Serialization/ObjectValueConverter`, `StandardPlatformAccessor`→`LimitedPlatformAccessor`,
  dossier `REPL` ; ~3 fichiers patchés : `CallbackFunction.FromMethodInfo`, `DataTypes/UserData`,
  `ModuleRegister` ; compiler **sans `DEBUG`**) ;
- sandbox par whitelist de modules : `Preset_SoftSandbox` (pas d'`io`/`load`, garde
  coroutines/metadata/`os.time`/JSON) ;
- exposition d'API **100 % sans réflexion** : `new CallbackFunction((ctx, args) => …)` +
  `DynValue`, jamais `UserData.RegisterType` ;
- nos propres usages `System.IO` du noyau : uniquement des types **whitelistés**
  (`BinaryReader/Writer`, `MemoryStream`, `Stream`, `Path`) ;
- détails : attribution du scanner — les attributs custom ne sont pas scannés (tables
  metadata) et `Debug.Assert` disparaît en build release.

**Plan B : UniLua** (Lua 5.2, MIT, 37 fichiers, 0 DllImport, retirer juste `LuaFFILib.cs`) —
avec une **preuve in-game décisive** : le *Lua Scripting Mod* d'ilyshk4 (Workshop 2383785201,
2021, multi, émulation de touches, `set_slider`, infos blocs, raycasts, scripts embarqués
dans le `.bsg`, éditeur in-game) tourne sur le loader officiel avec UniLua. C'est le
**précédent exact** de ce projet — à étudier en premier (wiki : `github.com/ilyshk4/luamod-wiki`).
MoonSharp reste préféré pour son confort (coroutines natives pour les boucles de contrôle,
sandbox par whitelist, débogueur VSCode en dev) ; UniLua est le choix de sûreté (déjà prouvé
dans Besiege) et un excellent modèle pour notre couche capteurs/actionneurs.

**Pourquoi pas Python ?** (question posée à l'étude — réponse : triple bloqueur technique) :
- IronPython (le seul Python .NET sérieux) repose sur le DLR → compile les scripts en IL
  **à l'exécution** via `System.Reflection.Emit` → banni par le préfixe `System.Reflection`
  de la liste noire ;
- de surcroît IronPython dépend des assemblies `Microsoft.Scripting`/`Microsoft.Dynamic`
  → préfixe `Microsoft` banni (double KO, même en contournant le premier point) ;
- Python embarqué natif (Python.NET/CPython) = DLL natives + `DllImport` → rejetés
  (`HasPInvokeInfo`) ; aucun interpréteur Python pur-managé mature n'existe pour CLR 2.0 ;
- même en réussissant : la sandbox interdit `os`/`sys`/`io`/imports arbitraires → il ne
  resterait que la syntaxe, sans stdlib ni pip ;
- précédents : LenchScripterMod (Python/IronPython) n'a existé que sous spaar (pré-scanner) ;
  ilyshk4, confronté au même loader officiel, a choisi Lua.

Autres précédents : LenchScripterMod (Python/IronPython, **ère spaar — serait rejeté
aujourd'hui** à cause du DLR), AdvancedControlsMod (idem + SDL2), Lachcim/Rbtwrrr
Scripter, L3tum/BesiScript (transpilation *offline* vers blocs logiques vanilla — approche
différente, sans moteur runtime), Logic extensions mod (Workshop 2006662730).

### Q4 — Performance ✅ (réponse partielle, bench au M0)

L'émulation vanilla est cadencée à 50 fps (v1.02). Un interpréteur pur (MoonSharp/UniLua)
à quelques centaines d'appels par tick est négligeable pour du contrôle machine — à
confirmer par un benchmark au M0, en `SimulateFixedUpdateHost` (50 Hz).

### Bonus — valeur ajoutée vs vanilla

Le système logique vanilla (v1.00→v1.26 : Sensor, Altimeter, Speedometer, Anglometer, Timer,
Logic Gates, latch, Binary Counter, Random, variables, Length Detector, Edge Detector)
**ne couvre pas** : GPS/accéléro/gyro, vitesse *relative*, chaînage de blocs par clé de
référence, PID/boucles de contrôle — exactement l'apport du mod de scripting.

## 4. Architecture retenue (v0)

- **Bloc moddé « Microcontrôleur »** (décision Q2) : le joueur pose un bloc script sur sa
  machine ; son `BlockScript` monte le runtime, expose `on_tick`, gère les canaux
  d'émulation. Avantages : cycle de vie lié à la machine, MP-friendly (host simule),
  UI mapper native (choix du script, paramètres), persistance intégrée au fichier machine.
- **Runtime script** : **MoonSharp v2.0.0.0 en build élaguée** (voir Q3), embarqué dans le
  dossier du mod (DLL), sandbox `Preset_SoftSandbox` (pas d'IO dans le langage, budget
  d'instructions par tick), binding manuel `CallbackFunction`/`DynValue` sans réflexion.
- **API script** (surface minimale v0) :
  - lecture : `machine`, `blocks()`, `block.position/rotation/velocity/health/burning`,
    `block.slider(name)`, `input.is_down(key)`, contacts (`on_collide`)…
  - action **binaire** : `emulate(channel, bool)` — le canal peut être une touche **ou un
    message nommé** : les `MKey` vanilla peuvent écouter des « variables » textuelles
    (`MKey.useMessage`/`message[]`, dictionnaire `KeyInputController.usedMessages`), et
    l'émulation vise aussi les messages (`KeyInputController.Emulate` gère `useMessage`) →
    **canaux nommés illimités**, pas limités au clavier (système v1.10) ;
  - action **analogique** : `set_slider(block, name, value)` — confirmé : les blocs
    vanilla relisent leurs sliders **en continu pendant la sim** (ex. moteur
    `CogMotorControllerHinge` : `Velocity = input * speedSlider.Value` chaque tick) →
    contrôle proportionnel (sortie de PID → vitesse). ⚠️ sémantique par bloc : certains
    sliders ne sont lus qu'au démarrage de la sim → documenter les sliders « live » ;
  - `print()` → console en jeu (`ModConsole`) ; erreurs de script → console + pas de crash.

### Hot reload (workflow de dev)

- **Scripts Lua = données → rechargeables à chaud, sans relancer le jeu** :
  - commande console `script reload` (réexport du moteur : `ModIO.ReadAllText` +
    nouvelle instance `Script` + `DoString`) ;
  - rechargement automatique à chaque début de simulation (état frais à chaque run) ;
  - auto-recharge à la sauvegarde possible en **pollant** le contenu via
    `ModIO.ReadAllText` (comparaison de chaîne, coût négligeable) — `FileSystemWatcher`
    est inaccessible (namespace `System.IO` interdit, `ModIO` n'expose pas les
    timestamps) ;
- **DLL du mod (C#) : PAS rechargeable** — les assemblies ne se déchargent pas sous
  Mono (cf. doc : mod SP-only ⇒ redémarrage avant MP). Le watcher du loader
  (`InternalModding.Loading/ModReloading.cs`) ne surveille que `*.xml` (valeurs, mode
  Debug, touche `Ctrl+H`) et son évènement est dans `InternalModding` (interdit aux mods).
  → modifier le C# du mod = redémarrer le jeu ; modifier un script Lua = rien à relancer.
- **Persistance** : script texte via `ModIO` dans le dossier data du mod ; données par
  machine via `OnSave/OnLoad` du bloc contrôleur (piste à étudier : scripts embarqués
  dans le `.bsg` comme le fait le mod Lua d'ilyshk4) ; éditeur externe (VS Code) +
  commandes console `script reload` / `script list`.

## 6. Verdict

**GO.** Tous les mécanismes côté jeu sont confirmés (doc officielle + code décompilé) :
capteurs, actionneurs (émulation native + sliders directs), persistance, console, multi.
Le risque Q3 est levé par la recherche : **MoonSharp v2.0.0.0 en build élaguée** (recommandé)
ou **UniLua** (plan B déjà prouvé in-game par le mod Lua d'ilyshk4). La seule validation
restante est empirique : faire passer la DLL du moteur par le vrai `AssemblyScanner` (M0).

Règles d'or imposées par le scanner (à respecter dans tout le code du mod) :
1. jamais de réflexion dans notre code — binding manuel via `CallbackFunction`/`DynValue` ;
2. IO uniquement via `Modding.ModIO` ; pas de DLL native ; build **release** (sans `DEBUG`) ;
3. namespaces du mod ne commençant ni par `ModIO`, ni par `Microsoft` ;
4. compiler le moteur et le mod contre les DLL du jeu (`mcs -nostdlib -noconfig`, CLR 2.0).

## 7. Jalons proposés

1. **M0 — POC moteur de script (GO/NO-GO définitif)** :
   a. cloner MoonSharp tag `v2.0.0.0`, appliquer l'élagage documenté ci-dessus, compiler
      en DLL CLR 2.0 via Docker (`mcs`) ;
   b. mod sandbox qui charge la DLL et exécute `print("hello")` dans le jeu → vérifier
      que l'`AssemblyScanner` accepte la DLL (sinon : itérer, plan B UniLua) ;
   c. micro-benchmark tick 50 Hz.
2. **M1 — Bloc microcontrôleur vide** : bloc moddé (mesh cube OBJ minimal), mapper avec
   choix de script, `on_tick` loggué à 50 Hz.
3. **M2 — API capteurs** : machine/blocks/position/vitesse/health + `print`.
4. **M3 — API actionneurs** : `emulate(channel, bool)` via `AddEmulatorKey` (touches **et
   messages nommés**) + test sur moteur vanilla ; puis `set_slider(block, name, value)`
   (analogique) + hot reload (`script reload`, recharge au début de chaque sim).
5. **M4 — Persistance + commandes console** (`script reload/list`), gestion d'erreurs,
   budget d'instructions.
6. **M5 — Durcissement** : sandbox moteur (`Preset_SoftSandbox`), limites de tick, doc joueur,
   étude du mod Lua d'ilyshk4 pour les patterns éprouvés (raycasts, scripts dans le `.bsg`).

## Annexe — sources externes clés

- MoonSharp : `github.com/moonsharp-devs/moonsharp` (tag v2.0.0.0), `moonsharp.org`, NuGet
  `MoonSharp/2.0.0` (cible net35)
- UniLua : `github.com/xebecnan/UniLua` (MIT)
- Lua Scripting Mod (précédent direct) : Workshop 2383785201, wiki `github.com/ilyshk4/luamod-wiki`
- Historique vanilla : `besiege.fandom.com/wiki/Version_History`
- Moteurs écartés : NLua, Jint (net40 min), Jurassic (Emit, issue #15), KopiLua (dormant)
