// BlockAngleRamps — rampes d'angle cible (branche actionnement-direct).
//
// b:set_angle(angle) : la cible avance vers l'angle vise A LA VITESSE DU BLOC —
// transparent : c'est le slider "rotation-speed" du mapper, la meme vitesse que
// quand on tient la touche (le bloc deplace lui-meme sa cible de totalSpeed par
// tick, decomp SteeringWheel l.373). Plus de saut brusque qui casse la machine.
// b:set_angle(angle, v_max) : surcharge la vitesse en degres/seconde.
//
// Le registre vit PAR CONTROLEUR (une instance par runtime, comme
// BlockKeyActuator), avance au tick du controleur (50 Hz), purge a l'arret de sim.
// Limite D4 : deux scripts rampons le meme bloc — responsabilite du joueur.
// Slider a 0 = vitesse nulle : la rampe attend (meme comportement que la touche).

using System.Collections.Generic;
using UnityEngine;

namespace ScriptingMod
{
    internal class BlockAngleRamps
    {
        private struct Ramp
        {
            public float Goal;          // degres vises
            public float DegPerSec;     // > 0 : vitesse explicite ; <= 0 : vitesse du bloc
        }

        // bloc orientable -> rampe en cours (cle = instance stable pendant la sim)
        private readonly Dictionary<BlockBehaviour, Ramp> m_Ramps =
            new Dictionary<BlockBehaviour, Ramp>();

        /// <summary>b:set_angle(angle) — vitesse du bloc ; b:set_angle(angle, v_max).</summary>
        internal void Set(BlockBehaviour bb, float goal, float degPerSec)
        {
            Ramp r;
            r.Goal = goal;
            r.DegPerSec = degPerSec;
            m_Ramps[bb] = r;
        }

        /// <summary>Avance les cibles vers leur but (appele au tick du controleur).</summary>
        internal void Tick(float dt)
        {
            List<BlockBehaviour> done = null;
            foreach (KeyValuePair<BlockBehaviour, Ramp> kv in m_Ramps)
            {
                Ramp r = kv.Value;
                float step = (r.DegPerSec > 0f) ? r.DegPerSec * dt : BlockSpeedPerTick(kv.Key);
                if (step <= 0f)
                {
                    continue;       // slider a 0 : la rampe attend
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

        /// <summary>Purge les rampes (fin de simulation) — la cible reste ou elle est.</summary>
        internal void Clear()
        {
            m_Ramps.Clear();
        }

        /// <summary>Vitesse du bloc, degres PAR TICK (celle du slider rotation-speed).</summary>
        private static float BlockSpeedPerTick(BlockBehaviour bb)
        {
            SteeringWheel sw = bb as SteeringWheel;
            if (sw != null)
            {
                return sw.totalSpeed;      // public, recalcule par le bloc a chaque tick
            }
            RudderController rudder = bb as RudderController;
            if (rudder != null && rudder.SpeedSlider != null)
            {
                // formule du decomp RudderController l.331 : accumulation angleyToBe
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
