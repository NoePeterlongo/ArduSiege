// BlockKeyActuator - direct actuation of a block's keys (actionnement-direct work).
//
// b:key("forward", true) - b:forward/b:backward/b:spin (sugar) - with NO vanilla
// "variable" binding: the block's MKey emulation COUNTER is incremented directly
// (MKey.UpdateEmulation, public). The block reads EmulationHeld() at the official
// emulation tick (CogMotorControllerHinge l.501) - the same path as vanilla
// emulation through logic gates. Our Microcontroller already forces
// Prefab.EmulatesAnyKeys -> the machine emulation system is active.
//
// IDEMPOTENCE (M3 lesson): UpdateEmulation is a counter (+= +/-1), not a state -
// only TRANSITIONS are sent; a channel absent from the registry is already
// "false" (the game's birth state): never a phantom initial "false".
//
// Registry PER CONTROLLER (one instance per runtime): reset to zero (release
// everything) when that controller's simulation stops. D4 limit: two scripts
// driving the same MKey add up their counters - multi-controller is the
// player's responsibility.

using System.Collections.Generic;
using MoonSharp.Interpreter;

namespace ArduSiege
{
    internal class BlockKeyActuator
    {
        // states applied by THIS runtime (MKey -> current state)
        private readonly Dictionary<MKey, bool> m_Applied = new Dictionary<MKey, bool>();

        /// <summary>
        /// Applies the desired state to an MKey (idempotent: transitions only).
        /// </summary>
        internal void Set(MKey key, bool state)
        {
            bool previously;
            m_Applied.TryGetValue(key, out previously);   // absent = false (game baseline)
            if (previously == state)
            {
                return;
            }
            key.UpdateEmulation(state);
            m_Applied[key] = state;
        }

        /// <summary>Releases everything that was applied (end of simulation).</summary>
        internal void ResetAll()
        {
            foreach (KeyValuePair<MKey, bool> kv in m_Applied)
            {
                if (kv.Value)
                {
                    kv.Key.UpdateEmulation(false);
                }
            }
            m_Applied.Clear();
        }
    }
}
