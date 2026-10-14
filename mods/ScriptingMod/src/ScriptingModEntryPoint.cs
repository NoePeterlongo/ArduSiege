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
            ModConsole.Log("[ScriptingMod] M0 POC : chargement...");

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

            ModConsole.Log("[ScriptingMod] M0 pret. Commandes : lua \"<code>\", bench, watchdog");
        }

        // --- Execute du Lua, affiche les erreurs avec ligne, retourne le resultat en string ---

        private string RunLua(string code, string chunkName = null)
        {
            m_Watchdog.Reset();
            try
            {
                DynValue v = m_Script.DoString(code, null, chunkName ?? "chunk");
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

        private void LuaCommand(string[] cmdArgs)
        {
            if (cmdArgs.Length == 0)
            {
                ModConsole.Log("Usage : lua \"<code lua>\"");
                return;
            }
            // la console gere le quoting d'un argument avec espaces : on recolle tout au cas ou
            string code = string.Join(" ", cmdArgs);
            string result = RunLua(code, "console");
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
                ModConsole.Log("[watchdog] Interruption par un autre type d'exception : " + ex.GetType().Name + " : " + ex.Message);
            }
        }
    }
}
