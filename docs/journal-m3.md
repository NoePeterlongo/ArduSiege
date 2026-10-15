# Journal M3 — API actionneurs

> Objectif M3 (étude §7) : `emulate(canal, etat)` (touches émulées → pilotent les blocs
> vanilla par canaux nommés) + sliders (`block:slider/set_slider/sliders`).
> Suite de `docs/journal-m2.md` (capteurs validés).

## État : EN COURS (test joueur attendu)

---

## Étape 1 — Vérifications préalables (code décompilé)

- `KeyInputController.Emulate` (l.318) : pour un MKey `useMessage`, chaque message →
  `usedMessages[text]` → `key.UpdateEmulation(etat)` sur chaque écouteur.
  **Deux pièges découverts** :
  1. **NRE garanti si le canal n'a aucun écouteur** (`usedMessages[text]` sans garde) →
     il faut vérifier les écouteurs AVANT d'émuler ;
  2. **compteur, pas un état** : `UpdateEmulation` incrémente/décrémente `emulators` →
     l'émulation doit être **idempotente** (appliquer uniquement les CHANGEMENTS).
- `HasEmulationBlocks` (Machine.cs l.1970) lit `Prefab.EmulatesAnyKeys` — pour les blocs
  moddés, propagé depuis l'override runtime `EmulatesAnyKeys` évalué au `SafeAwake`
  (`ModBlockBehaviourHandler.cs:174`) → notre override suffit ✓ ;
- le tick d'application officiel : `SendKeyEmulationUpdateHost` (host uniquement — le
  simulateur ✓ cohérent MP) ;
- `MKey` : **ctor public** `new MKey(displayName, key, defaultKey, isEmulator)` — les
  canaux sont créés dynamiquement (jamais dans un mapper), `useMessage`/`message`
  publics ;
- écouteurs : scan des **`MapperTypes` publics** des blocs (`MKey.useMessage` +
  `message[]`) — les liaisons « variables à la place des touches » (v1.10) du joueur ;
- sliders : `MapperType.Key` (public), `MSlider.Value` setter public → `InvokeValueChanged`
  (le moteur lit `speedSlider.Value` en continu, établi au M0) ;
- sémantique `__call` Lua : le callee est **passé en premier argument** → parsing robuste
  par type (premier string = canal, premier booléen = état).

## Étape 2 — Implémentation

- **`src/LuaActuators.cs`** — `LuaActuatorApi` (IUserDataType callable) :
  - `emulate(canal, etat)` (global Lua via `__call`) → stocke l'état **désiré** ;
  - `ApplyPending()` (appelé au tick d'émulation) : idempotent, garde anti-NRE
    (`ChannelHasListeners`), crée le `MKey` de canal à la demande, appelle
    `MicrocontrollerScript.EmulateChannel` → `ModBlockBehaviour.EmulateKeys(new MKey[0],
    key, etat)` ;
  - `ResetAll()` à l'arrêt de la sim (symétrie du compteur) ;
  - warning console unique par canal sans écouteur (avec mode d'emploi du mapper).
- **`src/LuaApi.cs`** — extension `LuaBlockApi` : `sliders` (table clé→libellé),
  `slider(nom)` (lecture), `set_slider(nom, valeur)` (écriture directe, SP privilégié
  — non networké en MP, documenté) ;
- **`MicrocontrollerScript`** : `EmulatesAnyKeys => true` (indispensable — active le
  système machine), `SendKeyEmulationUpdateHost → ApplyPending`, `OnSimulateStop →
  ResetAll` (relâche les canaux), `ChannelHasListeners` (scan MapperTypes) ;
- démo **`actionneurs.lua` (SMDEMO v1)** : moteur lié au message « pousser » suit la
  touche P + `set_slider("speed", …)` module la vitesse toutes les 2 s.
- Version 0.3.0. Build OK ; scan propre (`MemberInfo::` 0, `Path` seul).

## Étape 2bis — Bug démo #1 (corrigé) : ligne 3 non commentée dans `actionneurs.lua`

- **Symptôme joueur** : `erreur Lua au chargement : actionneurs:(3,0-9): unexpected
  symbol near "pousser"`.
- **Cause** : dans la constante `ACTIONNEURS_LUA`, la 3e ligne
  (`"pousser" (champ texte/variable du mapper...)`) n'avait pas le préfixe `--` —
  Lua tentait de l'exécuter (les 2 premières étaient des commentaires).
- **Leçon** : le code Lua embarqué en constantes C# doit être validé par un parseur
  AVANT le test joueur. Procédure validée : extraire la constante (script python) puis
  `Script.DoString` via un harnais mcs/mono Docker référençant la DLL MoonSharp
  (`PARSE+RUN OK`) — le parseur exact du jeu, sans lancer Besiege.
- **Fix** : ligne commentée + prérequis « Advanced Building » documenté dans l'en-tête
  de la démo ; passage en **SMDEMO v2** (le re-seed écrase automatiquement le fichier
  v1 cassé chez le joueur). Build OK ; scan propre (`MemberInfo::` 0).

## Étape 5 — Bug joueur #2 CAUSE TROUVÉE : `emulate(false)` initial décrémente le compteur

- **Protocole joueur décisif** : `set_slider` OK ; `emulate("pousser", true)` en dur →
  moteur tourne ; `print(input:is_held("P"))` correct ; mais avec
  `emulate("pousser", input:is_held("P"))` le moteur ne tourne jamais.
- **Cause** : `MKey.UpdateEmulation` fait `emulators += (etat ? +1 : -1)` — un
  COMPTEUR, pas un état. Au premier tick, P est relâché → le script envoie
  `false` → compteur 0 → **-1**. Puis tenir P envoie `true` → -1 → **0**.
  Le moteur teste `Emulating => emulators > 0` → jamais vrai. (Avec `true` en dur :
  0 → 1 → moteur OK. Cohérent avec tout le protocole.)
  Le piège « compteur, pas un état » du journal (étape 1) était donc double :
  idempotence entre nos envois, MAIS aussi état de départ — le jeu naît avec
  compteur=0 (état « relâché »), donc un `false` initial est un changement **fictif**
  qui décale le compteur d'un cran.
- **Fix** (`LuaActuators.ApplyPending`) : un canal absent de `m_Applied` est traité
  comme « déjà appliqué à false » (via `TryGetValue` qui écrit `false` si absent) —
  plus jamais de `false` initial fantôme. Les transitions restent seules envoyées.
- **Sonde corrigée** : le log « envoye au jeu » apparaît désormais à CHAQUE
  transition appliquée (avant : première fois par canal — message trompeur qui a
  fait croire à un envoi unique).
- **Limite documentée (D4)** : si un autre bloc émule déjà le même canal au démarrage
  (ex. porte logique vanilla active), son compteur est partagé — un script qui
  éteint le canal éteint aussi l'émulation de l'autre bloc. Multi-émulateurs sur un
  même canal = hors périmètre v0 (décision D4), à documenter pour le joueur.
- Build OK ; scan propre (`MemberInfo::` 0).

## Étape 4 — Bug joueur #2 (en cours) : « P ne fait rien » — sondes posées

- **Symptôme** : `set_slider` fonctionne (donc `on_tick` tourne, moteur trouvé), mais
  tenir P n'actionne pas le moteur lié au message `pousser`. Aucun message d'erreur.
- **Analyse statique du decomp** (chaîne vérifiée saine de bout en bout) :
  - `Machine.InitSimBlock` : `AddMKey` (enregistrement des messages dans
    `usedMessages`) n'est appelé que **par KeyCode de la MKey** — si `KeysCount==0`,
    un message n'est JAMAIS enregistré (hypothèse H3) ;
  - `KeyInputController.Emulate` : `usedMessages[text]` lève `KeyNotFoundException`
    si le message n'y est pas — attrapé nulle part côté jeu ;
  - `SendEmulationUpdateBlock` (notre point d'entrée) exige : bloc dans
    `simSendEmulationUpdate` (inscription `Machine.RegisterEmulationUpdate`, gate
    `HasEmulationBlocks` machine + `Prefab.EmulatesAnyKeys` + `Prefab.RegisterEmulationUpdate`,
    posé par `BlockPrefabCreator` pour tout bloc moddé) — hypothèse H1 si jamais appelé ;
  - moteur : `EmulationUpdateBlock` lit `EmulationHeld` → OK vanilla.
- **Sondes posées** (une seule partie pour trancher entre H1/H2/H3/liaison) :
  1. `SendKeyEmulationUpdateHost` : log unique « tick d'emulation actif » ;
  2. garde échouée : dump des MKey de la machine (`useMessage`, `keys=N`,
     `messages=[...]`) — montre le message réellement tapé et le compteur de keycodes ;
  3. `EmulateKeys` : try/catch par canal (une `KeyNotFoundException` casserait le tick
     machine du jeu à chaque frame — désormais capturée et loguée une fois) ;
  4. log unique du premier envoi réussi par canal.
- Build OK ; scan propre (`MemberInfo::` 0).

## Étape 3 — Test joueur (attendu)

> **Prérequis découvert en test** : le bouton qui transforme une touche en MESSAGE
> n'apparaît que si l'option **Advanced Building** est activée (réglages du jeu).
> `Selectors/KeySelector.cs` : `messageToggleOn.gameObject.SetActive(... &&
> OptionsMaster.BesiegeConfig.AdvancedBuilding)` — sans elle, la ligne de touche ne
> propose aucun champ texte (c'est le « je ne vois pas d'input » du testeur).
> Après bascule, un `TagSelector` (autocomplétion sur les messages existants de la
> machine) remplace la touche ; persisté dans le `.bsg` via
> `MKey.Serialize` → `Message=pousser;Use=True`.

1. Relancer le jeu → `actionneurs.lua mis a jour (SMDEMO v2)` ;
   **activer Advanced Building dans les options** ;
2. machine avec un moteur (Cog) ; dans le mapper du moteur : cliquer le bouton de
   bascule de la ligne de touche (visible uniquement en Advanced Building), puis taper
   le MESSAGE `pousser` dans le champ texte qui apparaît ;
3. Microcontroller → script `actionneurs` → simuler ;
4. tenir **P** → le moteur tourne ; toutes les 2 s : `slider speed : 0.5 -> 2.5` (vitesse
   visible) ;
5. console : `lua "return motor.type"` (variables du script accessibles — contexte
   contrôleur).

*(à compléter : résultats)*
