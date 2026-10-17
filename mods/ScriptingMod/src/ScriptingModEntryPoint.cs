// ScriptingModEntryPoint - M0 POC (Besiege).
//
// Validates in game, end to end:
//   1. that the trimmed MoonSharp DLL (third-party/moonsharp-trimmed) passes the
//      mod loader's AssemblyScanner - otherwise the mod does not load at all
//      ([Security] message) ;
//   2. that a sandboxed Script (Preset_SoftSandbox) runs: hello Lua, os.time,
//      string.* ;
//   3. the tick micro-benchmark (`bench` command) ;
//   4. the instruction watchdog (`watchdog` command -> interrupted infinite loop).
//
// In-game console: Ctrl+Shift+K; logs visible after `show_logs true`.
// Mods load on entering a level/multiverse (not at launch).
// See docs/journal-m0.md (step 8).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Modding;
using MoonSharp.Interpreter;
// The game exposes Modding.DataType (network types): alias to remove ambiguity
// with MoonSharp.Interpreter.DataType (Lua value types).
using MoonDataType = MoonSharp.Interpreter.DataType;

namespace ScriptingMod
{
    public class ScriptingModEntryPoint : ModEntryPoint
    {
        // Per-execution budget (VM instructions per DoString/Call) - to be calibrated.
        private const long WATCHDOG_BUDGET = 50000000L;

        private Script m_Script;
        private WatchdogDebugger m_Watchdog;

        public override void OnLoad()
        {
            ModConsole.Log("[ScriptingMod] M1: loading...");

            try
            {
                // 1. Instance Lua sandboxee
                m_Script = new Script(CoreModules.Preset_SoftSandbox);

                // print -> console du jeu
                m_Script.Globals.Set("print", DynValue.NewCallback(
                    (ctx, args) =>
                    {
                        PrintToConsole(args);
                        return DynValue.Nil;
                    }, "print"));

                // M2: input usable from the console (no machine context)
                m_Script.Globals.Set("input", UserData.Create(new LuaInputApi()));

                // 2. Watchdog branchee sur la VM
                m_Watchdog = new WatchdogDebugger(WATCHDOG_BUDGET);
                m_Script.AttachDebugger(m_Watchdog);
            }
            catch (Exception ex)
            {
                ModConsole.Log("[ScriptingMod] MoonSharp init ERROR: " + ex);
                return;
            }

            // 3. Hello Lua (teste aussi : parse, sandbox os.time, string.format)
            string helloResult = RunLua(
                "print('hello depuis Lua !') " +
                "return string.format('%s %s', _VERSION, _MOONSHARP.version)");
            if (helloResult != null)
                ModConsole.Log("[ScriptingMod] hello OK -> " + helloResult);

            // 4. Sanbox verifiee : io/load interdits, os.time autorise
            string sandboxResult = RunLua(
                "local ok = (io == nil) and (load == nil) and (os ~= nil and os.time() > 0)\n" +
                "return ok and 'sandbox OK' or 'SANDBOX KO'");
            if (sandboxResult != null)
                ModConsole.Log("[ScriptingMod] sandbox -> " + sandboxResult);

            // 5. Commandes console de test
            ModConsole.RegisterCommand("lua", LuaCommand,
                "lua \"<lua code>\" - runs Lua code and prints the result");
            ModConsole.RegisterCommand("bench", BenchCommand,
                "bench [n] - Lua micro-benchmark (n iterations, default 1,000,000)");
            ModConsole.RegisterCommand("watchdog", WatchdogCommand,
                "watchdog - runs a Lua infinite loop: must be interrupted by the watchdog");

            // M4: library (D1-ter: name only)
            ModConsole.RegisterCommand("script", ScriptCommand,
                "script list|show|reload [name]|folder|logs - script library and logs");

            // M4: refresh the Microcontrollers' menu when a mapper opens
            // (new scripts visible without restarting the level).
            BlockMapper.onMapperOpen += OnMapperOpen;

            // 6. M1: seed the script library (the mod's data folder)
            SeedScriptLibrary();

            ModConsole.Log("[ScriptingMod] M1 ready. Commands: lua \"<code>\", bench, watchdog");
        }

        // --- M1: script library (the mod's persistent data folder) ---

        private const string SCRIPTS_DIR = "Scripts";

        private static void SeedScriptLibrary()
        {
            try
            {
                if (!Modding.ModIO.ExistsDirectory(SCRIPTS_DIR, data: true))
                {
                    Modding.ModIO.CreateDirectory(SCRIPTS_DIR, data: true);
                }
                // M2: versioned demos (SMDEMO vN tag) - updated by the mod,
                // never overwritten when the player renamed/customized them (no tag).
                WriteDemoIfOutdated("hello.lua", 2, HELLO_LUA);
                WriteDemoIfOutdated("capteurs.lua", 4, CAPTEURS_LUA);
                WriteDemoIfOutdated("actionneurs.lua", 3, ACTIONNEURS_LUA);
                WriteDemoIfOutdated("noms.lua", 4, NOMS_LUA);
                WriteDemoIfOutdated("pilotage.lua", 3, PILOTAGE_LUA);
                WriteDemoIfOutdated("logger.lua", 1, LOGGER_LUA);
            }
            catch (Exception ex)
            {
                ModConsole.Log("[ScriptingMod] library seed ERROR: " + ex.Message);
            }
        }

        // Demos: the embedded content carries an "SMDEMO vN" tag in its header. The
        // local file is (re)written when absent, or when its SMDEMO tag is older. A
        // file without a tag (player-customized) is NEVER touched.
        private static void WriteDemoIfOutdated(string fileName, int version, string content)
        {
            try
            {
                string path = SCRIPTS_DIR + "/" + fileName;
                string tag = "SMDEMO v" + version;

                if (!Modding.ModIO.ExistsFile(path, data: true))
                {
                    Modding.ModIO.WriteAllText(path, content, data: true);
                    ModConsole.Log("[ScriptingMod] library: " + fileName + " installed (" + tag + ")");
                    return;
                }

                string current = Modding.ModIO.ReadAllText(path, data: true);
                int at = current != null ? current.IndexOf("SMDEMO v", System.StringComparison.Ordinal) : -1;
                if (at < 0)
                {
                    return; // not a demo (player file) - do not touch
                }
                int localVersion;
                int start = at + "SMDEMO v".Length;
                int digits = 0;
                while (start + digits < current.Length && current[start + digits] >= '0' && current[start + digits] <= '9')
                {
                    digits++;
                }
                int.TryParse(current.Substring(start, digits), out localVersion);
                if (localVersion >= version)
                {
                    return; // a jour
                }
                Modding.ModIO.WriteAllText(path, content, data: true);
                ModConsole.Log("[ScriptingMod] library: " + fileName + " updated (" + tag + ")");
            }
            catch (Exception ex)
            {
                ModConsole.Log("[ScriptingMod] demo " + fileName + ": " + ex.Message);
            }
        }

// hello.lua demo (SMDEMO v2): minimal on_tick loop
        private const string HELLO_LUA =
            "-- hello.lua (SMDEMO v2): minimal on_tick loop\n" +
            "local ticks = 0\n" +
            "\n" +
            "function on_sim_start()\n" +
            "    print(\"script started: on_tick loop at 50 Hz\")\n" +
            "end\n" +
            "\n" +
            "function on_tick()\n" +
            "    ticks = ticks + 1\n" +
            "    if ticks % 50 == 0 then\n" +
            "        print(ticks .. \" ticks (\" .. (ticks / 50) .. \" s of simulation)\")\n" +
            "    end\n" +
            "end\n" +
            "\n" +
            "function on_sim_stop()\n" +
            "    print(\"script stopped after \" .. ticks .. \" ticks\")\n" +
            "end\n";


        // Demo M2 : capteurs + cinematique derivee (SMDEMO v3 : omega/accel/nivelement)
// capteurs.lua demo (SMDEMO v4): sensors + derived kinematics
        private const string CAPTEURS_LUA =
            "-- capteurs.lua (SMDEMO v4): sensors + derived kinematics\n" +
            "-- exposes: self (block: position/velocity/rotation/quaternion/axes),\n" +
            "--          machine (blocks), input (REAL keyboard)\n" +
            "-- NB: SMDEMO files are updated by the mod; copy this file under another\n" +
            "--     name to customize it without being overwritten.\n" +
            "local ticks = 0\n" +
            "local last_v = {x = 0, y = 0, z = 0}\n" +
            "local accel_s = 0.0\n" +
            "\n" +
            "function on_sim_start()\n" +
            "    print(\"sensors: machine of \" .. machine.block_count .. \" blocks, mass \" .. string.format(\"%.0f\", machine.mass))\n" +
            "    local per_type = {}\n" +
            "    for _, b in ipairs(machine:blocks()) do\n" +
            "        per_type[b.type] = (per_type[b.type] or 0) + 1\n" +
            "    end\n" +
            "    for t, n in pairs(per_type) do\n" +
            "        print(\"  - \" .. t .. \" x\" .. n)\n" +
            "    end\n" +
            "end\n" +
            "\n" +
            "function on_tick()\n" +
            "    ticks = ticks + 1\n" +
            "    -- derived acceleration: dv/dt with dt = 1/50 s (50 Hz fixed tick)\n" +
            "    local v = self.velocity\n" +
            "    local ax = (v.x - last_v.x) * 50\n" +
            "    local ay = (v.y - last_v.y) * 50\n" +
            "    local az = (v.z - last_v.z) * 50\n" +
            "    last_v = v\n" +
            "    local a = math.sqrt(ax * ax + ay * ay + az * az)\n" +
            "    accel_s = accel_s * 0.95 + a * 0.05    -- exponential smoothing\n" +
            "\n" +
            "    if ticks % 50 ~= 0 then return end\n" +
            "    local p = self.position\n" +
            "    local speed = math.sqrt(v.x * v.x + v.y * v.y + v.z * v.z)\n" +
            "    local w = self.angular_velocity\n" +
            "    local omega = math.sqrt(w.x * w.x + w.y * w.y + w.z * w.z) * 180 / math.pi\n" +
            "    local up = self.up\n" +
            "    local flat = up.y > 0.95    -- block up axis aligned with world up?\n" +
            "    print(string.format(\"t=%ds  pos=(%.1f, %.1f, %.1f)  |v|=%.2f  |a|=%.2f  omega=%.1f deg/s  flat=%s  P=%s\",\n" +
            "        ticks / 50, p.x, p.y, p.z, speed, accel_s, omega, tostring(flat), tostring(input:is_held(\"P\"))))\n" +
            "end\n" +
            "\n" +
            "function on_sim_stop()\n" +
            "    print(\"sensors: done after \" .. ticks .. \" ticks\")\n" +
            "end\n";


        // M3 demo: actuators - emulate(channel) + set_slider (SMDEMO v3)
// actionneurs.lua demo (SMDEMO v3): drive vanilla blocks from the script
        private const string ACTIONNEURS_LUA =
            "-- actionneurs.lua (SMDEMO v3): drive vanilla blocks from the script\n" +
            "-- SETUP: enable the Advanced Building option in the game settings,\n" +
            "-- then in the motor's mapper, toggle its key line into a text field\n" +
            "-- (button shown by Advanced Building) and type the MESSAGE \"push\".\n" +
            "local ticks = 0\n" +
            "local motor = nil\n" +
            "\n" +
            "function on_sim_start()\n" +
            "    print(\"actuators: bind a motor to the message 'push' (mapper), then hold P\")\n" +
            "    local motors = machine:blocks(\"Cog\")\n" +
            "    if #motors > 0 then\n" +
            "        motor = motors[1]\n" +
            "        print(\"  target motor: \" .. motor.type)\n" +
            "        for k, d in pairs(motor.sliders) do\n" +
            "            print(\"  slider: \" .. k .. \" (\" .. d .. \")\")\n" +
            "        end\n" +
            "    end\n" +
            "end\n" +
            "\n" +
            "function on_tick()\n" +
            "    emulate(\"push\", input:is_held(\"P\"))\n" +
            "    ticks = ticks + 1\n" +
            "    if motor == nil or ticks % 100 ~= 0 then return end\n" +
            "    -- modulates the motor speed every 2 s (analog set_slider demo)\n" +
            "    local v = motor:slider(\"speed\")\n" +
            "    local target = (v < 1.5) and 2.5 or 0.5\n" +
            "    motor:set_slider(\"speed\", target)\n" +
            "    print(string.format(\"  slider speed: %.1f -> %.1f\", v, target))\n" +
            "end\n" +
            "\n" +
            "function on_sim_stop()\n" +
            "    print(\"actuators: done after \" .. ticks .. \" ticks\")\n" +
            "end\n";


        // Demo refs : adressage des blocs par variable vanilla (SMDEMO v3)
// refs demo (SMDEMO v4): address blocks by vanilla variable (refs)
        private const string NOMS_LUA =
            "-- noms.lua (SMDEMO v4): address blocks by vanilla variable (refs)\n" +
            "-- SETUP: bind a block's key to the MESSAGE \"push\" in its\n" +
            "-- mapper (text/variable field, Advanced Building); several tags\n" +
            "-- are possible (a;b): control AND identification on the same key.\n" +
            "local ticks = 0\n" +
            "local motor = nil\n" +
            "\n" +
            "function on_sim_start()\n" +
            "    local pushers = machine:refs(\"push\")\n" +
            "    print(#pushers .. \" block(s) listen to the variable 'push'\")\n" +
            "    for i, b in ipairs(pushers) do\n" +
            "        print(\"  refs: \" .. b.type)\n" +
            "    end\n" +
            "    if #pushers > 0 then\n" +
            "        motor = pushers[1]\n" +
            "        for k, d in pairs(motor.sliders) do\n" +
            "            print(\"  slider: \" .. k .. \" (\" .. d .. \")\")\n" +
            "        end\n" +
            "    end\n" +
            "end\n" +
            "\n" +
            "function on_tick()\n" +
            "    emulate(\"push\", input:is_held(\"P\"))\n" +
            "    ticks = ticks + 1\n" +
            "    if motor == nil or ticks % 100 ~= 0 then return end\n" +
            "    -- modulates the listener block's speed every 2 s (set_slider demo)\n" +
            "    local v = motor:slider(\"speed\")\n" +
            "    local target = (v < 1.5) and 2.5 or 0.5\n" +
            "    motor:set_slider(\"speed\", target)\n" +
            "    print(string.format(\"  [refs] slider speed: %.1f -> %.1f\", v, target))\n" +
            "end\n" +
            "\n" +
            "function on_sim_stop()\n" +
            "    print(\"refs: done after \" .. ticks .. \" ticks\")\n" +
            "end\n";


        // Demo actionnement direct : les actions portent le nom vu dans l'UI (SMDEMO v2)
// direct actuation demo (SMDEMO v3): actions carry their UI names
        private const string PILOTAGE_LUA =
            "-- pilotage.lua (SMDEMO v3): drive a block through its ACTIONS (UI names)\n" +
            "-- SETUP: none! Place a motor (Cog) - no mapper binding required.\n" +
            "-- A block's actions are visible in its mapper (keys):\n" +
            "-- motor: forward/backward ; flying block: spin ; spring: contract...\n" +
            "-- Idiom: FULL STATE every tick - b:forward(P held), b:backward(O held).\n" +
            "local motor = nil\n" +
            "local ticks = 0\n" +
            "\n" +
            "function on_sim_start()\n" +
            "    local motors = machine:blocks(\"Cog\")\n" +
            "    if #motors == 0 then\n" +
            "        print(\"place a motor (Cog) - no mapper binding required!\")\n" +
            "        return\n" +
            "    end\n" +
            "    motor = motors[1]\n" +
            "    print(\"driven motor: \" .. motor.type .. \" - P forward, O backward\")\n" +
            "    for name, label in pairs(motor.actions) do\n" +
            "        print(\"  action: \" .. name .. \" (\" .. label .. \")\")\n" +
            "    end\n" +
            "end\n" +
            "\n" +
            "function on_tick()\n" +
            "    if motor == nil then return end\n" +
            "    ticks = ticks + 1\n" +
            "    motor:forward(input:is_held(\"P\"))\n" +
            "    motor:backward(input:is_held(\"O\"))\n" +
            "    if ticks % 100 == 0 then\n" +
            "        local v = motor:slider(\"speed\")\n" +
            "        motor:set_slider(\"speed\", (v < 1.5) and 2.5 or 0.5)\n" +
            "    end\n" +
            "end\n" +
            "\n" +
            "function on_sim_stop()\n" +
            "    print(\"pilotage: done after \" .. ticks .. \" ticks\")\n" +
            "end\n";


        // logger.lua demo (SMDEMO v1): per-simulation CSV log
        private const string LOGGER_LUA =
            "-- logger.lua (SMDEMO v1): per-simulation CSV log\n" +
            "-- Logs one CSV row per second (position, velocity) to Logs/<name>_<time>.csv.\n" +
            "-- The file is created on the first log.row call, flushed regularly and\n" +
            "-- closed automatically at simulation stop. 'script logs' opens the folder.\n" +
            "local ticks = 0\n" +
            "\n" +
            "function on_sim_start()\n" +
            "    log.row(\"t\", \"pos_x\", \"pos_y\", \"pos_z\", \"speed\")   -- header\n" +
            "    print(\"logging to: \" .. (log.path or \"(created on first row)\"))\n" +
            "end\n" +
            "\n" +
            "function on_tick()\n" +
            "    ticks = ticks + 1\n" +
            "    if ticks % 50 ~= 0 then return end\n" +
            "    local p = self.position\n" +
            "    local v = self.velocity\n" +
            "    local speed = math.sqrt(v.x * v.x + v.y * v.y + v.z * v.z)\n" +
            "    log.row(ticks / 50, p.x, p.y, p.z, speed)\n" +
            "end\n" +
            "\n" +
            "function on_sim_stop()\n" +
            "    print(\"logger: done - check \" .. (log.path or \"(nothing logged)\"))\n" +
            "end\n";

        // --- Runs Lua, shows errors with line numbers, returns the result as a string ---

        private string RunLua(Script lua, WatchdogDebugger watchdog, string code, string chunkName = null)
        {
            if (watchdog != null)
            {
                watchdog.Reset();
            }
            try
            {
                DynValue v = lua.DoString(code, null, chunkName ?? "chunk");
                if (v == null || v.IsNil()) return null;
                if (v.Type == MoonDataType.String) return v.String;
                return v.ToString();
            }
            catch (WatchdogBudgetExceededException wex)
            {
                ModConsole.Log("[ScriptingMod] WATCHDOG: " + wex.Message);
                return null;
            }
            catch (InterpreterException iex) // couvre SyntaxErrorException + ScriptRuntimeException
            {
                ModConsole.Log("[ScriptingMod] Lua ERROR: " + (iex.DecoratedMessage ?? iex.Message));
                return null;
            }
            catch (Exception ex)
            {
                ModConsole.Log("[ScriptingMod] unexpected ERROR: " + ex);
                return null;
            }
        }

        private string RunLua(string code, string chunkName = null)
        {
            return RunLua(m_Script, m_Watchdog, code, chunkName);
        }

        private void PrintToConsole(CallbackArguments args)
        {
            // print(...) - concatenates all arguments separated by tabs
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < args.Count; i++)
            {
                if (i > 0) sb.Append('\t');
                sb.Append(args[i].ToString());
            }
            ModConsole.Log("[lua] " + sb.ToString());
        }

        // --- Commandes console ---

        // --- M2: the Microcontroller's active runtime (for the `lua` console command) ---
        // During a simulation, `lua "..."` runs in the microcontroller's context
        // (the running script's self/machine/input). Otherwise, back to the console
        // POC (input available; machine resolved on the fly for the local player).

        internal static Script ActiveRuntime { get; private set; }
        internal static WatchdogDebugger ActiveWatchdog { get; private set; }
        internal static string ActiveScriptName { get; private set; }

        internal static void SetActiveRuntime(Script lua, WatchdogDebugger watchdog, string scriptName)
        {
            ActiveRuntime = lua;
            ActiveWatchdog = watchdog;
            ActiveScriptName = scriptName;
            ModConsole.Log("[ScriptingMod] lua console -> microcontroller context (" + (scriptName ?? "?") + ")");
        }

        internal static void ClearActiveRuntime()
        {
            ActiveRuntime = null;
            ActiveWatchdog = null;
            ActiveScriptName = null;
        }

        private void RefreshConsoleMachine()
        {
            // local machine, re-resolved on every call (the POC has no block
            // context); nil when there is no machine (menu, multiverse...)
            // NB M4: GetLocalPlayer() is null in SOLO (PlayerData.localPlayer is
            // only assigned in MP) -> GetLocalMachine() covers SP too.
            DynValue machineValue = DynValue.Nil;
            try
            {
                Modding.Blocks.PlayerMachine machine = GetLocalMachine();
                if (machine != null)
                {
                    machineValue = UserData.Create(new LuaMachineApi(machine));
                }
            }
            catch (Exception)
            {
                // machine laissee a nil
            }
            m_Script.Globals.Set("machine", machineValue);
        }

        private void LuaCommand(string[] cmdArgs)
        {
            if (cmdArgs.Length == 0)
            {
                ModConsole.Log("Usage: lua \"<lua code>\"");
            ModConsole.Log("(simulating: microcontroller context - self/machine/input; otherwise POC - local player's machine/input)");
                return;
            }
            // the console handles quoting an argument with spaces: re-join everything just in case
            string code = string.Join(" ", cmdArgs);

            Script target = ActiveRuntime;
            WatchdogDebugger watchdog = ActiveWatchdog;

            if (target != null)
            {
                ModConsole.Log("[lua] (context: " + ActiveScriptName + ")");
            }
            else
            {
                target = m_Script;
                watchdog = m_Watchdog;
                RefreshConsoleMachine();
            }

            string result = RunLua(target, watchdog, code, "console");
            if (result != null)
                ModConsole.Log("[ScriptingMod] -> " + result);
        }

        private void BenchCommand(string[] cmdArgs)
        {
            long n = 1000000L;
            if (cmdArgs.Length > 0) long.TryParse(cmdArgs[0], out n);
            if (n < 1) n = 1000000L;

            ModConsole.Log(string.Format("[bench] Lua: {0:N0} iterations + additions...", n));

            // benchmark 1: pure arithmetic (with watchdog = active hooks - real conditions)
            m_Watchdog.Reset();
            Stopwatch sw = Stopwatch.StartNew();
            DynValue res = m_Script.DoString(
                "local s = 0 for i = 1, " + n + " do s = s + i end return s", null, "bench");
            sw.Stop();
            double ipsWithDbg = n / Math.Max(0.001, sw.Elapsed.TotalSeconds);

            ModConsole.Log(string.Format(
                "[bench] 1) boucle + adds (watchdog ON) : {0} ms, {1:N0} instr/s approx, res={2}",
                sw.ElapsedMilliseconds, ipsWithDbg, res.Number));

            // extrapolation: cost of a 2000-instruction tick at 50 Hz
            double msPerTick = (sw.Elapsed.TotalMilliseconds / Math.Max(1, n)) * 2000.0;
            ModConsole.Log(string.Format(
                "[bench] 2) cout estime d'un tick 50 Hz a 2000 instr : {0:0.000} ms/frame (budget Unity ~ 20 ms)",
                msPerTick));

            ModConsole.Log(string.Format(
                "[bench] 3) watchdog budget actuel : {0:N0} instr/execution", WATCHDOG_BUDGET));
        }

        private void WatchdogCommand(string[] cmdArgs)
        {
            ModConsole.Log("[watchdog] running `while true do end` - if the watchdog works, this message is followed by an interruption; otherwise the game freezes.");

            m_Watchdog.Reset();
            try
            {
                m_Script.DoString("while true do end", null, "watchdog-test");
                // On ne doit JAMAIS atteindre cette ligne :
                ModConsole.Log("[watchdog] FAIL: the infinite loop finished without interruption?!");
            }
            catch (WatchdogBudgetExceededException wex)
            {
                ModConsole.Log("[watchdog] SUCCESS: " + wex.Message);
                ModConsole.Log("[watchdog] The game keeps running. Replay the simulation to keep playing normally.");
            }
            catch (Exception ex)
            {
                ModConsole.Log("[watchdog] Interrupted by another exception type: " + ex.GetType().FullName + ": " + ex.Message);
            }
        }

        // --- M4: `script` commands (library, D1-bis) ---

        private void ScriptCommand(string[] cmdArgs)
        {
            if (cmdArgs.Length == 0)
            {
                ModConsole.Log("Usage: script list | show | reload [name] | folder | logs");
                return;
            }
            string arg = cmdArgs.Length > 1 ? cmdArgs[1] : null;
            switch (cmdArgs[0])
            {
                case "list":
                    CmdScriptList();
                    return;
                case "show":
                    CmdScriptShow();
                    return;
                case "reload":
                    CmdScriptReload(arg);
                    return;
                case "folder":
                    Modding.ModIO.OpenFolderInFileBrowser(SCRIPTS_DIR, data: true);
                    ModConsole.Log("[script] library folder opened in the file explorer");
                    return;
                case "logs":
                    Modding.ModIO.CreateDirectory("Logs", data: true);
                    Modding.ModIO.OpenFolderInFileBrowser("Logs", data: true);
                    ModConsole.Log("[script] log folder opened in the file explorer");
                    return;
                default:
                    ModConsole.Log("[script] unknown subcommand: " + cmdArgs[0]
                        + " (list | show | reload | folder | logs)");
                    return;
            }
        }

        /// <summary>
        /// M4: when a mapper opens - if it is a Microcontroller, refresh its menu
        /// from the library then rebuild the widgets (Rebuild, NOT Refresh:
        /// Refresh calls Open() again, therefore onMapperOpen = infinite loop).
        /// </summary>
        private void OnMapperOpen()
        {
            try
            {
                BlockMapper mapper = BlockMapper.CurrentInstance;
                if (mapper == null || mapper.Block == null)
                {
                    return;
                }
                MicrocontrollerScript mc = mapper.Block.GetComponent<MicrocontrollerScript>();
                if (mc == null)
                {
                    return;
                }
                mc.RefreshMenu();
                mapper.Rebuild();
            }
            catch (Exception)
            {
                // a mapper of some other object type: nothing to do
            }
        }

        private void CmdScriptList()
        {
            try
            {
                string[] files = Modding.ModIO.GetFiles(SCRIPTS_DIR, "*.lua", data: true);
                ModConsole.Log("[script] library: " + files.Length + " script(s)");
                foreach (string file in files)
                {
                    ModConsole.Log("  - " + System.IO.Path.GetFileNameWithoutExtension(file));
                }
            }
            catch (Exception ex)
            {
                ModConsole.Log("[script] list failed: " + ex.Message);
            }
        }

        private void CmdScriptShow()
        {
            List<MicrocontrollerScript> controllers = GetControllers();
            if (m_ScanInfo != null)
            {
                ModConsole.Log("[script] scan : " + m_ScanInfo);
            }
            if (controllers.Count == 0)
            {
                ModConsole.Log("[script] no Microcontroller block found (place one or load a machine that has one)");
                return;
            }
            int i = 1;
            foreach (MicrocontrollerScript mc in controllers)
            {
                string name = mc.MenuSelection ?? "(aucun)";
                string etat = mc.HasScriptFile ? "present" : "MISSING (marked * in the mapper)";
                string actif = mc.IsRuntimeActive ? " ; SIMULATING" : string.Empty;
                ModConsole.Log("[script] controller " + i++ + ": \"" + name + "\" - "
                    + etat + actif);
            }
        }

        private void CmdScriptReload(string name)
        {
            List<MicrocontrollerScript> controllers = GetControllers();
            if (m_ScanInfo != null)
            {
                ModConsole.Log("[script] scan : " + m_ScanInfo);
            }
            if (controllers.Count == 0)
            {
                ModConsole.Log("[script] reload: no Microcontroller block found");
                return;
            }
            int n = 0;
            foreach (MicrocontrollerScript mc in controllers)
            {
                string target = mc.MenuSelection;
                if (name != null && target != name)
                {
                    continue;
                }
                if (mc.ReloadFromLibrary())
                {
                    n++;
                }
            }
            ModConsole.Log("[script] reload: " + n + " controller(s) reloaded");
        }

        // trace of the last scan (script show/reload diagnostics)
        private string m_ScanInfo;

        /// <summary>
        /// Local machine's Microcontrollers. Scans the ACTIVE list (sim or building)
        /// then the other as a fallback, deduplicated by GUID; m_ScanInfo describes
        /// each step (diagnostics: player, machine, blocks, components).
        /// </summary>
        private List<MicrocontrollerScript> GetControllers()
        {
            m_ScanInfo = null;
            List<MicrocontrollerScript> result = new List<MicrocontrollerScript>();
            try
            {
                // SP: PlayerData.localPlayer is only assigned in MP (ChatController)
                // -> GetLocalPlayer() returns null in single player. The SP access
                // point is Machine.Active() (MachineObjectTracker.activeMachine).
                Modding.Blocks.PlayerMachine machine = GetLocalMachine();
                if (machine == null)
                {
                    m_ScanInfo = "aucune machine locale (hors niveau ?)";
                    return result;
                }

                bool sim = Modding.Game.IsSimulating;
                System.Collections.ObjectModel.ReadOnlyCollection<Modding.Blocks.Block> blocks =
                    sim ? machine.SimulationBlocks : machine.BuildingBlocks;
                int scanned = ScanControllers(blocks, result);

                // fallback: the inactive list may remain the only valid one depending on mode
                System.Collections.ObjectModel.ReadOnlyCollection<Modding.Blocks.Block> other =
                    sim ? machine.BuildingBlocks : machine.SimulationBlocks;
                int scannedOther = result.Count == 0 ? ScanControllers(other, result) : 0;

                m_ScanInfo = "player OK, machine OK; "
                    + scanned + " block(s) in " + (sim ? "simulation" : "building")
                    + (scannedOther > 0 ? " + " + scannedOther + " in the other list" : string.Empty)
                    + "; " + result.Count + " microcontroller(s)";
            }
            catch (Exception ex)
            {
                m_ScanInfo = "exception: " + ex.GetType().FullName + ": " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// Local machine: MP -> local player; SP -> Machine.Active().
        /// </summary>
        internal static Modding.Blocks.PlayerMachine GetLocalMachine()
        {
            try
            {
                Modding.Common.Player player = Modding.Common.Player.GetLocalPlayer();
                if (player != null && player.Machine != null)
                {
                    return player.Machine;
                }
            }
            catch (Exception)
            {
                // outside MP: Playerlist/PlayerData not initialized
            }
            try
            {
                Machine active = Machine.Active();
                if (active != null)
                {
                    return Modding.Blocks.PlayerMachine.From(active);
                }
            }
            catch (Exception)
            {
                // no active machine (menu)
            }
            return null;
        }

        /// <summary>Collects MicrocontrollerScripts from a block collection (GUID dedup).</summary>
        private static int ScanControllers(
            System.Collections.ObjectModel.ReadOnlyCollection<Modding.Blocks.Block> blocks,
            List<MicrocontrollerScript> result)
        {
            if (blocks == null)
            {
                return 0;
            }
            HashSet<System.Guid> seen = new HashSet<System.Guid>();
            int scanned = 0;
            foreach (Modding.Blocks.Block block in blocks)
            {
                scanned++;
                BlockBehaviour bb = block.InternalObject;
                MicrocontrollerScript mc = bb == null ? null : bb.GetComponent<MicrocontrollerScript>();
                if (mc != null)
                {
                    System.Guid guid;
                    try
                    {
                        guid = block.Guid;
                    }
                    catch (Exception)
                    {
                        guid = System.Guid.Empty;
                    }
                    if (seen.Add(guid))
                    {
                        result.Add(mc);
                    }
                }
            }
            return scanned;
        }
    }
}
