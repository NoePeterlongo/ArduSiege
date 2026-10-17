// MicrocontrollerScript - the "Microcontroller" block of ArduSiege (Besiege).
//
//   M1: vanilla mapper (script menu + "active" toggle), Lua runtime per simulation,
//       on_tick at 50 Hz, fresh state every run, watchdog-bounded executions.
//   M2: sensors API (self/machine/input).  M3: actuators (emulate/set_slider).
//   M4: name-based persistence (player decision D1-ter) - the library is the truth ;
//       menu re-pointed by name, refreshed on mapper open.
//   Refs: machine:refs(channel) exposes the blocks listening to a vanilla variable
//       ("variables instead of keys" bindings - native .bsg persistence).
//
// Console: Ctrl+Shift+K. Journals: docs/journal-m1..m4, nommage-blocs.

using System.Collections.Generic;
using Modding;
using MoonSharp.Interpreter;
using UnityEngine;
// NB: fully qualify Modding.ModIO - the game has a global ModIO namespace (forbidden)
// that hides the Modding.ModIO class during name resolution.
// Alias DataType : Modding.DataType (jeu) vs MoonSharp.Interpreter.DataType (Lua).
using MoonDataType = MoonSharp.Interpreter.DataType;

namespace ArduSiege
{
    public class MicrocontrollerScript : BlockScript
    {
        // Per-tick instruction budget (M5: configurable via the `watchdog budget`
        // console command; default 1M - deliberately low: M0 measured 50M = ~17 s
        // freeze, see journal M0 step 9). Applies to runtimes mounted AFTER the
        // change; not persisted across game restarts.
        internal static long WatchdogBudgetPerTick = 1000000L;

        private const string SCRIPTS_DIR = "Scripts";

        // M4: the script's name persists in the .bsg (the library is the truth,
        // the name is enough to find the file - player decision D1-ter).
        private const string KEY_NAME = "sm-script-name";

        // M4: "no longer exists" marker in the menu (missing file).
        private const string MISSING_MARK = "* ";

        private MMenu m_ScriptMenu;
        private MToggle m_ActiveToggle;

        private Script m_Lua;
        private WatchdogDebugger m_Watchdog;
        private LuaActuatorApi m_Actuator;
        private BlockKeyActuator m_KeyActuator;
        private BlockAngleRamps m_AngleRamps;
        private SimLogger m_Logger;
        private int m_TickCount;
        private string m_ScriptName;

        // M3: mandatory - without this flag the machine does not enable the
        // emulation system (Machine.cs reads Prefab.EmulatesAnyKeys, propagated from
        // our SafeAwake override via ModBlockBehaviourHandler.cs:174).
        public override bool EmulatesAnyKeys
        {
            get { return true; }
        }

        public override void SafeAwake()
        {
            m_ScriptMenu = AddMenu("script", 0, GetScriptList(), footerMenu: false);
            m_ActiveToggle = AddToggle("actif", "actif", true);

            Debug.Log("[ArduSiege] Microcontroller: SafeAwake (block instantiated)");
        }

        // --- M4: name-based persistence (D1-ter) - the library is the truth ---

        public override void OnLoad(XDataHolder data)
        {
            if (data.HasKey(KEY_NAME))
            {
                string name = data.ReadString(KEY_NAME);
                if (!string.IsNullOrEmpty(name))
                {
                    Debug.Log("[ArduSiege] load: script \"" + name + "\"");
                    // Re-point the menu BY NAME (the saved index may be shifted by a
                    // library that changed since - latent M1 bug fixed).
                    RepointMenu(name);
                }
            }
        }

        public override void OnSave(XDataHolder data)
        {
            string name = MenuSelection;
            if (name != null && name != "(no script)")
            {
                data.Write(KEY_NAME, name);
            }
        }

        /// <summary>
        /// Selects the menu entry matching the name (adds it marked
        /// "* missing" if the file is absent from the library).
        /// SetValue does not invoke ValueChanged: no spurious event.
        /// </summary>
        private void RepointMenu(string name)
        {
            if (m_ScriptMenu == null || name == null)
            {
                return;
            }
            List<string> items = m_ScriptMenu.Items;
            int idx = -1;
            for (int i = 0; i < items.Count; i++)
            {
                if (ResolveName(items[i]) == name)
                {
                    idx = i;
                    break;
                }
            }
            if (idx < 0)
            {
                items.Add(MISSING_MARK + name);
                idx = items.Count - 1;
            }
            else
            {
                // dynamic marker: marked if the file is now absent
                items[idx] = (ReadScript(name) != null) ? name : MISSING_MARK + name;
            }
            m_ScriptMenu.SetValue(idx);
        }

        /// <summary>
        /// Refreshes the menu from the library (new scripts visible without
        /// restarting the level), keeping the current selection by name.
        /// Called on mapper open (onMapperOpen, see ArduSiegeEntryPoint).
        /// </summary>
        internal void RefreshMenu()
        {
            if (m_ScriptMenu == null)
            {
                return;
            }
            string current = MenuSelection;
            m_ScriptMenu.Items = GetScriptList();
            if (!string.IsNullOrEmpty(current) && current != "(no script)")
            {
                RepointMenu(current);
            }
        }

        /// <summary>Real name of a menu entry (strips the "missing" marker).</summary>
        internal static string ResolveName(string item)
        {
            if (string.IsNullOrEmpty(item))
            {
                return null;
            }
            return item.StartsWith(MISSING_MARK) ? item.Substring(MISSING_MARK.Length) : item;
        }

        /// <summary>Name selected in the menu (no marker) - target of reload/show.</summary>
        internal string MenuSelection
        {
            get { return m_ScriptMenu == null ? null : ResolveName(m_ScriptMenu.Selection); }
        }

        /// <summary>True if this controller's runtime is mounted (simulation running).</summary>
        internal bool IsRuntimeActive
        {
            get { return m_Lua != null; }
        }

        /// <summary>Does the selected script exist in the library?</summary>
        internal bool HasScriptFile
        {
            get { return ReadScript(MenuSelection) != null; }
        }

        /// <summary>
        /// M4: script reload - re-reads the file and hot-remounts the runtime if this
        /// controller was simulating.
        /// </summary>
        internal bool ReloadFromLibrary()
        {
            string name = MenuSelection;
            if (name == null || name == "(no script)")
            {
                ModConsole.Log("[ArduSiege] reload: no script associated");
                return false;
            }
            string file = ReadScript(name);
            if (file == null)
            {
                ModConsole.Log("[ArduSiege] reload: file not found: " + name
                    + " (renamed/deleted? - 'script list' for the library)");
                return false;
            }

            bool remount = m_Lua != null && Modding.Game.IsSimulating;
            if (remount)
            {
                OnSimulateStop();       // on_sim_stop + release channels + teardown
            }
            RepointMenu(name);          // "missing" marker removed if it came back
            if (remount)
            {
                OnSimulateStart();      // remount with the fresh source
            }
            ModConsole.Log("[ArduSiege] " + name + ": reloaded"
                + (remount ? " and re-executed" : " (applied at next simulation start)"));
            return true;
        }

        public override void OnSimulateStart()
        {
            m_TickCount = 0;
            m_Lua = null;
            m_Watchdog = null;
            m_Actuator = null;
            m_KeyActuator = null;
            m_AngleRamps = null;
            m_Logger = null;

            if (!m_ActiveToggle.IsActive)
            {
                Debug.Log("[ArduSiege] Microcontroller: inactive (toggle off)");
                return;
            }

            // M4: the library is the single source (D1-ter) - name-based resolution.
            string name = MenuSelection;
            string source = ReadScript(name);

            m_ScriptName = name;
            if (source == null)
            {
                ModConsole.Log("[ArduSiege] Microcontroller: script \""
                    + (name ?? "?") + "\" not found in the library (renamed/deleted?)"
                    + " - this controller does nothing this run. 'script list' to see the library.");
                return;
            }

            try
            {
                m_Lua = new Script(CoreModules.Preset_SoftSandbox);
                m_Lua.Globals.Set("print", DynValue.NewCallback(
                    (ctx, args) =>
                    {
                        System.Text.StringBuilder b = new System.Text.StringBuilder();
                        for (int i = 0; i < args.Count; i++)
                        {
                            if (i > 0) b.Append('\t');
                            b.Append(args[i]);
                        }
                        ModConsole.Log("[MC:" + m_ScriptName + "] " + b);
                        return DynValue.Nil;
                    }, "print"));

                m_Watchdog = new WatchdogDebugger(WatchdogBudgetPerTick);
                m_Lua.AttachDebugger(m_Watchdog);

                // direct actuation: b:key/forward/backward/spin (no variables) -
                // created BEFORE Install: the block wrappers carry it.
                m_KeyActuator = new BlockKeyActuator();
                m_AngleRamps = new BlockAngleRamps();
                m_Logger = new SimLogger(m_ScriptName);

                // M2: sensors API (self, machine, input) - before DoString so the
                // script can use them from on_sim_start.
                LuaApi.Install(m_Lua, this, m_KeyActuator, m_AngleRamps, m_Logger);

                // M3: actuators API - emulate(channel, state)
                m_Actuator = new LuaActuatorApi(this);
                m_Lua.Globals.Set("emulate", UserData.Create(m_Actuator));

                m_Watchdog.Reset();
                m_Lua.DoString(source, null, m_ScriptName);
                CallIfDefined("on_sim_start");
                Debug.Log("[ArduSiege] Microcontroller: script " + m_ScriptName + " started");

                // M2: the `lua` console command will run in THIS context
                ArduSiegeEntryPoint.SetActiveRuntime(m_Lua, m_Watchdog, m_ScriptName);
            }
            catch (WatchdogBudgetExceededException wex)
            {
                Debug.LogWarning("[ArduSiege] Microcontroller: watchdog at load (" + wex.Budget + ") - script disabled");
                m_Lua = null;
            }
            catch (InterpreterException iex)
            {
                Debug.LogWarning("[ArduSiege] Microcontroller: Lua error at load: "
                    + (iex.DecoratedMessage ?? iex.Message));
                m_Lua = null;
            }
        }

        public override void SimulateFixedUpdateHost()
        {
            if (m_Lua == null)
            {
                return;
            }

            m_TickCount++;
            try
            {
                m_Watchdog.Reset();          // budget PER TICK (not per simulation)
                CallIfDefined("on_tick");
                if (m_AngleRamps != null)
                {
                    m_AngleRamps.Tick(Time.fixedDeltaTime);   // target-angle ramps
                }
            }
            catch (WatchdogBudgetExceededException wex)
            {
                Debug.LogWarning("[ArduSiege] Microcontroller: script " + m_ScriptName
                    + " disabled (tick " + m_TickCount + ": budget " + wex.Budget + " exceeded)");
                m_Lua = null;
            }
            catch (InterpreterException iex)
            {
                Debug.LogWarning("[ArduSiege] Microcontroller: Lua error tick " + m_TickCount
                    + ": " + (iex.DecoratedMessage ?? iex.Message));
                m_Lua = null;               // disabled until the next run
            }
        }

        public override void OnSimulateStop()
        {
            CallIfDefined("on_sim_stop");

            // M3: release emulated channels BEFORE teardown (counter symmetry -
            // otherwise blocks would stay "stuck" until the next run)
            if (m_Actuator != null)
            {
                m_Actuator.ResetAll();
                m_Actuator = null;
            }

            // direct actuation: same symmetry for the driven keys
            if (m_KeyActuator != null)
            {
                m_KeyActuator.ResetAll();
                m_KeyActuator = null;
            }

            // angle ramps: clear (targets stay where they are)
            if (m_AngleRamps != null)
            {
                m_AngleRamps.Clear();
                m_AngleRamps = null;
            }

            // per-simulation log: final flush + summary (even on script error)
            if (m_Logger != null)
            {
                m_Logger.Close();
                m_Logger = null;
            }

            m_Lua = null;
            m_Watchdog = null;
            ArduSiegeEntryPoint.ClearActiveRuntime();
        }

        // --- M3: channel application at the official emulation tick (host) ---

        public override void SendKeyEmulationUpdateHost()
        {
            if (m_Actuator != null)
            {
                m_Actuator.ApplyPending();
            }
        }

        /// <summary>Emule un canal (appele par LuaActuatorApi.ApplyPending).</summary>
        internal void EmulateChannel(MKey channelKey, bool state)
        {
            // ModBlockBehaviour.EmulateKeys (public wrapper) - empty activationKeys:
            // our block has no keys of its own, no loop possible.
            EmulateKeys(new MKey[0], channelKey, state);
        }

        /// <summary>
        /// Does the channel have at least one listener in the machine? (anti-NRE
        /// guard: KeyInputController.Emulate dereferences usedMessages[text] without
        /// checking.) Scans the blocks' public MapperTypes - messages = v1.10 mapper
        /// bindings.
        /// </summary>
        internal bool ChannelHasListeners(string channel)
        {
            return FindChannelKeys(channel).Count > 0;
        }

        /// <summary>
        /// Diagnostic : resume des MKey de la machine (useMessage, keycodes, messages).
        /// Helps understand why a channel has no listener (mistyped message,
        /// missing useMessage, or KeysCount==0 - the case where the game does NOT
        /// register the message in usedMessages, see Machine.InitSimBlock).
        /// </summary>
        internal string DumpMachineKeys()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            Modding.Blocks.PlayerMachine pm = Machine;
            if (pm == null)
            {
                return "(machine not found)";
            }
            System.Collections.ObjectModel.ReadOnlyCollection<Modding.Blocks.Block> blocks =
                Modding.Game.IsSimulating ? pm.SimulationBlocks : pm.BuildingBlocks;

            int n = 0;
            foreach (Modding.Blocks.Block block in blocks)
            {
                BlockBehaviour bb = block.InternalObject;
                if (bb == null || bb.MapperTypes == null)
                {
                    continue;
                }
                foreach (MapperType mt in bb.MapperTypes)
                {
                    MKey key = mt as MKey;
                    if (key == null)
                    {
                        continue;
                    }
                    n++;
                    sb.Append("\n  ").Append(bb.Prefab.name ?? "?").Append(" : ")
                        .Append(key.Key)
                        .Append(" useMessage=").Append(key.useMessage)
                        .Append(" keys=").Append(key.KeysCount)
                        .Append(" messages=[").Append(string.Join(";",
                            key.message ?? new string[0])).Append("]");
                }
            }
            if (n == 0)
            {
                sb.Append("\n  (no MKey in the machine)");
            }
            return n + " MKey(s) :" + sb;
        }

        private List<MKey> FindChannelKeys(string channel)
        {
            List<MKey> found = new List<MKey>();
            Modding.Blocks.PlayerMachine pm = Machine;
            if (pm == null)
            {
                return found;
            }
            System.Collections.ObjectModel.ReadOnlyCollection<Modding.Blocks.Block> blocks =
                Modding.Game.IsSimulating ? pm.SimulationBlocks : pm.BuildingBlocks;

            foreach (Modding.Blocks.Block block in blocks)
            {
                BlockBehaviour bb = block.InternalObject;
                if (bb == null || bb.MapperTypes == null)
                {
                    continue;
                }
                foreach (MapperType mt in bb.MapperTypes)
                {
                    MKey key = mt as MKey;
                    if (key == null || !key.useMessage)
                    {
                        continue;
                    }
                    string[] messages = key.message;
                    if (messages == null)
                    {
                        continue;
                    }
                    foreach (string message in messages)
                    {
                        if (message == channel)
                        {
                            found.Add(key);
                        }
                    }
                }
            }
            return found;
        }

        // --- helpers ---

        private void CallIfDefined(string func)
        {
            if (m_Lua == null)
            {
                return;     // runtime not mounted (load failed) - nothing to call
            }
            DynValue f = m_Lua.Globals.Get(func);
            if (f != null && f.Type == MoonDataType.Function)
            {
                m_Lua.Call(f);
            }
        }

        private List<string> GetScriptList()
        {
            List<string> names = new List<string>();
            try
            {
                if (!Modding.ModIO.ExistsDirectory(SCRIPTS_DIR, data: true))
                {
                    Modding.ModIO.CreateDirectory(SCRIPTS_DIR, data: true);
                }
                string[] files = Modding.ModIO.GetFiles(SCRIPTS_DIR, "*.lua", data: true);
                foreach (string file in files)
                {
                    // displayed/saved name: file name without extension
                    string name = System.IO.Path.GetFileNameWithoutExtension(file);
                    if (!string.IsNullOrEmpty(name))
                    {
                        names.Add(name);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[ArduSiege] Microcontroller: cannot list scripts: " + ex.Message);
            }
            if (names.Count == 0)
            {
                names.Add("(no script)");
            }
            return names;
        }

        private string ReadScript(string name)
        {
            if (name == null || name == "(no script)")
            {
                return null;
            }
            try
            {
                string path = System.IO.Path.Combine(SCRIPTS_DIR, name + ".lua");
                if (!Modding.ModIO.ExistsFile(path, data: true))
                {
                    return null;
                }
                return Modding.ModIO.ReadAllText(path, data: true);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[ArduSiege] Microcontroller: cannot read (" + name + "): " + ex.Message);
                return null;
            }
        }
    }
}
