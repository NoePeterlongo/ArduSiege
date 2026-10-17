// FICHIER GENERE par tools/moonsharp-gen (GenModuleRegister) — NE PAS EDITER A LA MAIN.
// Source : reflexion hors-ligne sur la DLL MoonSharp v2.0.0.0 complete (tag
// 3154416e). Regenerer via : bash tools/moonsharp-build.sh --regen
// Ce fichier remplace l'enregistrement reflexif des modules (interdit par
// l'AssemblyScanner de Besiege). Voir docs/journal-m0.md, etape 4.

using System;

namespace MoonSharp.Interpreter
{
	internal static class HardwiredModuleRegister
	{
		internal static string GetNamespace(Type t)
		{
			if (t == typeof(MoonSharp.Interpreter.CoreLib.BasicModule)) return null;
			if (t == typeof(MoonSharp.Interpreter.CoreLib.Bit32Module)) return "bit32";
			if (t == typeof(MoonSharp.Interpreter.CoreLib.CoroutineModule)) return "coroutine";
			if (t == typeof(MoonSharp.Interpreter.CoreLib.DebugModule)) return "debug";
			if (t == typeof(MoonSharp.Interpreter.CoreLib.DynamicModule)) return "dynamic";
			if (t == typeof(MoonSharp.Interpreter.CoreLib.ErrorHandlingModule)) return null;
			if (t == typeof(MoonSharp.Interpreter.CoreLib.JsonModule)) return "json";
			if (t == typeof(MoonSharp.Interpreter.CoreLib.MathModule)) return "math";
			if (t == typeof(MoonSharp.Interpreter.CoreLib.MetaTableModule)) return null;
			if (t == typeof(MoonSharp.Interpreter.CoreLib.OsSystemModule)) return "os";
			if (t == typeof(MoonSharp.Interpreter.CoreLib.OsTimeModule)) return "os";
			if (t == typeof(MoonSharp.Interpreter.CoreLib.StringModule)) return "string";
			if (t == typeof(MoonSharp.Interpreter.CoreLib.TableIteratorsModule)) return null;
			if (t == typeof(MoonSharp.Interpreter.CoreLib.TableModule)) return "table";
			if (t == typeof(MoonSharp.Interpreter.CoreLib.TableModule_Globals)) return null;
			return null;
		}

		internal static void RegisterMembers(Table gtable, Table ns, Type t)
		{
			if (t == typeof(MoonSharp.Interpreter.CoreLib.BasicModule)) { Register_BasicModule(gtable, ns); return; }
			if (t == typeof(MoonSharp.Interpreter.CoreLib.Bit32Module)) { Register_Bit32Module(gtable, ns); return; }
			if (t == typeof(MoonSharp.Interpreter.CoreLib.CoroutineModule)) { Register_CoroutineModule(gtable, ns); return; }
			if (t == typeof(MoonSharp.Interpreter.CoreLib.DebugModule)) { Register_DebugModule(gtable, ns); return; }
			if (t == typeof(MoonSharp.Interpreter.CoreLib.DynamicModule)) { Register_DynamicModule(gtable, ns); return; }
			if (t == typeof(MoonSharp.Interpreter.CoreLib.ErrorHandlingModule)) { Register_ErrorHandlingModule(gtable, ns); return; }
			if (t == typeof(MoonSharp.Interpreter.CoreLib.JsonModule)) { Register_JsonModule(gtable, ns); return; }
			if (t == typeof(MoonSharp.Interpreter.CoreLib.MathModule)) { Register_MathModule(gtable, ns); return; }
			if (t == typeof(MoonSharp.Interpreter.CoreLib.MetaTableModule)) { Register_MetaTableModule(gtable, ns); return; }
			if (t == typeof(MoonSharp.Interpreter.CoreLib.OsSystemModule)) { Register_OsSystemModule(gtable, ns); return; }
			if (t == typeof(MoonSharp.Interpreter.CoreLib.OsTimeModule)) { Register_OsTimeModule(gtable, ns); return; }
			if (t == typeof(MoonSharp.Interpreter.CoreLib.StringModule)) { Register_StringModule(gtable, ns); return; }
			if (t == typeof(MoonSharp.Interpreter.CoreLib.TableIteratorsModule)) { Register_TableIteratorsModule(gtable, ns); return; }
			if (t == typeof(MoonSharp.Interpreter.CoreLib.TableModule)) { Register_TableModule(gtable, ns); return; }
			if (t == typeof(MoonSharp.Interpreter.CoreLib.TableModule_Globals)) { Register_TableModule_Globals(gtable, ns); return; }
			throw new NotSupportedException("[ArduSiege] Module non supporte dans ce build MoonSharp elague : " + (t == null ? "<null>" : t.FullName));
		}

		private static void Register_BasicModule(Table gtable, Table ns)
		{
			ns.Set("assert", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.BasicModule.assert, "assert"));
			ns.Set("collectgarbage", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.BasicModule.collectgarbage, "collectgarbage"));
			ns.Set("error", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.BasicModule.error, "error"));
			ns.Set("print", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.BasicModule.print, "print"));
			ns.Set("select", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.BasicModule.select, "select"));
			ns.Set("tonumber", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.BasicModule.tonumber, "tonumber"));
			ns.Set("tostring", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.BasicModule.tostring, "tostring"));
			ns.Set("type", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.BasicModule.type, "type"));
		}

		private static void Register_Bit32Module(Table gtable, Table ns)
		{
			ns.Set("arshift", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.Bit32Module.arshift, "arshift"));
			ns.Set("band", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.Bit32Module.band, "band"));
			ns.Set("bnot", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.Bit32Module.bnot, "bnot"));
			ns.Set("bor", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.Bit32Module.bor, "bor"));
			ns.Set("btest", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.Bit32Module.btest, "btest"));
			ns.Set("bxor", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.Bit32Module.bxor, "bxor"));
			ns.Set("extract", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.Bit32Module.extract, "extract"));
			ns.Set("lrotate", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.Bit32Module.lrotate, "lrotate"));
			ns.Set("lshift", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.Bit32Module.lshift, "lshift"));
			ns.Set("replace", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.Bit32Module.replace, "replace"));
			ns.Set("rrotate", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.Bit32Module.rrotate, "rrotate"));
			ns.Set("rshift", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.Bit32Module.rshift, "rshift"));
		}

		private static void Register_CoroutineModule(Table gtable, Table ns)
		{
			ns.Set("create", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.CoroutineModule.create, "create"));
			ns.Set("resume", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.CoroutineModule.resume, "resume"));
			ns.Set("running", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.CoroutineModule.running, "running"));
			ns.Set("status", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.CoroutineModule.status, "status"));
			ns.Set("wrap", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.CoroutineModule.wrap, "wrap"));
			ns.Set("yield", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.CoroutineModule.yield, "yield"));
		}

		private static void Register_DebugModule(Table gtable, Table ns)
		{
			ns.Set("debug", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.DebugModule.debug, "debug"));
			ns.Set("getmetatable", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.DebugModule.getmetatable, "getmetatable"));
			ns.Set("getregistry", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.DebugModule.getregistry, "getregistry"));
			ns.Set("getupvalue", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.DebugModule.getupvalue, "getupvalue"));
			ns.Set("getuservalue", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.DebugModule.getuservalue, "getuservalue"));
			ns.Set("setmetatable", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.DebugModule.setmetatable, "setmetatable"));
			ns.Set("setupvalue", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.DebugModule.setupvalue, "setupvalue"));
			ns.Set("setuservalue", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.DebugModule.setuservalue, "setuservalue"));
			ns.Set("traceback", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.DebugModule.traceback, "traceback"));
			ns.Set("upvalueid", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.DebugModule.upvalueid, "upvalueid"));
			ns.Set("upvaluejoin", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.DebugModule.upvaluejoin, "upvaluejoin"));
		}

		private static void Register_DynamicModule(Table gtable, Table ns)
		{
			MoonSharp.Interpreter.CoreLib.DynamicModule.MoonSharpInit(gtable, ns);
			ns.Set("eval", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.DynamicModule.eval, "eval"));
			ns.Set("prepare", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.DynamicModule.prepare, "prepare"));
		}

		private static void Register_ErrorHandlingModule(Table gtable, Table ns)
		{
			ns.Set("pcall", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.ErrorHandlingModule.pcall, "pcall"));
			ns.Set("xpcall", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.ErrorHandlingModule.xpcall, "xpcall"));
		}

		private static void Register_JsonModule(Table gtable, Table ns)
		{
			ns.Set("isnull", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.JsonModule.isnull, "isnull"));
			ns.Set("null", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.JsonModule.@null, "null"));
			ns.Set("parse", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.JsonModule.parse, "parse"));
			ns.Set("serialize", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.JsonModule.serialize, "serialize"));
		}

		private static void Register_MathModule(Table gtable, Table ns)
		{
			MoonSharp.Interpreter.CoreLib.MathModule.MoonSharpInit(gtable, ns);
			ns.Set("abs", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.abs, "abs"));
			ns.Set("acos", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.acos, "acos"));
			ns.Set("asin", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.asin, "asin"));
			ns.Set("atan", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.atan, "atan"));
			ns.Set("atan2", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.atan2, "atan2"));
			ns.Set("ceil", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.ceil, "ceil"));
			ns.Set("cos", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.cos, "cos"));
			ns.Set("cosh", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.cosh, "cosh"));
			ns.Set("deg", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.deg, "deg"));
			ns.Set("exp", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.exp, "exp"));
			ns.Set("floor", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.floor, "floor"));
			ns.Set("fmod", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.fmod, "fmod"));
			ns.Set("frexp", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.frexp, "frexp"));
			ns.Set("ldexp", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.ldexp, "ldexp"));
			ns.Set("log", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.log, "log"));
			ns.Set("max", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.max, "max"));
			ns.Set("min", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.min, "min"));
			ns.Set("modf", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.modf, "modf"));
			ns.Set("pow", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.pow, "pow"));
			ns.Set("rad", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.rad, "rad"));
			ns.Set("random", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.random, "random"));
			ns.Set("randomseed", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.randomseed, "randomseed"));
			ns.Set("sin", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.sin, "sin"));
			ns.Set("sinh", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.sinh, "sinh"));
			ns.Set("sqrt", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.sqrt, "sqrt"));
			ns.Set("tan", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.tan, "tan"));
			ns.Set("tanh", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MathModule.tanh, "tanh"));
			ns.Set("huge", DynValue.NewNumber(double.Parse("1.7976931348623157E+308", System.Globalization.CultureInfo.InvariantCulture)));
			ns.Set("pi", DynValue.NewNumber(double.Parse("3.1415926535897931", System.Globalization.CultureInfo.InvariantCulture)));
		}

		private static void Register_MetaTableModule(Table gtable, Table ns)
		{
			ns.Set("getmetatable", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MetaTableModule.getmetatable, "getmetatable"));
			ns.Set("rawequal", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MetaTableModule.rawequal, "rawequal"));
			ns.Set("rawget", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MetaTableModule.rawget, "rawget"));
			ns.Set("rawlen", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MetaTableModule.rawlen, "rawlen"));
			ns.Set("rawset", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MetaTableModule.rawset, "rawset"));
			ns.Set("setmetatable", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.MetaTableModule.setmetatable, "setmetatable"));
		}

		private static void Register_OsSystemModule(Table gtable, Table ns)
		{
			ns.Set("execute", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.OsSystemModule.execute, "execute"));
			ns.Set("exit", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.OsSystemModule.exit, "exit"));
			ns.Set("getenv", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.OsSystemModule.getenv, "getenv"));
			ns.Set("remove", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.OsSystemModule.remove, "remove"));
			ns.Set("rename", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.OsSystemModule.rename, "rename"));
			ns.Set("setlocale", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.OsSystemModule.setlocale, "setlocale"));
			ns.Set("tmpname", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.OsSystemModule.tmpname, "tmpname"));
		}

		private static void Register_OsTimeModule(Table gtable, Table ns)
		{
			ns.Set("clock", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.OsTimeModule.clock, "clock"));
			ns.Set("date", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.OsTimeModule.date, "date"));
			ns.Set("difftime", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.OsTimeModule.difftime, "difftime"));
			ns.Set("time", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.OsTimeModule.time, "time"));
		}

		private static void Register_StringModule(Table gtable, Table ns)
		{
			MoonSharp.Interpreter.CoreLib.StringModule.MoonSharpInit(gtable, ns);
			ns.Set("byte", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.StringModule.@byte, "byte"));
			ns.Set("char", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.StringModule.@char, "char"));
			ns.Set("contains", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.StringModule.contains, "contains"));
			ns.Set("dump", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.StringModule.dump, "dump"));
			ns.Set("endsWith", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.StringModule.endsWith, "endsWith"));
			ns.Set("find", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.StringModule.find, "find"));
			ns.Set("format", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.StringModule.format, "format"));
			ns.Set("gmatch", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.StringModule.gmatch, "gmatch"));
			ns.Set("gsub", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.StringModule.gsub, "gsub"));
			ns.Set("len", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.StringModule.len, "len"));
			ns.Set("lower", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.StringModule.lower, "lower"));
			ns.Set("match", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.StringModule.match, "match"));
			ns.Set("rep", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.StringModule.rep, "rep"));
			ns.Set("reverse", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.StringModule.reverse, "reverse"));
			ns.Set("startsWith", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.StringModule.startsWith, "startsWith"));
			ns.Set("sub", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.StringModule.sub, "sub"));
			ns.Set("unicode", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.StringModule.unicode, "unicode"));
			ns.Set("upper", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.StringModule.upper, "upper"));
		}

		private static void Register_TableIteratorsModule(Table gtable, Table ns)
		{
			ns.Set("ipairs", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.TableIteratorsModule.ipairs, "ipairs"));
			ns.Set("next", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.TableIteratorsModule.next, "next"));
			ns.Set("pairs", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.TableIteratorsModule.pairs, "pairs"));
		}

		private static void Register_TableModule(Table gtable, Table ns)
		{
			ns.Set("concat", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.TableModule.concat, "concat"));
			ns.Set("insert", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.TableModule.insert, "insert"));
			ns.Set("pack", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.TableModule.pack, "pack"));
			ns.Set("remove", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.TableModule.remove, "remove"));
			ns.Set("sort", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.TableModule.sort, "sort"));
			ns.Set("unpack", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.TableModule.unpack, "unpack"));
		}

		private static void Register_TableModule_Globals(Table gtable, Table ns)
		{
			ns.Set("pack", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.TableModule_Globals.pack, "pack"));
			ns.Set("unpack", DynValue.NewCallback(MoonSharp.Interpreter.CoreLib.TableModule_Globals.unpack, "unpack"));
		}

	}
}
