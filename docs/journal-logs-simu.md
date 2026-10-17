# Journal logs-simu — per-simulation log files

> Branch `logs-simu` (post-English-switch). Player need: export structured data
> (CSV or other) from scripts to analyze control loops offline. One log file
> per simulation at most.

## Design

- **API**: `log.row(v1, v2, ...)` (separator-joined line, default ","),
  `log.write(line)` (raw line), `log.path` (nil until first write),
  `log.separator = ";"` (settable).
- **Lifecycle** (one file per sim at most): file created lazily on the FIRST
  write (idle sims log nothing), auto-named `Logs/<script>_<timestamp>.csv` in
  the data folder; flushed every ~200 lines (survives a kill) and closed with a
  console summary at simulation stop — even on script error.
- **Sandbox**: Lua has no `io` — all writes go through C#. `StreamWriter` is
  forbidden by the whitelist (exact types), so: in-memory StringBuilder +
  `ModIO.WriteAllText` flushes.
- **Locale**: numbers formatted with `CultureInfo.InvariantCulture` — on a
  French Windows, default `ToString()` gives "0,5", a silent CSV killer
  (cf. AGENTS.md rule 10, the OBJ loader lesson).
- **Cap**: 2 MB per simulation (watchdog bounds CPU, this bounds memory);
  writes refused past it with a console message.
- **Console**: new `script logs` subcommand opens the log folder.

## Implementation

- `src/SimLogger.cs` — core logger (buffer, lazy file, flush, cap) +
  `LuaLogApi` wrapper (`IUserDataType`).
- `MicrocontrollerScript`: logger created at runtime mount, passed through
  `LuaApi.Install` (global `log`), closed in `OnSimulateStop` BEFORE teardown.
- Demo `logger.lua` (SMDEMO v2): one CSV row per second (position, speed) —
  validated by the MoonSharp parse harness (PARSE+RUN OK).
- User guide: new section 7 "Logging data (per simulation)"; `script logs` in
  the command table.
- Build OK; static scan clean (`MemberInfo::` 0, `Globalization` present in IL
  but not on the blacklist — definitive verdict at in-game load).


## Revision (player feedback): format-agnostic

- The forced `.csv` extension betrayed a format bias. Now **agnostic**:
  - `log.ext` (settable, before first write, letters/digits only, default
    "txt") — the file is named `Logs/<script>_<timestamp>.<ext>` ;
  - `log.write` = raw lines (any format), `log.row` = generic
    values-joined-by-separator (nothing CSV-specific in the API itself) ;
  - `log.ext` after file creation is ignored silently (the file exists).
- Demo `logger.lua` bumped to SMDEMO v2: sets `log.ext = "csv"` explicitly,
  teaching the pattern.

## Player test (expected)

1. Launch the game -> `logger.lua installed/updated (SMDEMO v2)`;
2. simulate any machine with script `logger` -> console: `log opened: Logs/...`;
3. let it run, stop the sim -> console: `log written: ... (N lines, X KiB)`;
4. `script logs` -> open the CSV (comma decimal separators even on FR locale);
5. idle sim (script that never logs) -> no file created;
6. a script that errors mid-run -> log still written at stop.

*(to be completed: results)*
