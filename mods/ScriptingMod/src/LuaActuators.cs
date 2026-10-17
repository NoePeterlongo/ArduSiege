// LuaActuators - ScriptingMod M3 (Besiege): actuator API.
//
// emulate(channel, state) - drives vanilla blocks through native key emulation:
//   - the player binds a block key to the MESSAGE "mychannel" in its mapper
//     (vanilla "variables instead of keys" system, v1.10) ;
//   - the script calls emulate("mychannel", true/false) every tick ;
//   - the desired state is APPLIED at the official emulation tick
//     (SendKeyEmulationUpdateHost -> ModBlockBehaviour.EmulateKeys -> KeyInputController) ;
//   - IDEMPOTENCE is mandatory: KeyInputController.Emulate works on a counter
//     (MKey.UpdateEmulation increments/decrements `emulators`) - EmulateKeys is called
//     ONLY on state changes (pending != applied) ;
//   - anti-NRE guard: KeyInputController.Emulate dereferences usedMessages[text]
//     WITHOUT checking - emulating a channel with no listener would throw - machine
//     blocks (public MapperTypes) are scanned to validate the channel first.
//
// The wrapper is directly CALLABLE: emulate("x", true) via the __call metamethod.
// See docs/journal-m3.md.

using System.Collections.Generic;
using Modding;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;
using UnityEngine;

namespace ScriptingMod
{
    internal class LuaActuatorApi : IUserDataType
    {
        private readonly MicrocontrollerScript m_Controller;

        // channel -> emulation MKey (useMessage=true, message=[channel])
        private readonly Dictionary<string, MKey> m_ChannelKeys = new Dictionary<string, MKey>();

        // desired states (updated by Lua every tick) / applied states (last sent)
        private readonly Dictionary<string, bool> m_Pending = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> m_Applied = new Dictionary<string, bool>();

        // channels reported with no listener (logged once per channel per simulation)
        private readonly HashSet<string> m_Warned = new HashSet<string>();

        // M3 probes: channels in error (logged once per channel)
        private readonly HashSet<string> m_Errored = new HashSet<string>();

        internal LuaActuatorApi(MicrocontrollerScript controller)
        {
            m_Controller = controller;
        }

        // --- Lua: emulate(channel, state) ---

        public DynValue Index(Script script, DynValue index, bool isDirectIndexing)
        {
            if (index.Type != MoonSharp.Interpreter.DataType.String || index.String != "emulate")
            {
                return null;
            }
            return DynValue.NewCallback(EmulateCallback, "emulate");
        }

        public DynValue MetaIndex(Script script, string metaname)
        {
            // makes the wrapper directly callable: emulate("channel", true)
            if (metaname == "__call")
            {
                return DynValue.NewCallback(EmulateCallback, "emulate");
            }
            return null;
        }

        private DynValue EmulateCallback(ScriptExecutionContext ctx, CallbackArguments args)
        {
            // Robust to both forms: emulate("channel", true) via __call (args =
            // [wrapper, channel, state]) and emulate.emulate("channel", true) via Index
            // (args = [channel, state]). First string = channel, first boolean = state.
            DynValue channelArg = null;
            DynValue stateArg = null;
            for (int i = 0; i < args.Count && (channelArg == null || stateArg == null); i++)
            {
                if (channelArg == null && args[i].Type == MoonSharp.Interpreter.DataType.String)
                {
                    channelArg = args[i];
                }
                else if (stateArg == null && args[i].Type == MoonSharp.Interpreter.DataType.Boolean)
                {
                    stateArg = args[i];
                }
            }

            if (channelArg == null || channelArg.String.Length == 0)
            {
                throw new ScriptRuntimeException("emulate(channel, state): channel (string) required, e.g. emulate(\"push\", true)");
            }
            if (stateArg == null)
            {
                throw new ScriptRuntimeException("emulate(channel, state): state (boolean) required, e.g. emulate(\"push\", true)");
            }

            m_Pending[channelArg.String] = stateArg.Boolean;
            return DynValue.Nil;
        }

        public bool SetIndex(Script script, DynValue index, DynValue value, bool isDirectIndexing)
        {
            return false;
        }

        // --- Application at the official emulation tick (called by the controller) ---

        internal void ApplyPending()
        {
            if (m_Pending.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<string, bool> kv in m_Pending)
            {
                string channel = kv.Key;
                bool state = kv.Value;

                // Idempotence vs the game's REAL state. UpdateEmulation is a COUNTER
                // (+= +/-1), not a state: sending "false" to a never-active channel
                // brings it to -1, and the following "true" only brings it back to 0
                // (Emulating = emulators > 0 stays false - dead motor). At startup the
                // game is in the "released" state (counter 0): a channel absent from
                // m_Applied is therefore ALREADY "applied to false" - TryGetValue
                // writes false into previously.
                bool previously;
                m_Applied.TryGetValue(channel, out previously);
                if (previously == state)
                {
                    continue; // idempotence: nothing to change
                }

                if (!m_Controller.ChannelHasListeners(channel))
                {
                    if (!m_Warned.Contains(channel))
                    {
                        m_Warned.Add(channel);
                        ModConsole.Log("[ScriptingMod] actuator: channel \""
                            + channel + "\" has no listener - bind a block key to this MESSAGE in its mapper (variable: \""
                            + channel + "\")");
                        // Probe: shows exactly what the machine's MKeys contain
                        // (typed message, useMessage, keycodes).
                        ModConsole.Log("[ScriptingMod] machine keys dump: " + m_Controller.DumpMachineKeys());
                    }
                    continue;
                }

                try
                {
                    m_Controller.EmulateChannel(GetOrCreateChannelKey(channel), state);
                }
                catch (System.Exception ex)
                {
                    // KeyInputController.Emulate dereferences usedMessages[text] - an
                    // exception here would break the game's machine tick; captured once
                    // per channel.
                    if (!m_Errored.Contains(channel))
                    {
                        m_Errored.Add(channel);
                        ModConsole.Log("[ScriptingMod] ERROR emulate(\"" + channel + "\"): "
                            + ex.GetType().FullName + ": " + ex.Message);
                    }
                    continue;
                }
                m_Applied[channel] = state;
            }
        }

        /// <summary>Releases everything that was applied (end of simulation).</summary>
        internal void ResetAll()
        {
            foreach (KeyValuePair<string, bool> kv in m_Applied)
            {
                if (kv.Value)
                {
                    m_Controller.EmulateChannel(GetOrCreateChannelKey(kv.Key), false);
                }
            }
            m_Pending.Clear();
            m_Applied.Clear();
            m_Warned.Clear();
            m_Errored.Clear();
        }

        private MKey GetOrCreateChannelKey(string channel)
        {
            MKey key;
            if (!m_ChannelKeys.TryGetValue(channel, out key))
            {
                // Manually built MKey (public ctor) - never displayed in a mapper ;
                // useMessage=true: the emulation targets the MESSAGE (named channel,
                // not a key).
                key = new MKey("emulate-" + channel, "sm-emulate-" + channel, KeyCode.None, isEmulator: true);
                key.useMessage = true;
                key.message = new string[1] { channel };
                m_ChannelKeys[channel] = key;
            }
            return key;
        }
    }
}
