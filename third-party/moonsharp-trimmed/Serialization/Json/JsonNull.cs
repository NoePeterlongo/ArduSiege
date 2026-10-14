using System;

namespace MoonSharp.Interpreter.Serialization.Json
{
	/// <summary>
	/// UserData representing a null value in a table converted from Json
	///
	/// [TRIMMED — mod ScriptingMod Besiege, décision D-M0-1] En amont, JsonNull est un
	/// userdata enregistré via le registre interop réflexif (interdit par l'AssemblyScanner).
	/// Divergence acceptée : le JSON null est représenté par nil Lua. Conséquence connue :
	/// dans une table Lua issue de json.parse, une clé à null est INDISTINGUABLE d'une clé
	/// absente (t.Set("a", nil) supprime la clé). Voir third-party/README.md et docs/journal-m0.md.
	/// </summary>
	public sealed class JsonNull
	{
		public static bool isNull() { return true; }

		[MoonSharpHidden]
		public static bool IsJsonNull(DynValue v)
		{
			return v.IsNil();
		}

		[MoonSharpHidden]
		public static DynValue Create()
		{
			return DynValue.Nil;
		}
	}
}
