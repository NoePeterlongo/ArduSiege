// Disable warnings about XML documentation
#pragma warning disable 1591


namespace MoonSharp.Interpreter.CoreLib
{
	/// <summary>
	/// Class implementing dynamic expression evaluations at runtime (a MoonSharp addition).
	/// </summary>
	[MoonSharpModule(Namespace = "dynamic")]
	public class DynamicModule
	{
		// [TRIMMED — mod ArduSiege Besiege] En amont : UserData.RegisterType<
		// DynamicExprWrapper>(HideMembers) — le registre reflexif est interdit par
		// l'AssemblyScanner. DynamicExprWrapper implement desormais IUserDataType
		// (boite noire), servis automatiquement par UserData.Create. Ce module n'est
		// pas dans Preset_SoftSandbox ; le init devient donc un no-op.
		private class DynamicExprWrapper : MoonSharp.Interpreter.Interop.IUserDataType
		{
			public DynamicExpression Expr;

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

		public static void MoonSharpInit(Table globalTable, Table stringTable)
		{
			// no-op (cf. commentaire TRIMMED ci-dessus)
		}

		[MoonSharpModuleMethod]
		public static DynValue eval(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			try
			{
				if (args[0].Type == DataType.UserData)
				{
					UserData ud = args[0].UserData;
					if (ud.Object is DynamicExprWrapper)
					{
						return ((DynamicExprWrapper)ud.Object).Expr.Evaluate(executionContext);
					}
					else
					{
						throw ScriptRuntimeException.BadArgument(0, "dynamic.eval", "A userdata was passed, but was not a previously prepared expression.");
					}
				}
				else
				{
					DynValue vs = args.AsType(0, "dynamic.eval", DataType.String, false);
					DynamicExpression expr = executionContext.GetScript().CreateDynamicExpression(vs.String);
					return expr.Evaluate(executionContext);
				}
			}
			catch (SyntaxErrorException ex)
			{ 
				throw new ScriptRuntimeException(ex);
			}
		}

		[MoonSharpModuleMethod]
		public static DynValue prepare(ScriptExecutionContext executionContext, CallbackArguments args)
		{
			try
			{
				DynValue vs = args.AsType(0, "dynamic.prepare", DataType.String, false);
				DynamicExpression expr = executionContext.GetScript().CreateDynamicExpression(vs.String);
				return UserData.Create(new DynamicExprWrapper() { Expr = expr });
			}
			catch (SyntaxErrorException ex)
			{
				throw new ScriptRuntimeException(ex);
			}
		}


	}

}
