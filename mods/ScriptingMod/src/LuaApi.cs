// LuaApi - ScriptingMod M2 (Besiege): the sensors API exposed to Lua.
//
// Three IUserDataType wrappers (pattern validated at M0: zero reflection -
// AGENTS.md golden rules) :
//   self    : the microcontroller block          (position, velocity, health, type...)
//   machine : the machine during simulation       (blocks, filters, stats)
//   input   : the player's REAL keyboard (UnityEngine.Input - allowed by the scanner)
//
// Lua access: self.position (table {x,y,z}), machine:blocks("Cog"), input:is_held("Space")...
// All data is MoonSharp primitives/tables - never raw CLR objects.
// Sources: official Modding.Blocks API (Block, PlayerMachine) + Modding.Game.
// See docs/journal-m2.md.

using System;
using System.Collections.Generic;
using Modding;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;
using UnityEngine;

namespace ScriptingMod
{
    /// <summary>
    /// Builds + installs the Lua globals (self, machine, input) on a Script
    /// instance. Called at runtime mount (before DoString: the script can use
    /// them from on_sim_start).
    /// </summary>
    internal static class LuaApi
    {
        internal static void Install(Script lua, BlockScript controller, BlockKeyActuator keys,
            BlockAngleRamps ramps)
        {
            Modding.Blocks.Block selfBlock = Modding.Blocks.Block.From(controller);
            if (selfBlock != null)
            {
                lua.Globals.Set("self", UserData.Create(new LuaBlockApi(selfBlock, keys, ramps)));
            }
            lua.Globals.Set("machine", UserData.Create(new LuaMachineApi(controller.Machine, keys, ramps)));
            lua.Globals.Set("input", UserData.Create(new LuaInputApi()));
        }

        // --- helpers partages ---

        internal static DynValue Vec3(Script script, Vector3 v)
        {
            // fresh {x=, y=, z=} table - readable, no interop
            DynValue t = DynValue.NewTable(script);
            Table tab = t.Table;
            tab.Set("x", DynValue.NewNumber(v.x));
            tab.Set("y", DynValue.NewNumber(v.y));
            tab.Set("z", DynValue.NewNumber(v.z));
            return t;
        }
    }

    /// <summary>
    /// Lua wrapper of a block (self or elements of machine:blocks()).
    /// Reads: type, prefab_name, guid, position, rotation (euler), velocity,
    /// angular_velocity, health, is_burning, is_destroyed, is_simulating.
    /// </summary>
    internal class LuaBlockApi : IUserDataType
    {
        private readonly Modding.Blocks.Block m_Block;

        // direct actuation of the block's keys (null in console context)
        private readonly BlockKeyActuator m_Keys;

        // target-angle ramps (null in console context)
        private readonly BlockAngleRamps m_Ramps;

        internal LuaBlockApi(Modding.Blocks.Block block)
        {
            m_Block = block;
            m_Keys = null;
            m_Ramps = null;
        }

        internal LuaBlockApi(Modding.Blocks.Block block, BlockKeyActuator keys, BlockAngleRamps ramps)
        {
            m_Block = block;
            m_Keys = keys;
            m_Ramps = ramps;
        }

        public DynValue Index(Script script, DynValue index, bool isDirectIndexing)
        {
            if (index.Type != MoonSharp.Interpreter.DataType.String)
            {
                return null;
            }

            BlockBehaviour bb = m_Block.InternalObject;
            if (bb == null)
            {
                return null;
            }

            switch (index.String)
            {
                case "type":
                    return DynValue.NewString(bb.Prefab.Type.ToString());
                case "prefab_name":
                    return DynValue.NewString(bb.Prefab.name ?? "");
                case "guid":
                    return DynValue.NewString(bb.Guid.ToString());
                case "position":
                    return LuaApi.Vec3(script, bb.transform.position);
                case "rotation":
                    return LuaApi.Vec3(script, bb.transform.eulerAngles);
                case "forward":
                case "up":
                case "right":
                    // DUAL: block axis (b.forward = x/y/z vector) AND action
                    // (b:forward(true) = its mapper key, if the block has one).
                    return AxisAndAction(script, bb, index.String);
                case "quaternion":
                {
                    Quaternion q = bb.transform.rotation;
                    DynValue t = DynValue.NewTable(script);
                    Table tab = t.Table;
                    tab.Set("x", DynValue.NewNumber(q.x));
                    tab.Set("y", DynValue.NewNumber(q.y));
                    tab.Set("z", DynValue.NewNumber(q.z));
                    tab.Set("w", DynValue.NewNumber(q.w));
                    return t;
                }
                case "velocity":
                {
                    Rigidbody rb = bb.Rigidbody;
                    return (rb == null) ? DynValue.Nil : LuaApi.Vec3(script, rb.velocity);
                }
                case "angular_velocity":
                {
                    Rigidbody rb = bb.Rigidbody;
                    return (rb == null) ? DynValue.Nil : LuaApi.Vec3(script, rb.angularVelocity);
                }
                case "health":
                    return DynValue.NewNumber(m_Block.Health);
                case "is_burning":
                    return DynValue.NewBoolean(m_Block.IsBurning);
                case "is_destroyed":
                    return DynValue.NewBoolean(bb.IsDestroyed);
                // --- M3: sliders (direct read/write, SP-first) ---
                case "sliders":
                    return BuildSlidersTable(script, bb);
                case "slider":
                    return DynValue.NewCallback((ctx, args) => SliderRead(script, args), "slider");
                case "set_slider":
                    return DynValue.NewCallback((ctx, args) => SliderWrite(script, args), "set_slider");
                // --- direct actuation of keys (no vanilla variables) ---
                // --- direct actuation: the block's actions by their UI name ---
                case "actions":
                    return BuildActionsTable(script, bb);
                case "action":
                    return DynValue.NewCallback((ctx, args) => ActionCommand(script, args), "action");
                // --- etats des actionneurs : angle de charniere, toggles ---
                case "angle_target":
                    return BlockAngleTarget(bb);
                case "set_angle":
                    return DynValue.NewCallback((ctx, args) => SetAngleCommand(script, args), "set_angle");
                case "toggles":
                    return BuildTogglesTable(script, bb);
                case "toggle":
                    return DynValue.NewCallback((ctx, args) => ToggleRead(script, args), "toggle");
                default:
                    // b:<nom_action>(etat) : resolution par cle de mapper OU libelle
                    // UI, case-insensitive) - e.g. b:contract(true), b:wind(true).
                    return ActionByName(script, bb, index.String);
            }
        }

        // --- actionnement direct : MKey.UpdateEmulation (compteur, idempotent) ---
        // Actions carry the name seen in the UI: mapper key ("forward", "spin",
        // "contract", "wind"...) or displayed label, case-insensitive.

        private static UnityEngine.Vector3 AxisVector(BlockBehaviour bb, string axis)
        {
            switch (axis)
            {
                case "up": return bb.transform.up;
                case "right": return bb.transform.right;
                default: return bb.transform.forward;
            }
        }

        /// <summary>
        /// DUAL value for axes: {x,y,z} table AND callable action -
        /// b.forward (vector, M2) and b:forward(true) (its mapper key, if any).
        /// </summary>
        private DynValue AxisAndAction(Script script, BlockBehaviour bb, string axis)
        {
            MKey key = FindKeyByName(bb, axis);
            DynValue t = LuaApi.Vec3(script, AxisVector(bb, axis));
            Table meta = new Table(script);
            meta.Set("__call", DynValue.NewCallback(
                (ctx, args) => key == null
                    ? UnknownAction(axis, bb)
                    : ApplyKey(script, key, args, axis),
                axis));
            t.Table.MetaTable = meta;
            return t;
        }

        /// <summary>b.actions: callable name -> UI label table.</summary>
        private static DynValue BuildActionsTable(Script script, BlockBehaviour bb)
        {
            DynValue result = DynValue.NewTable(script);
            Table table = result.Table;
            if (bb.MapperTypes == null)
            {
                return result;
            }
            foreach (MapperType mt in bb.MapperTypes)
            {
                MKey key = mt as MKey;
                if (key == null)
                {
                    continue;
                }
                string libelle = key.DisplayName ?? key.Key;
                table.Set(key.Key, DynValue.NewString(libelle));
                if (!string.IsNullOrEmpty(libelle) && table.Get(libelle).IsNil())
                {
                    table.Set(libelle, DynValue.NewString(libelle));
                }
            }
            return result;
        }

        /// <summary>b:<name>(state) - null if the name matches no action.</summary>
        private DynValue ActionByName(Script script, BlockBehaviour bb, string name)
        {
            MKey key = FindKeyByName(bb, name);
            if (key == null)
            {
                return null;    // unknown property (standard behaviour: nil)
            }
            return DynValue.NewCallback((ctx, args) => ApplyKey(script, key, args, name), name);
        }

        /// <summary>b:action(name, state) - generic access (computed names).</summary>
        private DynValue ActionCommand(Script script, CallbackArguments args)
        {
            string name = FirstStringArg(args);
            BlockBehaviour bb = m_Block.InternalObject;
            MKey key = bb == null ? null : FindKeyByName(bb, name);
            if (key == null)
            {
                return UnknownAction(name, bb);
            }
            return ApplyKey(script, key, args, name);
        }

        /// <summary>Finds an MKey by mapper key OR UI label (case-insensitive).</summary>
        private static MKey FindKeyByName(BlockBehaviour bb, string name)
        {
            if (bb == null || bb.MapperTypes == null || string.IsNullOrEmpty(name))
            {
                return null;
            }
            MKey byKey = null;
            MKey byDisplay = null;
            foreach (MapperType mt in bb.MapperTypes)
            {
                MKey key = mt as MKey;
                if (key == null)
                {
                    continue;
                }
                if (string.Compare(key.Key, name, System.StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return key;
                }
                if (byDisplay == null && !string.IsNullOrEmpty(key.DisplayName)
                    && string.Compare(key.DisplayName, name, System.StringComparison.OrdinalIgnoreCase) == 0)
                {
                    byDisplay = key;
                }
            }
            return byDisplay;
        }

        /// <summary>Applies the desired state to an MKey (transitions only).</summary>
        private DynValue ApplyKey(Script script, MKey key, CallbackArguments args, string name)
        {
            if (m_Keys == null)
            {
                throw new ScriptRuntimeException("direct actuation unavailable in this context (no controller)");
            }
            bool? state = null;
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i].Type == MoonSharp.Interpreter.DataType.Boolean)
                {
                    state = args[i].Boolean;
                    break;
                }
            }
            if (!state.HasValue)
            {
                throw new ScriptRuntimeException("b:" + name + "(state): boolean required, e.g. b:"
                    + name + "(true) - full state every tick recommended (e.g. b:forward(input:is_held(\"P\")))");
            }
            m_Keys.Set(key, state.Value);
            return DynValue.Nil;
        }

        private static DynValue BuildSlidersTable(Script script, BlockBehaviour bb)
        {
            DynValue result = DynValue.NewTable(script);
            Table table = result.Table;
            if (bb.MapperTypes == null)
            {
                return result;
            }
            foreach (MapperType mt in bb.MapperTypes)
            {
                MSlider slider = mt as MSlider;
                if (slider != null)
                {
                    table.Set(slider.Key, DynValue.NewString(slider.DisplayName ?? slider.Key));
                }
            }
            return result;
        }

        /// <summary>Retrouve un MSlider par sa cle (exacte puis insensible a la casse).</summary>
        private static MSlider FindSlider(BlockBehaviour bb, string name)
        {
            if (bb.MapperTypes == null || string.IsNullOrEmpty(name))
            {
                return null;
            }
            MSlider insensitive = null;
            foreach (MapperType mt in bb.MapperTypes)
            {
                MSlider slider = mt as MSlider;
                if (slider == null)
                {
                    continue;
                }
                if (slider.Key == name)
                {
                    return slider;
                }
                if (string.Compare(slider.Key, name, System.StringComparison.OrdinalIgnoreCase) == 0)
                {
                    insensitive = slider;
                }
            }
            return insensitive;
        }

        private DynValue SliderRead(Script script, CallbackArguments args)
        {
            // b:slider("speed") or b.slider("speed") - first string argument
            string name = FirstStringArg(args);
            MSlider slider = FindSlider(m_Block.InternalObject, name);
            if (slider == null)
            {
                throw new ScriptRuntimeException("unknown slider \"" + name + "\" - check b.sliders for the list");
            }
            return DynValue.NewNumber(slider.Value);
        }

        private DynValue SliderWrite(Script script, CallbackArguments args)
        {
            // b:set_slider("speed", 1.5) - first string = name, first number = value
            string name = FirstStringArg(args);
            double? value = null;
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i].Type == MoonSharp.Interpreter.DataType.Number)
                {
                    value = args[i].Number;
                    break;
                }
            }
            if (name == null || !value.HasValue)
            {
                throw new ScriptRuntimeException("set_slider(name, value): e.g. b:set_slider(\"speed\", 1.5)");
            }
            MSlider slider = FindSlider(m_Block.InternalObject, name);
            if (slider == null)
            {
                throw new ScriptRuntimeException("unknown slider \"" + name + "\" - check b.sliders for the list");
            }
            slider.Value = (float)value.Value;   // setter public -> notifie le bloc (InvokeValueChanged)
            return DynValue.Nil;
        }

        internal static string FirstStringArg(CallbackArguments args)
        {
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i].Type == MoonSharp.Interpreter.DataType.String)
                {
                    return args[i].String;
                }
            }
            return null;
        }

        // --- etats des actionneurs ---
        // Hinge angle: the game only exposes the TARGET (AngleToBe, degrees) -
        // the real angle has no internal source (the Anglometer computes it
        // geometrically). b:set_angle(v) forces targetAngleMode (public component
        // field, non-persistent: blocks are re-instantiated every sim) and
        // commands the target.

        /// <summary>b.angle_target: commanded angle (degrees) - hinge/rudder.</summary>
        private static DynValue BlockAngleTarget(BlockBehaviour bb)
        {
            SteeringWheel sw = bb as SteeringWheel;
            if (sw != null)
            {
                return DynValue.NewNumber(sw.AngleToBe);
            }
            RudderController rudder = bb as RudderController;
            return (rudder == null) ? DynValue.Nil : DynValue.NewNumber(rudder.AngleToBe);
        }

        /// <summary>
        /// b:set_angle(degrees): commands the target angle of an orientable block
        /// (SteeringWheel = hinge; RudderController = rudder), RAMPED at the block's
        /// own speed (rotation-speed slider - transparent: same speed as its key).
        /// b:set_angle(degrees, v_max): overrides in degrees/second.
        /// Target-angle mode is enabled on the fly (reset at the next sim).
        /// </summary>
        private DynValue SetAngleCommand(Script script, CallbackArguments args)
        {
            if (m_Ramps == null)
            {
                throw new ScriptRuntimeException("direct actuation unavailable in this context (no controller)");
            }
            double? angle = null;
            double? vMax = null;
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i].Type == MoonSharp.Interpreter.DataType.Number)
                {
                    if (!angle.HasValue)
                    {
                        angle = args[i].Number;
                    }
                    else if (!vMax.HasValue)
                    {
                        vMax = args[i].Number;
                    }
                }
            }
            if (!angle.HasValue)
            {
                throw new ScriptRuntimeException("b:set_angle(degrees[, v_max]): e.g. b:set_angle(45)"
                    + " - ramp at the block's speed; b:set_angle(45, 30): 30 degrees/second");
            }
            BlockBehaviour bb = m_Block.InternalObject;
            if (bb is SteeringWheel || bb is RudderController)
            {
                m_Ramps.Set(bb, (float)angle.Value, (float)(vMax ?? 0.0));
                return DynValue.Nil;
            }
            throw new ScriptRuntimeException("set_angle: block is not orientable (hinge or rudder only)");
        }

        private static DynValue BuildTogglesTable(Script script, BlockBehaviour bb)
        {
            DynValue result = DynValue.NewTable(script);
            Table table = result.Table;
            if (bb.MapperTypes == null)
            {
                return result;
            }
            foreach (MapperType mt in bb.MapperTypes)
            {
                MToggle toggle = mt as MToggle;
                if (toggle != null)
                {
                    table.Set(toggle.Key, DynValue.NewBoolean(toggle.IsActive));
                }
            }
            return result;
        }

        /// <summary>b:toggle(name): reads one of the block's toggles.</summary>
        private DynValue ToggleRead(Script script, CallbackArguments args)
        {
            string name = FirstStringArg(args);
            BlockBehaviour bb = m_Block.InternalObject;
            if (bb == null || bb.MapperTypes == null || string.IsNullOrEmpty(name))
            {
                throw new ScriptRuntimeException("b:toggle(name): name required, e.g. b:toggle(\"automatic\")");
            }
            MToggle found = null;
            foreach (MapperType mt in bb.MapperTypes)
            {
                MToggle toggle = mt as MToggle;
                if (toggle == null)
                {
                    continue;
                }
                if (toggle.Key == name)
                {
                    return DynValue.NewBoolean(toggle.IsActive);
                }
                if (string.Compare(toggle.Key, name, System.StringComparison.OrdinalIgnoreCase) == 0)
                {
                    found = toggle;
                }
            }
            if (found == null)
            {
                throw new ScriptRuntimeException("unknown toggle \"" + name
                    + "\" - check b.toggles for the list");
            }
            return DynValue.NewBoolean(found.IsActive);
        }

        private static DynValue UnknownAction(string name, BlockBehaviour bb)
        {
            string known = "(none)";
            if (bb != null && bb.MapperTypes != null)
            {
                foreach (MapperType mt in bb.MapperTypes)
                {
                    MKey k = mt as MKey;
                    if (k != null)
                    {
                        known = (known == "(none)") ? k.Key : known + ", " + k.Key;
                    }
                }
            }
            throw new ScriptRuntimeException("unknown action \"" + name + "\" on this block - actions: "
                + known + " (b.actions for the full list)");
        }

        public bool SetIndex(Script script, DynValue index, DynValue value, bool isDirectIndexing)
        {
            return false; // read-only for M2
        }

        public DynValue MetaIndex(Script script, string metaname)
        {
            return null;
        }
    }

    /// <summary>
    /// Lua wrapper of the machine (Modding.Blocks.PlayerMachine).
    /// Properties: position, rotation, mass, health, block_count, size.
    /// Method: machine:blocks([filter]) - table of LuaBlockApi, filter = substring,
    /// case-insensitive, on the (enum) type or the prefab name.
    /// </summary>
    internal class LuaMachineApi : IUserDataType
    {
        private readonly Modding.Blocks.PlayerMachine m_Machine;

        // actionnement direct : registres passes aux wrappers de blocs (null en console)
        private readonly BlockKeyActuator m_Keys;
        private readonly BlockAngleRamps m_Ramps;

        internal LuaMachineApi(Modding.Blocks.PlayerMachine machine)
        {
            m_Machine = machine;
            m_Keys = null;
            m_Ramps = null;
        }

        internal LuaMachineApi(Modding.Blocks.PlayerMachine machine, BlockKeyActuator keys,
            BlockAngleRamps ramps)
        {
            m_Machine = machine;
            m_Keys = keys;
            m_Ramps = ramps;
        }

        public DynValue Index(Script script, DynValue index, bool isDirectIndexing)
        {
            if (index.Type != MoonSharp.Interpreter.DataType.String)
            {
                return null;
            }

            switch (index.String)
            {
                case "position":
                    return LuaApi.Vec3(script, MachinePosition());
                case "rotation":
                    return LuaApi.Vec3(script, MachineRotationQuat().eulerAngles);
                // MACHINE frame axes (reference block, LIVE during sim -
                // Machine.Rotation reads buildingMachine, motionless in sim: frozen).
                case "up":
                    return LuaApi.Vec3(script, MachineUp());
                case "forward":
                    return LuaApi.Vec3(script, MachineForward());
                case "right":
                    return LuaApi.Vec3(script, MachineRight());
                case "velocity":
                    return LuaApi.Vec3(script, m_Machine.InternalObject.RefVel);
                case "mass":
                    return DynValue.NewNumber(m_Machine.Mass);
                case "health":
                    return DynValue.NewNumber(m_Machine.Health);
                case "block_count":
                    return DynValue.NewNumber(m_Machine.BlockCount);
                case "size":
                    return LuaApi.Vec3(script, m_Machine.Size);
                case "blocks":
                    return DynValue.NewCallback((ctx, args) => BuildBlocks(script, args), "blocks");
                // --- Refs : blocs qui ecoutent une variable/message vanilla (liaisons mapper) ---
                case "refs":
                    return DynValue.NewCallback((ctx, args) => RefsTable(script, args), "refs");
                default:
                    return null;
            }
        }

        /// <summary>
        /// machine:refs("channel") -> indexed table of the blocks whose key listens
        /// to the vanilla MESSAGE "channel" ("variables instead of keys" bindings).
        /// Native persistence (.bsg), survives moving, group addressing.
        /// </summary>
        private DynValue RefsTable(Script script, CallbackArguments args)
        {
            string channel = LuaBlockApi.FirstStringArg(args);
            if (string.IsNullOrEmpty(channel))
            {
                throw new ScriptRuntimeException("machine:refs(channel): channel required, e.g. machine:refs(\"rotor\")"
                    + " - the binding is made in the block's mapper (variable/message, Advanced Building)");
            }
            System.Collections.ObjectModel.ReadOnlyCollection<Modding.Blocks.Block> blocks =
                Modding.Game.IsSimulating ? m_Machine.SimulationBlocks : m_Machine.BuildingBlocks;

            DynValue result = DynValue.NewTable(script);
            Table table = result.Table;
            int n = 0;
            foreach (Modding.Blocks.Block block in blocks)
            {
                BlockBehaviour bb = block.InternalObject;
                if (bb == null || bb.MapperTypes == null)
                {
                    continue;
                }
                bool found = false;
                foreach (MapperType mt in bb.MapperTypes)
                {
                    MKey key = mt as MKey;
                    if (key == null || !key.useMessage || key.message == null)
                    {
                        continue;
                    }
                    foreach (string message in key.message)
                    {
                        if (message == channel)
                        {
                            found = true;
                            break;
                        }
                    }
                    if (found)
                    {
                        break;
                    }
                }
                if (found)
                {
                    n++;
                    table.Set(n, UserData.Create(new LuaBlockApi(block, m_Keys, m_Ramps)));
                }
            }
            return result;
        }

        private DynValue BuildBlocks(Script script, CallbackArguments args)
        {
            // args[0] = the machine wrapper (colon syntax) - ignored.
            string filter = null;
            if (args.Count > 1 && args[1].Type == MoonSharp.Interpreter.DataType.String)
            {
                filter = args[1].String.ToLower();
            }

            System.Collections.ObjectModel.ReadOnlyCollection<Modding.Blocks.Block> blocks =
                Modding.Game.IsSimulating ? m_Machine.SimulationBlocks : m_Machine.BuildingBlocks;

            DynValue result = DynValue.NewTable(script);
            Table table = result.Table;
            int n = 0;

            foreach (Modding.Blocks.Block block in blocks)
            {
                BlockBehaviour bb = block.InternalObject;
                if (bb == null)
                {
                    continue;
                }
                if (filter != null)
                {
                    string typeName = bb.Prefab.Type.ToString().ToLower();
                    string prefabName = (bb.Prefab.name ?? "").ToLower();
                    if (!typeName.Contains(filter) && !prefabName.Contains(filter))
                    {
                        continue;
                    }
                }
                n++;
                table.Set(n, UserData.Create(new LuaBlockApi(block, m_Keys, m_Ramps)));
            }

            return result;
        }

        // --- LIVE machine frame (RefBlock: moves with the machine during sim) ---
        // Machine.Position/Rotation read buildingMachine (motionless during sim).
        // The game's live source: Machine.RefUp/RefForward/RefCenter/RefVel, computed
        // from the reference block. Outside sim (building), Ref* return zero ->
        // fall back to the building frame (live in build mode).

        private UnityEngine.Vector3 MachineUp()
        {
            UnityEngine.Vector3 v = m_Machine.InternalObject.RefUp;
            return (v.sqrMagnitude < 1e-06f)
                ? m_Machine.Rotation * UnityEngine.Vector3.up
                : v;
        }

        private UnityEngine.Vector3 MachineForward()
        {
            UnityEngine.Vector3 v = m_Machine.InternalObject.RefForward;
            return (v.sqrMagnitude < 1e-06f)
                ? m_Machine.Rotation * UnityEngine.Vector3.forward
                : v;
        }

        private UnityEngine.Vector3 MachineRight()
        {
            return UnityEngine.Vector3.Cross(MachineUp(), MachineForward()).normalized;
        }

        private UnityEngine.Vector3 MachinePosition()
        {
            // RefCenter = reference block center (live during sim); while building,
            // Machine.Position (building root) is live.
            return (Modding.Game.IsSimulating)
                ? m_Machine.InternalObject.RefCenter
                : m_Machine.Position;
        }

        private UnityEngine.Quaternion MachineRotationQuat()
        {
            UnityEngine.Vector3 fwd = m_Machine.InternalObject.RefForward;
            if (fwd.sqrMagnitude < 1e-06f)
            {
                return m_Machine.Rotation;
            }
            return UnityEngine.Quaternion.LookRotation(fwd, MachineUp());
        }

        public bool SetIndex(Script script, DynValue index, DynValue value, bool isDirectIndexing)
        {
            return false;
        }

        public DynValue MetaIndex(Script script, string metaname)
        {
            return null;
        }
    }

    /// <summary>
    /// Lua wrapper of the player's REAL keyboard (UnityEngine.Input - not blacklisted).
    /// Methodes : input:is_held("Space"), input:is_pressed("Space"), input:is_released("Space").
    /// Les noms sont ceux de l'enum UnityEngine.KeyCode (Space, W, LeftControl, Mouse0...).
    /// NB : lecture directe du clavier, pas des touches du mapper des blocs (M3+).
    /// </summary>
    internal class LuaInputApi : IUserDataType
    {
        public DynValue Index(Script script, DynValue index, bool isDirectIndexing)
        {
            if (index.Type != MoonSharp.Interpreter.DataType.String)
            {
                return null;
            }

            switch (index.String)
            {
                case "is_held":
                    return DynValue.NewCallback((ctx, args) => Key(args, Input.GetKey), "is_held");
                case "is_pressed":
                    return DynValue.NewCallback((ctx, args) => Key(args, Input.GetKeyDown), "is_pressed");
                case "is_released":
                    return DynValue.NewCallback((ctx, args) => Key(args, Input.GetKeyUp), "is_released");
                default:
                    return null;
            }
        }

        private static DynValue Key(CallbackArguments args, Func<KeyCode, bool> probe)
        {
            // Accepts both forms: input:is_held("Space") (args[1]) and
            // input.is_held("Space") (args[0]).
            DynValue keyArg = null;
            if (args.Count >= 2)
            {
                keyArg = args[1];
            }
            else if (args.Count == 1)
            {
                keyArg = args[0];
            }

            if (keyArg == null || keyArg.Type != MoonSharp.Interpreter.DataType.String)
            {
                throw new ScriptRuntimeException("input: KeyCode name expected, e.g. input:is_held(\"Space\")");
            }
            string name = keyArg.String;
            try
            {
                // System.Enum (System namespace): allowed by the scanner - no reflection.
                KeyCode key = (KeyCode)Enum.Parse(typeof(KeyCode), name, true);
                return DynValue.NewBoolean(probe(key));
            }
            catch (ArgumentException)
            {
                throw new ScriptRuntimeException("unknown key: \"" + name + "\" (UnityEngine.KeyCode names, e.g. Space, W, LeftControl, Mouse0)");
            }
        }

        public bool SetIndex(Script script, DynValue index, DynValue value, bool isDirectIndexing)
        {
            return false;
        }

        public DynValue MetaIndex(Script script, string metaname)
        {
            return null;
        }
    }
}
