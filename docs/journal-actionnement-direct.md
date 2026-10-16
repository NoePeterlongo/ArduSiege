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

## API

- `b:keys` → table clé → libellé (les touches du mapper du bloc, ex. `forward`,
  `backward` pour un Cog) ;
- `b:key("forward", true)` → générique : pilote la touche par son nom de mapper ;
- sucre : `b:spin(etat)` → touche `forward` (relâche `backward` automatiquement),
  `b:reverse(etat)` → touche `backward` (relâche `forward`) ;
- **NB nommage** : `b.forward`/`b.up`/`b.right` restent les AXES du bloc (M2) —
  d'où `spin`/`reverse` plutôt que `forward`/`backward` pour le sucre ;
- idempotence (leçon M3) : registre par contrôleur (`BlockKeyActuator`), transitions
  uniquement, baseline « absent = relâché » — jamais de faux `false` initial ;
- relâchement global à l'arrêt de sim du contrôleur (`OnSimulateStop`) — symétrie du
  compteur, comme `emulate` ;
- hors contexte contrôleur (console POC) : erreur explicite.

## Limites documentées

- **D4 multi-contrôleurs** : deux scripts pilotant la même MKey cumulent leurs
  compteurs (registres indépendants) — responsabilité du joueur ;
- compatibilité `emulate(canal)` : les deux chemins convergent sur le même compteur —
  un bloc lié à un message ET piloté en direct additionne les deux (cohérent) ;
- ne pilote que ce que le bloc expose comme touches mapper (clés listées par
  `b:keys`).

## Démo `pilotage.lua` (SMDEMO v1)

Aucune préparation : un moteur (Cog), le script `pilotage`, P pour avant, O pour
arrière, vitesse modulée toutes les 2 s. Validée par le harnais MoonSharp
(`PARSE+RUN OK`). Build OK ; scan propre (`MemberInfo::` 0).

## Test joueur (attendu)

1. Relancer le jeu → `pilotage.lua installe (SMDEMO v1)` ;
2. machine avec un moteur (Cog), **aucune liaison mapper** → contrôleur → sim ;
3. tenir **P** → le moteur tourne (avant) ; **O** → arrière ; relâcher → stop ;
4. console `lua "return moteur.keys"` (tables des touches) ;
5. vérifier l'arrêt propre (relâchement) à la fin de sim (le moteur s'arrête).

*(à compléter : résultats)*
