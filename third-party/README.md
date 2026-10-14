# Third-party — MoonSharp v2.0.0.0 « trimmed » (build élaguée)

## Pourquoi

Le mod loader de Besiege interdit dans l'IL des mods tout un ensemble de namespaces
(`System.Reflection`, `System.IO`, `System.Diagnostics` hors Stopwatch, `Mono.Cecil`,
`Microsoft.*`, etc. — voir `docs/etude-faisabilite-scripting.md` Q1). MoonSharp contient
une couche d'interop CLR **optionnelle** fondée sur la réflexion : elle est amputée ici.
Seul le moteur (parseur, VM, modules sandboxés) est conservé, pour embarquer Lua dans le mod.

## Provenance

- Dépôt : https://github.com/moonsharp-devs/moonsharp
- Tag : **v2.0.0.0** — commit `3154416e535ab96c0d52bf12e3e472985a1532f4` (« RC for 2.0.0.0 »)
- Licence : **BSD-2-Clause** (voir `LICENSE` ci-dessous, et `LICENSE` amont dans le dépôt).
  À reproduire dans toute distribution du mod (fichier THIRD-PARTY-NOTICES).
- Base de sélection : `src/MoonSharp.Interpreter/MoonSharp.Interpreter.net35-client.csproj`
  (247 fichiers) — liste extraite dans `moonsharp-net35-files.txt`.
  Ce csproj est la cible **net35 officielle** en amont → compatible CLR 2.0 du jeu.

## Arborescence

```
moonsharp-trimmed/     ← CE dossier est compilé par tools/moonsharp-build.sh
  (204 fichiers + ModuleRegister.g.cs généré — voir journal M0)
moonsharp-net35-files.txt  ← liste des 247 fichiers de la cible net35 amont
```

## Régeneration depuis l'amont (reproductibilité)

Le dossier trimmed n'est pas un simple extrait : il contient des **patches** (journal
`docs/journal-m0.md`, étapes 2-5). Pour reconstituer l'amont :

```bash
# tarball du tag (équivalent : git clone --depth 1 --branch v2.0.0.0 …)
curl -L https://github.com/moonsharp-devs/moonsharp/archive/refs/tags/v2.0.0.0.tar.gz | tar xz
```

Divergences documentées vs amont (détails et motivation : `docs/journal-m0.md`) :

1. 43 fichiers retirés (io/load Lua, loaders fichiers, interop réflexion
   StandardDescriptors/UserDataRegistries/LuaStateInterop, REPL, ObjectValueConverter).
2. `ModuleRegister` réécrit : l'enregistrement des modules CoreLib, réflexif en amont,
   est remplacé par `Modules/ModuleRegister.g.cs` **généré hors-ligne**
   (`tools/moonsharp-gen/`, régénéré par `tools/moonsharp-build.sh`).
3. `PlatformAutoDetector` codé en dur (LimitedPlatformAccessor + InvalidScriptLoader) ;
   `StandardPlatformAccessor`/`DotNetCorePlatformAccessor` retirés.
4. `Compatibility/Framework*` (abstraction réflexion) retiré ; usages remplacés par des
   appels `System.Type` directs (autorisés par le scanner : `Type` est dans `System`).
5. `UserData`/`ClrToScriptConversions` : registre interop réflexion remplacé par
   `NotSupportedException` (notre mod n'expose que des `CallbackFunction` et des primitifs).
6. JSON : `null` devient `nil` (perte null/absent — D-M0-1 du journal).
7. Compilation sans define `DEBUG` (les `Debug.Assert` sont `[Conditional]`).

## Licence (BSD-2-Clause, MoonSharp)

Copyright (c) 2014-2016, Marco Mastropaolo
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
