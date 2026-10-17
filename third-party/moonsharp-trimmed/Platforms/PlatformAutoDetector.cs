using System;
using MoonSharp.Interpreter.Loaders;

namespace MoonSharp.Interpreter.Platforms
{
	/// <summary>
	/// A static class offering properties for autodetection of system/platform details
	///
	/// [TRIMMED — mod ArduSiege Besiege] La détection automatique utilisait de la
	/// réflexion (AppDomain.GetAssemblies) et System.Linq.Expressions (sonde AOT),
	/// interdites par l'AssemblyScanner du mod loader. Valeurs codées en dur pour
	/// Unity 5.4 / Mono (CLR 2.0). Voir third-party/README.md et docs/journal-m0.md.
	/// </summary>
	public static class PlatformAutoDetector
	{
		/// <summary>
		/// Gets a value indicating whether this instance is running on mono.
		/// </summary>
		public static bool IsRunningOnMono { get { return true; } }

		/// <summary>
		/// Gets a value indicating whether this instance is running on a CLR4 compatible implementation
		/// </summary>
		public static bool IsRunningOnClr4 { get { return false; } }

		/// <summary>
		/// Gets a value indicating whether this instance is running on Unity-3D
		/// </summary>
		public static bool IsRunningOnUnity { get { return true; } }

		/// <summary>
		/// Gets a value indicating whether this instance has been built as a Portable Class Library
		/// </summary>
		public static bool IsPortableFramework { get { return false; } }

		/// <summary>
		/// Gets a value indicating whether this instance has been compiled natively in Unity (as opposite to importing a DLL).
		/// </summary>
		public static bool IsUnityNative { get { return false; } }

		/// <summary>
		/// Gets a value indicating whether this instance has been compiled natively in Unity AND is using IL2CPP
		/// </summary>
		public static bool IsUnityIL2CPP { get { return false; } }

		/// <summary>
		/// Gets a value indicating whether this instance is running a system using Ahead-Of-Time compilation
		/// and not supporting JIT.
		/// [TRIMMED] Coded in the affirmative: no code path must attempt Reflection.Emit.
		/// </summary>
		public static bool IsRunningOnAOT { get { return true; } }

		internal static IPlatformAccessor GetDefaultPlatform()
		{
			return new LimitedPlatformAccessor();
		}

		internal static IScriptLoader GetDefaultScriptLoader()
		{
			return new InvalidScriptLoader("ArduSiege (Besiege)");
		}
	}
}
