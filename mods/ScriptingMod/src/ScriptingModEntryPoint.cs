// ScriptingModEntryPoint — POC M0 (Besiege).
//
// Valide en jeu, de bout en bout :
//   1. que la DLL MoonSharp elaguee (third-party/moonsharp-trimmed) passe l'AssemblyScanner
//      du mod loader — sinon le mod ne se charge pas du tout (message [Security]) ;
//   2. qu'un Script sandbox (Preset_SoftSandbox) s'execute : hello Lua, os.time, string.* ;
//   3. le micro-benchmark tick (commande `bench`) ;
//   4. la watchdog d'instructions (commande `watchdog` -> boucle infenie interrompue).
//
// Console en jeu : Ctrl+Shift+K ; logs visibles apres `show_logs true`.
// Les mods se chargent a l'entree dans un niveau/multiverse (pas au lancement).
// Voir docs/journal-m0.md (etape 8).

using System;
using System.Diagnostics;
using Modding;
using MoonSharp.Interpreter;
// Le jeu expose Modding.DataType (types reseau) : alias pour lever l'ambiguite
// avec MoonSharp.Interpreter.DataType (types de valeurs Lua).
using MoonDataType = MoonSharp.Interpreter.DataType;

namespace ScriptingMod
{
    public class ScriptingModEntryPoint : ModEntryPoint
    {
        // Budget par execution (instructions VM par DoString/Call) — a calibrer.
        private const long WATCHDOG_BUDGET = 50000000L;

        private Script m_Script;
        private WatchdogDebugger m_Watchdog;

        public override void OnLoad()
        {
            ModConsole.Log("[ScriptingMod] M1 : chargement...");

            try
            {
                // 1. Instance Lua sandboxee
                m_Script = new Script(CoreModules.Preset_SoftSandbox);

                // print -> console du jeu
                m_Script.Globals.Set("print", DynValue.NewCallback(
                    (ctx, args) =>
                    {
                        PrintToConsole(args);
                        return DynValue.Nil;
                    }, "print"));

                // M2 : input utilisable depuis la console (sans contexte machine)
                m_Script.Globals.Set("input", UserData.Create(new LuaInputApi()));

                // 2. Watchdog branchee sur la VM
                m_Watchdog = new WatchdogDebugger(WATCHDOG_BUDGET);
                m_Script.AttachDebugger(m_Watchdog);
            }
            catch (Exception ex)
            {
                ModConsole.Log("[ScriptingMod] ERREUR init MoonSharp : " + ex);
                return;
            }

            // 3. Hello Lua (teste aussi : parse, sandbox os.time, string.format)
            string helloResult = RunLua(
                "print('hello depuis Lua !') " +
                "return string.format('%s %s', _VERSION, _MOONSHARP.version)");
            if (helloResult != null)
                ModConsole.Log("[ScriptingMod] hello OK -> " + helloResult);

            // 4. Sanbox verifiee : io/load interdits, os.time autorise
            string sandboxResult = RunLua(
                "local ok = (io == nil) and (load == nil) and (os ~= nil and os.time() > 0)\n" +
                "return ok and 'sandbox OK' or 'SANDBOX KO'");
            if (sandboxResult != null)
                ModConsole.Log("[ScriptingMod] sandbox -> " + sandboxResult);

            // 5. Commandes console de test
            ModConsole.RegisterCommand("lua", LuaCommand,
                "lua \"<code lua>\" — execute du code Lua et affiche le retour");
            ModConsole.RegisterCommand("bench", BenchCommand,
                "bench [n] — micro-benchmark Lua (n iterations, defaut 1 000 000)");
            ModConsole.RegisterCommand("watchdog", WatchdogCommand,
                "watchdog — lance une boucle infinie Lua : doit etre interrompue par la watchdog");

            // 6. M1 : seed de la bibliotheque de scripts (dossier data du mod)
            SeedScriptLibrary();

            ModConsole.Log("[ScriptingMod] M1 pret. Commandes : lua \"<code>\", bench, watchdog");
        }

        // --- M1 : bibliotheque de scripts (dossier data persistant du mod) ---

        private const string SCRIPTS_DIR = "Scripts";

        private static void SeedScriptLibrary()
        {
            try
            {
                if (!Modding.ModIO.ExistsDirectory(SCRIPTS_DIR, data: true))
                {
                    Modding.ModIO.CreateDirectory(SCRIPTS_DIR, data: true);
                }
                // M2 : demos versionnees (tag SMDEMO vN) — mises a jour par le mod,
                // jamais ecrasees si le joueur les a renommees/personnalisees sans tag.
                WriteDemoIfOutdated("hello.lua", 1, HELLO_LUA);
                WriteDemoIfOutdated("capteurs.lua", 2, CAPTEURS_LUA);
            }
            catch (Exception ex)
            {
                ModConsole.Log("[ScriptingMod] ERREUR seed bibliotheque : " + ex.Message);
            }
        }

        // Demos : le contenu embarqueporte un tag "SMDEMO vN" en tete. Le fichier local
        // est (re)ecrit si absent, ou si son tag SMDEMO est plus ancien. Un fichier sans
        // tag (personnalise par le joueur) n'est JAMAIS touche.
        private static void WriteDemoIfOutdated(string fileName, int version, string content)
        {
            try
            {
                string path = SCRIPTS_DIR + "/" + fileName;
                string tag = "SMDEMO v" + version;

                if (!Modding.ModIO.ExistsFile(path, data: true))
                {
                    Modding.ModIO.WriteAllText(path, content, data: true);
                    ModConsole.Log("[ScriptingMod] bibliotheque : " + fileName + " installe (" + tag + ")");
                    return;
                }

                string current = Modding.ModIO.ReadAllText(path, data: true);
                int at = current != null ? current.IndexOf("SMDEMO v", System.StringComparison.Ordinal) : -1;
                if (at < 0)
                {
                    return; // pas une demo (fichier joueur) — ne pas toucher
                }
                int localVersion;
                int start = at + "SMDEMO v".Length;
                int digits = 0;
                while (start + digits < current.Length && current[start + digits] >= '0' && current[start + digits] <= '9')
                {
                    digits++;
                }
                int.TryParse(current.Substring(start, digits), out localVersion);
                if (localVersion >= version)
                {
                    return; // a jour
                }
                Modding.ModIO.WriteAllText(path, content, data: true);
                ModConsole.Log("[ScriptingMod] bibliotheque : " + fileName + " mis a jour (" + tag + ")");
            }
            catch (Exception ex)
            {
                ModConsole.Log("[ScriptingMod] demo " + fileName + " : " + ex.Message);
            }
        }

        private const string HELLO_LUA =
            "-- hello.lua (SMDEMO v1) : boucle on_tick minimale\n" +
            "local ticks = 0\n" +
            "\n" +
            "function on_sim_start()\n" +
            "    print(\"script demarre : boucle on_tick a 50 Hz\")\n" +
            "end\n" +
            "\n" +
            "function on_tick()\n" +
            "    ticks = ticks + 1\n" +
            "    if ticks % 50 == 0 then\n" +
            "        print(ticks .. \" ticks (\" .. (ticks / 50) .. \" s de simulation)\")\n" +
            "    end\n" +
            "end\n" +
            "\n" +
            "function on_sim_stop()\n" +
            "    print(\"script arrete apres \" .. ticks .. \" ticks\")\n" +
            "end\n";

        // Demo M2 : lecture des capteurs (self, machine, input) — SMDEMO v2 : touche P
        private const string CAPTEURS_LUA =
            "-- capteurs.lua (SMDEMO v2) : demo de l'API capteurs\n" +
            "-- expose : self (ce bloc), machine (les blocs), input (le clavier REEL)\n" +
            "-- NB : les demos SMDEMO sont mises a jour par le mod ; copiez ce fichier\n" +
            "--      sous un autre nom pour le personnaliser sans etre ecrase.\n" +
            "local ticks = 0\n" +
            "\n" +
            "function on_sim_start()\n" +
            "    print(\"capteurs : machine de \" .. machine.block_count .. \" blocs, masse \" .. string.format(\"%.0f\", machine.mass))\n" +
            "    local par_type = {}\n" +
            "    for _, b in ipairs(machine:blocks()) do\n" +
            "        par_type[b.type] = (par_type[b.type] or 0) + 1\n" +
            "    end\n" +
            "    for t, n in pairs(par_type) do\n" +
            "        print(\"  - \" .. t .. \" x\" .. n)\n" +
            "    end\n" +
            "end\n" +
            "\n" +
            "function on_tick()\n" +
            "    ticks = ticks + 1\n" +
            "    if ticks % 50 ~= 0 then return end\n" +
            "    local p = self.position\n" +
            "    local v = self.velocity\n" +
            "    local speed = math.sqrt(v.x * v.x + v.y * v.y + v.z * v.z)\n" +
            "    print(string.format(\"t=%ds  pos=(%.1f, %.1f, %.1f)  |v|=%.2f  touche P=%s\",\n" +
            "        ticks / 50, p.x, p.y, p.z, speed, tostring(input:is_held(\"P\"))))\n" +
            "end\n" +
            "\n" +
            "function on_sim_stop()\n" +
            "    print(\"capteurs : fin apres \" .. ticks .. \" ticks\")\n" +
            "end\n";

        // --- Execute du Lua, affiche les erreurs avec ligne, retourne le resultat en string ---

        private string RunLua(Script lua, WatchdogDebugger watchdog, string code, string chunkName = null)
        {
            if (watchdog != null)
            {
                watchdog.Reset();
            }
            try
            {
                DynValue v = lua.DoString(code, null, chunkName ?? "chunk");
                if (v == null || v.IsNil()) return null;
                if (v.Type == MoonDataType.String) return v.String;
                return v.ToString();
            }
            catch (WatchdogBudgetExceededException wex)
            {
                ModConsole.Log("[ScriptingMod] WATCHDOG : " + wex.Message);
                return null;
            }
            catch (InterpreterException iex) // couvre SyntaxErrorException + ScriptRuntimeException
            {
                ModConsole.Log("[ScriptingMod] ERREUR Lua : " + (iex.DecoratedMessage ?? iex.Message));
                return null;
            }
            catch (Exception ex)
            {
                ModConsole.Log("[ScriptingMod] ERREUR inattendue : " + ex);
                return null;
            }
        }

        private string RunLua(string code, string chunkName = null)
        {
            return RunLua(m_Script, m_Watchdog, code, chunkName);
        }

        private void PrintToConsole(CallbackArguments args)
        {
            // print(...) — concatene tous les arguments separes par des tabulations
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < args.Count; i++)
            {
                if (i > 0) sb.Append('\t');
                sb.Append(args[i].ToString());
            }
            ModConsole.Log("[lua] " + sb.ToString());
        }

        // --- Commandes console ---

        // --- M2 : runtime actif du Microcontroleur (pour la commande console `lua`) ---
        // Pendant une simulation, `lua "..."` s'execute dans le contexte du
        // microcontroleur (self/machine/input du script en cours). Sinon, retour au
        // POC console (input dispo ; machine resolu a la volee sur le joueur local).

        internal static Script ActiveRuntime { get; private set; }
        internal static WatchdogDebugger ActiveWatchdog { get; private set; }
        internal static string ActiveScriptName { get; private set; }

        internal static void SetActiveRuntime(Script lua, WatchdogDebugger watchdog, string scriptName)
        {
            ActiveRuntime = lua;
            ActiveWatchdog = watchdog;
            ActiveScriptName = scriptName;
            ModConsole.Log("[ScriptingMod] console lua -> contexte microcontroleur (" + (scriptName ?? "?") + ")");
        }

        internal static void ClearActiveRuntime()
        {
            ActiveRuntime = null;
            ActiveWatchdog = null;
            ActiveScriptName = null;
        }

        private void RefreshConsoleMachine()
        {
            // machine du joueur local, re-resolue a chaque appel (le POC n'a pas de
            // contexte bloc) ; nil si aucun joueur/machine
            DynValue machineValue = DynValue.Nil;
            try
            {
                Modding.Common.Player player = Modding.Common.Player.GetLocalPlayer();
                if (player != null && player.Machine != null)
                {
                    machineValue = UserData.Create(new LuaMachineApi(player.Machine));
                }
            }
            catch (Exception)
            {
                // pas de joueur local (menu, multiverse...) — machine laissee a nil
            }
            m_Script.Globals.Set("machine", machineValue);
        }

        private void LuaCommand(string[] cmdArgs)
        {
            if (cmdArgs.Length == 0)
            {
                ModConsole.Log("Usage : lua \"<code lua>\"");
                ModConsole.Log("(en simulation : contexte du microcontroleur — self/machine/input ; sinon POC — machine/input du joueur local)");
                return;
            }
            // la console gere le quoting d'un argument avec espaces : on recolle tout au cas ou
            string code = string.Join(" ", cmdArgs);

            Script target = ActiveRuntime;
            WatchdogDebugger watchdog = ActiveWatchdog;

            if (target != null)
            {
                ModConsole.Log("[lua] (contexte : " + ActiveScriptName + ")");
            }
            else
            {
                target = m_Script;
                watchdog = m_Watchdog;
                RefreshConsoleMachine();
            }

            string result = RunLua(target, watchdog, code, "console");
            if (result != null)
                ModConsole.Log("[ScriptingMod] -> " + result);
        }

        private void BenchCommand(string[] cmdArgs)
        {
            long n = 1000000L;
            if (cmdArgs.Length > 0) long.TryParse(cmdArgs[0], out n);
            if (n < 1) n = 1000000L;

            ModConsole.Log(string.Format("[bench] Lua : {0:N0} iterations + additions...", n));

            // benchmark 1 : arithmetique pure (avec watchdog = hooks actifs — condition reelle)
            m_Watchdog.Reset();
            Stopwatch sw = Stopwatch.StartNew();
            DynValue res = m_Script.DoString(
                "local s = 0 for i = 1, " + n + " do s = s + i end return s", null, "bench");
            sw.Stop();
            double ipsWithDbg = n / Math.Max(0.001, sw.Elapsed.TotalSeconds);

            ModConsole.Log(string.Format(
                "[bench] 1) boucle + adds (watchdog ON) : {0} ms, {1:N0} instr/s approx, res={2}",
                sw.ElapsedMilliseconds, ipsWithDbg, res.Number));

            // extrapolation : cout d'un tick de 2000 instructions a 50 Hz
            double msPerTick = (sw.Elapsed.TotalMilliseconds / Math.Max(1, n)) * 2000.0;
            ModConsole.Log(string.Format(
                "[bench] 2) cout estime d'un tick 50 Hz a 2000 instr : {0:0.000} ms/frame (budget Unity ~ 20 ms)",
                msPerTick));

            ModConsole.Log(string.Format(
                "[bench] 3) watchdog budget actuel : {0:N0} instr/execution", WATCHDOG_BUDGET));
        }

        private void WatchdogCommand(string[] cmdArgs)
        {
            ModConsole.Log("[watchdog] lancement de `while true do end` — si la watchdog fonctionne, ce message est suivi d'une interruption ; sinon le jeu gele.");

            m_Watchdog.Reset();
            try
            {
                m_Script.DoString("while true do end", null, "watchdog-test");
                // On ne doit JAMAIS atteindre cette ligne :
                ModConsole.Log("[watchdog] ECHEC : la boucle infinie est terminee sans interruption ?!");
            }
            catch (WatchdogBudgetExceededException wex)
            {
                ModConsole.Log("[watchdog] SUCCES : " + wex.Message);
                ModConsole.Log("[watchdog] Le jeu continue de tourner. Rejoue la simulation pour continuer a jouer normalement.");
            }
            catch (Exception ex)
            {
                ModConsole.Log("[watchdog] Interruption par un autre type d'exception : " + ex.GetType().FullName + " : " + ex.Message);
            }
        }
    }
}
