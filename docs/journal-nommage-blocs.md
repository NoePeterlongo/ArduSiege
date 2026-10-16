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

## Test joueur (attendu)

1. Relancer le jeu → `noms.lua installe (SMDEMO v1)` ;
2. viser le moteur (Cog) → `script name moteur` → message « bloc ... nomme "moteur" » ;
3. `script names` → résolution OK ;
4. contrôleur → script `noms` → sim : la table des noms s'affiche, le bloc nommé
   répond (P + slider si lié au message « pousser ») ;
5. sauver/recharger la machine → le nom tient (GUID) ;
6. `script name moteur` sur un autre bloc → rebind ; `script unname moteur` → purge.

*(à compléter : résultats)*
