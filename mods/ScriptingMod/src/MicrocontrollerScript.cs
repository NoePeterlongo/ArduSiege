// MicrocontrollerScript — M1 du mod ScriptingMod (Besiege).
//
// Comportement du bloc « Microcontroller » (M1 : validation du cycle bloc + runtime) :
//   - mapper vanilla : menu du script (bibliotheque du dossier data), toggle « actif » ;
//   - OnSimulateStart : monte une instance MoonSharp sandboxee + charge le script choisi
//     depuis la bibliotheque (dossier data du mod) + appelle on_sim_start si defini ;
//   - SimulateFixedUpdateHost (50 Hz) : appelle on_tick si defini ;
//   - OnSimulateStop : demonte le runtime (etat frais a chaque simulation).
//   - la watchdog borne chaque execution (budget M1 volontaire bas, cf. journal M0).
//
// Le bloc se place dans la barre de blocs (recherche : « Microcontroller », « Script »,
// « Lua »). Console : Ctrl+Shift+K. Voir docs/journal-m1.md.

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

        private MMenu m_ScriptMenu;
        private MToggle m_ActiveToggle;

        private Script m_Lua;
        private WatchdogDebugger m_Watchdog;
        private int m_TickCount;
        private string m_ScriptName;

        public override void SafeAwake()
        {
            m_ScriptMenu = AddMenu("script", 0, GetScriptList(), footerMenu: false);
            m_ActiveToggle = AddToggle("actif", "actif", true);

            // log de debug M1 au placement (visible avec show_logs true)
            Debug.Log("[ScriptingMod] Microcontroleur : SafeAwake (bloc instancie)");
        }

        public override void OnSimulateStart()
        {
            m_TickCount = 0;
            m_Lua = null;
            m_Watchdog = null;

            if (!m_ActiveToggle.IsActive)
            {
                Debug.Log("[ScriptingMod] Microcontroleur : inactif (toggle off)");
                return;
            }

            m_ScriptName = m_ScriptMenu.Selection;   // nom de fichier sans extension
            string source = ReadScript(m_ScriptName);
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
            m_Lua = null;
            m_Watchdog = null;
            ScriptingModEntryPoint.ClearActiveRuntime();
        }

        // --- helpers ---

        private void CallIfDefined(string func)
        {
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
