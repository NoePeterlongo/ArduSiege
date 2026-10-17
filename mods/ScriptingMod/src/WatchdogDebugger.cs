// WatchdogDebugger - ScriptingMod M0 POC (Besiege).
//
// Goal: prove that a runaway Lua script (e.g. `while true do end`) can be
// interrupted WITHOUT freezing the game. Mechanism: the MoonSharp IDebugger
// interface is called BY the VM during bytecode execution (IsPauseRequested,
// "called A LOT" - MoonSharp v2.0.0.0 source); we count the calls (~ one per
// instruction jump) and past the budget -> exception, which propagates out of
// Script.Call (the VM loop only catches InterpreterException - verified in
// Processor_InstructionLoop.cs:26/249).
//
// PERF note: an attached debugger activates ListenDebugger() on every
// instruction (cost to benchmark - `bench` command).
// See docs/journal-m0.md (step 8) and docs/etude-faisabilite-scripting.md (D3).

using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Debugging;

namespace ScriptingMod
{
    /// <summary>
    /// Thrown by the watchdog when an execution's instruction budget is exceeded.
    /// "Normal" exception (not InterpreterException): it crosses the VM without
    /// being caught and propagates to the caller of Script.Call.
    /// </summary>
    public class WatchdogBudgetExceededException : Exception
    {
        public long Budget { get; private set; }

        public WatchdogBudgetExceededException(long budget)
            : base("instruction budget exceeded (" + budget + ") - script interrupted by the watchdog")
        {
            Budget = budget;
        }
    }

    /// <summary>
    /// MoonSharp instruction counter through the IDebugger interface (VM execution
    /// hooks). Call Reset() before each execution to bound EACH tick/DoString
    /// independently.
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

        /// <summary>Call before each DoString/Call bounded by this budget.</summary>
        public void Reset()
        {
            m_Count = 0;
        }

        public DebuggerCaps GetDebuggerCaps()
        {
            return 0; // no debug capability required - we only count
        }

        public void SetDebugService(DebugService debugService) { }

        public void SetSourceCode(SourceCode sourceCode) { }

        public void SetByteCode(string[] byteCode) { }

        // --- The watchdog's core: called BY THE VM on every instruction ---

        public bool IsPauseRequested()
        {
            if (++m_Count > m_Budget)
                throw new WatchdogBudgetExceededException(m_Budget);
            return false;
        }

        public bool SignalRuntimeException(ScriptRuntimeException ex)
        {
            return false; // no pause on runtime errors: the exception propagates normally
        }

        public DebuggerAction GetAction(int ip, SourceRef sourceref)
        {
            // Never called in practice (IsPauseRequested never requests a pause),
            // but the interface requires it: keep executing.
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
