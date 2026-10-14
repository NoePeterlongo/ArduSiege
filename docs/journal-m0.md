# Journal M0 — POC moteur de script (MoonSharp élagué)

> Objectif M0 (voir `docs/etude-faisabilite-scripting.md` §7) : faire passer une DLL
> MoonSharp **élaguée** par l'`AssemblyScanner` du mod loader, exécuter `print("hello")`
> depuis Lua en jeu, benchmarker le tick 50 Hz, et prouver la watchdog (IDebugger).
> Ce journal retrace **chaque étape, décision et découverte** pour reprise par un autre agent.
> Convention : les preuves sont `fichier:ligne` (sources dans `third-party/moonsharp-trimmed/`,
> `decomp/` = jeu décompilé).

## État : TERMINÉ (GO définitif — 2026-10-14)

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

## Étape 7 — Contrôle statique anti-scanner (RÉUSSI)

Méthode finale : `ikdasm` (image mono:6.12 — disponible dans l'image, contrairement à
monodis) sur les deux DLL, grep des tokens interdits dans l'IL. Plus rapide et plus
précis que le cycle ILSpy. Résultats après corrections :

| Token | MoonSharp.Interpreter.dll | ScriptingMod.dll |
|---|---|---|
| `MemberInfo::` (appels réels) | **0** | **0** |
| System.IO non-whitelist | **0** (restent : Stream, TextReader/Writer, BinaryReader/Writer, MemoryStream, Path — tous whitelistés) | 0 |
| System.Diagnostics hors Stopwatch | 0 (restent : attributes metadata seulement) | 0 |
| System.Net / System.Xml / Microsoft.* / Mono.Cecil | 0 | 0 |
| pinvokeimpl | 0 | 0 |

Les seules strings `System.Reflection`/`System.IO`/`System.Diagnostics` restantes dans
les DLL = **tables metadata** (attributes d'assembly `AssemblyTitle` etc.,
`DefaultMemberAttribute`, `ConditionalAttribute`, `DebuggerBrowsableAttribute`) que
l'`AssemblyScanner` ne lit pas (il scanne champs, locals et corps IL uniquement —
`AssemblyScanner.ScanType`).

### ⭐ Découverte critique du M0 : la règle `Type.Name`

`Type` **dérive de** `System.Reflection.MemberInfo` → **`type.Name` compile en
`callvirt System.Reflection.MemberInfo::get_Name()`** → le scanner l'interdit
(type déclarant `System.Reflection`, par préfixe). **`Type.FullName` en revanche est
déclaré sur `System.Type` → autorisé.** RÈGLE D'OR POUR TOUT LE CODE DES MODS :
*ne jamais utiliser `.Name` sur un `Type` — toujours `.FullName`.*

Sites corrigés : `UserData.GetDescriptorForType`, `ParameterDescriptor.ToString`,
`ScriptRuntimeException` (2 factories d'erreurs), `ModuleRegister.g.cs` + générateur,
`ScriptingModEntryPoint.WatchdogCommand`.

### Autres correctifs de cette étape

- `ByteCode.Dump(string)` ( MoonSharp ) : `File.WriteAllText` → `NotSupportedException`
  (dump debug inutilisé ; `System.IO.File` hors whitelist) ;
- `IO/BinDumpBinaryReader.cs` : `IOException` → `System.Exception` (IOException hors
  whitelist — utilisé dans un throw de la lecture bytecode binaire) ;
- générateur : émission `t.FullName` + re-généré ;
- divergence à documenter dans third-party/README.md : ByteCode.Dump leve.

Le verdict FINAL appartient à l'AssemblyScanner du jeu au chargement (étape 8).

## Étape 8 — Test en jeu, verdict scanner #1 (UNE violation, corrigée)

Résultat du premier test en jeu (l'utilisateur a relancé, OCR de la console) :

```
[Security] You are not allowed to use System.IO.StreamReader
   (used in MoonSharp.Interpreter.DynValue MoonSharp.Interpreter.Script::LoadStream(...))
[Security] Not loading MoonSharp.Interpreter.dll
Referenced assembly did not validate: MoonSharp.Interpreter → ScriptingMod pas charge
```

**Le scanner fonctionne exactement comme décodé** (erreur précise = type + méthode), et il
n'y avait qu'**UNE** violation : `System.IO.StreamReader` dans `Script.LoadStream`.

### Deuxième leçon whitelist (M0) — à retenir

La whitelist d'`AssemblyScanner` est une liste de **types pleinement qualifiés** :
`System.IO.Stream`, `TextReader`, `TextWriter`, `BinaryReader`, `BinaryWriter`,
`MemoryStream`, `Path`, `SeekOrigin`. **`StreamReader`/`StreamWriter` n'en font PAS
partie** (ce sont des classes concrètes distinctes de TextReader/TextWriter) :
*le fait qu'un nom de type CONTIENNE un nom whitelisté ne suffit pas — l'égalité doit
être exacte.* Mon contrôle ikdasm de l'étape 7 avait filtré par sous-chaîne
(`grep -vE "Stream|..."`) — trop lâche : `StreamReader` contenait « Stream » et est
passé. **Toujours comparer à la liste exacte** (la méthode fiable : uniq des tokens
`System.IO[.A-Za-z]*` dans l'IL, comparé au à la liste blanche, ou directement le
verdict du scanner en jeu).

### Correctif

`Script.cs` `LoadStream` : `new StreamReader(stream).ReadToEnd()` → lecture manuelle
`Stream.Read` + `System.Text.Encoding.UTF8.GetString` (les deux whitelistés).
NB : le correctif ne ferme plus le flux — conforme à la doc amont (« This will NOT
close the stream », le `using StreamReader` amont le violait silencieusement).

Vérification IL post-fix : `Stream×126, BinaryWriter×51, BinaryReader×47,
MemoryStream×7, SeekOrigin×4, TextWriter×2` — tous whitelistés ; le seul
`System.IO.File` restant est un `ldstr` (message d'erreur de ByteCode.Dump), pas un
typeref. `ScriptingMod.dll` déjà propre.

Autre constat du test : `addmodsdir` OK, GUID généré et inséré dans `Mod.xml`
(`64dffd8d-80ec-465e-9a68-78535b2c1223`). Le message « connection request failed » =
serveur master MP de Besiege injoignable (sans rapport).

### Verdict scanner #2 : ACCEPTÉ — GO technique du M0 acquis

Deuxième test en jeu : plus aucun message `[Security]` — `MoonSharp.Interpreter.dll` ET
`ScriptingMod.dll` **passent l'AssemblyScanner**.

### Bug runtime #1 (corrigé) : AnonWrapper

Premier bug au chargement (trace OCR) : `NullReferenceException` dans
`Table.PerformTableSet` ← `MathModule.MoonSharpInit` ← `SetRandom` ←
`UserData.Create(new AnonWrapper<Random>(…))`.

**Cause** : mon `UserData.Create(object)` patché ne sert que les `IUserDataType` ;
`AnonWrapper<T>` n'implémentait pas l'interface (l'amont l'enregistrait dans le registre
réflexif, mode HideMembers) → `Create` retournait `null` → `Registry.Set(key, null)` → NRE.
**Correctif** : `AnonWrapper<T>` implémente `IUserDataType` en boite noire
(Index→null, SetIndex→false, MetaIndex→null — sémantique équivalente à HideMembers),
servi automatiquement par `UserData.Create`.

**Sweep préventif du même pattern** (appelants de `UserData.Create`/`RegisterType`) :
- `EnumerableWrapper.ConvertIterator` — déjà `IUserDataType` ✓ ;
- `DynamicModule` (hors preset SoftSandbox, corrigé pareil) :
  `DynamicExprWrapper` → `IUserDataType` boite noire + `MoonSharpInit` en no-op
  (l'amont y appelait `UserData.RegisterType` → aurait levé avec notre stub) ;
- `StringModule.MoonSharpInit` : pur bookkeeping de metatables (aucun interop) ✓.

### Retest final — SUCCÈS COMPLET (console OCR, 2026-10-14)

```
[ScriptingMod] M0 POC chargement...
[lua] "hello depuis Lua!"
[ScriptingMod] hello OK -> MoonSharp 2.0.0.0 2.0.0.0     (_VERSION + _MOONSHARP.version)
[ScriptingMod] sandbox -> sandbox OK                      (io/load nil, os.time OK)
[ScriptingMod] M0 pret. Commandes : lua "<code>", bench, watchdog
$ lua "return 2+2"      ->  [ScriptingMod] -> 4
$ bench
  1) boucle adds (watchdog ON) : 335 ms, 2 978 200 instr/s approx, res=500000500000
  2) cout estime d'un tick 50 Hz a 2000 instr : 0.672 ms/frame (budget Unity ~ 20 ms)
  3) watchdog budget actuel : 50 000 000 instr/execution
$ watchdog
  [watchdog] SUCCES : budget d'instructions depasse (50000000) — script interrompu
  [watchdog] Le jeu continue de tourner.
```

**Analyse** :
- **Perf** : ~3 M instructions Lua/s avec la watchdog branchée (hooks actifs). Un tick de
  contrôle réaliste (PID : quelques centaines d'instructions) coûte **< 0,2 ms/frame**
  sur un budget Unity de 20 ms → la performance n'est **pas un sujet** pour ce mod.
  Marge : ~59 000 instructions par frame avant d'atteindre 20 ms.
- **Watchdog** : fonctionne (boucle infinie interrompue, jeu vivant). ⚠️ Enseignement de
  calibration : à ~3 M instr/s, le budget POC de 50 M instructions = **~17 s de gel**
  avant interruption — inacceptable en production. Budget réaliste à calibrer en M4 :
  ~500 000 instr/tick (≈ 0,17 s de gel au pire) — largement au-dessus des besoins d'un
  tick de contrôle, assez bas pour être indolore. À rendre configurable par le joueur.
- `_VERSION` de MoonSharp = « MoonSharp 2.0.0.0 » (pas « Lua 5.2 ») — cosmétique.

## Étape 9 — Conclusions M0 : GO DÉFINITIF

| Objectif M0 | Verdict |
|---|---|
| a. DLL MoonSharp élaguée passe l'AssemblyScanner | ✅ (2e itération : StreamReader corrigé) |
| b. `print("hello")` Lua en jeu, sandbox vérifiée | ✅ |
| c. Bench tick 50 Hz | ✅ 0,672 ms/2000 instr → non-sujet |
| d. Watchdog d'instructions | ✅ (budget à calibrer : 50M → ~500k en production) |

**Deux leçons durables pour tout le reste du projet** (documentées dans AGENTS.md) :
1. `Type.Name` interdit → `Type.FullName` (MemberInfo::get_Name) ;
2. la whitelist du scanner = types EXACTS (StreamReader ∉ whitelist malgré « Stream »).

Le chemin technique est validé : MoonSharp v2.0.0.0 élaguée est **le moteur de script du
mod**. Les jalons M1+ peuvent démarrer.

## Étape 9 — Conclusions M0 et mise à jour des documents

*(à compléter : verdict GO/NO-GO définitif, chiffres bench, marge watchdog, mise à jour
étude/workflow/AGENTS.md)*
