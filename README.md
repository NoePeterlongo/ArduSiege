# ArduSiege — Lua scripting for Besiege machines

Script your Besiege machines in **Lua**: read sensors, run control loops at
50 Hz, drive blocks — no in-game key bindings required. The **Microcontroller**
block executes a plain `.lua` file from your script library.

- Sense: block positions, velocities, rotations, health, machine state
- Actuate: motor/spring/winch actions by name, sliders, hinge angles —
  with or without vanilla "variable" bindings
- Log: one data file per simulation (CSV or any format), locale-proof numbers
- Safe: sandboxed Lua, instruction watchdog, errors never freeze the game

Version 0.5.x — English player docs. Engineering journals live in `docs/`.

---

## 1. Installation

**Player install (from a GitHub release):**

1. Download the `ArduSiege-vX.Y.Z.zip` attached to a
   [release](../../releases) — it contains the compiled mod (DLLs included).
2. Extract it into the game's `Besiege_Data/Mods/` directory, so that you end
   up with `Besiege_Data/Mods/ArduSiege/Mod.xml`. The stock `Mods` folder is
   scanned automatically — no console command needed.
3. Launch Besiege, enter any level — mods load on level entry.
4. Open the console (`Ctrl+Shift+K`) and check the mod list: ArduSiege 0.5.0.

**Development setup (this repository):** from the in-game console, run

```
addmodsdir <absolute path to>/mods/ArduSiege
```

This registers the mod directory persistently — the game loads the mod
straight from the repository. Edit code, rebuild, `Ctrl+H` in game to reload.

## 2. Quick start

1. Open the in-game console with `Ctrl+Shift+K`. Type `show_logs true` for
   verbose logs.
2. Place a **Microcontroller** block on your machine (block search:
   "Microcontroller").
3. Open the script folder and edit a script:

   ```
   script folder
   ```

   (a file explorer opens on the library — VS Code + the "Lua" extension works
   well). Copy a demo (`pilotage.lua`, `capteurs.lua`...) under a new name to
   get started.
4. In the Microcontroller's mapper, pick your script in the **script** menu.
5. Start the simulation. `print()` output goes to the console.

Iterating: save the file, then `script reload` — re-reads and re-executes
immediately, even mid-simulation. Restarting the simulation always reloads
from disk. The mapper's script list refreshes on every open — no need to
re-enter the level to see new files.

## 3. The mental model

### Scripts live in the library, not inside machines

Scripts are plain `.lua` files in the mod's data folder
(`Besiege_Data/Mods/Data/ArduSiege_<GUID>/Scripts/`). A machine stores only
the **name** of its script — the library is the single source of truth:

- rename or delete a file and machines referencing it tell you at simulation
  start (the controller idles for that run; the mapper marks it `* missing`);
- `script reload` and simulation restarts always read from disk;
- sharing a machine on the Workshop does **not** ship its script — recipients
  need the script in their own library under the same name.

One Microcontroller = one independent script; several controllers run several
scripts. Each simulation starts with fresh script state.

### How to reference a block

```lua
local self = self                          -- the Microcontroller itself
local motors = machine:blocks("Cog")        -- by type/prefab (substring, case-insensitive)
local group = machine:refs("left")         -- every block tagged "left"
```

**Tagging blocks with `refs`**: in a block's mapper, the "variable instead of
key" field (Advanced Building required) attaches a **tag** to any of the
block's actions — whichever you like. This is just a trick to **identify**
blocks: `machine:refs("left")` returns every block tagged `left`, the tag
persists in the machine save and survives moving blocks, and one key accepts
several tags (`left;boost`). As a bonus, `emulate("left", true)` can also
drive the tagged keys as a group — but identification alone is the main use,
and no tag is ever required to actuate a block (see below).

### What you can read (sensors)

```lua
local p, v = self.position, self.velocity      -- world-space kinematics
local up = machine.up                          -- machine frame (live)
print(motor.sliders)                           -- discover a block's sliders/toggles/actions
local alt = machine:blocks("Altimeter")[1]
print(alt.height)                              -- live measured height
print(input:is_emulated("C"))                  -- presence sensor (Automate -> key C)
```

Vanilla sensor blocks (Anglometer, Altimeter, Speedometer, Sensor, Timer)
broadcast a boolean through their **Automate** binding when their condition
is met. Bind it to a plain key (the presence Sensor defaults to `C`) and read
`input:is_emulated("C")` from the script — no variable/channel needed.
`b.height`/`b.detecting` expose the live measurement where the game makes it
public. Angle and speed are private in the game code — recompute them from
`b.rotation`, `machine.up`, `b.velocity` instead.

### What you can drive (actuation)

```lua
motor:forward(input:is_held("P"))      -- any mapper action, by its UI name
motor:set_slider("speed", 2.5)          -- sliders
hinge:set_angle(30)                     -- steering hinge/rudder target (ramped)
emulate("left", input:is_held("A"))     -- vanilla variable channel (optional)
```

No vanilla binding is needed for actions and sliders — `emulate`/`refs` are
optimal conveniences, not prerequisites.

## 4. A complete example

Save this as `drive.lua` in the library (`script folder`), select it on a
Microcontroller placed on a machine with a Cog (powered) — nothing to bind
anywhere:

```lua
-- drive.lua: drive every motor with P, log the machine state every second
local motors = nil
local ticks = 0

function on_sim_start()
    motors = machine:blocks("Cog")
    print(#motors .. " motor(s) found")
    log.ext = "csv"
    log.row("t", "speed", "height")     -- header
end

function on_tick()
    ticks = ticks + 1
    local gas = input:is_held("P")
    for i = 1, #motors do
        motors[i]:forward(gas)          -- full state every tick
        motors[i]:backward(false)
    end
    if ticks % 50 == 0 then             -- one row per second
        local p = self.position
        local v = self.velocity
        local speed = math.sqrt(v.x * v.x + v.y * v.y + v.z * v.z)
        log.row(ticks / 50, speed, p.y)
    end
end

function on_sim_stop()
    print("drive: done after " .. ticks .. " ticks")
end
```

Hold **P**: the motors spin; `script logs` opens the CSV. Swap the action for
`b:contract(true)` on a spring, `b:wind(true)` on a winch — the names are the
ones you see in each block's mapper (`b.actions` lists them).

## 5. Logging data

`log` writes one file per simulation — content and format are entirely yours:

```lua
log.ext = "csv"                          -- extension of your choice (default "txt")
log.row("t", "pos_y", "speed")           -- values joined by log.separator
log.row(ticks / 50, p.y, speed)          -- any values, any columns
log.write('{"t":1,"v":2.5},')            -- raw line: JSON, text, whatever
```

The file is created lazily on the first write
(`Logs/<script>_<timestamp>.<ext>` in the data folder), flushed every ~200
lines and closed automatically at simulation stop — even if the script
errored. Numbers are locale-invariant (French Windows does not break your CSV
decimals). 2 MB cap per run. `script logs` opens the folder.

## 6. Reference frames and units

**Positions, velocities are always world-space** (X east, Y up, Z north).

Three frames matter:

| Frame | Access | Meaning |
|---|---|---|
| World | `self.position`, `self.velocity` | Unity world axes |
| Block | `self.forward`, `self.up`, `self.right` | the block's own axes as world vectors |
| Machine | `machine.up`, `machine.forward`, `machine.right`, `machine.position` | machine-root axes, tracked **live** during simulation ("keep level" = `machine.up.y` close to 1) |

Placement rule (game-wide, not mod-specific): a freshly placed block's
**forward is the normal of the surface it is attached to** — a block placed
on top of the machine has `forward` pointing up. Pressing R spins it around
its forward axis. `machine.rotation` gives Euler angles.

**All angles are degrees** — matching Besiege's UI. Convert at trigonometry
boundaries with `math.rad(x)` / `math.deg(x)`.

## 7. Console commands

| Command | Effect |
|---|---|
| `script list` | List library files |
| `script show` | Per controller: script name, state, running or not |
| `script reload [name]` | Re-read file(s) and hot-restart the script |
| `script folder` | Open the library folder in the file explorer |
| `script logs` | Open the per-simulation logs folder |
| `lua "<code>"` | Run Lua in the active controller's context (or the sandbox) |
| `bench [n]` | Micro-benchmark of the Lua engine |
| `watchdog` | Test the infinite-loop protection (must be interrupted) |
| `watchdog budget [n]` | Read/set the per-tick script instruction budget (default 1000000) |

## 8. API reference

### Globals

| Global | Type | Description |
|---|---|---|
| `print(...)` | function | console output |
| `self` | block | the Microcontroller block itself |
| `machine` | machine | the local player's machine |
| `input` | input | the player's real keyboard |
| `log` | logger | per-simulation data file |
| `emulate(channel, state)` | function | drive all blocks bound to a vanilla message/variable channel |

### Script callbacks (all optional, defined in your file)

| Callback | Called |
|---|---|
| `on_sim_start()` | simulation start (fresh state every run) |
| `on_tick()` | 50 Hz, while simulating |
| `on_sim_stop()` | simulation end |

### Block (any `machine:blocks()`/`refs()` element, and `self`)

| Expression | Returns | Description |
|---|---|---|
| `b.type` | string | block type name |
| `b.prefab_name` | string | prefab name |
| `b.guid` | string | block GUID (regenerated at each machine load) |
| `b.position` | vec3 | world position `{x, y, z}` |
| `b.velocity` | vec3 | world velocity |
| `b.angular_velocity` | vec3 | world angular velocity (rad/s) |
| `b.rotation` | vec3 | Euler angles, degrees |
| `b.quaternion` | table | `{x, y, z, w}` |
| `b.forward` / `b.up` / `b.right` | vec3 | block axes, world vectors |
| `b.health` | number |  |
| `b.is_burning` / `b.is_destroyed` | boolean |  |
| `b.sliders` | table | slider name → display name |
| `b:slider(name)` | number | read a slider |
| `b:set_slider(name, value)` | | write a slider (single-player) |
| `b.actions` | table | action name → display name (each is callable) |
| `b:action(name, state)` | | drive a mapper key by name (generic form) |
| `b:<action>(state)` | | **any** action of the block, by its mapper/UI name — e.g. `motor:forward(true)`, `spring:contract(true)`, `winch:wind(true)`, `flying:spin(true)` |
| `b.angle_target` | number | steering hinge/rudder: commanded target angle (degrees) |
| `b:set_angle(degrees [, v_max])` | | command target angle — ramped at the block's rotation-speed slider; `v_max` (degrees/second) overrides it |
| `b.toggles` | table | toggle name → state |
| `b:toggle(name)` | boolean | read a toggle (e.g. `"automatic"`) |
| `b.height` | number/nil | Altimeter only: live measured height |
| `b.detecting` | boolean/nil | Sensor only: live detection state (before range/reverse filtering) |

Note: `b.forward` (no call) is the block's forward **axis** (a vector);
`b:forward(true)` (call) drives its forward **action**. Both work on the same
name.

### Machine

| Expression | Returns | Description |
|---|---|---|
| `machine.position` | vec3 | reference block center (live during simulation) |
| `machine.velocity` | vec3 | machine velocity (live) |
| `machine.rotation` | vec3 | machine Euler angles, degrees |
| `machine.up` / `machine.forward` / `machine.right` | vec3 | machine axes, world vectors, live |
| `machine.mass` | number |  |
| `machine.health` | number |  |
| `machine.block_count` | number |  |
| `machine.size` | vec3 |  |
| `machine:blocks([filter])` | table | all blocks, or those matching `filter` (substring on type/prefab, case-insensitive) |
| `machine:refs(channel)` | table | blocks whose key listens to the vanilla message `channel` |
| `machine:emitters(channel)` | table | sensor/logic blocks whose Automate key broadcasts the vanilla message `channel` (the producers — `refs` only finds listeners) |

### Input

| Expression | Returns |
|---|---|
| `input:is_held("Space")` | boolean (held this frame) |
| `input:is_pressed("Space")` | boolean (pressed this frame) |
| `input:is_released("Space")` | boolean (released this frame) |
| `input:is_emulated("C")` | boolean — the key is being **emulated** by a sensor/logic block (its Automate binding), not physically pressed |
| `input:is_emulated_pressed("C")` | boolean — emulation started this tick |
| `input:is_emulated_released("C")` | boolean — emulation ended this tick |

Key names are Unity `KeyCode` names: `P`, `Space`, `LeftControl`, `Mouse0`...

`is_emulated` is the sensor side of the vanilla automation system: a block
whose Automate binding is set to key `C` emulates `C` while its condition
holds — the script reads it like a virtual input, without any block having
to listen to `C`.

### Log

| Expression | Description |
|---|---|
| `log.ext = "csv"` | file extension (default `"txt"`, set before first write) |
| `log.separator = ";"` | join separator for `log.row` (default `","`) |
| `log.path` | file path once created (nil before) |
| `log.write(line)` | append one raw line |
| `log.row(v1, v2, ...)` | append one separator-joined line |

### Error handling and limits

- Lua errors (load or runtime) show file + line in the console and disable
  the script until the next run — the game keeps running.
- The watchdog limits each tick to a configurable instruction budget
  (`watchdog budget`); a runaway loop is interrupted, not frozen.
- Sandbox: no `io`, no `load`, no CLR access — all world interaction goes
  through the audited API above.
- Multiplayer: declared incompatible (emulation is host-only by design).
- Two scripts driving the same key/action add up — your responsibility.

## 9. Building from source

Requirements: Docker (image `mono:6.12` pulled automatically). No local
mono/dotnet needed.

```bash
# once (or when MoonSharp sources change): build the trimmed MoonSharp DLL
bash tools/moonsharp-build.sh

# build the mod DLL
bash mods/ArduSiege/src/build.sh

# package the release zip (builds both DLLs, then zips the mod folder
# with its DLLs and licenses into dist/ArduSiege-v<version>.zip)
bash tools/make-release.sh
```

Release zips are built artifacts, not committed: attach
`dist/ArduSiege-v<version>.zip` to a GitHub release (`dist/` is gitignored).

The game runs a security scanner on mod assemblies: no `System.IO` (beyond a
few exact whitelisted types), no reflection, no runtime compilation. The
vendored MoonSharp (`third-party/moonsharp-trimmed/`) has its optional CLR
interop layer removed for that reason. Full constraint list and engineering
history: `AGENTS.md`, `docs/journal-*.md`. Build artifacts (`*.dll`) are not
committed; the game must be closed when editing `Mod.xml` (the loader watches
XML files and regenerates the mod GUID otherwise).

## 10. Troubleshooting

| Symptom | Meaning |
|---|---|
| `script not found in the library` at sim start | File renamed/deleted — `script list` |
| controller marked `* missing` in the mapper | Same, while building |
| `unknown action "x" on this block` | No such key on that block — check `b.actions` |
| `unknown slider "x"` / `unknown toggle "x"` | Check `b.sliders` / `b.toggles` |
| `channel "x" has no listener` | No block bound to that variable — mapper binding (Advanced Building) |
| `cannot access field ... of userdata` | API name typo, or a stale demo — update the mod |
| Script stops after an error | By design; fix and rerun. The watchdog may also have tripped (`watchdog budget`) |
| Mod does not appear in game | Check `Besiege_Data/Mods/` placement or `addmodsdir` registration; mods load on level entry |

## 11. Credits and licenses

- **[MoonSharp](https://github.com/moonsharp-devs/moonsharp)** by Marco
  Mastropaolo (BSD-2-Clause) — the Lua interpreter, in a trimmed build. Full
  license text and trim details: `mods/ArduSiege/ArduSiege/THIRD-PARTY-NOTICES.txt`.
- **[Lua Scripting Mod](https://steamcommunity.com/sharedfiles/filedetails/?id=2383785201)**
  by ilyshk4 — design inspiration (variable-based addressing, steering
  control); no code copied.
- **Lench Scripter Mod** by lenartbezek — API patterns studied while designing
  block addressing.

ArduSiege code: MIT (see `mods/ArduSiege/LICENSE.txt`).
