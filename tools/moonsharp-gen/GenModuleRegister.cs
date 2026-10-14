// GenModuleRegister — fait partie du build du mod ScriptingMod (Besiege).
//
// PROBLÈME : MoonSharp enregistre ses modules CoreLib (string, math, table, coroutine,
// json, os.time...) PAR RÉFLEXION (ModuleRegister : GetMethods/GetFields/
// GetCustomAttributes + Delegate.CreateDelegate). La réflexion est interdite dans l'IL
// des mods par l'AssemblyScanner du mod loader de Besiege.
//
// SOLUTION : ce générateur s'exécute HORS du jeu (chaîne de build Docker, où la réflexion
// est légale) contre la DLL MoonSharp COMPLÈTE (net35, non élaguée), réplique la logique
// de ModuleRegister.RegisterModuleType, et ÉMET un fichier C# sans aucune réflexion
// (classe HardwiredModuleRegister) qui installe exactement les mêmes membres.
//
// Le fichier généré est commité dans third-party/moonsharp-trimmed/Modules/ModuleRegister.g.cs
// (reproductibilité ; le build standard n'a pas besoin de le régénérer).
//
// Usage : mono GenModuleRegister.exe <MoonSharp.Full.dll> <sortie ModuleRegister.g.cs> [types à ignorer, virgules]
// (les types à ignorer = modules retirés du build élagué, ex : "IoModule,LoadModule")
//
// Compile/runtime : net35 / mono (outil de build uniquement — jamais chargé par le jeu).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

class GenModuleRegister
{
	static void Main(string[] args)
	{
		if (args.Length < 2)
		{
			Console.Error.WriteLine("Usage: GenModuleRegister <moonsharp-full.dll> <out.cs> [skipTypes]");
			Environment.Exit(2);
		}

		string dllPath = args[0];
		string outPath = args[1];
		HashSet<string> skip = new HashSet<string>(
			(args.Length > 2 ? args[2] : "IoModule,LoadModule").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0),
			StringComparer.Ordinal);

		Assembly asm = Assembly.LoadFrom(dllPath);

		// Attributs MoonSharp (par nom — indépendant des versions)
		Type modAttrT = asm.GetType("MoonSharp.Interpreter.MoonSharpModuleAttribute", true);
		Type modMethodAttrT = asm.GetType("MoonSharp.Interpreter.MoonSharpModuleMethodAttribute", true);
		Type modConstAttrT = asm.GetType("MoonSharp.Interpreter.MoonSharpModuleConstantAttribute", true);
		PropertyInfo modAttrNsProp = modAttrT.GetProperty("Namespace");
		PropertyInfo methodAttrNameProp = modMethodAttrT.GetProperty("Name");
		PropertyInfo constAttrNameProp = modConstAttrT.GetProperty("Name");

		const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.Static;

		// Tous les modules du namespace CoreLib avec [MoonSharpModule]
		// NB : les classes de modules MoonSharp ne sont NI abstract NI static —
		// de simples classes publiques avec membres statiques.
		List<Type> modules = asm.GetTypes()
			.Where(t => t.IsClass && !t.IsGenericTypeDefinition && t.Namespace == "MoonSharp.Interpreter.CoreLib")
			.Where(t => t.GetCustomAttributes(modAttrT, false).Length > 0)
			.Where(t => !skip.Contains(t.Name))
			.OrderBy(t => t.Name, StringComparer.Ordinal)
			.ToList();

		Console.WriteLine("[GenModuleRegister] modules trouves : " + string.Join(", ", modules.Select(m => m.Name).ToArray()));

		StringBuilder sb = new StringBuilder();
		sb.AppendLine("// FICHIER GENERE par tools/moonsharp-gen (GenModuleRegister) — NE PAS EDITER A LA MAIN.");
		sb.AppendLine("// Source : reflexion hors-ligne sur la DLL MoonSharp v2.0.0.0 complete (tag");
		sb.AppendLine("// 3154416e). Regenerer via : bash tools/moonsharp-build.sh --regen");
		sb.AppendLine("// Ce fichier remplace l'enregistrement reflexif des modules (interdit par");
		sb.AppendLine("// l'AssemblyScanner de Besiege). Voir docs/journal-m0.md, etape 4.");
		sb.AppendLine();
		sb.AppendLine("using System;");
		sb.AppendLine();
		sb.AppendLine("namespace MoonSharp.Interpreter");
		sb.AppendLine("{");
		sb.AppendLine("\tinternal static class HardwiredModuleRegister");
		sb.AppendLine("\t{");

		// GetNamespace
		sb.AppendLine("\t\tinternal static string GetNamespace(Type t)");
		sb.AppendLine("\t\t{");
		foreach (Type m in modules)
		{
			string ns = (string)modAttrNsProp.GetValue(((Attribute[])m.GetCustomAttributes(modAttrT, false))[0], null) ?? "";
			sb.AppendLine("\t\t\tif (t == typeof(" + m.FullName.Replace('+', '.') + ")) return " +
				(ns.Length == 0 ? "null" : CSharpString(ns)) + ";");
		}
		sb.AppendLine("\t\t\treturn null;");
		sb.AppendLine("\t\t}");
		sb.AppendLine();

		// RegisterMembers
		sb.AppendLine("\t\tinternal static void RegisterMembers(Table gtable, Table ns, Type t)");
		sb.AppendLine("\t\t{");
		foreach (Type m in modules)
		{
			sb.AppendLine("\t\t\tif (t == typeof(" + m.FullName.Replace('+', '.') + ")) { Register_" + m.Name + "(gtable, ns); return; }");
		}
		sb.AppendLine("\t\t\tthrow new NotSupportedException(\"[ScriptingMod] Module non supporte dans ce build MoonSharp elague : \" + (t == null ? \"<null>\" : t.Name));");
		sb.AppendLine("\t\t}");
		sb.AppendLine();

		// Un Register_ par module
		foreach (Type m in modules)
		{
			sb.AppendLine("\t\tprivate static void Register_" + m.Name + "(Table gtable, Table ns)");
			sb.AppendLine("\t\t{");

			// MoonSharpInit(Table gtable, Table table)
			MethodInfo init = null;
			foreach (MethodInfo cand in m.GetMethods(StaticFlags).Where(x => x.Name == "MoonSharpInit"))
			{
				ParameterInfo[] ps = cand.GetParameters();
				if (ps.Length == 2 && ps[0].ParameterType.Name == "Table" && ps[1].ParameterType.Name == "Table")
				{
					init = cand;
					break;
				}
			}
			if (init != null)
				sb.AppendLine("\t\t\t" + m.FullName.Replace('+', '.') + ".MoonSharpInit(gtable, ns);");

			// Methodes [MoonSharpModuleMethod] — signature imposee :
			// public static DynValue Name(ScriptExecutionContext, CallbackArguments)
			foreach (MethodInfo mi in m.GetMethods(StaticFlags).OrderBy(x => x.Name, StringComparer.Ordinal))
			{
				Attribute[] attrs = (Attribute[])mi.GetCustomAttributes(modMethodAttrT, false);
				if (attrs.Length == 0)
					continue;

				string name = (string)methodAttrNameProp.GetValue(attrs[0], null);
				if (string.IsNullOrEmpty(name))
					name = mi.Name;

				ParameterInfo[] ps = mi.GetParameters();
				if (!(ps.Length == 2
					&& ps[0].ParameterType.Name == "ScriptExecutionContext"
					&& ps[1].ParameterType.Name == "CallbackArguments"
					&& mi.ReturnType.Name == "DynValue"))
				{
					throw new InvalidOperationException(string.Format(
						"[GenModuleRegister] {0}.{1} : signature inattendue (attendu : DynValue (ScriptExecutionContext, CallbackArguments)).",
						m.Name, mi.Name));
				}

				sb.AppendLine("\t\t\tns.Set(" + CSharpString(name) + ", DynValue.NewCallback(" +
					m.FullName.Replace('+', '.') + "." + MemberName(mi.Name) + ", " + CSharpString(name) + "));");
			}

			// Champs-Lua [MoonSharpModuleMethod] (string : source Lua compilee a l'enregistrement)
			foreach (FieldInfo fi in m.GetFields(StaticFlags).OrderBy(x => x.Name, StringComparer.Ordinal))
			{
				Attribute[] attrs = (Attribute[])fi.GetCustomAttributes(modMethodAttrT, false);
				if (attrs.Length == 0)
					continue;

				string name = (string)methodAttrNameProp.GetValue(attrs[0], null);
				if (string.IsNullOrEmpty(name))
					name = fi.Name;

				if (fi.FieldType != typeof(string))
					throw new InvalidOperationException(string.Format(
						"[GenModuleRegister] {0}.{1} : champ Lua [MoonSharpModuleMethod] de type {2} (string attendu).",
						m.Name, fi.Name, fi.FieldType.Name));

				string lua = (string)fi.GetValue(null);
				sb.AppendLine("\t\t\tns.Set(" + CSharpString(name) + ", ns.OwnerScript.LoadFunction(" +
					CSharpString(lua) + ", ns, " + CSharpString(name) + "));");
			}

			// Champs constants [MoonSharpModuleConstant] (string ou double)
			foreach (FieldInfo fi in m.GetFields(StaticFlags).OrderBy(x => x.Name, StringComparer.Ordinal))
			{
				Attribute[] attrs = (Attribute[])fi.GetCustomAttributes(modConstAttrT, false);
				if (attrs.Length == 0)
					continue;

				string name = (string)constAttrNameProp.GetValue(attrs[0], null);
				if (string.IsNullOrEmpty(name))
					name = fi.Name;

				if (fi.FieldType == typeof(string))
				{
					sb.AppendLine("\t\t\tns.Set(" + CSharpString(name) + ", DynValue.NewString(" +
						CSharpString((string)fi.GetValue(null)) + "));");
				}
				else if (fi.FieldType == typeof(double))
				{
					// Emission via double.Parse : certains litteraux (double.MaxValue =
					// 1.797...E+308) font echouer le parseur de mcs a la compilation.
					double d = (double)fi.GetValue(null);
					string expr;
					if (double.IsNaN(d)) expr = "double.NaN";
					else if (double.IsPositiveInfinity(d)) expr = "double.PositiveInfinity";
					else if (double.IsNegativeInfinity(d)) expr = "double.NegativeInfinity";
					else
					{
						string lit = d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
						expr = "double.Parse(" + CSharpString(lit) + ", System.Globalization.CultureInfo.InvariantCulture)";
					}
					sb.AppendLine("\t\t\tns.Set(" + CSharpString(name) + ", DynValue.NewNumber(" + expr + "));");
				}
				else
				{
					throw new InvalidOperationException(string.Format(
						"[GenModuleRegister] {0}.{1} : constante de type {2} (string ou double attendu).",
						m.Name, fi.Name, fi.FieldType.Name));
				}
			}

			sb.AppendLine("\t\t}");
			sb.AppendLine();
		}

		sb.AppendLine("\t}");
		sb.AppendLine("}");

		File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
		Console.WriteLine("[GenModuleRegister] OK : " + outPath + " (" + modules.Count + " modules)");
	}

	// Litteral C# echappe (", \\, \r, \n, \t) — pas de verbatim strings (robuste quel que soit le contenu Lua).
	// Identifiant de membre : prefixe '@' si le nom est un mot-cle C#.
	// En amont, ces methodes sont declarees "@null", "@byte", "@char" (StringModule,
	// JsonModule...) : la reflexion rend le nom SANS le '@' (mi.Name = "null"), qu'il
	// faut restituer pour que le code genere compile.
	static HashSet<string> csharpKeywords = new HashSet<string>(new string[] {
		"abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
		"class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
		"enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
		"foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
		"long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
		"private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
		"sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
		"try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
		"void", "volatile", "while"
	});

	static string MemberName(string name)
	{
		return csharpKeywords.Contains(name) ? "@" + name : name;
	}

	static string CSharpString(string s)
	{
		if (s == null) return "\"\"";
		StringBuilder b = new StringBuilder("\"");
		foreach (char c in s)
		{
			switch (c)
			{
				case '"': b.Append("\\\""); break;
				case '\\': b.Append("\\\\"); break;
				case '\r': b.Append("\\r"); break;
				case '\n': b.Append("\\n"); break;
				case '\t': b.Append("\\t"); break;
				default:
					if (c < 32 || c > 126)
						b.Append("\\u").Append(((int)c).ToString("x4"));
					else
						b.Append(c);
					break;
			}
		}
		return b.Append("\"").ToString();
	}
}
