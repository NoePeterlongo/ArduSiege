// MicrocontrollerScript — bloc « Microcontroller » du mod ScriptingMod (Besiege).
//
//   M1 : mapper vanilla (menu script + toggle « actif »), runtime Lua par simulation,
//        on_tick a 50 Hz, etat frais a chaque run, watchdog borne chaque execution.
//   M2 : API capteurs (self/machine/input).  M3 : actionneurs (emulate/set_slider).
//   M4 : persistance .bsg + automate D1-bis :
//     - OnSave/OnLoad : la source du script voyage DANS la machine (D1) — la machine
//       est autonome (Workshop, fichier local renomme/supprime) ;
//     - l'association dans le mapper COPIE le fichier vers l'embarque ;
//     - 3 etats (D1-bis, docs/workflow-scripting.md) : synchronise / fichier avance /
//       orphelin (fichier absent, marque « * » dans le menu) ;
//     - au debut de sim : le FICHIER gagne s'il existe, l'EMBARQUE sinon ;
//     - remplacer un script orphelin sauvegarde automatiquement l'ancienne source
//       en <nom>.recup.lua (jamais de perte silencieuse).
//
// Console : Ctrl+Shift+K. Journaux : docs/journal-m1..m4.md.

using System.Collections.Generic;
using Modding;
using MoonSharp.Interpreter;
using UnityEngine;
// NB : qualifier Modding.ModIO en entier — le jeu a un namespace global ModIO (interdit)
// qui masque la classe Modding.ModIO dans la resolution de noms.
// Alias DataType : Modding.DataType (jeu) vs MoonSharp.Interpreter.DataType (Lua).
using MoonDataType = MoonSharp.Interpreter.DataType;

namespace ScriptingMod
{
    public class MicrocontrollerScript : BlockScript
    {
        // Budget volontaire bas pour M1 (M0 : 50M = ~17 s de gel ; cf. journal M0 etape 9).
        private const long WATCHDOG_BUDGET = 1000000L;

        private const string SCRIPTS_DIR = "Scripts";

        // M4 : cles XDataHolder de la source embarquee (D1 — la verite de la machine).
        private const string KEY_NAME = "sm-script-name";
        private const string KEY_SOURCE = "sm-script-source";

        // M4 : marqueur d'orphelin dans le menu (fichier absent de la bibliotheque).
        private const string ORPHAN_MARK = "* ";

        private MMenu m_ScriptMenu;
        private MToggle m_ActiveToggle;

        private Script m_Lua;
        private WatchdogDebugger m_Watchdog;
        private LuaActuatorApi m_Actuator;
        private int m_TickCount;
        private string m_ScriptName;

        // M4 : source embarquee (copie de la bibliotheque faite a l'association,
        // resynchronisee par reload ; verite de la machine quand le fichier est absent).
        private string m_EmbeddedName;
        private string m_EmbeddedSource;

        // M3 : indispensable — sans ce flag, la machine n'active pas le systeme
        // d'emulation (Machine.cs lit Prefab.EmulatesAnyKeys, propage depuis notre
        // override au SafeAwake via ModBlockBehaviourHandler.cs:174).
        public override bool EmulatesAnyKeys
        {
            get { return true; }
        }

        public override void SafeAwake()
        {
            m_ScriptMenu = AddMenu("script", 0, GetScriptList(), footerMenu: false);
            m_ActiveToggle = AddToggle("actif", "actif", true);

            // M4 : l'association dans le mapper copie le fichier vers l'embarque (D1).
            m_ScriptMenu.ValueChanged += OnScriptMenuChanged;

            Debug.Log("[ScriptingMod] Microcontroleur : SafeAwake (bloc instancie)");
        }

        // M4 : copie bibliotheque -> machine quand le joueur associe un script.
        // Garde : BlockMapper.CurrentInstance == null pendant le chargement d'une
        // machine (le DeSerialize des MapperTypes invoque ValueChanged hors UI —
        // copier alors lirait le mauvais fichier, index de menu non encore repointe).
        private void OnScriptMenuChanged(int index)
        {
            if (BlockMapper.CurrentInstance == null || m_ScriptMenu == null
                || index < 0 || index >= m_ScriptMenu.Items.Count)
            {
                return;
            }
            string name = ResolveName(m_ScriptMenu.Items[index]);
            string file = ReadScript(name);
            if (file == null)
            {
                // entree orpheline selectionnee : on garde l'embarque tel quel.
                return;
            }
            // anti-ecrasement (D1-bis) : si l'ancien embarque etait orphelin (fichier
            // absent), il allait etre perdu — sauvegarde automatique avant remplacement.
            if (m_EmbeddedName != null && m_EmbeddedName != name
                && m_EmbeddedSource != null && ReadScript(m_EmbeddedName) == null)
            {
                BackupOrphan(m_EmbeddedName, m_EmbeddedSource);
            }
            m_EmbeddedName = name;
            m_EmbeddedSource = file;
            ModConsole.Log("[ScriptingMod] script \"" + name + "\" associe (source copiee dans la machine)");
        }

        // --- M4 : persistance .bsg (D1) — la source voyage avec la machine ---

        public override void OnLoad(XDataHolder data)
        {
            if (data.HasKey(KEY_NAME))
            {
                m_EmbeddedName = data.ReadString(KEY_NAME);
                m_EmbeddedSource = data.ReadString(KEY_SOURCE);
                // Repointer le menu par NOM (l'index sauvegarde peut etre decale par
                // une bibliotheque qui a evolue entre-temps — bug latent M1 corrige).
                ReSelectMenuFor(m_EmbeddedName);
            }
        }

        public override void OnSave(XDataHolder data)
        {
            if (m_EmbeddedName != null)
            {
                data.Write(KEY_NAME, m_EmbeddedName);
                data.Write(KEY_SOURCE, m_EmbeddedSource ?? string.Empty);
            }
        }

        /// <summary>
        /// Selectionne dans le menu l'entree correspondant au nom embarque (l'ajoute
        /// marquee « * » si le fichier est absent — etat orphelin visible, D1-bis).
        /// SetValue n'invoque PAS ValueChanged : pas de copie recursive.
        /// </summary>
        private void ReSelectMenuFor(string name)
        {
            if (m_ScriptMenu == null || name == null)
            {
                return;
            }
            List<string> items = m_ScriptMenu.Items;
            string target = (ReadScript(name) != null) ? name : ORPHAN_MARK + name;

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
                items.Add(target);
                idx = items.Count - 1;
            }
            else
            {
                items[idx] = target;    // rafraichit le marqueur orphelin/synchronise
            }
            m_ScriptMenu.SetValue(idx);
        }

        /// <summary>Sauvegarde une source orpheline en <nom>.recup[.lua — jamais d'ecrasement silencieux.</summary>
        private static void BackupOrphan(string name, string source)
        {
            try
            {
                string path = SCRIPTS_DIR + "/" + name + ".recup.lua";
                int n = 2;
                while (Modding.ModIO.ExistsFile(path, data: true) && n < 100)
                {
                    path = SCRIPTS_DIR + "/" + name + ".recup" + n + ".lua";
                    n++;
                }
                Modding.ModIO.WriteAllText(path, source, data: true);
                ModConsole.Log("[ScriptingMod] ancienne source orpheline sauvegardee : "
                    + System.IO.Path.GetFileName(path) + " (elle allait etre ecrasee)");
            }
            catch (System.Exception ex)
            {
                ModConsole.Log("[ScriptingMod] sauvegarde orphelin impossible : " + ex.Message);
            }
        }

        /// <summary>Nom reel d'une entree de menu (retire le marqueur orphelin « * »).</summary>
        internal static string ResolveName(string item)
        {
            if (string.IsNullOrEmpty(item))
            {
                return null;
            }
            return item.StartsWith(ORPHAN_MARK) ? item.Substring(ORPHAN_MARK.Length) : item;
        }

        // --- M4 : accesseurs pour les commandes console (script show/reload/export) ---

        internal string EmbeddedName
        {
            get { return m_EmbeddedName; }
        }

        internal string EmbeddedSource
        {
            get { return m_EmbeddedSource; }
        }

        /// <summary>Nom selectionne au menu (sans marqueur) — cible de reload/export.</summary>
        internal string MenuSelection
        {
            get { return m_ScriptMenu == null ? null : ResolveName(m_ScriptMenu.Selection); }
        }

        /// <summary>Vrai si le runtime de ce controleur est monte (simulation en cours).</summary>
        internal bool IsRuntimeActive
        {
            get { return m_Lua != null; }
        }

        /// <summary>Etat de synchronisation embarque <-> bibliotheque (script show).</summary>
        internal string SyncState()
        {
            if (m_EmbeddedSource == null || m_EmbeddedName == null)
            {
                return "sans source embarquee (lecture bibliotheque)";
            }
            string file = ReadScript(m_EmbeddedName);
            if (file == null)
            {
                return "ORPHELIN (fichier absent — la machine vit avec sa source embarquee)";
            }
            if (file == m_EmbeddedSource)
            {
                return "synchronise";
            }
            return "DIFFERENT de la bibliotheque (sim = fichier ; 'script export' pour recuperer l'embarque)";
        }

        /// <summary>
        /// M4 : script reload — resynchronise fichier -> embarque (D1-bis) et remonte
        /// le runtime a chaud si ce controleur etait en simulation.
        /// </summary>
        internal bool ReloadFromLibrary()
        {
            string name = m_EmbeddedName ?? MenuSelection;
            if (name == null || name == "(aucun script)")
            {
                ModConsole.Log("[ScriptingMod] reload : aucun script associe");
                return false;
            }
            string file = ReadScript(name);
            if (file == null)
            {
                ModConsole.Log("[ScriptingMod] reload : fichier introuvable : " + name
                    + (m_EmbeddedSource != null ? " (source embarquee conservee)" : string.Empty));
                return false;
            }

            bool remount = m_Lua != null && Modding.Game.IsSimulating;
            if (remount)
            {
                OnSimulateStop();       // on_sim_stop + relache des canaux + demontage
            }
            m_EmbeddedName = name;
            m_EmbeddedSource = file;
            ReSelectMenuFor(name);
            if (remount)
            {
                OnSimulateStart();      // remontage avec la source fraiche
            }
            ModConsole.Log("[ScriptingMod] " + name + " : resynchronise depuis la bibliotheque"
                + (remount ? " et re-execute" : " (applique au prochain debut de sim)"));
            return true;
        }

        /// <summary>Rafraichit le marqueur du menu (apres export : orphelin -> synchronise).</summary>
        internal void RefreshMenuMarker()
        {
            if (m_EmbeddedName != null)
            {
                ReSelectMenuFor(m_EmbeddedName);
            }
        }

        public override void OnSimulateStart()
        {
            m_TickCount = 0;
            m_Lua = null;
            m_Watchdog = null;
            m_Actuator = null;

            if (!m_ActiveToggle.IsActive)
            {
                Debug.Log("[ScriptingMod] Microcontroleur : inactif (toggle off)");
                return;
            }

            // M4 : resolution de la source (D1-bis) — le FICHIER gagne s'il existe
            // (atelier, fluidite edition), l'EMBARQUE sinon (machine autonome).
            string name = MenuSelection;
            string source = ReadScript(name);

            if (source != null)
            {
                if (m_EmbeddedName != name || m_EmbeddedSource != source)
                {
                    m_EmbeddedName = name;
                    m_EmbeddedSource = source;      // resynchronise fichier -> machine
                }
            }
            else if (m_EmbeddedName == name && m_EmbeddedSource != null)
            {
                source = m_EmbeddedSource;
                ModConsole.Log("[ScriptingMod] " + name + " : fichier absent, source embarquee (machine autonome)");
            }
            else if (m_EmbeddedName != null && m_EmbeddedSource != null)
            {
                // menu decale par une bibliotheque ayant evolue : l'embarque est la verite
                name = m_EmbeddedName;
                source = m_EmbeddedSource;
                ModConsole.Log("[ScriptingMod] " + name + " : menu decale, source embarquee utilisee");
            }

            m_ScriptName = name;
            if (source == null)
            {
                Debug.LogWarning("[ScriptingMod] Microcontroleur : script introuvable : " + m_ScriptName);
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

                m_Watchdog = new WatchdogDebugger(WATCHDOG_BUDGET);
                m_Lua.AttachDebugger(m_Watchdog);

                // M2 : API capteurs (self, machine, input) — avant DoString pour
                // que le script puisse les utiliser des on_sim_start.
                LuaApi.Install(m_Lua, this);

                // M3 : API actionneurs — emulate(canal, etat)
                m_Actuator = new LuaActuatorApi(this);
                m_Lua.Globals.Set("emulate", UserData.Create(m_Actuator));

                m_Watchdog.Reset();
                m_Lua.DoString(source, null, m_ScriptName);
                CallIfDefined("on_sim_start");
                Debug.Log("[ScriptingMod] Microcontroleur : script " + m_ScriptName + " demarre");

                // M2 : la commande console `lua` s'executera dans CE contexte
                ScriptingModEntryPoint.SetActiveRuntime(m_Lua, m_Watchdog, m_ScriptName);
            }
            catch (WatchdogBudgetExceededException wex)
            {
                Debug.LogWarning("[ScriptingMod] Microcontroleur : watchdog au chargement (" + wex.Budget + ") — script desactive");
                m_Lua = null;
            }
            catch (InterpreterException iex)
            {
                Debug.LogWarning("[ScriptingMod] Microcontroleur : erreur Lua au chargement : "
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
                m_Watchdog.Reset();          // budget PAR TICK (et non par simulation)
                CallIfDefined("on_tick");
            }
            catch (WatchdogBudgetExceededException wex)
            {
                Debug.LogWarning("[ScriptingMod] Microcontroleur : script " + m_ScriptName
                    + " desactive (tick " + m_TickCount + " : budget " + wex.Budget + " depasse)");
                m_Lua = null;
            }
            catch (InterpreterException iex)
            {
                Debug.LogWarning("[ScriptingMod] Microcontroleur : erreur Lua tick " + m_TickCount
                    + " : " + (iex.DecoratedMessage ?? iex.Message));
                m_Lua = null;               // desactive jusqu'au prochain run
            }
        }

        public override void OnSimulateStop()
        {
            CallIfDefined("on_sim_stop");

            // M3 : relacher les canaux emules AVANT de demonter (symetrie du compteur
            // d'emulation — sinon les blocs resteraient "coinces" jusqu'au prochain run)
            if (m_Actuator != null)
            {
                m_Actuator.ResetAll();
                m_Actuator = null;
            }

            m_Lua = null;
            m_Watchdog = null;
            ScriptingModEntryPoint.ClearActiveRuntime();
        }

        // --- M3 : application des canaux au tick d'emulation officiel (host) ---

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
            // ModBlockBehaviour.EmulateKeys (wrapper public) — activationKeys vides :
            // notre bloc n'a aucune touche propre, pas de boucle possible.
            EmulateKeys(new MKey[0], channelKey, state);
        }

        /// <summary>
        /// Le canal a-t-il au moins un écouteur dans la machine ? (garde anti-NRE :
        /// KeyInputController.Emule dereference usedMessages[text] sans verification).
        /// Scan des MapperTypes publics des blocs — messages = liaisons mapper v1.10.
        /// </summary>
        internal bool ChannelHasListeners(string channel)
        {
            return FindChannelKeys(channel).Count > 0;
        }

        /// <summary>
        /// Diagnostic : resume des MKey de la machine (useMessage, keycodes, messages).
        /// Sert a comprendre pourquoi un canal n'a pas d'ecouteur (message mal tape,
        /// useMessage absent, ou KeysCount==0 — cas ou le jeu n'enregistre PAS le
        /// message dans usedMessages, cf. Machine.InitSimBlock).
        /// </summary>
        internal string DumpMachineKeys()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            Modding.Blocks.PlayerMachine pm = Machine;
            if (pm == null)
            {
                return "(machine introuvable)";
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
                sb.Append("\n  (aucune MKey dans la machine)");
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
                return;     // runtime non monte (chargement KO) — rien a appeler
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
                    // nom affiche/sauvegarde : nom de fichier sans extension
                    string name = System.IO.Path.GetFileNameWithoutExtension(file);
                    if (!string.IsNullOrEmpty(name))
                    {
                        names.Add(name);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[ScriptingMod] Microcontroleur : liste des scripts impossible : " + ex.Message);
            }
            if (names.Count == 0)
            {
                names.Add("(aucun script)");
            }
            return names;
        }

        private string ReadScript(string name)
        {
            if (name == null || name == "(aucun script)")
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
                Debug.LogWarning("[ScriptingMod] Microcontroleur : lecture impossible (" + name + ") : " + ex.Message);
                return null;
            }
        }
    }
}
