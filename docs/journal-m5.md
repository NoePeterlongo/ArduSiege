# Journal M5 — hardening (licences, watchdog budget, distribution readiness)

> Final milestone of docs/etude-faisabilite-scripting.md section 7: sandbox
> hardening, licenses/notices, instruction budget, player doc, distribution
> readiness.

## Status: DONE (code side; in-game sanity check pending at next play session)

## 1. Licenses and notices (D8)

- `mods/ScriptingMod/ScriptingMod/THIRD-PARTY-NOTICES.txt` — ships with the mod:
  MoonSharp v2.0.0.0 (BSD-2-Clause, full upstream text), the exact nature of the
  trim (reflection layer removed, ModuleRegister generated), the upstream
  commit, and design-inspiration credit to ilyshk4's Lua Scripting Mod (no
  code copied). Obligation satisfied before any Workshop distribution.
- `mods/ScriptingMod/LICENSE.txt` — MIT for the mod code (player choice of
  license: MIT proposed by default, owner may change it before publishing).

## 2. Watchdog budget (D3, calibration)

- The per-tick budget was a hard-coded const (1M instructions). Now a mutable
  static, read/set by a new console command:
  `watchdog budget [n]` (min 10000, default 1000000, read when called without
  a value). Applies to simulations STARTED after the change; in-memory only
  (resets on game restart) - persisting it is a future nicety if ever needed.
- Calibration data point (journal M0): 50M instructions ~ 17 s freeze ->
  1M/tick bounds a runaway loop to ~0.3 s of one tick, and a normal 2000-instr
  tick costs ~0.67 ms: the default leaves two orders of magnitude of headroom.

## 3. Repository cleanup

- HelloMod (the M0 chain-validation POC) removed: ScriptingMod is the
  functional reference now. AGENTS.md updated accordingly (structure, build
  command, registered mod dir).
- Repo layout documented in AGENTS.md (docs/journals, third-party, tools).

## 4. Carried-over hardening items (already done during M0-M4)

- Sandbox: Preset_SoftSandbox (no io/load/CLR), all world access through
  audited C# callbacks; watchdog (see 2); errors with file+line disable the
  script without freezing the game.
- Live-data audit: machine Ref* sources (frozen-value fix), locale-invariant
  numbers (OBJ loader lesson, applied to logs).

## 5. Not done (deliberate, documented)

- ilyshk4 pattern study (raycasts etc.): the harvestable patterns (refs-style
  block addressing, set_steering => set_angle) were absorbed during M3/M4 work;
  a deeper study is only worth it if the API grows further.
- API semver policy: the API is still pre-1.0; versioning discipline starts at
  the Workshop release.
- MP support: declared incompatible (Mod.xml), emulation is host-only by design.

## In-game sanity check (next session)

1. Mod loads (scanner verdict on the new SimLogger/globalization IL);
2. `watchdog budget` reads 1000000; set `watchdog budget 100000` -> run a
   heavy script -> disabled at ~100k as expected;
3. version 0.5.0 shown in the mod list;
4. quick tour: pilotage (direct actions), logger (per-sim log), noms (refs).
