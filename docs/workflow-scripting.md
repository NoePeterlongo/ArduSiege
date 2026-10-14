# Workflow d'écriture des scripts — analyse et décisions

> Complète `docs/etude-faisabilite-scripting.md` (verdict GO). Parcourt **tout le workflow
> d'écriture d'un script** du point de vue du joueur, sépare ce qui doit être tranché
> **maintenant** (D1–D8) de ce qui sera **naturellement géré plus tard** (§3).
> Les affirmations techniques sont vérifiées au source (`decomp/`), dans la doc officielle
> (`docs/modding-doc.txt`) ou dans le source MoonSharp v2.0.0.0 — sauf mention contraire.

Date : 2026-10-14

---

## 1. Le parcours complet du joueur (v0 visée)

Ce que le document d'étude ne décrivait pas encore : le **fil continu** de l'expérience.

1. **Installer le mod** (Workshop). Le mod fournit : le bloc Microcontrôleur, le moteur
   Lua embarqué, une bibliothèque de scripts, des exemples.
2. **Poser le bloc Microcontrôleur** sur sa machine. Son mapper (UI vanilla) propose :
   menu déroulant « script », toggle « actif », paramètres (sliders/toggles customs).
3. **Taguer les blocs à commander** dans leurs mappers vanilla : lier la touche d'un
   moteur au message `gauche`, etc. (adressage par canaux — cf. étude §4).
4. **Écrire le script** dans un éditeur externe (VS Code). Fichiers `.lua` dans la
   bibliothèque du mod. Des exemples fonctionnels sont fournis (`examples/`).
5. **Associer** : menu du contrôleur → nom du script. (Pas d'éditeur in-game en v0.)
6. **Lancer la simulation** : le script se charge, `on_sim_start()` puis `on_tick()`
   à 50 Hz (`SimulateFixedUpdateHost`). Les erreurs s'affichent dans la console (`Ctrl+Shift+K`).
7. **Itérer** : éditer → sauver → `script reload` (ou relancer la sim, qui recharge
   toujours). Rechargement à chaud sans relancer le jeu — cf. étude §4 « Hot reload ».
8. **Débuguer** : `print()` → console ; erreurs avec fichier + ligne ; watchdog
   d'instructions qui désactive un script déraillant sans geler le jeu.
9. **Sauver la machine** : le script voyage **dans le `.bsg`** (source embarqué dans le
   bloc contrôleur) → la machine est autonome.
10. **Partager** (Workshop) : le destinataire qui a le mod retrouve la machine avec son
    script embarqué ; sans le mod, avertissement vanilla standard (bloc manquant).

## 2. Décisions tranchées maintenant

### D1 — Persistance des scripts : source embarqué dans le `.bsg` + bibliothèque locale

- **Décision** : la vérité d'un script d'une machine = sa **source embarquée** dans les
  données du bloc contrôleur (`OnSave/OnLoad`). La **bibliothèque** (`*.lua` du dossier
  data) sert d'atelier : le mapper liste les scripts de la bibliothèque ; l'associer
  **copie** la source dans la machine. Recopier depuis la machine vers la bibliothèque :
  commande console (`script export`), pour éditer/sauver à nouveau.
- **Pourquoi** : machines 100 % portables et partageables (pattern du mod Lua
  d'ilyshk4) ; la machine ne casse pas si le fichier local est renommé/supprimé ;
  l'atelier reste simple (fichiers éditables à chaud).
- **Preuves** : `XDataHolder.Write(string, object)` accepte `string`/`string[]` de
  longueur arbitraire (source : `decomp/Assembly-CSharp/XDataHolder.cs:256-317`) —
  le contrôleur peut donc stocker nom + source + paramètres + associations tag→GUID
  dans le `.bsg`. Dossier data par mod : `Besiege_Data/Mods/Data/<ModName>_<GUID>/`
  (source : `ModPaths.GetDataDirectoryMod`), persistant hors du dossier mod (survit
  aux réinstallations workshop). Écriture via `ModIO.WriteAllText(path, text, data: true)`.

### D2 — Modèle d'exécution

- Une **instance MoonSharp `Script` par bloc contrôleur** (indépendants, budgets séparés).
- **État frais à chaque début de simulation** : la source est re-chargée et re-exécutée à
  chaque `OnSimulateStart` → aucun état résiduel entre deux runs (cohérent avec le
  comportement attendu d'une machine).
- Callbacks exposés : `on_sim_start()`, `on_tick()` (50 Hz, `SimulateFixedUpdateHost`),
  `on_sim_stop()`, `on_collide(block, autre)` — ce dernier depuis
  `OnSimulateCollisionEnter`. Tous **optionnels** (absence = pas d'appel).
- `machine`, `print`, `emulate`, `set_slider`… sont des `CallbackFunction` enregistrées
  dans `Globals` — aucune réflexion (règle d'or de l'étude §6).

### D3 — Robustesse : erreurs et watchdog d'instructions

- **Erreurs de parse** (au chargement) et **runtime** (pendant un tick) : interceptées,
  affichées dans la console avec fichier + numéro de ligne (MoonSharp :
  `ScriptRuntimeException.DecoratedMessage`, `SyntaxErrorException` — vérifié source
  v2.0.0.0), **contrôleur désactivé** jusqu'au prochain reload. La simulation continue —
  le jeu ne doit jamais crasher à cause d'un script.
- **Watchdog** : budget d'instructions par tick. **Faisabilité vérifiée** : l'interface
  `MoonSharp.Interpreter.Debugging.IDebugger` (source v2.0.0.0) expose
  `IsPauseRequested()` — « called by the script engine at execution time […]
  called A LOT » → le moteur appelle ce hook **pendant l'exécution du bytecode** : on
  attache un mini-debugger-compteur qui compte les appels et demande pause au-delà du
  budget → interruption d'un `while true do end` sans geler le jeu. À calibrer au M0-M4
  (valeur par défaut configurable par le joueur, ex. 10⁶ instructions/tick).

### D4 — Multi-contrôleurs

N blocs Microcontrôleur = N scripts indépendants (chacun son `Script`, son budget, son
état). Conflits possibles (deux scripts pilotant le même canal) : responsabilité du
joueur, à documenter. Pas de mécanisme de coordination en v0.

### D5 — UI v0 : mapper vanilla + console uniquement

- Contrôleur : `MMenu` (liste des scripts de la bibliothèque), `MToggle` (actif),
  sliders/toggles de paramètres passés au script — **zéro UI custom**.
- Console : `script list`, `script reload [nom]`, `script errors`, `script export <nom>`
  (machine→bibliothèque). `ModConsole.RegisterCommand` préfixe automatiquement en cas
  de conflit (doc officielle).
- Raison : le loader interdit l'accès aux prefabs d'UI custom ? Non — raison réelle :
  coût/fiabilité. L'UI custom (éditeur in-game, overlay) est reportée (§3).

### D6 — Édition : externe en v0

VS Code (déjà installé) + extension Lua (syntaxe) suffit ; fichiers plats `.lua`, un
script = un fichier = une machine logique. L'éditeur in-game d'ilyshk4 est un confort,
pas un prérequis — reporté.

### D7 — Sandbox du langage

`Preset_SoftSandbox` MoonSharp : pas d'`io`/`load`/fichiers depuis le Lua, pas d'accès
CLR, pas de réflexion. Tous les accès au monde réel passent par les `CallbackFunction`
que NOUS écrivons (C#). Complément : watchdog (D3). Les fichiers (bibliothèque, export)
sont manipulés côté C# via `ModIO`, jamais depuis le Lua.

### D8 — Distribution du mod et licences

- Mod : code à nous (licence libre à choisir, ex. MIT) + **MoonSharp v2.0.0.0 (BSD-2)**
  en build élaguée embarquée — obligation de mention + licence reproduite dans la
  distribution (fichier `THIRD-PARTY-NOTICES`).
- Crédit d'inspiration : *Lua Scripting Mod* d'ilyshk4 (Workshop 2383785201) — son wiki
  et son choix UniLua servent de référence (M5 : étude de ses patterns).
- Exemples fournis avec le mod (seed de la bibliothèque au premier lancement) : PID
  d'altitude, maintien de cap, séquenceur — documentation vivante.

## 3. Points explicitement reportés (avec jalon cible)

| Point | Jalon | Pourquoi plus tard |
|---|---|---|
| Éditeur de script in-game | v1 | Confort ; l'atelier externe + hot reload couvre le besoin v0 |
| Overlay UI custom (HUD, affichage de variables en jeu) | v1 | Nécessite UI custom = coût ; `print` + console suffisent |
| Débogueur VSCode MoonSharp branché sur le runtime | M5+ | Outil de dev du mod, pas du joueur ; `print` + erreurs ligneées d'abord |
| Intégration au système logique vanilla (triggers/events moddés exposés par le mod) | v2 | Le loader le supporte (`ModEvents.RegisterCallback`), mais c'est une 2e surface d'API |
| Synchronisation MP custom (`ModNetworking`) | v2 | L'émulation de touches est déjà MP-safe ; le reste du script est host-side |
| Versionnement de l'API script (semver, dépréciations) | M5 | Politique à écrire à la surface stable |
| Catalogue des sliders « live » par type de bloc | M3+ | Documenté progressivement depuis le decomp, au fil des types supportés |
| i18n de la doc joueur | v2 | V0 francophone/anglophone brute |
| Générateur de squelettes (`script new`) | M4 | Simple à ajouter mais non essentiel au premier tour |

## 4. Questions encore ouvertes (non bloquantes)

- Valeur par défaut du budget d'instructions (D3) → calibrer au benchmark M0.
- Faut-il permettre `script export` automatique (touche console) ou bouton mapper ? → M4.
- Les scripts doivent-ils pouvoir **lire** d'autres machines (multi) en v0 ? → Non a
  priori (SP d'abord), confirmer au M2.
- Le menu des scripts doit-il rafraîchir la liste à chaque ouverture du mapper
  (polling `ModIO.GetFiles` au `OnBlockPlaced`) → détail M1.

## 5. Impact sur les jalons (amendement de l'étude §7)

- **M0** gagne une sous-tâche : POC **watchdog** (`IDebugger` compteur) en plus du passage
  scanner + bench tick.
- **M1** précise le mapper du contrôleur : `MMenu` scripts (bibliothèque), `MToggle` actif.
- **M2/M3** inchangés (capteurs, actionneurs, adressage).
- **M4** devient explicitement : persistance `.bsg` (D1) + commandes console (D5) +
  seed des exemples (D8) + gestion d'erreurs complète (D3).
- **M5** ajoute : licences/mentions (D8), politique de versioning, étude ilyshk4.

---

*Références rapides — étude : `docs/etude-faisabilite-scripting.md` ; doc officielle
extrait : `docs/modding-doc.txt` ; code jeu : `decomp/Assembly-CSharp/` ;
MoonSharp v2.0.0.0 : `github.com/moonsharp-devs/moonsharp` (tag `v2.0.0.0`).*
