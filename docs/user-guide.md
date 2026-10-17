# ScriptingMod — User Guide

Script your Besiege machines in **Lua**. Sense, decide, actuate: the
*Microcontroller* block runs a Lua script on your machine at 50 Hz, reading
sensors and driving blocks — no in-game key bindings required.

Version 0.4.x. Work in progress: expect sharp edges, and read the console.

---

## 1. Getting started

1. **Install** the mod (Workshop, or copy the mod folder). Enter a level — mods
   load on level entry, not at game launch.
2. Open the in-game console with `Ctrl+Shift+K`, type `show_logs true` for
   verbose logs.
3. **Place a Microcontroller block** on your machine (block search:
   "Microcontroller", "Script", "Lua").
4. Its mapper shows a **script** menu (your library) and an **active** toggle.
5. Start the simulation: the script runs. `print()` goes to the console.

### The script library

Scripts are plain `.lua` files in the mod's data folder
(`Besiege_Data/Mods/Data/ScriptingMod_<GUID>/Scripts/`). Open it from the game
console with:

```
script folder
```

Edit files with any editor (VS Code + a Lua extension works well). Demos
(`hello.lua`, `capteurs.lua`, `actionneurs.lua`, `noms.lua`, `pilotage.lua`)
are installed automatically and updated by the mod; copy them under a new name
to customize without being overwritten.

Selecting a script in the mapper stores **its name** in your machine. The
library is the single source of truth: if a file is missing at simulation
start, the controller tells you in the console and does nothing for that run.

### Iterating

- Edit the file, then `script reload` in the console — re-reads and re-executes
  immediately, even mid-simulation.
- Restarting the simulation always reloads the script from disk.
- The mapper's script list refreshes every time you open it — no need to
  re-enter the level to see new files.

### Console commands

| Command | Effect |
|---|---|
| `script list` | List library files |
| `script show` | Per controller: script name, state, running or not |
| `script reload [name]` | Re-read file(s) and hot-restart the script |
| `script folder` | Open the library folder in the file explorer |
| `lua "<code>"` | Run Lua in the active controller's context (or the sandbox if none) |
| `bench [n]` | Micro-benchmark of the Lua engine |
| `watchdog` | Test the infinite-loop protection (should be interrupted) |

---

## 2. Reference frames and axes

Three frames matter. **Positions, velocities are always world-space.**

### World frame

X east, Y up, Z north (standard Unity). `self.position`, `self.velocity` etc.
are world-space: a stabilizer hovering at constant height reads
`self.position.y`.

### Block frame

`self.forward`, `self.up`, `self.right` are the block's **own** axes, in world
coordinates. By the game's placement rule (`AddPiece`, PlaceMode.Normal), a
freshly placed block's **forward is the normal of the surface it is attached
to**:

- placed on a horizontal surface → `forward` points up,
- placed on a wall → `forward` points away from the wall,
- pressing R spins it around its forward axis.

This applies to **every** block in Besiege, vanilla ones included. It is not a
bug in the Microcontroller. Use block axes for things expressed relative to
the block; use world/machine vectors for navigation and stabilization.

### Machine frame

`machine.up`, `machine.forward`, `machine.right` are the machine's own axes (in
world coordinates), tracked live during the simulation from the machine's
reference block — useful for control loops ("keep level" = `machine.up.y`
close to 1). `machine.rotation` gives the corresponding Euler angles,
`machine.position` the reference block's center, `machine.velocity` the
machine's velocity. These fall back to the build-machine frame while building.
Note: `machine.forward` follows the placement convention too — the direction
the machine was built in, not a fixed world axis.

---

## 3. Script structure

A script is a Lua file defining any of these optional callbacks:

```lua
function on_sim_start()          -- simulation start (fresh state each run)
end

function on_tick()              -- 50 Hz, while simulating
end

function on_sim_stop()           -- simulation end
end
```

State is fresh at every simulation start. A watchdog limits each run to 1M VM
instructions; exceeding it disables the script (console message) without
freezing the game. Errors show file and line in the console and disable the
script until the next run.

Available globals: `print`, `self`, `machine`, `input`, `emulate`, plus block
methods below.

---

## 4. Addressing blocks

Three complementary ways to get a block handle:

```lua
-- by type/prefab filter (substring, case-insensitive)
local wheels = machine:blocks("Cog")       -- array of blocks

-- by vanilla variable binding: all blocks whose key listens to message "x"
-- (mapper "variable instead of key" field, Advanced Building required)
local group = machine:refs("pousser")

-- the controller itself
local pos = self.position
```

Machine data: `machine.position`, `machine.velocity`, `machine.rotation`,
`machine.up`, `machine.forward`, `machine.right`, `machine.mass`,
`machine.health`, `machine.block_count`, `machine.size`.

---

## 5. Reading data (sensors)

On any block handle `b`:

| Expression | Meaning |
|---|---|
| `b.type`, `b.prefab_name`, `b.guid` | identity |
| `b.position`, `b.velocity`, `b.angular_velocity` | world-space kinematics |
| `b.rotation` | Euler angles (degrees) |
| `b.quaternion` | `{x,y,z,w}` |
| `b.forward`, `b.up`, `b.right` | block axes (world-space vectors) |
| `b.health`, `b.is_burning`, `b.is_destroyed` | state |

Actuator state:

| Expression | Meaning |
|---|---|
| `b.angle_target` | steering hinge / rudder: commanded target angle (degrees) |
| `b:set_angle(45)` | steering hinge / rudder: command the target angle (enables target-angle mode for the run) |
| `b.toggles` | table of the block's toggles, name → state |
| `b:toggle("automatic")` | read one toggle's state |

The hinge's *actual* angle is not exposed by the game (no internal state; the
vanilla Anglometer computes it geometrically) — use `angle_target` as feedback
of your command, or derive geometry from block `position`/`rotation`.

Keyboard: `input:is_held("P")`, `input:is_pressed("Space")`,
`input:is_released("Mouse0")` — Unity `KeyCode` names.

---

## 6. Driving blocks

### Actions — the names you see in the mapper

Every key binding of a block is callable by its name (mapper key or display
name, case-insensitive). Motors have `forward`/`backward`, flying blocks
`spin`, springs `contract`, rope winches `wind`/`unwind`:

```lua
local motor = machine:blocks("Cog")[1]

motor:forward(input:is_held("P"))    -- full state every tick
motor:backward(input:is_held("O"))   -- opposite key handled independently

print(motor.actions)    -- list available actions: { forward = "Forward", ... }
motor:action("forward", true)   -- generic form (computed names)
```

**Idiom: set the complete state every tick** (as in the examples above). The
mod only sends changes; if both directions are held, motors prioritize
forward. All keys are released automatically when the simulation stops.

Note: `b.forward` without a call is the block's forward **axis** (a vector);
`b:forward(true)` with a call drives its forward **action**. Both work on the
same name.

### Sliders

```lua
print(b.sliders)                  -- list: { speed = "Speed", ... }
local v = b:slider("speed")       -- read
b:set_slider("speed", 1.5)        -- write (single-player)
```

### Key emulation via vanilla variables (optional)

If you bind a block's key to a **message** in its mapper (Advanced Building,
"variable instead of key"), you can drive it by channel — and address every
block bound to that channel:

```lua
emulate("pousser", input:is_held("P"))   -- drive all listeners of "pousser"
local listeners = machine:refs("pousser")
```

Variables are optional for actuation (direct actions need no binding at all)
but remain handy: they persist in the machine save natively and group blocks.

---

## 7. Limits and notes

- One script per Microcontroller; several controllers run independent
  scripts. Two scripts driving the same key/action add up — your
  responsibility.
- Multiplayer: not compatible for now (emulation is host-only by design).
- The Microcontroller block itself is what activates the machine's key
  emulation system — keep one in the machine.
- Missing script file → console message, controller idle for the run.

## 8. Troubleshooting

| Symptom | Meaning |
|---|---|
| `script introuvable` at sim start | File renamed/deleted — `script list` |
| `action inconnue "x" sur ce bloc` | No such key on that block — check `b.actions` |
| `cannot access field ... of userdata` | API name typo, or a stale demo — update the mod |
| Script stops after an error | By design; fix and rerun, watchdog may also have tripped |
