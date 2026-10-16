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

## Test joueur (attendu)

1. Relancer le jeu → `pilotage.lua installe (SMDEMO v1)` ;
2. machine avec un moteur (Cog), **aucune liaison mapper** → contrôleur → sim :
   `b.actions` listé au démarrage ;
3. tenir **P** → le moteur tourne (avant) ; **O** → arrière ; relâcher → stop ;
4. tester sur d'autres types de blocs : `b:contract(true)` (spring), `b:wind(true)`
   (treuil), `b:spin(true)` (bloc de vol) — noms du mapper ;
5. vérifier l'arrêt propre (relâchement) à la fin de sim.

*(à compléter : résultats)*
