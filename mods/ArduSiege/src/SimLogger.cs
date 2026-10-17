// SimLogger - per-simulation log files (logs-simu branch).
//
// Scripts need to export data for offline analysis - instrumenting a control
// loop without it is guesswork. FORMAT-AGNOSTIC (player decision): the file
// contains whatever the script writes - raw lines (log.write), values joined by
// a configurable separator (log.row), the extension is the script's choice
// (log.ext, default "txt"). The Lua sandbox has no io: all file access goes
// through C#/ModIO (TextWriter is whitelisted, StreamWriter is NOT: buffer in
// memory + WriteAllText flushes).
//
// Model (player decision): ONE log file per simulation at most.
//   - the file is created lazily on the FIRST write (an idle sim logs nothing) ;
//   - auto-named Logs/<script>_<timestamp>.<ext> in the mod's data folder ;
//   - flushed every ~200 lines and at simulation stop (survives a kill) ;
//   - 2 MB cap: past it, writes are refused with a console message (the watchdog
//     bounds CPU; this bounds memory).
//
// NB numbers are formatted with the INVARIANT culture: on a French locale the
// default ToString gives "0,5" - a silent CSV killer (cf. AGENTS.md rule 10).

using System;
using System.Globalization;
using System.Text;
using Modding;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;

namespace ArduSiege
{
    internal class SimLogger
    {
        private const string LOGS_DIR = "Logs";
        private const int FLUSH_EVERY_LINES = 200;
        private const long MAX_BYTES = 2 * 1024 * 1024;     // 2 MB cap

        private readonly string m_ScriptName;
        private readonly System.Text.StringBuilder m_Buffer = new System.Text.StringBuilder();
        private string m_Path;              // set on first write (lazy)
        private string m_Separator = ",";
        private string m_Ext = "txt";       // format-agnostic extension
        private int m_Unflushed;
        private bool m_Full;
        private bool m_Closed;

        internal SimLogger(string scriptName)
        {
            m_ScriptName = scriptName;
        }

        // --- Lua surface ---

        /// <summary>log.separator - column separator for log.row (default ",").</summary>
        internal string Separator
        {
            get { return m_Separator; }
            set { if (!string.IsNullOrEmpty(value)) m_Separator = value; }
        }

        /// <summary>log.path - file path (relative to the data folder), nil until first write.</summary>
        internal string Path
        {
            get { return m_Path; }
        }

        /// <summary>log.ext - file extension, the script's choice (before first write).</summary>
        internal string Ext
        {
            get { return m_Ext; }
            set
            {
                if (m_Path != null || m_Closed)
                {
                    return;     // file already created: too late
                }
                string e = value == null ? "" : value.Trim().TrimStart('.').ToLower();
                foreach (char c in e)
                {
                    if (!char.IsLetterOrDigit(c))
                    {
                        return;     // reject anything exotic silently
                    }
                }
                m_Ext = (e.Length == 0) ? "txt" : e;
            }
        }

        /// <summary>log.write(line) - appends one raw line.</summary>
        internal void Write(string line)
        {
            if (m_Closed || m_Full)
            {
                return;
            }
            EnsureFile();
            m_Buffer.Append(line).Append('\n');
            m_Unflushed++;
            ConsiderFlush();
        }

        /// <summary>log.row(v1, v2, ...) - appends one separator-joined line.</summary>
        internal void Row(CallbackArguments args)
        {
            if (m_Closed || m_Full)
            {
                return;
            }
            EnsureFile();
            for (int i = 0; i < args.Count; i++)
            {
                if (i > 0)
                {
                    m_Buffer.Append(m_Separator);
                }
                m_Buffer.Append(FormatValue(args[i]));
            }
            m_Buffer.Append('\n');
            m_Unflushed++;
            ConsiderFlush();
        }

        /// <summary>Flushes the buffer to disk (also called automatically).</summary>
        internal void Flush()
        {
            if (m_Path == null || m_Buffer.Length == 0)
            {
                return;
            }
            try
            {
                Modding.ModIO.WriteAllText(m_Path, m_Buffer.ToString(), data: true);
                m_Unflushed = 0;
            }
            catch (Exception ex)
            {
                ModConsole.Log("[ArduSiege] log flush failed: " + ex.Message);
            }
        }

        /// <summary>Final flush + console summary (simulation stop).</summary>
        internal void Close()
        {
            if (m_Closed)
            {
                return;
            }
            m_Closed = true;
            if (m_Path == null)
            {
                return;     // nothing was ever logged
            }
            Flush();
            int lines = CountLines();
            ModConsole.Log("[ArduSiege] log written: " + m_Path + " ("
                + lines + " lines, " + (m_Buffer.Length / 1024) + " KiB) - 'script logs' opens the folder");
        }

        // --- internals ---

        private void EnsureFile()
        {
            if (m_Path != null)
            {
                return;
            }
            try
            {
                Modding.ModIO.CreateDirectory(LOGS_DIR, data: true);
                string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
                m_Path = LOGS_DIR + "/" + m_ScriptName + "_" + stamp + "." + m_Ext;
                ModConsole.Log("[ArduSiege] log opened: " + m_Path);
            }
            catch (Exception ex)
            {
                m_Full = true;  // no path -> writes are refused
                ModConsole.Log("[ArduSiege] log cannot be created: " + ex.Message);
            }
        }

        private void ConsiderFlush()
        {
            if (m_Buffer.Length >= MAX_BYTES)
            {
                m_Full = true;
                Flush();
                ModConsole.Log("[ArduSiege] log full (" + (MAX_BYTES / 1024 / 1024)
                    + " MB cap) - further writes refused this run");
                return;
            }
            if (m_Unflushed >= FLUSH_EVERY_LINES)
            {
                Flush();
            }
        }

        private int CountLines()
        {
            int n = 0;
            for (int i = 0; i < m_Buffer.Length; i++)
            {
                if (m_Buffer[i] == '\n')
                {
                    n++;
                }
            }
            return n;
        }

        /// <summary>Invariant-culture formatting (French locale: 0.5 -> "0,5" otherwise).</summary>
        private static string FormatValue(DynValue v)
        {
            switch (v.Type)
            {
                case MoonSharp.Interpreter.DataType.Number:
                    return v.Number.ToString(CultureInfo.InvariantCulture);
                case MoonSharp.Interpreter.DataType.Boolean:
                    return v.Boolean ? "true" : "false";
                case MoonSharp.Interpreter.DataType.String:
                    return v.String;
                case MoonSharp.Interpreter.DataType.Nil:
                    return "";
                default:
                    return v.ToString();
            }
        }
    }

    /// <summary>
    /// Lua wrapper of the per-simulation logger: log.write(line), log.row(...),
    /// log.path, log.separator. The file is created on the first write and closed
    /// automatically at simulation stop.
    /// </summary>
    internal class LuaLogApi : IUserDataType
    {
        private readonly SimLogger m_Logger;

        internal LuaLogApi(SimLogger logger)
        {
            m_Logger = logger;
        }

        public DynValue Index(Script script, DynValue index, bool isDirectIndexing)
        {
            if (index.Type != MoonSharp.Interpreter.DataType.String)
            {
                return null;
            }
            switch (index.String)
            {
                case "write":
                    return DynValue.NewCallback((ctx, args) => WriteLine(script, args), "write");
                case "row":
                    return DynValue.NewCallback((ctx, args) => WriteRow(script, args), "row");
                case "path":
                    return m_Logger.Path == null ? DynValue.Nil : DynValue.NewString(m_Logger.Path);
                case "separator":
                    return DynValue.NewString(m_Logger.Separator);
                case "ext":
                    return DynValue.NewString(m_Logger.Ext);
                default:
                    return null;
            }
        }

        private DynValue WriteLine(Script script, CallbackArguments args)
        {
            if (args.Count < 1 || args[0].Type != MoonSharp.Interpreter.DataType.String)
            {
                throw new ScriptRuntimeException("log.write(line): one string required, e.g. log.write('t=1')");
            }
            m_Logger.Write(args[0].String);
            return DynValue.Nil;
        }

        private DynValue WriteRow(Script script, CallbackArguments args)
        {
            m_Logger.Row(args);
            return DynValue.Nil;
        }

        public bool SetIndex(Script script, DynValue index, DynValue value, bool isDirectIndexing)
        {
            if (index.Type != MoonSharp.Interpreter.DataType.String
                || value.Type != MoonSharp.Interpreter.DataType.String)
            {
                return false;
            }
            switch (index.String)
            {
                case "separator":
                    m_Logger.Separator = value.String;
                    return true;
                case "ext":
                    m_Logger.Ext = value.String;   // ignored once the file exists
                    return true;
                default:
                    return false;
            }
        }

        public DynValue MetaIndex(Script script, string metaname)
        {
            return null;
        }
    }
}
