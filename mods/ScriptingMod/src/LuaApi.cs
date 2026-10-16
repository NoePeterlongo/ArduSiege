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
        internal static void Install(Script lua, BlockScript controller)
        {
            Modding.Blocks.Block selfBlock = Modding.Blocks.Block.From(controller);
            if (selfBlock != null)
            {
                lua.Globals.Set("self", UserData.Create(new LuaBlockApi(selfBlock)));
            }
            lua.Globals.Set("machine", UserData.Create(new LuaMachineApi(controller.Machine)));
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

        internal LuaBlockApi(Modding.Blocks.Block block)
        {
            m_Block = block;
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
                    return LuaApi.Vec3(script, bb.transform.forward);
                case "up":
                    return LuaApi.Vec3(script, bb.transform.up);
                case "right":
                    return LuaApi.Vec3(script, bb.transform.right);
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
                default:
                    return null;
            }
        }

        // --- M3 : sliders du bloc (MapperTypes publics, MSlider.Value notifie le bloc) ---

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

        internal LuaMachineApi(Modding.Blocks.PlayerMachine machine)
        {
            m_Machine = machine;
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
                    return LuaApi.Vec3(script, m_Machine.Position);
                case "rotation":
                    return LuaApi.Vec3(script, m_Machine.Rotation.eulerAngles);
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
                // --- Experience nommage-blocs : acces par nom (BlockNames.tsv) ---
                case "block":
                    return DynValue.NewCallback((ctx, args) => BlockByName(script, args), "block");
                case "named":
                    return NamedTable(script);
                default:
                    return null;
            }
        }

        /// <summary>machine:block("nom") -> bloc nomme (nil si absent de la machine).</summary>
        private DynValue BlockByName(Script script, CallbackArguments args)
        {
            string name = LuaBlockApi.FirstStringArg(args);
            if (name == null)
            {
                throw new ScriptRuntimeException("machine:block(nom) : nom requis, ex. machine:block(\"moteur_gauche\")");
            }
            System.Guid guid;
            if (!BlockNames.TryGet(name, out guid))
            {
                return DynValue.Nil;
            }
            Modding.Blocks.Block found = FindByGuid(guid);
            return found == null ? DynValue.Nil : UserData.Create(new LuaBlockApi(found));
        }

        /// <summary>machine:named() -> table nom -> bloc (uniquement ceux resolus dans la machine).</summary>
        private DynValue NamedTable(Script script)
        {
            DynValue result = DynValue.NewTable(script);
            Table table = result.Table;
            foreach (System.Collections.Generic.KeyValuePair<string, System.Guid> kv in BlockNames.All)
            {
                Modding.Blocks.Block found = FindByGuid(kv.Value);
                if (found != null)
                {
                    table.Set(kv.Key, UserData.Create(new LuaBlockApi(found)));
                }
            }
            return result;
        }

        private Modding.Blocks.Block FindByGuid(System.Guid guid)
        {
            System.Collections.ObjectModel.ReadOnlyCollection<Modding.Blocks.Block> blocks =
                Modding.Game.IsSimulating ? m_Machine.SimulationBlocks : m_Machine.BuildingBlocks;
            foreach (Modding.Blocks.Block block in blocks)
            {
                BlockBehaviour bb = block.InternalObject;
                if (bb != null && bb.Guid == guid)
                {
                    return block;
                }
            }
            return null;
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
                table.Set(n, UserData.Create(new LuaBlockApi(block)));
            }

            return result;
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
