# Journal M4 — Persistance `.bsg` + automate D1-bis + commandes console

> Objectif M4 (étude §7 + workflow D1/D5/D1-bis) : la source du script voyage dans le
> `.bsg` (machine autonome, partageable), automate de synchronisation machine ↔
> bibliothèque à trois états, commandes console `script`.

## État : TERMINÉ (M4 validé en jeu — 2026-10-15)

Parcours complet validé par le joueur (modèle D1-ter) : nouveau script visible au
mapper sans relancer le niveau ; machine sauvegardée/rechargée avec bibliothèque
évolutive → le bon script (re-point par nom) ; `script reload` re-exécute à chaud ;
script renommé → marqueur `*` au mapper, message clair en sim, contrôleur inerte,
retour à la normale au retour du fichier ; `script show/list/folder` cohérents.
Deux jalons de debug nécessaires (GetLocalPlayer null en solo ; event UI
d'association peu fiable → supprimé par le pivot D1-ter). Budget watchdog : inchangé
(1M instr/tick — recalibrage reporté à M5).

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

## Étape 6 — PIVOT JOUEUR (D1-ter) : persistance par nom uniquement

- **Retours joueur** sur le modèle embarqué (D1-bis implémenté ci-dessus) : parfois
  la machine semble pointer vers le script renommé ; la sauvegarde n'embarque pas la
  nouvelle version éditée ; changer de script écrase l'orphelin malgré le `.recup` ;
  les nouveaux scripts n'apparaissent pas au menu sans relancer le niveau.
- **Décision joueur** : **abandonner la source embarquée** — la bibliothèque est
  l'unique source de vérité ; la machine ne stocke que le NOM (`sm-script-name`).
  Plus simple, plus fiable, aucun état divergent. Conséquence assumée : plus de
  promesse « machine autonome » (partage = le destinataire doit avoir le script dans
  sa bibliothèque, même nom). D1-bis reste documenté (workflow) au cas où le partage
  Workshop devienne central.
- **Implémentation** :
  - retrait : `sm-script-source`, copie à l'association, `BackupOrphan`, `export`,
    états synchronisé/différent, fallback embarqué au sim start ;
  - conservation : re-point du menu **par nom** au chargement (corrige l'index
    décalé), marqueur `* n'existe plus` au mapper, message console clair si le
    script manque au début de sim (le contrôleur ne fait rien ce run) ;
  - **menu rafraîchi à l'ouverture du mapper** : `BlockMapper.onMapperOpen` →
    `RefreshMenu()` (re-liste la bibliothèque, conserve la sélection par nom) puis
    `mapper.Rebuild()` — NB : PAS `mapper.Refresh()`, qui rappelle `Open()` donc
    `onMapperOpen` = boucle infinie ;
  - `script show` : nom + `present` / `N'EXISTE PLUS` + en sim ; `script reload`
    relit le fichier et remonte le runtime à chaud.
- Build OK ; scan propre.

## Étape 7 — Test joueur (attendu, modèle D1-ter)

1. Relancer le jeu, poser un Microcontroller, sélectionner un script au mapper →
   sim : il tourne ;
2. **créer un nouveau script** `.lua` dans la bibliothèque (VS Code) → ouvrir le
   mapper du contrôleur : **le nouveau script est visible sans relancer le niveau** ;
3. **Sauver la machine**, quitter le niveau, recharger → sim : le bon script tourne
   (persistance par nom, même si la bibliothèque a évolué) ;
4. éditer le `.lua` → `script reload` (sim en cours : re-exécution) ;
5. **renommer/supprimer** le fichier → mapper : entrée `* nom` (« n'existe plus ») ;
   sim : message console clair, contrôleur inerte ce run ; `script show` :
   `N'EXISTE PLUS` ;
6. remettre le fichier sous le même nom → mapper sans marqueur, sim : le script
   tourne à nouveau.

**Résultats (2026-10-15) : TOUT PASSE.** Notamment : le rafraîchissement du menu à
l'ouverture du mapper (onMapperOpen + Rebuild) ne provoque aucun scintillement ni
souci d'affichage — plan B (rebuild différé) inutile. Leçons durables M4 :
(1) `Modding.Common.Player` est MP-first — en solo, `Machine.Active()` ;
(2) un modèle à source unique (bibliothèque) bat un modèle à synchronisation dès que
la synchronisation dépend d'événements UI qu'on ne contrôle pas.
