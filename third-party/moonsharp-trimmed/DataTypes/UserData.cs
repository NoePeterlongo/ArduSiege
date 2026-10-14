using System;
using System.Collections.Generic;
using MoonSharp.Interpreter.Interop;
using MoonSharp.Interpreter.Interop.BasicDescriptors;
using MoonSharp.Interpreter.Interop.RegistrationPolicies;
using MoonSharp.Interpreter.Serialization.Json;

namespace MoonSharp.Interpreter
{
	/// <summary>
	/// Class exposing C# objects as Lua userdata.
	/// For efficiency, a global registry of types is maintained, instead of a per-script one.
	///
	/// [TRIMMED — mod ScriptingMod Besiege] Le registre interop réflexif
	/// (TypeDescriptorRegistry, descripteurs par réflexion) est interdit par
	/// l'AssemblyScanner du mod loader Besiege. Il est remplacé par :
	///   - les objets implémentant IUserDataType (auto-descripteurs), servis par
	///     AutoDescribingUserDataDescriptor — c'est le chemin utilisé par la VM
	///     (itérateurs « for » génériques via EnumerableWrapper) ;
	///   - les descripteurs explicites via Create(object, IUserDataDescriptor).
	/// Tout autre usage lève NotSupportedException. Voir third-party/README.md
	/// et docs/journal-m0.md (décisions D-M0-2).
	/// </summary>
	public class UserData : RefIdObject
	{
		private UserData()
		{
			// This type can only be instantiated using one of the Create methods
		}

		/// <summary>
		/// Gets or sets the "uservalue". See debug.getuservalue and debug.setuservalue.
		/// http://www.lua.org/manual/5.2/manual.html#pdf-debug.setuservalue
		/// </summary>
		public DynValue UserValue { get; set; }

		/// <summary>
		/// Gets the object associated to this userdata (null for statics)
		/// </summary>
		public object Object { get; private set; }

		/// <summary>
		/// Gets the type descriptor of this userdata
		/// </summary>
		public IUserDataDescriptor Descriptor { get; private set; }

		/// <summary>
		/// Gets or sets the registration policy (kept for API compatibility; the
		/// reflection registry is gone in this trimmed build).
		/// </summary>
		public static IRegistrationPolicy RegistrationPolicy { get; set; }

		/// <summary>
		/// Gets or sets the default access mode (kept for API compatibility).
		/// </summary>
		public static InteropAccessMode DefaultAccessMode { get; set; }

		private const string TRIMMED_MSG =
			"[ScriptingMod] Reflection-based userdata registration is not available in this trimmed MoonSharp build. " +
			"Use IUserDataType-implementing objects or explicit descriptors (UserData.Create(object, IUserDataDescriptor)).";

		/// <summary>
		/// Registers a type for userdata interop — NOT available in this build.
		/// </summary>
		public static IUserDataDescriptor RegisterType<T>(InteropAccessMode accessMode = InteropAccessMode.Default, string friendlyName = null)
		{
			throw new NotSupportedException(TRIMMED_MSG);
		}

		/// <summary>
		/// Registers a type for userdata interop — NOT available in this build.
		/// </summary>
		public static IUserDataDescriptor RegisterType(Type type, InteropAccessMode accessMode = InteropAccessMode.Default, string friendlyName = null)
		{
			throw new NotSupportedException(TRIMMED_MSG);
		}

		/// <summary>
		/// Registers a type for userdata interop — NOT available in this build.
		/// </summary>
		public static IUserDataDescriptor RegisterProxyType(IProxyFactory proxyFactory, InteropAccessMode accessMode = InteropAccessMode.Default, string friendlyName = null)
		{
			throw new NotSupportedException(TRIMMED_MSG);
		}

		/// <summary>
		/// Registers a type for userdata interop — NOT available in this build.
		/// </summary>
		public static IUserDataDescriptor RegisterProxyType<TProxy, TTarget>(Func<TTarget, TProxy> wrapDelegate, InteropAccessMode accessMode = InteropAccessMode.Default, string friendlyName = null)
		{
			throw new NotSupportedException(TRIMMED_MSG);
		}

		/// <summary>
		/// Registers a type for userdata interop — NOT available in this build.
		/// </summary>
		public static IUserDataDescriptor RegisterType<T>(IUserDataDescriptor customDescriptor)
		{
			throw new NotSupportedException(TRIMMED_MSG);
		}

		/// <summary>
		/// Registers a type for userdata interop — NOT available in this build.
		/// </summary>
		public static IUserDataDescriptor RegisterType(Type type, IUserDataDescriptor customDescriptor)
		{
			throw new NotSupportedException(TRIMMED_MSG);
		}

		/// <summary>
		/// Registers a type for userdata interop — NOT available in this build.
		/// </summary>
		public static IUserDataDescriptor RegisterType(IUserDataDescriptor customDescriptor)
		{
			throw new NotSupportedException(TRIMMED_MSG);
		}

		/// <summary>
		/// Registers a full assembly for interop — NOT available in this build.
		/// [TRIMMED] Signature amputée du paramètre System.Reflection.Assembly (namespace
		/// interdit ; aucune méthode appelante ne subsiste dans ce build).
		/// </summary>
		public static void RegisterAssembly(bool includeExtensionTypes = false)
		{
			throw new NotSupportedException(TRIMMED_MSG);
		}

		/// <summary>
		/// Determines whether a type is registered — always false for reflection-registered
		/// types in this build (IUserDataType are dynamic and not "registered").
		/// </summary>
		public static bool IsTypeRegistered(Type t)
		{
			return false;
		}

		/// <summary>
		/// Determines whether a type is registered — see IsTypeRegistered(Type).
		/// </summary>
		public static bool IsTypeRegistered<T>()
		{
			return false;
		}

		/// <summary>
		/// Unregisters a type — NOT available in this build.
		/// </summary>
		public static void UnregisterType<T>()
		{
			throw new NotSupportedException(TRIMMED_MSG);
		}

		/// <summary>
		/// Unregisters a type — NOT available in this build.
		/// </summary>
		public static void UnregisterType(Type t)
		{
			throw new NotSupportedException(TRIMMED_MSG);
		}

		/// <summary>
		/// Creates a userdata DynValue from the specified object, using a specific descriptor
		/// </summary>
		/// <param name="o">The object</param>
		/// <param name="descr">The descriptor.</param>
		/// <returns></returns>
		public static DynValue Create(object o, IUserDataDescriptor descr)
		{
			return DynValue.NewUserData(new UserData()
			{
				Descriptor = descr,
				Object = o
			});
		}

		/// <summary>
		/// Creates a userdata DynValue from the specified object.
		/// In this build, only IUserDataType-implementing objects are supported
		/// (served through AutoDescribingUserDataDescriptor).
		/// </summary>
		/// <param name="o">The object</param>
		/// <returns></returns>
		public static DynValue Create(object o)
		{
			var descr = GetDescriptorForObject(o);
			if (descr == null)
			{
				return null;
			}

			return Create(o, descr);
		}

		/// <summary>
		/// Creates a static userdata DynValue from the specified IUserDataDescriptor
		/// </summary>
		/// <param name="descr">The IUserDataDescriptor</param>
		/// <returns></returns>
		public static DynValue CreateStatic(IUserDataDescriptor descr)
		{
			if (descr == null) return null;

			return DynValue.NewUserData(new UserData()
			{
				Descriptor = descr,
				Object = null
			});
		}

		/// <summary>
		/// Creates a static userdata DynValue from the specified Type — NOT available
		/// in this build (no reflection registry).
		/// </summary>
		/// <param name="t">The type</param>
		/// <returns></returns>
		public static DynValue CreateStatic(Type t)
		{
			return null;
		}

		/// <summary>
		/// Creates a static userdata DynValue from the specified Type — NOT available
		/// in this build (no reflection registry).
		/// </summary>
		/// <typeparam name="T">The Type</typeparam>
		/// <returns></returns>
		public static DynValue CreateStatic<T>()
		{
			return null;
		}

		/// <summary>
		/// Gets the descriptor of this type, if the type can have one in this build
		/// (i.e. it implements IUserDataType).
		/// </summary>
		public static IUserDataDescriptor GetDescriptorForType(Type t, bool searchInterfaces)
		{
			if (t == null) return null;

			if (typeof(IUserDataType).IsAssignableFrom(t))
				return new AutoDescribingUserDataDescriptor(t, t.Name);

			return null;
		}

		/// <summary>
		/// Gets the descriptor of this type — see GetDescriptorForType.
		/// </summary>
		public static IUserDataDescriptor GetDescriptorForType<T>(bool searchInterfaces)
		{
			return GetDescriptorForType(typeof(T), searchInterfaces);
		}

		private static IUserDataDescriptor GetDescriptorForObject(object o)
		{
			if (o == null) return null;

			return GetDescriptorForType(o.GetType(), false);
		}

		/// <summary>
		/// Registers an extension type — NOT available in this build.
		/// </summary>
		public static void RegisterExtensionType(Type type, InteropAccessMode mode = InteropAccessMode.Default)
		{
			throw new NotSupportedException(TRIMMED_MSG);
		}

		/// <summary>
		/// Gets extension methods by name and type — NOT available in this build.
		/// </summary>
		public static List<IOverloadableMemberDescriptor> GetExtensionMethodsByNameAndType(string name, Type extendedType)
		{
			return null;
		}

		/// <summary>
		/// Gets the current extension methods version (no extension methods in this build).
		/// </summary>
		public static int GetExtensionMethodsChangeVersion()
		{
			return 0;
		}
	}
}
