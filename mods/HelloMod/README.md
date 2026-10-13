# HelloMod — premier mod Besiege

Mod minimal qui affiche un message dans la console en jeu au chargement du mod.
Sert de validation complète de la chaîne : code C# → compilation Docker → chargement par le mod loader officiel du jeu.

## Structure (conforme à la doc officielle, `Besiege_Data/Documentation/Modding.pdf`)

```
mods/HelloMod/            ← dossier "projet" (enregistré auprès du jeu via addmodsdir)
├── HelloMod/             ← CE dossier est chargé par le jeu
│   ├── Mod.xml           ← manifeste du mod
│   └── HelloMod.dll      ← assembly compilé (généré par le build)
└── src/
    ├── HelloMod.cs       ← code source
    └── build.sh          ← compilation via Docker (image mono:6.12)
```

## Compiler

```bash
bash mods/HelloMod/src/build.sh
```

La compilation se fait dans Docker (image `mono:6.12`, téléchargée au premier build, ~650 Mo)
en référençant les DLL du jeu (`Besiege/Besiege_Data/Managed/`) → runtime CLR 2.0 identique,
zéro installation sur la machine.

## Enregistrer le mod auprès du jeu (une seule fois)

1. Lancer le jeu depuis la copie : `cd Besiege && ./Besiege.x86_64`
2. Ouvrir la console : `Ctrl+Shift+K`
3. Taper : `addmodsdir "/home/noe/Code/Modding_besiege/mods/HelloMod"`

## Tester

1. Dans la console : `show_logs true` (affiche tous les logs en jeu)
2. Entrer dans le multiverse ou dans un niveau (le mod se charge à ce moment-là, pas au lancement)
3. Le message `[HelloMod] Bonjour !` apparaît dans la console

## Notes

- `<Debug>true</Debug>` dans Mod.xml : recharge à chaud de valeurs (colliders, transforms…)
  quand un fichier du mod change + recompilation/maintenance à chaque chargement.
- Après une modification du code : relancer `build.sh` puis `Ctrl+H` en jeu (reload), ou juste redémarrer le jeu.
- Ne jamais commiter `HelloMod.dll` (ignoré par .gitignore) ni le dossier `Besiege/`.
- Le mod ID (GUID) est inséré automatiquement à la fin de Mod.xml au premier chargement : commiter ce fichier avec son ID pour que l'identité du mod reste stable.
