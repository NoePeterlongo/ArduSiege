// LuaActuators — M3 du mod ScriptingMod (Besiege) : API actionneurs.
//
// emulate(canal, etat) — pilote les blocs vanilla via l'emulation de touches native :
//   - le joueur lie la touche d'un bloc au MESSAGE "moncanal" dans son mapper
//     (systeme vanilla « variables a la place des touches », v1.10) ;
//   - le script appelle emulate("moncanal", true/false) chaque tick ;
//   - l'etat desiré est APPLIQUE au tick d'emulation officiel
//     (SendKeyEmulationUpdateHost -> ModBlockBehaviour.EmulateKeys -> KeyInputController) ;
//   - IDEMPOTENCE obligatoire : KeyInputController.Emulate fait un compteur
//     (MKey.UpdateEmulation incrémente/décrémente `emulators`) — on n'appelle EmulateKeys
//     QUE sur les changements d'etat (pending != applied) ;
//   - garde anti-NRE : KeyInputController.Emulate fait usedMessages[text] SANS
//     verification — émuler un canal sans écouteur lèverait — on scanne les blocs de la
//     machine (MapperTypes publics) pour vérifier le canal avant d'émuler.
//
// Le wrapper est directement CALLABLE : emulate("x", true) via le metamethod __call.
// Voir docs/journal-m3.md.

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

        // canal -> MKey d'emulation (useMessage=true, message=[canal])
        private readonly Dictionary<string, MKey> m_ChannelKeys = new Dictionary<string, MKey>();

        // etats desires (mis a jour par Lua a chaque tick) / etats appliques (dernier envoye)
        private readonly Dictionary<string, bool> m_Pending = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> m_Applied = new Dictionary<string, bool>();

        // canaux signales sans écouteur (log unique par canal par simulation)
        private readonly HashSet<string> m_Warned = new HashSet<string>();

        // sondes M3 : premier envoi reussi par canal / canaux en erreur (log unique)
        private readonly HashSet<string> m_AppliedOnce = new HashSet<string>();
        private readonly HashSet<string> m_Errored = new HashSet<string>();

        internal LuaActuatorApi(MicrocontrollerScript controller)
        {
            m_Controller = controller;
        }

        // --- Lua : emulate(canal, etat) ---

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
            // rend le wrapper directement callable : emulate("canal", true)
            if (metaname == "__call")
            {
                return DynValue.NewCallback(EmulateCallback, "emulate");
            }
            return null;
        }

        private DynValue EmulateCallback(ScriptExecutionContext ctx, CallbackArguments args)
        {
            // Robuste aux deux formes : emulate("canal", true) via __call (args =
            // [wrapper, canal, etat]) et emulate.emulate("canal", true) via Index (args =
            // [canal, etat]). On prend le premier string comme canal, le premier booleen
            // comme etat.
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
                throw new ScriptRuntimeException("emulate(canal, etat) : canal (chaine) requis, ex. emulate(\"pousser\", true)");
            }
            if (stateArg == null)
            {
                throw new ScriptRuntimeException("emulate(canal, etat) : etat (booleen) requis, ex. emulate(\"pousser\", true)");
            }

            m_Pending[channelArg.String] = stateArg.Boolean;
            return DynValue.Nil;
        }

        public bool SetIndex(Script script, DynValue index, DynValue value, bool isDirectIndexing)
        {
            return false;
        }

        // --- Application au tick d'emulation officiel (appele par le controller) ---

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

                bool previously;
                if (m_Applied.TryGetValue(channel, out previously) && previously == state)
                {
                    continue; // idempotence : rien a changer
                }

                if (!m_Controller.ChannelHasListeners(channel))
                {
                    if (!m_Warned.Contains(channel))
                    {
                        m_Warned.Add(channel);
                        ModConsole.Log("[ScriptingMod] diag cles machine : " + m_Controller.DumpMachineKeys());
                        ModConsole.Log("[ScriptingMod] actionneur : canal \"" + channel
                            + "\" sans écouteur — liez la touche d'un bloc a ce MESSAGE dans son mapper (variable/« " + channel + " »)");
                    }
                    continue;
                }

                try
                {
                    m_Controller.EmulateChannel(GetOrCreateChannelKey(channel), state);
                }
                catch (System.Exception ex)
                {
                    // KeyInputController.Emulate fait usedMessages[text] — une exception
                    // ici casserait le tick machine du jeu ; on capture une fois par canal.
                    if (!m_Errored.Contains(channel))
                    {
                        m_Errored.Add(channel);
                        ModConsole.Log("[ScriptingMod] ERREUR emulate(\"" + channel + "\") : "
                            + ex.GetType().FullName + " : " + ex.Message);
                    }
                    continue;
                }
                m_Applied[channel] = state;

                // Sonde : premier envoi reussi du canal vers le jeu.
                if (!m_AppliedOnce.Contains(channel))
                {
                    m_AppliedOnce.Add(channel);
                    ModConsole.Log("[ScriptingMod] diag : emulate(\"" + channel + "\", " + state + ") envoye au jeu");
                }
            }
        }

        /// <summary>Relache tout ce qui a ete applique (fin de simulation).</summary>
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
            m_AppliedOnce.Clear();
            m_Errored.Clear();
        }

        private MKey GetOrCreateChannelKey(string channel)
        {
            MKey key;
            if (!m_ChannelKeys.TryGetValue(channel, out key))
            {
                // MKey construit manuellement (ctor public) — jamais affiche dans un mapper ;
                // useMessage=true : l'emulation vise le MESSAGE (canal nomme, pas une touche).
                key = new MKey("emulate-" + channel, "sm-emulate-" + channel, KeyCode.None, isEmulator: true);
                key.useMessage = true;
                key.message = new string[1] { channel };
                m_ChannelKeys[channel] = key;
            }
            return key;
        }
    }
}
