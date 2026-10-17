// LuaApi — M2 du mod ScriptingMod (Besiege) : API capteurs exposee a Lua.
//
// Trois wrappers IUserDataType (pattern valide au M0 : AutoDescribingUserDataDescriptor,
// zero reflexion — regles d'or AGENTS.md) :
//   self    : le bloc microcontroleur            (position, vitesse, sante, type...)
//   machine : la machine pendant la simulation    (blocs, filtres, stats)
//   input   : le clavier REEL du joueur (UnityEngine.Input — autorise par le scanner)
//
// Acces Lua : self.position (table {x,y,z}), machine:blocks("Cog"), input:is_held("Space")...
// Toutes les donnees sont des primitives/tables MoonSharp — jamais d'objets CLR bruts.
// Sources : API officielle Modding.Blocks (Block, PlayerMachine) + Modding.Game.
// Voir docs/journal-m2.md.

using System;
using System.Collections.Generic;
using Modding;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;
using UnityEngine;

namespace ScriptingMod
{
    /// <summary>
    /// Fabrique + installation des globals Lua (self, machine, input) sur une instance
    /// Script. Appele au montage du runtime (avant DoString : le script peut les utiliser
    /// dans on_sim_start).
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
            // table {x=, y=, z=} fraiche — lisible et sans interop
            DynValue t = DynValue.NewTable(script);
            Table tab = t.Table;
            tab.Set("x", DynValue.NewNumber(v.x));
            tab.Set("y", DynValue.NewNumber(v.y));
            tab.Set("z", DynValue.NewNumber(v.z));
            return t;
        }
    }

    /// <summary>
    /// Wrapper Lua d'un bloc (self ou elements de machine:blocks()).
    /// Lectures : type, prefab_name, guid, position, rotation (euler), velocity,
    /// angular_velocity, health, is_burning, is_destroyed, is_simulating.
    /// </summary>
    internal class LuaBlockApi : IUserDataType
    {
        private readonly Modding.Blocks.Block m_Block;

        // actionnement direct des touches du bloc (null en contexte console)
        private readonly BlockKeyActuator m_Keys;

        // rampes d'angle cible (null en contexte console)
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
                    // DUAL : axe du bloc (b.forward = vecteur x/y/z) ET action
                    // (b:forward(true) = touche du mapper, si le bloc en a une).
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
                // --- M3 : sliders (lecture/ecriture directes, SP privilegie) ---
                case "sliders":
                    return BuildSlidersTable(script, bb);
                case "slider":
                    return DynValue.NewCallback((ctx, args) => SliderRead(script, args), "slider");
                case "set_slider":
                    return DynValue.NewCallback((ctx, args) => SliderWrite(script, args), "set_slider");
                // --- actionnement direct des touches (sans variables vanilla) ---
                // --- actionnement direct : actions du bloc par leur nom UI ---
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
                    // UI (insensible a la casse) — ex. b:contract(true), b:wind(true).
                    return ActionByName(script, bb, index.String);
            }
        }

        // --- actionnement direct : MKey.UpdateEmulation (compteur, idempotent) ---
        // Les actions portent le nom vu dans l'UI : cle de mapper ("forward", "spin",
        // "contract", "wind"...) ou libelle affiche, insensible a la casse.

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
        /// Valeur DUALE pour les axes : table {x,y,z} ET action appelable —
        /// b.forward (vecteur, M2) et b:forward(true) (touche du mapper, si presente).
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

        /// <summary>b.actions : table nom appelable -> libelle UI.</summary>
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

        /// <summary>b:<nom>(etat) — null si le nom ne correspond a aucune action.</summary>
        private DynValue ActionByName(Script script, BlockBehaviour bb, string name)
        {
            MKey key = FindKeyByName(bb, name);
            if (key == null)
            {
                return null;    // propriete inconnue (comportement standard : nil)
            }
            return DynValue.NewCallback((ctx, args) => ApplyKey(script, key, args, name), name);
        }

        /// <summary>b:action(nom, etat) — acces generique (noms calcules).</summary>
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

        /// <summary>Retrouve une MKey par cle de mapper OU libelle UI (insensible a la casse).</summary>
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

        /// <summary>Applique l'etat desire a une MKey (transitions uniquement).</summary>
        private DynValue ApplyKey(Script script, MKey key, CallbackArguments args, string name)
        {
            if (m_Keys == null)
            {
                throw new ScriptRuntimeException("actionnement direct indisponible dans ce contexte (hors controleur)");
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
                throw new ScriptRuntimeException("b:" + name + "(etat) : booleen requis, ex. b:"
                    + name + "(true) — etat complet a chaque tick conseille (ex. b:forward(input:is_held(\"P\")))");
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
            // b:slider("speed") ou b.slider("speed") — premier argument string
            string name = FirstStringArg(args);
            MSlider slider = FindSlider(m_Block.InternalObject, name);
            if (slider == null)
            {
                throw new ScriptRuntimeException("slider inconnu \"" + name + "\" — consultez b.sliders pour la liste");
            }
            return DynValue.NewNumber(slider.Value);
        }

        private DynValue SliderWrite(Script script, CallbackArguments args)
        {
            // b:set_slider("speed", 1.5) — premier string = nom, premier nombre = valeur
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
                throw new ScriptRuntimeException("set_slider(nom, valeur) : ex. b:set_slider(\"speed\", 1.5)");
            }
            MSlider slider = FindSlider(m_Block.InternalObject, name);
            if (slider == null)
            {
                throw new ScriptRuntimeException("slider inconnu \"" + name + "\" — consultez b.sliders pour la liste");
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
        // Angle de charniere : le jeu n'expose que la CIBLE (AngleToBe, degres) —
        // l'angle reel n'a pas de source interne (l'Anglometer calcule par geometrie).
        // b:set_angle(v) force targetAngleMode (champ public du composant, non
        // persistant : les blocs se reinstancient a chaque sim) et commande la cible.

        /// <summary>b.angle_target : angle COMMANDE (degres) — charniere/gouvernail.</summary>
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
        /// b:set_angle(degres) : commande l'angle cible d'un bloc orientable
        /// (SteeringWheel = charniere ; RudderController = gouvernail), en RAMPE a
        /// la vitesse du bloc (slider rotation-speed — transparent : meme vitesse que
        /// la touche). b:set_angle(degres, v_max) : surcharge en degres/seconde.
        /// Le mode "angle cible" est active au passage (reinitialise a la prochaine sim).
        /// </summary>
        private DynValue SetAngleCommand(Script script, CallbackArguments args)
        {
            if (m_Ramps == null)
            {
                throw new ScriptRuntimeException("actionnement direct indisponible dans ce contexte (hors controleur)");
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
                throw new ScriptRuntimeException("b:set_angle(degres[, v_max]) : ex. b:set_angle(45)"
                    + " — rampe a la vitesse du bloc ; b:set_angle(45, 30) : 30 degres/seconde");
            }
            BlockBehaviour bb = m_Block.InternalObject;
            if (bb is SteeringWheel || bb is RudderController)
            {
                m_Ramps.Set(bb, (float)angle.Value, (float)(vMax ?? 0.0));
                return DynValue.Nil;
            }
            throw new ScriptRuntimeException("set_angle : bloc non orientable (charniere ou gouvernail uniquement)");
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

        /// <summary>b:toggle(nom) : lit l'etat d'un toggle du bloc.</summary>
        private DynValue ToggleRead(Script script, CallbackArguments args)
        {
            string name = FirstStringArg(args);
            BlockBehaviour bb = m_Block.InternalObject;
            if (bb == null || bb.MapperTypes == null || string.IsNullOrEmpty(name))
            {
                throw new ScriptRuntimeException("b:toggle(nom) : nom requis, ex. b:toggle(\"automatic\")");
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
                throw new ScriptRuntimeException("toggle inconnu \"" + name
                    + "\" — consultez b.toggles pour la liste");
            }
            return DynValue.NewBoolean(found.IsActive);
        }

        private static DynValue UnknownAction(string name, BlockBehaviour bb)
        {
            string connus = "(aucune)";
            if (bb != null && bb.MapperTypes != null)
            {
                foreach (MapperType mt in bb.MapperTypes)
                {
                    MKey k = mt as MKey;
                    if (k != null)
                    {
                        connus = (connus == "(aucune)") ? k.Key : connus + ", " + k.Key;
                    }
                }
            }
            throw new ScriptRuntimeException("action inconnue \"" + name + "\" sur ce bloc — actions : "
                + connus + " (b.actions pour la liste complete)");
        }

        public bool SetIndex(Script script, DynValue index, DynValue value, bool isDirectIndexing)
        {
            return false; // lecture seule pour M2
        }

        public DynValue MetaIndex(Script script, string metaname)
        {
            return null;
        }
    }

    /// <summary>
    /// Wrapper Lua de la machine (Modding.Blocks.PlayerMachine).
    /// Proprietes : position, rotation, mass, health, block_count, size.
    /// Methode : machine:blocks([filtre]) — table de LuaBlockApi, filtre = sous-chaine
    /// insensible a la casse sur le type (enum) ou le nom de prefab.
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
                // axes du repere MACHINE (bloc de reference, LIVE pendant la sim —
                // Machine.Rotation lit buildingMachine, immobile en sim : fige).
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
        /// machine:refs("canal") -> table (indexee) des blocs dont une touche ecoute
        /// le MESSAGE vanilla "canal" (liaisons « variables a la place des touches »).
        /// Persistance native (.bsg), survit au deplacement, adressage de groupe.
        /// </summary>
        private DynValue RefsTable(Script script, CallbackArguments args)
        {
            string channel = LuaBlockApi.FirstStringArg(args);
            if (string.IsNullOrEmpty(channel))
            {
                throw new ScriptRuntimeException("machine:refs(canal) : canal requis, ex. machine:refs(\"rotor\")"
                    + " — la liaison se fait dans le mapper du bloc (variable/message, Advanced Building)");
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
            // args[0] = le wrapper machine (syntaxe deux-points) — ignore.
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

        // --- repere machine LIVE (RefBlock : bouge avec la machine en sim) ---
        // Machine.Position/Rotation lisent buildingMachine (immobile pendant la sim).
        // La source live du jeu : Machine.RefUp/RefForward/RefCenter/RefVel, calcules
        // depuis le bloc de reference. Hors sim (construction), Ref* rend zero ->
        // repli sur le repere de construction (vivant en mode construction).

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
            // RefCenter = centre du bloc de reference (live en sim) ; en construction,
            // Machine.Position (racine construction) est vivante.
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
    /// Wrapper Lua du clavier REEL du joueur (UnityEngine.Input — hors liste noire).
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
            // Accepte les deux syntaxes : input:is_held("Space") (args[1]) et
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
                throw new ScriptRuntimeException("input: attend un nom de touche KeyCode, ex. input:is_held(\"Space\")");
            }
            string name = keyArg.String;
            try
            {
                // System.Enum (namespace System) : autorise par le scanner — pas de reflexion.
                KeyCode key = (KeyCode)Enum.Parse(typeof(KeyCode), name, true);
                return DynValue.NewBoolean(probe(key));
            }
            catch (ArgumentException)
            {
                throw new ScriptRuntimeException("touche inconnue : \"" + name + "\" (noms UnityEngine.KeyCode, ex. Space, W, LeftControl, Mouse0)");
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
