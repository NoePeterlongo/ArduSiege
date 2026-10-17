# Journal actionnement-direct — piloter les blocs sans variables vanilla

> Branche `actionnement-direct` (post-refs). Motivation joueur : rendre l'usage des
> variables vanilla **optionnel** pour l'actionnement — `b:spin(true)` etc., sans
> aucune liaison dans le mapper.

## État : EN COURS (test joueur attendu)

## Mécanisme (vérifié au decomp)

- `MKey.UpdateEmulation(bool)` est public : `emulators += ±1` sur la touche du bloc —
  **pas besoin du dispatch `KeyInputController`** (canaux/`usedMessages`) : on vise la
  MKey du bloc directement. Le bloc lit `EmulationHeld()` au tick d'émulation
  officiel (`CogMotorControllerHinge` l.501 : `emuForwardHeld`), même chemin que
  l'émulation vanilla par portes logiques ;
- le tick d'émulation exige `Machine.HasEmulationBlocks` — notre Microcontroller
  force déjà `Prefab.EmulatesAnyKeys=true` (M3) : le système est actif dès qu'un
  contrôleur est posé (prérequis naturel) ;
- le moteur s'inscrit au tick via `Prefab.RegisterEmulationUpdate` (vanilla —
  l.418), comme pour toute émulation vanilla.

## API (revue joueur : « basique et facile » — les actions portent le nom de l'UI)

- **`b:<nom_action>(etat)`** — dispatch dynamique : toute MKey du bloc est appelable
  par le nom que le joueur voit dans le mapper — clé (`forward`, `backward`, `spin`,
  `contract`, `wind`, `unwind`...) ou libellé UI, insensible à la casse. Aucun sucre
  codé en dur : le bloc définit ses actions, l'API les expose toutes ;
- `b.actions` → table nom appelable → libellé (découverte, comme `b.sliders`) ;
- `b:action(nom, etat)` → accès générique (noms calculés) ;
- **axes duaux** : `b.forward`/`b.up`/`b.right` restent les vecteurs (M2) ET sont
  appelables — `b:forward(true)` pilote l'action via une metatable `__call` sur la
  table du vecteur (support MoonSharp vérifié au source, Processor l.512) ;
- **pas de relâchement magique de l'opposé** : l'idiome recommandé est l'état complet
  à chaque tick (`b:forward(input:is_held("P"))` + `b:backward(input:is_held("O"))`),
  comme `emulate`. Si les deux sont tenus, le moteur priorise l'avant (CheckKeys,
  vérifié au decomp) ;
- idempotence (leçon M3) : registre par contrôleur (`BlockKeyActuator`), transitions
  uniquement, baseline « absent = relâché » ;
- relâchement global à l'arrêt de sim du contrôleur — symétrie du compteur ;
- action inconnue → erreur listant les actions du bloc.

## Limites documentées

- **D4 multi-contrôleurs** : deux scripts pilotant la même MKey cumulent leurs
  compteurs (registres indépendants) — responsabilité du joueur ;
- compatibilité `emulate(canal)` : les deux chemins convergent sur le même compteur —
  un bloc lié à un message ET piloté en direct additionne les deux (cohérent) ;
- ne pilote que ce que le bloc expose comme touches mapper (clés listées par
  `b:keys`).

## Démo `pilotage.lua` (SMDEMO v1)

Aucune préparation : un moteur (Cog), le script `pilotage`. Idiome état complet :
`moteur:forward(input:is_held("P"))` / `moteur:backward(input:is_held("O"))`, vitesse
modulée toutes les 2 s, `b.actions` listé au démarrage. Validée par le harnais
MoonSharp (`PARSE+RUN OK`). Build OK ; scan propre (`MemberInfo::` 0).

## Bug #1 (corrigé) : démo re-seedée sans bump de version

- **Symptôme joueur** : `pilotage:(16,4-43): cannot access field keys of userdata` —
  le fichier local était la PREMIÈRE itération (`moteur.keys`, sucre spin/reverse),
  seedée avant le redesign en actions-dynamiques.
- **Cause** : le contenu de la démo a changé sans incrémenter le tag SMDEMO (resté
  v1) → `WriteDemoIfOutdated` n'a jamais écrasé l'exemplaire local. Vieux Lua +
  nouvelle DLL = API désynchronisée.
- **Fix** : bump SMDEMO v2 (le re-seed remplace le fichier local).
- **Leçon durable** : TOUT changement de contenu d'une démo embarquée DOIT
  incrémenter son tag SMDEMO dans le même commit — c'est la seule protection du
  joueur contre la désynchronisation. (Deuxième occurrence du schéma
  « démo/mod désynchronisés », après la ligne non commentée du M3.)

## Bug #2 (corrigé) : `machine.forward` figé pendant la sim

- **Symptôme joueur** : `machine.forward.z` toujours 1, machine tournante ou non.
- **Cause** (decomp) : `Machine.Position`/`Machine.Rotation` lisent
  `buildingMachine` — la racine de CONSTRUCTION, immobile pendant la simulation.
  Les axes machine dérivés de `Machine.Rotation` étaient donc figés.
- **Source live du jeu** : `Machine.RefUp`/`RefForward`/`RefCenter`/`RefVel` —
  propriétés publiques calculées depuis le **bloc de référence**
  (`refBlockTransform`, capturé au début de sim, bouge avec la machine).
- **Fix** (`LuaMachineApi`) : `machine.up/forward/right/rotation/position` (et
  nouveau `machine.velocity`) suivent les `Ref*` en simulation, avec repli sur le
  repère de construction en mode build (les `Ref*` rendent zéro hors sim).
  Sémantique : `machine.position` = centre du bloc de référence en sim.
- **Leçon durable** : toute donnée "machine" lue depuis `Modding.Blocks.PlayerMachine`
  doit être validée LIVE vs BUILD — les propriétés de `Machine` peuvent n'être
  que construction-side (buildingMachine) ; les `Ref*` sont la source sim.

## État des actionneurs (lecture) — révision joueur

- 1re tentative : `b.angle` calculé par géométrie (rotation relative GO/rigidbody
  autour de l'axe) — **jugé peu fiable au test joueur** (offsets selon montage),
  retiré.
- Retenu : **`b.angle_target`** (`AngleToBe`, public, degres — la cible commandée ;
  généralisé à `RudderController`) et **`b:set_angle(v)`** — écrit la cible et
  force `targetAngleMode` (champ public du composant ; non persistant car les blocs
  se réinstancient à chaque sim). L'angle réel n'a pas de source interne dans le jeu
  (l'Anglometer calcule par géométrie) — la cible sert de retour.
- **`b.toggles` / `b:toggle(nom)`** : toggles du mapper (`MToggle.IsActive`).
- Équivalent ilyshk4 : `hinge.set_steering(45)` — même mécanisme (`AngleToBe`).

## Test joueur (attendu)

1. Relancer le jeu → `pilotage.lua installe (SMDEMO v1)` ;
2. machine avec un moteur (Cog), **aucune liaison mapper** → contrôleur → sim :
   `b.actions` listé au démarrage ;
3. tenir **P** → le moteur tourne (avant) ; **O** → arrière ; relâcher → stop ;
4. tester sur d'autres types de blocs : `b:contract(true)` (spring), `b:wind(true)`
   (treuil), `b:spin(true)` (bloc de vol) — noms du mapper ;
5. vérifier l'arrêt propre (relâchement) à la fin de sim.

*(à compléter : résultats)*
