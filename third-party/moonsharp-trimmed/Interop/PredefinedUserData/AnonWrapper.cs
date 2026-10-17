
using MoonSharp.Interpreter.Interop;

namespace MoonSharp.Interpreter.Interop
{
	/// <summary>
	/// Internal type used by <see cref="AnonWrapper{T}"/> for registration
	/// </summary>
	public class AnonWrapper
	{
	}

	/// <summary>
	/// Wrapper which allows for easier management of userdata without registering a new userdata type - useful 
	/// if a type which is not exposed to scripts but can be managed as a "black box" by scripts is desired.
	/// </summary>
	/// <typeparam name="T">The type to wrap</typeparam>
	// [TRIMMED — mod ArduSiege Besiege] En amont, AnonWrapper<T> etait enregistre
	// dans le registre interop reflexif (mode HideMembers) par le ctor statique de
	// UserData — registre interdit par l'AssemblyScanner. Implémentation IUserDataType
	// (boite noire : aucun membre expose aux scripts), le chemin supporte par notre
	// build : UserData.Create(o) sert ainsi les IUserDataType (MathModule.randomseed,
	// DynamicModule...). Voir docs/journal-m0.md (etape 8).
	public class AnonWrapper<T> : AnonWrapper, IUserDataType
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="AnonWrapper{T}"/> class.
		/// </summary>
		public AnonWrapper()
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="AnonWrapper{T}"/> class.
		/// </summary>
		/// <param name="o">The o.</param>
		public AnonWrapper(T o)
		{
			Value = o;
		}

		/// <summary>
		/// Gets or sets the value this instance wraps.
		/// </summary>
		public T Value { get; set; }

		// --- IUserDataType : boite noire (equivalent du mode amont HideMembers) ---

		public DynValue Index(Script script, DynValue index, bool isDirectIndexing)
		{
			return null;
		}

		public bool SetIndex(Script script, DynValue index, DynValue value, bool isDirectIndexing)
		{
			return false;
		}

		public DynValue MetaIndex(Script script, string metaname)
		{
			return null;
		}
	}

}
