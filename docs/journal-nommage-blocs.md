# Journal nommage-blocs — expérience : adresser les blocs par nom

> Branche `nommage-blocs` (post-M4). Motivation joueur : l'identification des blocs
> depuis Lua est fastidieuse (`machine:blocks("Cog")` + index) ; et le système vanilla
> de « variables à la place des touches » ne doit pas être un prérequis pour
> l'adressage (il reste utile pour `emulate`, indépendant).

## État : EN COURS (test joueur attendu)

---

## Décisions

- **Nommer** : `script name <nom>` — cible = 1 bloc sélectionné (Advanced Building)
  ou, à défaut, le bloc **visé** par la souris (`AddPiece.HoveredBlock` — vérifié au
  decomp : propriété publique ; `AddPiece.SelectedBlocks` statique).
- **Stockage** : `BlockNames.tsv` (dossier data) — nom TAB GUID. Namespace **global**
  (un nom = un bloc), résolution dans la machine courante. Choix cohérent avec D1-ter :
  une seule source de vérité, pas de duplication par contrôleur (pas de divergence
  possible), pas de dépendance à un bloc Microcontroller. Limite assumée : les noms ne
  voyagent PAS avec un `.bsg` partagé (comme les scripts — D1-ter).
- **Le GUID est la clé** : stable à travers sauvegardes/rechargements (c'est son rôle
  vanilla, étude §4) — un bloc détruit/reconstruit perd son nom (nouveau GUID).
- **Lua** : `machine:block("nom")` → bloc ou `nil` ; `machine:named()` → table
  nom→bloc (seulement ceux résolus dans la machine courante).
- CLR 2.0 : `Guid.TryParse` n'existe pas (net40) → `new Guid(string)` + catch.

## Implémentation

- `src/BlockNames.cs` — manager (load/save TSV, Set/Remove/All, log au rebind).
- `ScriptingModEntryPoint` — sous-commandes `name` / `names` / `unname` (cible
  sélection → survol ; refus si sélection multiple), liste avec état de résolution.
- `LuaApi.cs` (`LuaMachineApi`) — `block("nom")` + `named()` ; résolution par scan
  GUID des blocs de la machine (sim ou construction selon l'état).
- Démo `noms.lua` (SMDEMO v1) : `machine:named()` au démarrage, `machine:block("moteur")`
  + les mêmes `emulate`/`set_slider` que `actionneurs.lua` mais sur le bloc nommé.
- Validation : `PARSE+RUN OK` via le harnais MoonSharp Docker (procédure M3) ;
  build OK ; scan propre (`MemberInfo::` 0, pas de réflexion hors metadata).

## Bug #1 (corrigé) : `named` exposé comme propriété au lieu d'une méthode

- **Symptôme joueur** : `noms:(8,4-46): attempt to call a table value` —
  `machine:named()` appelait une TABLE.
- **Cause** : `named` retournait la table directement (propriété) alors que la
  convention de l'API est le style méthode (`machine:blocks()`) ; avec les parenthèses,
  Lua appelait la table.
- **Fix** : `named` est désormais un `CallbackFunction` comme `blocks` —
  `machine:named()` et `machine.named()` marchent tous deux.
- **Leçon** : le harnais de parse (M3) ne peut pas attraper ce type d'erreur — il
  exécute le top-level, pas les callbacks (`on_sim_start` n'y tourne jamais). Les
  erreurs d'usage de l'API ne se voient qu'en jeu ; garder les démos comme
  *smoke-tests* de l'API à chaque évolution.

## Étape 2 — Pivots de conception (discussion joueur)

1. **GUID abandonné** : `Machine.AddBlocksFromInfo` RÉGÉNÈRE tous les GUID à chaque
   chargement de machine (decomp l.3232) — le GUID n'est stable qu'en session. Un nom
   binde au GUID mourrait à chaque rechargement de niveau.
2. **Étude « mécanisme slider »** (proposition joueur) : la valeur d'un slider reste
   liée à son bloc parce qu'elle vit DANS ses données (clé `bmt-…`, contenance, zéro
   référencement). Écriture dans `block.LastState` (public) possible MAIS perte
   silencieuse à chaque régénération d'état (mapper, undo) — écarté.
3. **Étude concurrentielle** :
   - ilyshk4 (Lua mod) : clés de référence vanilla (`get_refs_control('rotor')`,
     groupées) + index — persistance native, pas de noms joueur ;
   - LenchScripterMod : IDs séquentiels auto (« WHEEL 1 », compteur par type, régénérés
     par session) + GUID — zéro persistance, renumérotation silencieuse ;
   - personne ne fait de noms joueur persistants — notre besoin est réel.
4. **Décisions** :
   - noms joueur : clé = **position locale** (mm entiers, tolérance 5 cm), stockage
     dans les données du **Microcontroller** (embarqués dans le .bsg, namespace par
     machine, voyagent avec une machine partagée) — le seul design sans perte
     silencieuse (casse visible : déplacer le bloc) ;
   - ajout de **`machine:refs(canal)`** (idée joueur, convergente avec ilyshk4) :
     table des blocs dont une touche écoute le message vanilla — persistance native,
     survit au déplacement, groupé, multi-tags (`a;b`) donc identification compatible
     avec le canal de contrôle ;
   - les trois mécanismes (noms / refs / filtre de type) coexistent sur la branche ;
     le verdict d'usage tranchera ce qui fusionnera dans master.
5. Noms : commandes `script name/names/unname` écrivent dans TOUS les contrôleurs de
   construction (instances sauvegardées — les instances de sim sont des copies).

## Test joueur (attendu)

1. Relancer le jeu → `noms.lua installe (SMDEMO v1)` ;
2. viser le moteur (Cog) → `script name moteur` → message « bloc ... nomme "moteur" » ;
3. `script names` → résolution OK ;
4. contrôleur → script `noms` → sim : la table des noms s'affiche, le bloc nommé
   répond (P + slider si lié au message « pousser ») ;
5. sauver/recharger la machine → le nom tient (GUID) ;
6. `script name moteur` sur un autre bloc → rebind ; `script unname moteur` → purge.

*(à compléter : résultats)*
