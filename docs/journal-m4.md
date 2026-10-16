# Journal M4 — Persistance `.bsg` + automate D1-bis + commandes console

> Objectif M4 (étude §7 + workflow D1/D5/D1-bis) : la source du script voyage dans le
> `.bsg` (machine autonome, partageable), automate de synchronisation machine ↔
> bibliothèque à trois états, commandes console `script`.

## État : EN COURS (test joueur attendu)

---

## Étape 1 — Vérifications API (code décompilé)

- `XDataHolder.Write(key, object)` : `string` → `XString`, `string[]` → `XStringArray`,
  longueur arbitraire ; `ReadString`/`HasKey` publics → stockage nom + source ✓ ;
- `MMenu` : sélection par **index** (`XInteger bmt-script` persisté par le jeu),
  `Items` settable, `SetValue` n'invoque PAS `ValueChanged` (re-point silencieux),
  `ValueChanged(int)` — event public ✓ ;
- **Bug latent M1 découvert** : l'index de menu persisté se décale quand la
  bibliothèque évolue (fichier ajouté/supprimé) → la machine chargée pointe le
  mauvais script SILENCIEUSEusement. Corrigé par résolution par **nom** (M4) ;
- `ModBlockBehaviourHandler.OnLoad` : `base.OnLoad` (→ DeSerialize des MapperTypes,
  qui invoque `ValueChanged` hors UI) PUIS `modBlockBehaviour.OnLoad` → notre copie
  d'association doit ignorer les événements hors mapper
  (`BlockMapper.CurrentInstance == null`) ;
- `ModIO.OpenFolderInFileBrowser(path, data)` : ouvre l'explorateur sur le dossier —
  réponse UX à « le trouver » (pas de construction de chemin absolu nécessaire) ;
- `ModPaths.GetFilePathData` : le dossier data est par mod (`<Nom>_<GUID>/`) — tout
  `ModIO` à `data:true` est déjà isolé.

## Étape 2 — Implémentation

**`MicrocontrollerScript`** :
- `OnSave/OnLoad` : clés `sm-script-name`/`sm-script-source` (D1 — vérité de la
  machine) ; `OnLoad` **re-pointe le menu par nom** (corrige le décalage d'index) ;
- `OnScriptMenuChanged` (ValueChanged) : copie fichier → embarqué à l'association ;
  garde `BlockMapper.CurrentInstance` (événements de chargement ignorés) ;
  anti-écrasement : remplacer un script **orphelin** sauvegarde l'ancienne source en
  `<nom>.recup.lua` (jamais de perte silencieuse — v0 : pas de dialogue vanilla
  possible, la sauvegarde automatique remplace l'avertissement bloquant) ;
- résolution au début de sim (D1-bis) : **fichier gagne** s'il existe (resynchronise
  l'embarqué — fluidité édition), **source embarquée** sinon (machine autonome,
  import Workshop) ; menu décalé → embarqué = vérité ;
- marqueur orphelin `« * »` dans le menu (entrée ajoutée au besoin), `ResolveName`
  retire le marqueur ;
- `ReloadFromLibrary()` : resynchronise fichier → embarqué + **remontage à chaud**
  (`OnSimulateStop` puis `OnSimulateStart`) si le contrôleur était en simulation ;
- `CallIfDefined` : garde `m_Lua == null` (NRE latent : `OnSimulateStop` sur un
  script dont le chargement avait échoué).

**`ScriptingModEntryPoint`** — commande `script` :
- `list` : fichiers de la bibliothèque ; `show` : par contrôleur — nom + état
  (synchronisé / orphelin / différent / sans source embarquée) ;
- `reload [nom]` : resynchronise tous les contrôleurs visés (nom = embarqué, ou
  tous si omis) — remonte le runtime si en sim ;
- `export <nom>` : embarqué → bibliothèque ; **refuse** si le fichier existe avec un
  contenu différent (jamais d'écrasement silencieux) ; rafraîchit le marqueur ;
- `folder` : ouvre le dossier Scripts dans l'explorateur (UX « le trouver »).

## Étape 3 — Écarts assumés vs spec D1-bis

- Spec : « l'embarqué gagne en cas de collision import/nom ». Implémenté : **le
  fichier gagne partout** (règle unique). Motif : préserver l'habitude M1-M3
  « éditer → relancer la sim » sans commande intermédiaire ; l'import avec collision
  de nom est un coin rare, `script show` l'affiche (« DIFFERENT ») et `script export`
  récupère l'embarqué. À réévaluer si le partage Workshop devient central.
- Spec : « avertit avant d'écraser » → implémenté en **sauvegarde automatique**
  (`.recup.lua`) + message console : plus protecteur qu'un avertissement qu'on ne
  peut pas rendre bloquant en UI vanilla.

## Étape 4 — Bug joueur #1 (corrigé) : `GetLocalPlayer()` est null en solo

- **Symptôme** : `script show` → « aucun joueur local » dans un niveau, en sim ou hors sim.
- **Cause** (decomp) : `PlayerData.localPlayer` n'est assigné que par `ChatController`
  (MP) → `Modding.Common.Player.GetLocalPlayer()` retourne null en solo. Le fallback
  console `lua` de M2 (`RefreshConsoleMachine`) avait le même bug latent — jamais vu
  car la console utilisait le contexte runtime actif en sim.
- **Fix** : `GetLocalMachine()` — MP : joueur local ; SP : `Machine.Active()`
  (`MachineObjectTracker.activeMachine`, même accès que `CinematicCam`). Appliqué à
  `GetControllers` (commandes script) ET à `RefreshConsoleMachine`.
- **Leçon durable** : l'API `Modding.Common.Player` est MP-first — en solo, toujours
  passer par `Machine.Active()` (ou un contexte bloc : `BlockScript.Machine`).

## Étape 5 — Build + scan

Build OK (Docker mcs). Scan propre : `MemberInfo::` 0, pas de StreamReader/Writer,
pas de System.Reflection hors metadata.

## Étape 6 — Test joueur (attendu)

1. Relancer le jeu, poser un Microcontroller, associer un script au mapper →
   console : `script "..." associe (source copiee dans la machine)` ;
2. **Sauver la machine**, quitter le niveau, recharger la machine → sim : le script
   tourne (persistance nom+source ; re-point du menu par nom) ;
3. éditer le `.lua` dans VS Code → `script reload` (sim en cours : re-exécution) ;
4. renommer/supprimer le fichier → sim : `fichier absent, source embarquee (machine
   autonome)` ; mapper : entrée `* nom` ;
5. `script show` avant/après (états), `script list`, `script folder` ;
6. avec le script orphelin, associer un autre script → console : sauvegarde
   `.recup.lua` ;
7. `script export nom` depuis un orphelin → fichier recréé, marqueur retiré.

*(à compléter : résultats)*
