using System;
using System.Linq;
using MoonSharp.Interpreter.CoreLib;
using MoonSharp.Interpreter.Platforms;

namespace MoonSharp.Interpreter
{
	/// <summary>
	/// Class managing modules (mostly as extension methods)
	///
	/// [TRIMMED — mod ScriptingMod Besiege] L'enregistrement des modules amont
	/// fonctionne par réflexion (GetMethods/GetFields/GetCustomAttributes +
	/// Delegate.CreateDelegate), interdite par l'AssemblyScanner du mod loader.
	/// Il est remplacé par un registre GENERÉ hors-ligne :
	///   tools/moonsharp-gen (outils/moonsharp-build.sh) émet Modules/ModuleRegister.g.cs
	///   (classe HardwiredModuleRegister) qui installe les mêmes membres, sans réflexion.
	/// Voir third-party/README.md et docs/journal-m0.md (étape 4).
	/// Modules non supportés dans ce build : IoModule (CoreModules.IO), LoadModule
	/// (CoreModules.LoadMethods) — fichiers .cs supprimés ; le flag est ignoré.
	/// </summary>
	public static class ModuleRegister
	{
		/// <summary>
		/// Register the core modules to a table
		/// </summary>
		/// <param name="table">The table.</param>
		/// <param name="modules">The modules.</param>
		/// <returns></returns>
		public static Table RegisterCoreModules(this Table table, CoreModules modules)
		{
			modules = Script.GlobalOptions.Platform.FilterSupportedCoreModules(modules);

			if (modules.Has(CoreModules.GlobalConsts)) RegisterConstants(table);
			if (modules.Has(CoreModules.TableIterators)) RegisterModuleType<TableIteratorsModule>(table);
			if (modules.Has(CoreModules.Basic)) RegisterModuleType<BasicModule>(table);
			if (modules.Has(CoreModules.Metatables)) RegisterModuleType<MetaTableModule>(table);
			if (modules.Has(CoreModules.String)) RegisterModuleType<StringModule>(table);
			if (modules.Has(CoreModules.Table)) RegisterModuleType<TableModule>(table);
			if (modules.Has(CoreModules.Table)) RegisterModuleType<TableModule_Globals>(table);
			if (modules.Has(CoreModules.ErrorHandling)) RegisterModuleType<ErrorHandlingModule>(table);
			if (modules.Has(CoreModules.Math)) RegisterModuleType<MathModule>(table);
			if (modules.Has(CoreModules.Coroutine)) RegisterModuleType<CoroutineModule>(table);
			if (modules.Has(CoreModules.Bit32)) RegisterModuleType<Bit32Module>(table);
			if (modules.Has(CoreModules.Dynamic)) RegisterModuleType<DynamicModule>(table);
			if (modules.Has(CoreModules.OS_System)) RegisterModuleType<OsSystemModule>(table);
			if (modules.Has(CoreModules.OS_Time)) RegisterModuleType<OsTimeModule>(table);
			if (modules.Has(CoreModules.IO)) { /* [TRIMMED] IoModule removed from this build */ }
			if (modules.Has(CoreModules.Debug)) RegisterModuleType<DebugModule>(table);
			if (modules.Has(CoreModules.Json)) RegisterModuleType<JsonModule>(table);
			if (modules.Has(CoreModules.LoadMethods)) { /* [TRIMMED] LoadModule removed from this build */ }

			return table;
		}

		/// <summary>
		/// Registers the standard constants (_G, _VERSION, _MOONSHARP) to a table
		/// </summary>
		/// <param name="table">The table.</param>
		/// <returns></returns>
		public static Table RegisterConstants(this Table table)
		{
			DynValue moonsharp_table = DynValue.NewTable(table.OwnerScript);
			Table m = moonsharp_table.Table;

			table.Set("_G", DynValue.NewTable(table));
			table.Set("_VERSION", DynValue.NewString(string.Format("MoonSharp {0}", Script.VERSION)));
			table.Set("_MOONSHARP", moonsharp_table);

			m.Set("version", DynValue.NewString(Script.VERSION));
			m.Set("luacompat", DynValue.NewString(Script.LUA_VERSION));
			m.Set("platform", DynValue.NewString(Script.GlobalOptions.Platform.GetPlatformName()));
			m.Set("is_aot", DynValue.NewBoolean(Script.GlobalOptions.Platform.IsRunningOnAOT()));
			m.Set("is_unity", DynValue.NewBoolean(PlatformAutoDetector.IsRunningOnUnity));
			m.Set("is_mono", DynValue.NewBoolean(PlatformAutoDetector.IsRunningOnMono));
			m.Set("is_clr4", DynValue.NewBoolean(PlatformAutoDetector.IsRunningOnClr4));
			m.Set("is_pcl", DynValue.NewBoolean(PlatformAutoDetector.IsPortableFramework));
			m.Set("banner", DynValue.NewString(Script.GetBanner()));

			return table;
		}

		/// <summary>
		/// Registers a module type to the specified table, using the generated
		/// (reflection-free) hardwired register.
		/// </summary>
		/// <param name="gtable">The table.</param>
		/// <param name="t">The type</param>
		/// <returns></returns>
		public static Table RegisterModuleType(this Table gtable, Type t)
		{
			Table table = CreateModuleNamespace(gtable, HardwiredModuleRegister.GetNamespace(t));
			HardwiredModuleRegister.RegisterMembers(gtable, table, t);
			return gtable;
		}

		/// <summary>
		/// Registers a module type to the specified table
		/// </summary>
		/// <typeparam name="T">The module type</typeparam>
		/// <param name="table">The table.</param>
		/// <returns></returns>
		public static Table RegisterModuleType<T>(this Table table)
		{
			return RegisterModuleType(table, typeof(T));
		}

		private static Table CreateModuleNamespace(Table gtable, string ns)
		{
			if (string.IsNullOrEmpty(ns))
			{
				return gtable;
			}
			else
			{
				Table table = null;

				DynValue found = gtable.Get(ns);

				if (found.Type == DataType.Table)
				{
					table = found.Table;
				}
				else
				{
					table = new Table(gtable.OwnerScript);
					gtable.Set(ns, DynValue.NewTable(table));
				}


				DynValue package = gtable.RawGet("package");

				if (package == null || package.Type != DataType.Table)
				{
					gtable.Set("package", package = DynValue.NewTable(gtable.OwnerScript));
				}


				DynValue loaded = package.Table.RawGet("loaded");

				if (loaded == null || loaded.Type != DataType.Table)
				{
					package.Table.Set("loaded", loaded = DynValue.NewTable(gtable.OwnerScript));
				}

				loaded.Table.Set(ns, DynValue.NewTable(table));

				return table;
			}
		}
	}
}
