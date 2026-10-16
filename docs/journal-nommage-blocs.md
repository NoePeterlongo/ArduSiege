# Journal nommage-blocs — expérience : adresser les blocs

> Branche `nommage-blocs` (post-M4). Motivation joueur : l'identification des blocs
> depuis Lua est fastidieuse (`machine:blocks("Cog")` + index) ; et le système vanilla
> de « variables à la place des touches » ne devait pas être un prérequis pour
> l'adressage.

## État : TERMINÉ — décision joueur : seul `machine:refs(canal)` est retenu

L'expérience a traversé trois designs (GUID → noms joueur par position → refs) ; le
verdict d'usage est **refs seul** : le mécanisme vanilla de liaisons par message,
exposé en lecture, bat les noms joueur sur tous les critères — persistance native
dans le `.bsg` (zéro stockage côté mod), survit au déplacement du bloc, adressage de
groupe, multi-tags (`a;b` : canal de contrôle ET identification sur la même touche).

### API retenue (fusionnée dans master)

- `machine:refs(canal)` → table indexée des blocs dont une touche écoute le message
  vanilla `canal` (scan des `MKey` publiques : `useMessage` + `message[]`) ;
- démo `noms.lua` (SMDEMO v3) : utilise `refs("pousser")` comme cible de
  `set_slider` — même préparation que `actionneurs.lua` ;
- la liaison se fait dans le mapper du bloc (champ variable, Advanced Building —
  prérequis documenté au journal M3).

### Designs explorés et écartés (historique pour la reprise)

| Design | Pourquoi écarté |
|---|---|
| GUID comme clé | `Machine.AddBlocksFromInfo` régénère TOUS les GUID à chaque chargement (decomp l.3232) — noms morts à chaque rechargement |
| Nom joueur → position locale (mm), stocké dans le Microcontroller | Fonctionne mais : casse au déplacement, nécessite un contrôleur dans la machine, stockage à maintenir — tout ce que refs donne gratuitement |
| Nom dans `LastState` du bloc (« comme les sliders ») | Perte silencieuse à chaque régénération d'état (mapper, undo) — aucun hook de sauvegarde machine pour réinjecter |
| Étude concurrentielle | ilyshk4 : clés de référence vanilla (`get_refs_control('rotor')`) — le même choix final que nous ; LenchScripterMod : IDs séquentiels auto (« WHEEL 1 », renumérotation silencieuse) — personne ne fait de noms joueur persistants |

Code retiré au nettoyage : commandes `script name/names/unname`, `machine:block()` et
`machine:named()`, stockage `sm-block-names` dans le contrôleur, `BlockNames.cs`.

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
