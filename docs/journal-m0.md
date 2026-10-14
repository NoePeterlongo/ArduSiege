# Journal M0 — POC moteur de script (MoonSharp élagué)

> Objectif M0 (voir `docs/etude-faisabilite-scripting.md` §7) : faire passer une DLL
> MoonSharp **élaguée** par l'`AssemblyScanner` du mod loader, exécuter `print("hello")`
> depuis Lua en jeu, benchmarker le tick 50 Hz, et prouver la watchdog (IDebugger).
> Ce journal retrace **chaque étape, décision et découverte** pour reprise par un autre agent.
> Convention : les preuves sont `fichier:ligne` (sources dans `third-party/moonsharp-trimmed/`,
> `decomp/` = jeu décompilé).

## État : EN COURS

---

## Étape 1 — Récupération des sources MoonSharp (2026-10-14)

- Clone `git clone --depth 1 --branch v2.0.0.0 https://github.com/moonsharp-devs/moonsharp`
- Commit amont : `3154416e535ab96c0d52bf12e3e472985a1532f4` (« RC for 2.0.0.0 »).
- **Découverte clé** : `src/MoonSharp.Interpreter/MoonSharp.Interpreter.net35-client.csproj`
  liste déjà **247 fichiers** (sur 500) pour la cible net35 → c'est le sous-ensemble net35
  officiel en amont. Le trim part donc de cette liste (extraite dans
  `third-party/moonsharp-net35-files.txt`), pas de l'arborescence complète.
- Pas de DefineConstants spéciales pour net35 (juste TRACE/DEBUG) → on compile **sans
  DEBUG** (impératif : `System.Diagnostics.Debug` n'est PAS whitelisté par le scanner ;
  les appels `[Conditional("DEBUG")]` disparaissent à la compilation).
- Le dossier `src/MoonSharp.Interpreter/` contient 500 fichiers : ~253 sont pour d'autres
  cibles (PCL, dotnet-core, MonoGame…), non retenus par le csproj net35.

## Étape 2 — Trim initial : 247 → 204 fichiers

Base : copie des 247 fichiers du csproj net35 dans `third-party/moonsharp-trimmed/`.
Retraits (43 fichiers) — tous liés à des APIs interdites par le scanner (voir étude §Q1) :

- `CoreLib/IoModule.cs`, `CoreLib/LoadModule.cs`, `CoreLib/IO/*` (5 fichiers) →
  bibliothèques Lua `io`/`file`/`load` (accès fichiers + compilation de code).
- `Loaders/{FileSystemScriptLoader,UnityAssetsScriptLoader,EmbeddedResourcesScriptLoader}.cs`
  → chargeurs de fichiers.
- `Serialization/ObjectValueConverter.cs` → conversion réflexion CLR↔Lua.
- `REPL/*` (3 fichiers).
- `Interop/StandardDescriptors/**` (23 fichiers) → descripteurs interop par réflexion.
- `Interop/UserDataRegistries/**` (2 fichiers) → registre de types interop réflexion.
- `Interop/LuaStateInterop/**` (5 fichiers) → compat C-API Lua.

⚠️ Distinction importante : le dossier **`IO/`** (à la racine de MoonSharp.Interpreter :
`BinDumpBinaryReader/Writer`, `UndisposableStream`) est **conservé** — c'est la sérialisation
bytecode interne (types whitelistés : `BinaryReader/Writer`, `MemoryStream`, `Stream`),
n'a rien à voir avec `CoreLib/IO/` (bibliothèque `io` de Lua). Ne pas confondre lors d'un
re-trim. `Script.LoadStream/DoStream/Dump` en dépendent.

## Étape 3 — Inventaire des usages interdits restants (avant patch)

Grep sur les 204 fichiers (`using System.Reflection|System.Reflection\.`, `Linq.Expressions`,
`DllImport|InteropServices`, `Diagnostics` non-Stopwatch) :

**Réellement à patcher (usages IL) :**

| Fichier | Problème | Plan |
|---|---|---|
| `DataTypes/UserData.cs` | registre interop réflexion (`TypeDescriptorRegistry`, retiré) | réécrire en stub (tout jeter NotSupportedException) |
| `DataTypes/CallbackFunction.cs` | `FromDelegate`/`FromMethodInfo`/`CheckCallbackSignature` (MethodInfo) | supprimer ces 3 méthodes (le ctor par délégué reste — c'est NOTRE chemin) |
| `Interop/Converters/ClrToScriptConversions.cs` | fallback réflexion pour objets CLR arbitraires | ne garder que primitifs/string/Table/DynValue, sinon exception |
| `Interop/BasicDescriptors/ParameterDescriptor.cs` | ctor depuis `ParameterInfo` | supprimer le ctor réflexion |
| `Interop/DescriptorHelpers.cs` | helpers réflexion (MethodInfo/FieldInfo/PropertyInfo) | supprimer le fichier (utilisateurs retirés) |
| `Interop/PropertyTableAssigner.cs` | réflexion pure (PropertyInfo…) | supprimer (plus d'utilisateurs) |
| `Interop/BasicDescriptors/DispatchingUserDataDescriptor.cs` | utilisé par StandardDescriptors (retirés) ; référence `Framework.Do` | supprimer si orphelin à la compilation |
| `Modules/ModuleRegister.cs` | **enregistre les modules CoreLib PAR RÉFLEXION** (GetMethods/GetFields/GetCustomAttributes + Delegate.CreateDelegate) | ⭐ voir étape 4 : génération hors-ligne |
| `Platforms/PlatformAutoDetector.cs` | `System.Linq.Expressions` (sonde AOT), `AppDomain.GetAssemblies` (réflexion), refs vers loaders retirés | réécrire en valeurs codées en dur |
| `Platforms/StandardPlatformAccessor.cs`, `Platforms/DotNetCorePlatformAccessor.cs` | chemin standard/IO ; DotNetCore vide sans define | supprimer ; hardcoder `LimitedPlatformAccessor` |
| `Compatibility/Framework.cs` + `Compatibility/Frameworks/**` | couche d'abstraction réflexion (FrameworkCLR) | supprimer ; remplacer les usages par des appels directs |
| `Serialization/Json/JsonNull.cs` | passe par `UserData.CreateStatic` (registre réflexion) | voir décision D-M0-1 ci-dessous |
| `Interop/Converters/TableConversions.cs` | `Framework.Do.IsAssignableFrom` (wrapper) | appels directs `Type.IsAssignableFrom` (autorisé : Type est dans `System`, pas `System.Reflection`) |
| `Interop/Converters/ScriptToClrConversions.cs` | idem + chemins userdata/enum réflexion | appels directs Type.* (booléens) ; jeter les chemins réflexion |
| `Tree/Lexer/LexerUtils.cs` | `Framework.Do.StringContainsChar` | remplacer par `str.IndexOf('\\') >= 0` |

**Faux positifs vérifiés (aucun patch) :**

- `Execution/VM/Processor/Processor_Debugger.cs` : les matches `Debug.` sont le champ
  `m_Debug` (DebugService de MoonSharp, code maison) — pas `System.Diagnostics`.
- `Compatibility/Frameworks/FrameworkWin8.cs` / `FrameworkCore.cs` /
  `Platforms/DotNetCorePlatformAccessor.cs` : entièrement sous `#if NETFX_CORE` /
  `#if DOTNET_CORE` → **vides** sans leurs defines.
- `Properties/AssemblyInfo.cs` : uniquement des **attributs d'assembly** (metadata, pas
  d'IL) → le scanner ne les lit pas (il scanne les corps de méthodes/champs : voir
  `AssemblyScanner.ScanType`). Conservation OK. Idem tous les attributs
  `[MoonSharpModuleMethod]` etc. : ce sont des tables metadata.
- `Diagnostics/PerformanceCounters/*`, `Compatibility/Stopwatch.cs` : `Stopwatch` est
  **whitelisté** par le scanner.

**Décisions M0 :**

- **D-M0-1 (JSON)** : `JsonNull` existe pour distinguer `null` de l'absence de clé
  (`table.Set("a", nil)` supprime la clé). Pour éviter le registre interop réflexion :
  divergence acceptée et documentée — `null` JSON → `nil` (perte de la distinction
  null/absent dans `json.parse`). Si nécessaire plus tard : descripteur manuel.
- **D-M0-2 (règle générale)** : les chemins interop morts (CLR→Lua d'objets arbitraires)
  lèvent `NotSupportedException` au lieu d'être retirés quand ils sont appelés par du code
  vivant (ex. `DynValue.FromObject` → `ClrToScriptConversions`). Notre mod ne passe
  jamais d'objets CLR exotiques : uniquement primitifs + chaînes + Table.

## Étape 4 — Le problème ModuleRegister, et sa solution (découverte structurante)

`ModuleRegister.RegisterCoreModules` est le chemin qui installe les bibliothèques
standard (basic, string, math, table, coroutine, error handling, json, os.time…) dans
`Script.Globals`. Il fonctionne **entièrement par réflexion** : itère `GetMethods()` +
`GetCustomAttributes([MoonSharpModuleMethod])`, crée des delegates via
`Delegate.CreateDelegate`, et charge des **champs constants Lua** (des sources Lua
pré-écrites, ex. patterns de `string`) via `LoadFunction`. Le projet amont
`MoonSharpPreGen` (génération de descripteurs hardwired) n'est **qu'un stub** (affiche
l'usage) : aucune génération prête à réutiliser.

**Solution retenue : génération hors-ligne.** La réflexion est interdite *au runtime
dans le jeu*, pas à nos heures de build :

1. compiler dans Docker (mono:6.12) une DLL MoonSharp **complète** (les 247 fichiers
   net35, non patchés) — utilisée uniquement comme input du générateur ;
2. exécuter un **générateur** (programme C# maison, `tools/moonsharp-gen/`) qui réplique
   la logique de `ModuleRegister` par réflexion et **émet un fichier C# généré**
   (`ModuleRegister.g.cs`) : namespaces des modules, callbacks `DynValue.NewCallback(ModuleClasse.methode, "nom")`,
   appels `LoadFunction` pour les champs-Lua (valeurs intégrées comme littéraux),
   constantes, appels `MoonSharpInit` — zéro réflexion dans le code généré ;
3. inclure ce fichier généré dans le build élagué ; `ModuleRegister` est réécrit pour
   router `RegisterModuleType<T>` vers le registre généré.

Le fichier généré est **commité** (reproductibilité, pas de dépendance au réseau au
build). La regeneration nécessite le tarball amont (voir `third-party/README.md`).

## Étape 5 — Patches chirurgicaux (réalisés)

Tous appliqués dans `third-party/moonsharp-trimmed/` (fichiers marqués `[TRIMMED — mod
ScriptingMod Besiege]`) :

| Fichier | Nature du patch |
|---|---|
| `Platforms/PlatformAutoDetector.cs` | **réécrit** : valeurs codées en dur (Unity Mono, AOT=true, LimitedPlatformAccessor + InvalidScriptLoader) — la détection amont utilisait `AppDomain.GetAssemblies` + `System.Linq.Expressions` |
| `Platforms/StandardPlatformAccessor.cs`, `Platforms/DotNetCorePlatformAccessor.cs` | **supprimés** |
| `Compatibility/Framework.cs` + `Compatibility/Frameworks/**` | **supprimés** (couche d'abstraction réflexion) — usages remplacés par appels `System.Type.*` directs (autorisés : `Type` est dans `System`, pas `System.Reflection`) |
| `DataTypes/CallbackFunction.cs` | `FromDelegate`/`FromMethodInfo`/`CheckCallbackSignature` supprimés (MethodInfo) — le ctor par délégué reste le chemin du mod |
| `DataTypes/UserData.cs` | **réécrit** : payload conservé (`Object`/`Descriptor`/`UserValue`) ; registre réflexif remplacé par le chemin `IUserDataType` → `AutoDescribingUserDataDescriptor` ( indispensable : la VM l'utilise pour les itérateurs `for` génériques via `EnumerableWrapper`, cf. `Processor_InstructionLoop.cs:516`) ; `Register*` → `NotSupportedException` ; signature de `RegisterAssembly` amputée de `System.Reflection.Assembly` (aucun appelant restant) |
| `Interop/StandardDescriptors/AutoDescribingUserDataDescriptor.cs` | **restauré depuis l'amont** + `IsTypeCompatible` en `Type.IsInstanceOfType` direct (seul usage Framework de ce fichier) |
| `Interop/LuaStateInterop/**` (6 fichiers) | **restaurés depuis l'amont** — la recherche les avait listés « à retirer » à tort : ils sont le support du port KopiLua des patterns `string.find/match/gsub` (`StringModule.EmulateClassicCall(Func<LuaState,int>)`). Vérifiés scanner-clean (pur C#). |
| `Interop/BasicDescriptors/ParameterDescriptor.cs` | **restauré** + ctor `ParameterInfo` supprimé + `RestrictType` en appel direct — nécessaire car `IOverloadableMemberDescriptor.Parameters` le référence |
| `Interop/BasicDescriptors/DispatchingUserDataDescriptor.cs`, `Interop/DescriptorHelpers.cs`, `Interop/PropertyTableAssigner.cs` | **supprimés** (utilisateurs interop réflexifs retirés) |
| `Interop/Converters/ClrToScriptConversions.cs` | **réécrit** : primitifs/string/Closure/Table/CallbackFunction + IList/IDictionary→Table + enums ; delegates arbitraires et UserData-régistry retirés (D-M0-2) |
| `Interop/Converters/ScriptToClrConversions.cs` | `Framework.Do.*` → `Type.*` directs (tous booléens) ; reste inchangé |
| `Interop/Converters/TableConversions.cs` | idem + `GetGenericArguments` direct (retourne `Type[]`, autorisé) |
| `Tree/Lexer/LexerUtils.cs` | `Framework.Do.StringContainsChar` → `str.IndexOf('\\')` |
| `Serialization/Json/JsonNull.cs` | **réécrit** (D-M0-1) : null JSON → nil ; `Create()` → `DynValue.Nil` |
| `Serialization/Json/JsonTableConverter.cs` | `ObjectToJson(object)` → `NotSupportedException` (dépendait d'`ObjectValueConverter`, réflexion) ; `json.serialize` Lua reste fonctionnel via `ValueToJson` |
| `CoreLib/DebugModule.cs` | `debug.debug` (REPL interactif) → erreur propre (ReplInterpreter retiré du build) |
| `Modules/ModuleRegister.cs` | **réécrit** : route vers `HardwiredModuleRegister` (généré) ; `CreateModuleNamespace` conservé (bookkeeping `package.loaded`) ; flags IO/LoadMethods ignorés (fichiers retirés) |
| `Interop/RegistrationPolicies/DefaultRegistrationPolicy.cs` | using fantôme StandardDescriptors retiré |
| `Properties/AssemblyInfo.cs` | **conservé** : attributs = tables metadata, pas d'IL → non scannés |

## Étape 6 — Compilation Docker + itération (réalisée)

Commandes : `bash tools/moonsharp-build.sh [--regen]` (mono:6.12, mcs, `-nostdlib -noconfig`
contre les DLL du jeu ; `--regen` reconstruit aussi la DLL complète + relance le générateur).

**Journal d'erreurs et résolutions** (ordre chronologique) :

1. Tarball GitHub du tag `v2.0.0.0` s'extrait dans `moonsharp-2.0.0.0` (le « v » est
   retiré) → chemin corrigé dans le script.
2. Générateur : accolade orpheline après édition → fix.
3. DLL MoonSharp **complète** (247 fichiers net35, non patchée) : compile **du premier
   coup** — la cible net35 amont est saine avec les DLL du jeu.
4. DLL élaguée — erreurs restantes, par vagues :
   - `KopiLua_StrLib.cs` : `CharPtr`/`LuaState`/`LuaLBuffer` introuvables → restauration
     de `Interop/LuaStateInterop` (cf. étape 5 — la recherche disait de le retirer,
     c'est le support des patterns string) ;
   - `IOverloadableMemberDescriptor` référence `ParameterDescriptor` → restauration
     patchée ;
   - `ScriptExecutionContext.cs:126` (`EmulateClassicCall`) → résolu par LuaStateInterop ;
   - `DefaultRegistrationPolicy.cs` using `StandardDescriptors` fantôme → retrait ;
   - `TableConversions.cs` using `MoonSharp.Interpreter.Compatibility` fantôme →
     retrait (namespace devenu vide : `Compatibility/Stopwatch.cs`+`Attributes.cs` sont
     intégralement `#if PCL/ENABLE_DOTNET`) ;
   - `JsonTableConverter.cs` → `ObjectValueConverter` retiré → stub (étape 5) ;
   - `DebugModule.cs` → `ReplInterpreter` retiré → stub de `debug.debug`.
5. **Générateur** — trois bugs à la première émission :
   - **zéro module trouvé** : filtre `t.IsAbstract` erroné (les modules MoonSharp sont des
     classes simples, pas static/abstract) → filtre corrigé ;
   - **`StringModule.byte`/`char`, `JsonModule.null`** : méthodes déclarées `@byte`/`@null`
     en amont (mots-clés C#) — la réflexion rend le nom SANS le `@` qu'il faut restituer
     dans le code généré (`MemberName()`) — et un `.` avalé par un sed a donné
     `JsonModule@null` (invalide) avant correction ;
   - **littéral `1.797…E+308`** (`math.huge` = `double.MaxValue`) : le parseur de mcs le
     rejette (« Floating-point constant is outside the range of type double ») → émission
     via `double.Parse("…", InvariantCulture)` à la place du littéral (registration à
     l'exécution, coût nul).
6. **Résultat : BUILD OK** — `mods/ScriptingMod/ScriptingMod/MoonSharp.Interpreter.dll`
   = **292 Ko**, 16 modules enregistrés sans réflexion (`ModuleRegister.g.cs`, 15 modules
   CoreLib + tables itératrices).

**Modules générés** : Basic, Bit32, Coroutine, Debug, Dynamic, ErrorHandling, Json, Math,
MetaTable, OsSystem, OsTime, String, TableIterators, Table, TableModule_Globals.
(IoModule et LoadModule exclus : fichiers retirés.)

**Mod POC** (`mods/ScriptingMod/`) : `Mod.xml` (2 assemblies), `WatchdogDebugger.cs`
(IDebugger-compteur : `IsPauseRequested` compte et **lève** au-delà du budget — vérifié :
la boucle VM ne catch que `InterpreterException`, `Processor_InstructionLoop.cs:26/249`,
donc l'exception watchdog traverse la VM et remonte à l'appelant), `ScriptingModEntryPoint.cs`
(hello Lua, check sandbox, commandes console `lua`/`bench`/`watchdog`), `build.sh`.
Compilation : BUILD OK (10,5 Ko). Fix : collision `Modding.DataType` (jeu) vs
`MoonSharp.Interpreter.DataType` → alias `MoonDataType`.

## Étape 7 — Contrôle statique anti-scanner (en cours)

Méthode : décompilation ILSpy de NOTRE DLL dans Docker (dotnet SDK), grep des namespaces
interdits dans le C# re-décompilé (proxy du scan Cecil de l'AssemblyScanner).
⚠️ `dotnet tool install ilspycmd` à chaque conteneur = lent ; à dockeriser avec un volume
outils si l'étape devient fréquente.

*(à compléter : résultats du grep, corrections éventuelles)*

## Étape 8 — Mod POC « ScriptingMod » + test en jeu

*(à compléter : Mod.xml à 2 assemblies, code C# du POC (hello, bench, watchdog),
résultats console, verdict scanner)*

## Étape 9 — Conclusions M0 et mise à jour des documents

*(à compléter : verdict GO/NO-GO définitif, chiffres bench, marge watchdog, mise à jour
étude/workflow/AGENTS.md)*
