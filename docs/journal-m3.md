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

## Étape 3 — Test joueur (attendu)

1. Relancer le jeu → `actionneurs.lua installe (SMDEMO v1)` ;
2. machine avec un moteur (Cog) ; dans le mapper du moteur : remplacer sa touche par le
   MESSAGE `pousser` (champ texte du mapper — « variables à la place des touches ») ;
3. Microcontroller → script `actionneurs` → simuler ;
4. tenir **P** → le moteur tourne ; toutes les 2 s : `slider speed : 0.5 -> 2.5` (vitesse
   visible) ;
5. console : `lua "return motor.type"` (variables du script accessibles — contexte
   contrôleur).

*(à compléter : résultats)*
