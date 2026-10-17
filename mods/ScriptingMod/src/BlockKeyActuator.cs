// BlockKeyActuator — actionnement direct des touches d'un bloc (branche
// actionnement-direct).
//
// b:key("forward", true) — b:forward/b:backward/b:spin (sucre) — sans AUCUNE liaison
// « variable » vanilla : on incremente directement le COMPTEUR d'emulation de la MKey
// du bloc (MKey.UpdateEmulation, public). Le bloc lit EmulationHeld() au tick
// d'emulation officiel (CogMotorControllerHinge l.501) — meme chemin que l'emulation
// vanilla par portes logiques. Notre Microcontroller force deja
// Prefab.EmulatesAnyKeys -> le systeme d'emulation machine est actif.
//
// IDEMPOTENCE (lecon M3) : UpdateEmulation est un compteur (+= ±1), pas un etat —
// on n'envoie QUE les transitions ; un canal absent du registre est deja a "false"
// (etat de naissance du jeu) : jamais de faux "false" initial.
//
// Registre PAR CONTROLEUR (une instance par runtime) : remis a zero (relache tout)
// a l'arret de la sim du controleur. Limite D4 : deux scripts pilotant la meme MKey
// cumulent leurs compteurs — multi-controleurs = responsabilite du joueur.

using System.Collections.Generic;
using MoonSharp.Interpreter;

namespace ScriptingMod
{
    internal class BlockKeyActuator
    {
        // etats appliques par CE runtime (MKey -> etat courant)
        private readonly Dictionary<MKey, bool> m_Applied = new Dictionary<MKey, bool>();

        /// <summary>
        /// Applique l'etat desire a une MKey (idempotent : transition uniquement).
        /// </summary>
        internal void Set(MKey key, bool state)
        {
            bool previously;
            m_Applied.TryGetValue(key, out previously);   // absent = false (baseline jeu)
            if (previously == state)
            {
                return;
            }
            key.UpdateEmulation(state);
            m_Applied[key] = state;
        }

        /// <summary>Relache tout ce qui a ete applique (fin de simulation).</summary>
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
