// WatchdogDebugger — POC M0 du mod ScriptingMod (Besiege).
//
// Objectif : prouver qu'un script Lua degenere (ex. `while true do end`) peut etre
// interrompu SANS geler le jeu. Mecanisme : l'interface MoonSharp IDebugger est appellee
// PAR LA VM pendant l'execution du bytecode (IsPauseRequested, "called A LOT" — source
// MoonSharp v2.0.0.0) ; on y compte les appels (~ un par saut d'instruction) et on
// depasse le budget -> exception, qui se propage hors de Script.Call (la boucle de la VM
// ne catch que InterpreterException — verifie dans Processor_InstructionLoop.cs:26/249).
//
// ATTENTION perf : un debugger attache active ListenDebugger() a chaque instruction
// (cout a benchmarker — commande `bench`).
// Voir docs/journal-m0.md (etape 8) et docs/etude-faisabilite-scripting.md (D3).

using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Debugging;

namespace ScriptingMod
{
    /// <summary>
    /// Levee par la watchdog quand le budget d'instructions d'une execution est depasse.
    /// Exception "normale" (pas InterpreterException) : elle traverse la VM sans etre
    /// interceptee et remonte a l'appelant de Script.Call.
    /// </summary>
    public class WatchdogBudgetExceededException : Exception
    {
        public long Budget { get; private set; }

        public WatchdogBudgetExceededException(long budget)
            : base("budget d'instructions depasse (" + budget + ") — script interrompu par la watchdog")
        {
            Budget = budget;
        }
    }

    /// <summary>
    /// Compteur d'instructionsMoonSharp via l'interface IDebugger (hooks d'execution VM).
    /// Reset() avant chaque execution pour borner CHAQUE tick/DoString independamment.
    /// </summary>
    public class WatchdogDebugger : IDebugger
    {
        private readonly long m_Budget;
        private long m_Count;

        public WatchdogDebugger(long budgetPerExecution)
        {
            m_Budget = budgetPerExecution;
            m_Count = 0;
        }

        /// <summary>A appeler avant chaque DoString/Call borne par ce budget.</summary>
        public void Reset()
        {
            m_Count = 0;
        }

        public DebuggerCaps GetDebuggerCaps()
        {
            return 0; // aucune capacite de debug requise — on ne fait que compter
        }

        public void SetDebugService(DebugService debugService) { }

        public void SetSourceCode(SourceCode sourceCode) { }

        public void SetByteCode(string[] byteCode) { }

        // --- Le coeur de la watchdog : appele PAR LA VM a chaque instruction ---

        public bool IsPauseRequested()
        {
            if (++m_Count > m_Budget)
                throw new WatchdogBudgetExceededException(m_Budget);
            return false;
        }

        public bool SignalRuntimeException(ScriptRuntimeException ex)
        {
            return false; // pas de pause sur erreur runtime : l'exception remonte normalement
        }

        public DebuggerAction GetAction(int ip, SourceRef sourceref)
        {
            // Jamais appele en pratique (IsPauseRequested ne demande jamais pause),
            // mais l'interface l'exige : on continue l'execution.
            return new DebuggerAction { Action = DebuggerAction.ActionType.Run };
        }

        public void SignalExecutionEnded() { }

        public void Update(WatchType watchType, IEnumerable<WatchItem> items) { }

        public List<DynamicExpression> GetWatchItems()
        {
            return new List<DynamicExpression>();
        }

        public void RefreshBreakpoints(IEnumerable<SourceRef> refs) { }
    }
}
