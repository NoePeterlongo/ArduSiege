// BlockAngleRamps - target-angle ramps (actionnement-direct work).
//
// b:set_angle(angle): the target moves toward the angle AT THE BLOCK'S OWN
// SPEED - transparent: that is the mapper's "rotation-speed" slider, the same
// speed as holding its key (the block itself moves its target by totalSpeed
// per tick, decomp SteeringWheel l.373). No more machine-breaking snaps.
// b:set_angle(angle, v_max): overrides the speed in degrees/second.
//
// The registry lives PER CONTROLLER (one instance per runtime, like
// BlockKeyActuator), advanced by the controller's tick (50 Hz), cleared on
// simulation stop. D4 limit: two scripts ramping the same block is the
// player's responsibility. Speed slider at 0 = no motion: the ramp waits
// (same behaviour as the key).

using System.Collections.Generic;
using UnityEngine;

namespace ScriptingMod
{
    internal class BlockAngleRamps
    {
        private struct Ramp
        {
            public float Goal;          // target angle, degrees
            public float DegPerSec;     // > 0: explicit speed ; <= 0: block's own speed
        }

        // orientable block -> ongoing ramp (key = instance stable during the sim)
        private readonly Dictionary<BlockBehaviour, Ramp> m_Ramps =
            new Dictionary<BlockBehaviour, Ramp>();

        /// <summary>b:set_angle(angle) - block speed ; b:set_angle(angle, v_max).</summary>
        internal void Set(BlockBehaviour bb, float goal, float degPerSec)
        {
            Ramp r;
            r.Goal = goal;
            r.DegPerSec = degPerSec;
            m_Ramps[bb] = r;
        }

        /// <summary>Advances the targets toward their goal (called on the controller's tick).</summary>
        internal void Tick(float dt)
        {
            List<BlockBehaviour> done = null;
            foreach (KeyValuePair<BlockBehaviour, Ramp> kv in m_Ramps)
            {
                Ramp r = kv.Value;
                float step = (r.DegPerSec > 0f) ? r.DegPerSec * dt : BlockSpeedPerTick(kv.Key);
                if (step <= 0f)
                {
                    continue;       // speed slider at 0: the ramp waits
                }
                float current = ReadTarget(kv.Key);
                float delta = r.Goal - current;
                if (delta > step)
                {
                    delta = step;
                }
                else if (delta < -step)
                {
                    delta = -step;
                }
                float next = current + delta;
                WriteTarget(kv.Key, next);
                if (next == r.Goal)
                {
                    if (done == null)
                    {
                        done = new List<BlockBehaviour>();
                    }
                    done.Add(kv.Key);
                }
            }
            if (done != null)
            {
                foreach (BlockBehaviour bb in done)
                {
                    m_Ramps.Remove(bb);
                }
            }
        }

        /// <summary>Clears the ramps (end of simulation) - targets stay where they are.</summary>
        internal void Clear()
        {
            m_Ramps.Clear();
        }

        /// <summary>The block's own speed, degrees PER TICK (the rotation-speed slider's).</summary>
        private static float BlockSpeedPerTick(BlockBehaviour bb)
        {
            SteeringWheel sw = bb as SteeringWheel;
            if (sw != null)
            {
                return sw.totalSpeed;      // public, recomputed by the block every tick
            }
            RudderController rudder = bb as RudderController;
            if (rudder != null && rudder.SpeedSlider != null)
            {
                // decomp RudderController l.331 formula: angleyToBe accumulation
                return Time.fixedDeltaTime * 100f * rudder.targetAngleSpeed * rudder.SpeedSlider.Value;
            }
            return 0f;
        }

        private static float ReadTarget(BlockBehaviour bb)
        {
            SteeringWheel sw = bb as SteeringWheel;
            if (sw != null)
            {
                return sw.AngleToBe;
            }
            RudderController rudder = bb as RudderController;
            return rudder != null ? rudder.AngleToBe : 0f;
        }

        private static void WriteTarget(BlockBehaviour bb, float value)
        {
            SteeringWheel sw = bb as SteeringWheel;
            if (sw != null)
            {
                sw.targetAngleMode = true;
                sw.AngleToBe = value;
                return;
            }
            RudderController rudder = bb as RudderController;
            if (rudder != null)
            {
                rudder.targetAngleMode = true;
                rudder.AngleToBe = value;
            }
        }
    }
}
