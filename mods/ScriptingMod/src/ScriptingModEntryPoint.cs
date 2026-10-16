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
using System.Collections.Generic;
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

            // M4 : bibliotheque + sources embarquees (D1-ter : nom seulement)
            ModConsole.RegisterCommand("script", ScriptCommand,
                "script list|show|reload [nom]|folder — bibliotheque de scripts");

            // M4 : rafraichir le menu des Microcontroleurs a l'ouverture du mapper
            // (nouveaux scripts visibles sans relancer le niveau).
            BlockMapper.onMapperOpen += OnMapperOpen;

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
                WriteDemoIfOutdated("capteurs.lua", 3, CAPTEURS_LUA);
                WriteDemoIfOutdated("actionneurs.lua", 2, ACTIONNEURS_LUA);
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

        // Demo M2 : capteurs + cinematique derivee (SMDEMO v3 : omega/accel/nivelement)
        private const string CAPTEURS_LUA =
            "-- capteurs.lua (SMDEMO v3) : capteurs + cinematique derivee\n" +
            "-- expose : self (bloc, position/vitesse/rotation/quaternion/directeurs),\n" +
            "--           machine (blocs), input (clavier REEL)\n" +
            "-- NB : les demos SMDEMO sont mises a jour par le mod ; copiez ce fichier\n" +
            "--      sous un autre nom pour le personnaliser sans etre ecrase.\n" +
            "local ticks = 0\n" +
            "local last_v = {x = 0, y = 0, z = 0}\n" +
            "local accel_s = 0.0\n" +
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
            "    -- acceleration derivee : dv/dt avec dt = 1/50 s (tick fixe 50 Hz)\n" +
            "    local v = self.velocity\n" +
            "    local ax = (v.x - last_v.x) * 50\n" +
            "    local ay = (v.y - last_v.y) * 50\n" +
            "    local az = (v.z - last_v.z) * 50\n" +
            "    last_v = v\n" +
            "    local a = math.sqrt(ax * ax + ay * ay + az * az)\n" +
            "    accel_s = accel_s * 0.95 + a * 0.05    -- lissage exponentiel\n" +
            "\n" +
            "    if ticks % 50 ~= 0 then return end\n" +
            "    local p = self.position\n" +
            "    local speed = math.sqrt(v.x * v.x + v.y * v.y + v.z * v.z)\n" +
            "    local w = self.angular_velocity\n" +
            "    local omega = math.sqrt(w.x * w.x + w.y * w.y + w.z * w.z) * 180 / math.pi\n" +
            "    local up = self.up\n" +
            "    local plat = up.y > 0.95    -- axe up du bloc aligne avec le haut monde ?\n" +
            "    print(string.format(\"t=%ds  pos=(%.1f, %.1f, %.1f)  |v|=%.2f  |a|=%.2f  omega=%.1f deg/s  plat=%s  P=%s\",\n" +
            "        ticks / 50, p.x, p.y, p.z, speed, accel_s, omega, tostring(plat), tostring(input:is_held(\"P\"))))\n" +
            "end\n" +
            "\n" +
            "function on_sim_stop()\n" +
            "    print(\"capteurs : fin apres \" .. ticks .. \" ticks\")\n" +
            "end\n";

        // Demo M3 : actionneurs — emulate(canal) + set_slider (SMDEMO v2)
        private const string ACTIONNEURS_LUA =
            "-- actionneurs.lua (SMDEMO v2) : piloter les blocs vanilla depuis le script\n" +
            "-- PREPARATION : activer l'option Advanced Building dans les reglages du jeu,\n" +
            "-- puis dans le mapper du moteur, basculez la ligne de touche en champ texte\n" +
            "-- (bouton apparu grace a Advanced Building) et tapez le MESSAGE \"pousser\".\n" +
            "local ticks = 0\n" +
            "local motor = nil\n" +
            "\n" +
            "function on_sim_start()\n" +
            "    print(\"actionneurs : liez un moteur au message 'pousser' (mapper), puis tenez P\")\n" +
            "    local moteurs = machine:blocks(\"Cog\")\n" +
            "    if #moteurs > 0 then\n" +
            "        motor = moteurs[1]\n" +
            "        print(\"  moteur cible : \" .. motor.type)\n" +
            "        for k, d in pairs(motor.sliders) do\n" +
            "            print(\"  slider : \" .. k .. \" (\" .. d .. \")\")\n" +
            "        end\n" +
            "    end\n" +
            "end\n" +
            "\n" +
            "function on_tick()\n" +
            "    emulate(\"pousser\", input:is_held(\"P\"))\n" +
            "    ticks = ticks + 1\n" +
            "    if motor == nil or ticks % 100 ~= 0 then return end\n" +
            "    -- module la vitesse du moteur toutes les 2 s (demo set_slider analogique)\n" +
            "    local v = motor:slider(\"speed\")\n" +
            "    local cible = (v < 1.5) and 2.5 or 0.5\n" +
            "    motor:set_slider(\"speed\", cible)\n" +
            "    print(string.format(\"  slider speed : %.1f -> %.1f\", v, cible))\n" +
            "end\n" +
            "\n" +
            "function on_sim_stop()\n" +
            "    print(\"actionneurs : fin apres \" .. ticks .. \" ticks\")\n" +
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
            // machine locale, re-resolue a chaque appel (le POC n'a pas de contexte
            // bloc) ; nil si aucune machine (menu, multiverse...)
            // NB M4 : GetLocalPlayer() est null en SOLO (PlayerData.localPlayer n'est
            // assigne qu'en MP) -> GetLocalMachine() couvre aussi le cas SP.
            DynValue machineValue = DynValue.Nil;
            try
            {
                Modding.Blocks.PlayerMachine machine = GetLocalMachine();
                if (machine != null)
                {
                    machineValue = UserData.Create(new LuaMachineApi(machine));
                }
            }
            catch (Exception)
            {
                // machine laissee a nil
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

        // --- M4 : commandes `script` (bibliotheque + sources embarquees, D1-bis) ---

        private void ScriptCommand(string[] cmdArgs)
        {
            if (cmdArgs.Length == 0)
            {
                ModConsole.Log("Usage : script list | show | reload [nom] | folder");
                return;
            }
            string arg = cmdArgs.Length > 1 ? cmdArgs[1] : null;
            switch (cmdArgs[0])
            {
                case "list":
                    CmdScriptList();
                    return;
                case "show":
                    CmdScriptShow();
                    return;
                case "reload":
                    CmdScriptReload(arg);
                    return;
                case "folder":
                    Modding.ModIO.OpenFolderInFileBrowser(SCRIPTS_DIR, data: true);
                    ModConsole.Log("[script] dossier de la bibliotheque ouvert dans l'explorateur");
                    return;
                default:
                    ModConsole.Log("[script] sous-commande inconnue : " + cmdArgs[0]
                        + " (list | show | reload | folder)");
                    return;
            }
        }

        /// <summary>
        /// M4 : a l'ouverture d'un mapper — si c'est un Microcontroller, rafraichir son
        /// menu depuis la bibliotheque puis reconstruire les widgets (Rebuild, PAS
        /// Refresh : Refresh rappelle Open() et donc onMapperOpen = boucle infinie).
        /// </summary>
        private void OnMapperOpen()
        {
            try
            {
                BlockMapper mapper = BlockMapper.CurrentInstance;
                if (mapper == null || mapper.Block == null)
                {
                    return;
                }
                MicrocontrollerScript mc = mapper.Block.GetComponent<MicrocontrollerScript>();
                if (mc == null)
                {
                    return;
                }
                mc.RefreshMenu();
                mapper.Rebuild();
            }
            catch (Exception)
            {
                // un mapper d'un autre type d'objet : rien a faire
            }
        }

        private void CmdScriptList()
        {
            try
            {
                string[] files = Modding.ModIO.GetFiles(SCRIPTS_DIR, "*.lua", data: true);
                ModConsole.Log("[script] bibliotheque : " + files.Length + " script(s)");
                foreach (string file in files)
                {
                    ModConsole.Log("  - " + System.IO.Path.GetFileNameWithoutExtension(file));
                }
            }
            catch (Exception ex)
            {
                ModConsole.Log("[script] list impossible : " + ex.Message);
            }
        }

        private void CmdScriptShow()
        {
            List<MicrocontrollerScript> controllers = GetControllers();
            if (m_ScanInfo != null)
            {
                ModConsole.Log("[script] scan : " + m_ScanInfo);
            }
            if (controllers.Count == 0)
            {
                ModConsole.Log("[script] aucun bloc Microcontroller trouve (placez-en un ou chargez une machine qui en contient)");
                return;
            }
            int i = 1;
            foreach (MicrocontrollerScript mc in controllers)
            {
                string name = mc.MenuSelection ?? "(aucun)";
                string etat = mc.HasScriptFile ? "present" : "N'EXISTE PLUS (marque * au mapper)";
                string actif = mc.IsRuntimeActive ? " ; EN SIMULATION" : string.Empty;
                ModConsole.Log("[script] controleur " + i++ + " : \"" + name + "\" — "
                    + etat + actif);
            }
        }

        private void CmdScriptReload(string name)
        {
            List<MicrocontrollerScript> controllers = GetControllers();
            if (m_ScanInfo != null)
            {
                ModConsole.Log("[script] scan : " + m_ScanInfo);
            }
            if (controllers.Count == 0)
            {
                ModConsole.Log("[script] reload : aucun bloc Microcontroller trouve");
                return;
            }
            int n = 0;
            foreach (MicrocontrollerScript mc in controllers)
            {
                string target = mc.MenuSelection;
                if (name != null && target != name)
                {
                    continue;
                }
                if (mc.ReloadFromLibrary())
                {
                    n++;
                }
            }
            ModConsole.Log("[script] reload : " + n + " controleur(s) recharge(s)");
        }

        // trace du dernier scan (diagnostic script show/reload)
        private string m_ScanInfo;

        /// <summary>
        /// Microcontroleurs de la machine locale. Scan la liste ACTIVE (sim ou
        /// construction) puis l'autre en secours, dedupliquise par GUID ; m_ScanInfo
        /// decrit chaque etape (diagnostic : joueur, machine, blocs, composants).
        /// </summary>
        private List<MicrocontrollerScript> GetControllers()
        {
            m_ScanInfo = null;
            List<MicrocontrollerScript> result = new List<MicrocontrollerScript>();
            try
            {
                // SP : PlayerData.localPlayer n'est assigne qu'en MP (ChatController)
                // -> GetLocalPlayer() retourne null en solo. Le point d'acces solo est
                // Machine.Active() (MachineObjectTracker.activeMachine).
                Modding.Blocks.PlayerMachine machine = GetLocalMachine();
                if (machine == null)
                {
                    m_ScanInfo = "aucune machine locale (hors niveau ?)";
                    return result;
                }

                bool sim = Modding.Game.IsSimulating;
                System.Collections.ObjectModel.ReadOnlyCollection<Modding.Blocks.Block> blocks =
                    sim ? machine.SimulationBlocks : machine.BuildingBlocks;
                int scanned = ScanControllers(blocks, result);

                // secours : la liste inactive peut rester la seule valable selon le mode
                System.Collections.ObjectModel.ReadOnlyCollection<Modding.Blocks.Block> other =
                    sim ? machine.BuildingBlocks : machine.SimulationBlocks;
                int scannedOther = result.Count == 0 ? ScanControllers(other, result) : 0;

                m_ScanInfo = "joueur OK, machine OK ; "
                    + scanned + " bloc(s) en " + (sim ? "simulation" : "construction")
                    + (scannedOther > 0 ? " + " + scannedOther + " dans l'autre liste" : string.Empty)
                    + " ; " + result.Count + " microcontroleur(s)";
            }
            catch (Exception ex)
            {
                m_ScanInfo = "exception : " + ex.GetType().FullName + " : " + ex.Message;
            }
            return result;
        }

        /// <summary>
        /// Machine locale : MP -> joueur local ; SP -> Machine.Active().
        /// </summary>
        internal static Modding.Blocks.PlayerMachine GetLocalMachine()
        {
            try
            {
                Modding.Common.Player player = Modding.Common.Player.GetLocalPlayer();
                if (player != null && player.Machine != null)
                {
                    return player.Machine;
                }
            }
            catch (Exception)
            {
                // hors MP : Playerlist/PlayerData non initialises
            }
            try
            {
                Machine active = Machine.Active();
                if (active != null)
                {
                    return Modding.Blocks.PlayerMachine.From(active);
                }
            }
            catch (Exception)
            {
                // pas de machine active (menu)
            }
            return null;
        }

        /// <summary>Collecte les MicrocontrollerScript d'une collection de blocs (dedup par GUID).</summary>
        private static int ScanControllers(
            System.Collections.ObjectModel.ReadOnlyCollection<Modding.Blocks.Block> blocks,
            List<MicrocontrollerScript> result)
        {
            if (blocks == null)
            {
                return 0;
            }
            HashSet<System.Guid> seen = new HashSet<System.Guid>();
            int scanned = 0;
            foreach (Modding.Blocks.Block block in blocks)
            {
                scanned++;
                BlockBehaviour bb = block.InternalObject;
                MicrocontrollerScript mc = bb == null ? null : bb.GetComponent<MicrocontrollerScript>();
                if (mc != null)
                {
                    System.Guid guid;
                    try
                    {
                        guid = block.Guid;
                    }
                    catch (Exception)
                    {
                        guid = System.Guid.Empty;
                    }
                    if (seen.Add(guid))
                    {
                        result.Add(mc);
                    }
                }
            }
            return scanned;
        }
    }
}
