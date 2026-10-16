// MicrocontrollerScript — bloc « Microcontroller » du mod ScriptingMod (Besiege).
//
//   M1 : mapper vanilla (menu script + toggle « actif »), runtime Lua par simulation,
//        on_tick a 50 Hz, etat frais a chaque run, watchdog borne chaque execution.
//   M2 : API capteurs (self/machine/input).  M3 : actionneurs (emulate/set_slider).
//   M4 : persistance par NOM uniquement (decision joueur, D1-ter — cf. journal M4) :
//     - la machine stocke le NOM du script (cle sm-script-name) — la BIBLIOTHEQUE
//       est l'unique source de verite (pas de source embarquee) ;
//     - le menu est repointe par nom au chargement (corrige l'index decale) et
//       rafraichi a l'ouverture du mapper (nouveaux scripts visibles sans relancer
//       le niveau) ;
//     - un script absent de la bibliotheque est marque « * » au mapper et le
//       controleur le signale clairement (il ne fait rien ce run).
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

        // M4 : nom du script persiste dans le .bsg (la bibliotheque est la verite,
        // le nom suffit a retrouver le fichier — decision joueur D1-ter).
        private const string KEY_NAME = "sm-script-name";

        // M4 : marqueur « n'existe plus » dans le menu (fichier absent).
        private const string MISSING_MARK = "* ";

        private MMenu m_ScriptMenu;
        private MToggle m_ActiveToggle;

        private Script m_Lua;
        private WatchdogDebugger m_Watchdog;
        private LuaActuatorApi m_Actuator;
        private int m_TickCount;
        private string m_ScriptName;

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

            Debug.Log("[ScriptingMod] Microcontroleur : SafeAwake (bloc instancie)");
        }

        // --- M4 : persistance par nom (D1-ter) — la bibliotheque est la verite ---

        public override void OnLoad(XDataHolder data)
        {
            if (!data.HasKey(KEY_NAME))
            {
                return;     // machine pre-M4 : l'index vanilla suffit (comportement M1)
            }
            string name = data.ReadString(KEY_NAME);
            if (string.IsNullOrEmpty(name))
            {
                return;
            }
            Debug.Log("[ScriptingMod] chargement : script \"" + name + "\"");
            // Repointer le menu par NOM (l'index sauvegarde peut etre decale par une
            // bibliotheque qui a evolue — bug latent M1 corrige).
            RepointMenu(name);
        }

        public override void OnSave(XDataHolder data)
        {
            string name = MenuSelection;
            if (name != null && name != "(aucun script)")
            {
                data.Write(KEY_NAME, name);
            }
        }

        /// <summary>
        /// Selectionne dans le menu l'entree correspondant au nom (l'ajoute marquee
        /// « * n'existe plus » si le fichier est absent de la bibliotheque).
        /// SetValue n'invoque pas ValueChanged : pas d'evenement parasite.
        /// </summary>
        private void RepointMenu(string name)
        {
            if (m_ScriptMenu == null || name == null)
            {
                return;
            }
            List<string> items = m_ScriptMenu.Items;
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
                items.Add(MISSING_MARK + name);
                idx = items.Count - 1;
            }
            else
            {
                // marqueur dynamique : marque si le fichier est desormais absent
                items[idx] = (ReadScript(name) != null) ? name : MISSING_MARK + name;
            }
            m_ScriptMenu.SetValue(idx);
        }

        /// <summary>
        /// Rafraichit le menu depuis la bibliotheque (nouveaux scripts visibles sans
        /// relancer le niveau) en conservant la selection courante par nom.
        /// Appele a l'ouverture du mapper (onMapperOpen, cf. ScriptingModEntryPoint).
        /// </summary>
        internal void RefreshMenu()
        {
            if (m_ScriptMenu == null)
            {
                return;
            }
            string current = MenuSelection;
            m_ScriptMenu.Items = GetScriptList();
            if (!string.IsNullOrEmpty(current) && current != "(aucun script)")
            {
                RepointMenu(current);
            }
        }

        /// <summary>Nom reel d'une entree de menu (retire le marqueur « n'existe plus »).</summary>
        internal static string ResolveName(string item)
        {
            if (string.IsNullOrEmpty(item))
            {
                return null;
            }
            return item.StartsWith(MISSING_MARK) ? item.Substring(MISSING_MARK.Length) : item;
        }

        /// <summary>Nom selectionne au menu (sans marqueur) — cible de reload/show.</summary>
        internal string MenuSelection
        {
            get { return m_ScriptMenu == null ? null : ResolveName(m_ScriptMenu.Selection); }
        }

        /// <summary>Vrai si le runtime de ce controleur est monte (simulation en cours).</summary>
        internal bool IsRuntimeActive
        {
            get { return m_Lua != null; }
        }

        /// <summary>Le script selectionne existe-t-il dans la bibliotheque ?</summary>
        internal bool HasScriptFile
        {
            get { return ReadScript(MenuSelection) != null; }
        }

        /// <summary>
        /// M4 : script reload — relit le fichier et remonte le runtime a chaud si ce
        /// controleur etait en simulation.
        /// </summary>
        internal bool ReloadFromLibrary()
        {
            string name = MenuSelection;
            if (name == null || name == "(aucun script)")
            {
                ModConsole.Log("[ScriptingMod] reload : aucun script associe");
                return false;
            }
            string file = ReadScript(name);
            if (file == null)
            {
                ModConsole.Log("[ScriptingMod] reload : fichier introuvable : " + name
                    + " (renomme/supprime ? — 'script list' pour la bibliotheque)");
                return false;
            }

            bool remount = m_Lua != null && Modding.Game.IsSimulating;
            if (remount)
            {
                OnSimulateStop();       // on_sim_stop + relache des canaux + demontage
            }
            RepointMenu(name);          // marqueur « n'existe plus » retire si revenu
            if (remount)
            {
                OnSimulateStart();      // remontage avec la source fraiche
            }
            ModConsole.Log("[ScriptingMod] " + name + " : recharge"
                + (remount ? " et re-execute" : " (applique au prochain debut de sim)"));
            return true;
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

            // M4 : la bibliotheque est l'unique source (D1-ter) — resolution par nom.
            string name = MenuSelection;
            string source = ReadScript(name);

            m_ScriptName = name;
            if (source == null)
            {
                ModConsole.Log("[ScriptingMod] Microcontroleur : script \""
                    + (name ?? "?") + "\" introuvable dans la bibliotheque (renomme/supprime ?)"
                    + " — ce controleur ne fait rien ce run. 'script list' pour voir la bibliotheque.");
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
