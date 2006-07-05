/*
  Copyright (C) 2002, 2003, 2004, 2005, 2006 Jeroen Frijters

  This software is provided 'as-is', without any express or implied
  warranty.  In no event will the authors be held liable for any damages
  arising from the use of this software.

  Permission is granted to anyone to use this software for any purpose,
  including commercial applications, and to alter it and redistribute it
  freely, subject to the following restrictions:

  1. The origin of this software must not be misrepresented; you must not
     claim that you wrote the original software. If you use this software
     in a product, an acknowledgment in the product documentation would be
     appreciated but is not required.
  2. Altered source versions must be plainly marked as such, and must not be
     misrepresented as being the original software.
  3. This notice may not be removed or altered from any source distribution.

  Jeroen Frijters
  jeroen@frijters.net
  
*/
#if !COMPACT_FRAMEWORK
using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

namespace IKVM.Internal
{
	class DynamicClassLoader : ClassLoaderWrapper
	{
#if !WHIDBEY
		internal static bool arrayConstructionHack;
		internal static readonly object arrayConstructionLock = new object();
#endif // !WHIDBEY
		private static readonly Hashtable dynamicTypes = Hashtable.Synchronized(new Hashtable());
		// FXBUG moduleBuilder is static, because multiple dynamic assemblies is broken (TypeResolve doesn't fire)
		// so for the time being, we share one dynamic assembly among all classloaders
		private static ModuleBuilder moduleBuilder;
		private static bool saveDebugImage;
		private static ArrayList saveDebugAssemblies;
		private static int instanceCounter = 0;
		private int instanceId = System.Threading.Interlocked.Increment(ref instanceCounter);

		static DynamicClassLoader()
		{
			// TODO AppDomain.TypeResolve requires ControlAppDomain permission, but if we don't have that,
			// we should handle that by disabling dynamic class loading
			AppDomain.CurrentDomain.TypeResolve += new ResolveEventHandler(OnTypeResolve);
		}

		internal DynamicClassLoader(object javaClassLoader)
			: base(javaClassLoader)
		{
		}

		private static Assembly OnTypeResolve(object sender, ResolveEventArgs args)
		{
#if !WHIDBEY
			lock(arrayConstructionLock)
			{
				Tracer.Info(Tracer.ClassLoading, "OnTypeResolve: {0} (arrayConstructionHack = {1})", args.Name, arrayConstructionHack);
				if(arrayConstructionHack)
				{
					return null;
				}
			}
#endif // !WHIDBEY
			TypeWrapper type = (TypeWrapper)dynamicTypes[args.Name];
			if(type == null)
			{
				return null;
			}
			// During static compilation, a TypeResolve event should never trigger a finish.
			if(JVM.IsStaticCompilerPhase1)
			{
				JVM.CriticalFailure("Finish triggered during phase 1 of compilation.", null);
				return null;
			}
			try
			{
				type.Finish();
			}
#if !STATIC_COMPILER
			catch(RetargetableJavaException x)
			{
				throw x.ToJava();
			}
#endif // !STATIC_COMPILER
			finally
			{
			}
			// NOTE We used to remove the type from the hashtable here, but that creates a race condition if
			// another thread also fires the OnTypeResolve event while we're baking the type.
			// I really would like to remove the type from the hashtable, but at the moment I don't see
			// any way of doing that that wouldn't cause this race condition.
			// UPDATE since we now also use the dynamicTypes hashtable to keep track of type names that
			// have been used already, we cannot remove the keys.
			return type.TypeAsTBD.Assembly;
		}

		internal override TypeWrapper DefineClass(ClassFile f, object protectionDomain)
		{
			string dotnetAssembly = f.IKVMAssemblyAttribute;
			if(dotnetAssembly != null)
			{
				// The sole purpose of the stub class is to let us load the assembly that the class lives in,
				// once we've done that, all types in it become visible.
				Assembly asm;
				try
				{
#if WHIDBEY && STATIC_COMPILER
					asm = Assembly.ReflectionOnlyLoad(dotnetAssembly);
#else
					asm = Assembly.Load(dotnetAssembly);
#endif
				}
				catch(Exception x)
				{
					throw new NoClassDefFoundError(f.Name + " (" + x.Message + ")");
				}
				TypeWrapper tw = ClassLoaderWrapper.GetAssemblyClassLoader(asm).LoadClassByDottedName(f.Name);
				if(tw.Assembly != asm)
				{
					throw new NoClassDefFoundError(f.Name + " (assembly mismatch)");
				}
				return RegisterInitiatingLoader(tw);
			}
			lock(types.SyncRoot)
			{
				if(types.ContainsKey(f.Name))
				{
					throw new LinkageError("duplicate class definition: " + f.Name);
				}
				// mark the type as "loading in progress", so that we can detect circular dependencies.
				types.Add(f.Name, null);
			}
			try
			{
				DynamicTypeWrapper type = CreateDynamicTypeWrapper(f);
				// this step can throw a retargettable exception, if the class is incorrect
				bool hasclinit;
				type.CreateStep1(out hasclinit);
				// now we can allocate the mangledTypeName, because the next step cannot fail
				string mangledTypeName = f.Name;
				lock(dynamicTypes.SyncRoot)
				{
					// FXBUG the 1.1 CLR doesn't like type names that end with a period.
					if(dynamicTypes.ContainsKey(mangledTypeName) || mangledTypeName.EndsWith("."))
					{
#if STATIC_COMPILER
						Tracer.Warning(Tracer.Compiler, "Class name clash: {0}", mangledTypeName);
#endif
						mangledTypeName += "/" + instanceId;
					}
					dynamicTypes.Add(mangledTypeName, null);
				}
				// This step actually creates the TypeBuilder. It is not allowed to throw any exceptions,
				// if an exception does occur, it is due to a programming error in the IKVM or CLR runtime
				// and will cause a CriticalFailure and exit the process.
				type.CreateStep2NoFail(hasclinit, mangledTypeName);
				lock(types.SyncRoot)
				{
					// in very extreme conditions another thread may have beaten us to it
					// and loaded (not defined) a class with the same name, in that case
					// we'll leak the the Reflection.Emit defined type. Also see the comment
					// in ClassLoaderWrapper.RegisterInitiatingLoader().
					TypeWrapper race = (TypeWrapper)types[f.Name];
					if(race == null)
					{
						Debug.Assert(dynamicTypes.ContainsKey(mangledTypeName) && dynamicTypes[mangledTypeName] == null);
						dynamicTypes[mangledTypeName] = type;
						types[f.Name] = type;
#if !STATIC_COMPILER
						type.SetClassObject(JVM.Library.newClass(type, protectionDomain));
#endif
					}
					else
					{
						throw new LinkageError("duplicate class definition: " + f.Name);
					}
				}
				return type;
			}
			catch
			{
				lock(types.SyncRoot)
				{
					if(types[f.Name] == null)
					{
						// if loading the class fails, we remove the indicator that we're busy loading the class,
						// because otherwise we get a ClassCircularityError if we try to load the class again.
						types.Remove(f.Name);
					}
				}
				throw;
			}
		}

		protected virtual DynamicTypeWrapper CreateDynamicTypeWrapper(ClassFile f)
		{
			return new DynamicTypeWrapper(f, this);
		}

		internal static void PrepareForSaveDebugImage()
		{
			Debug.Assert(moduleBuilder == null);
			saveDebugImage = true;
		}

		internal static bool IsSaveDebugImage
		{
			get
			{
				return saveDebugImage;
			}
		}

		internal static void FinishAll(bool forDebug)
		{
			JVM.FinishingForDebugSave = forDebug;
			Hashtable done = new Hashtable();
			bool more = true;
			while(more)
			{
				more = false;
				ArrayList l = new ArrayList(dynamicTypes.Values);
				foreach(TypeWrapper tw in l)
				{
					if(!done.ContainsKey(tw))
					{
						more = true;
						done.Add(tw, tw);
						Tracer.Info(Tracer.Runtime, "Finishing {0}", tw.TypeAsTBD.FullName);
						tw.Finish();
					}
				}
			}
		}

#if !STATIC_COMPILER
		internal static void SaveDebugImage(object mainClass)
		{
			FinishAll(true);
			TypeWrapper mainTypeWrapper = TypeWrapper.FromClass(mainClass);
			mainTypeWrapper.Finish();
			Type mainType = mainTypeWrapper.TypeAsTBD;
			MethodInfo main = mainType.GetMethod("main", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(string[]) }, null);
			AssemblyBuilder asm = ((AssemblyBuilder)moduleBuilder.Assembly);
			asm.SetEntryPoint(main, PEFileKinds.ConsoleApplication);
			asm.Save("ikvmdump.exe");
			if(saveDebugAssemblies != null)
			{
				foreach(AssemblyBuilder ab in saveDebugAssemblies)
				{
					ab.Save(ab.GetName().Name + ".dll");
				}
			}
		}
#endif

		internal static void RegisterForSaveDebug(AssemblyBuilder ab)
		{
			if(saveDebugAssemblies == null)
			{
				saveDebugAssemblies = new ArrayList();
			}
			saveDebugAssemblies.Add(ab);
		}

		internal ModuleBuilder ModuleBuilder
		{
			get
			{
				lock(this)
				{
					if(moduleBuilder == null)
					{
						moduleBuilder = CreateModuleBuilder();
					}
					return moduleBuilder;
				}
			}
		}

		protected virtual ModuleBuilder CreateModuleBuilder()
		{
#if STATIC_COMPILER
			// HACK this is required because DelegateInnerClassTypeWrapper currently uses the ModuleBuilder
			// property to get a ModuleBuilder on the class loader that defined the delegate,
			// instead of the class loader that is using the delegate (as it probably should)
			return ((DynamicClassLoader)GetBootstrapClassLoader()).ModuleBuilder;
#else // STATIC_COMPILER
			AssemblyName name = new AssemblyName();
			if(saveDebugImage)
			{
				name.Name = "ikvmdump";
			}
			else
			{
				name.Name = "ikvm_dynamic_assembly__" + instanceId + "__" + (uint)Environment.TickCount;
			}
			DateTime now = DateTime.Now;
			name.Version = new Version(now.Year, (now.Month * 100) + now.Day, (now.Hour * 100) + now.Minute, (now.Second * 1000) + now.Millisecond);
			AssemblyBuilder assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(name, saveDebugImage ? AssemblyBuilderAccess.RunAndSave : AssemblyBuilderAccess.Run, null, null, null, null, null, true);
			CustomAttributeBuilder debugAttr = new CustomAttributeBuilder(typeof(DebuggableAttribute).GetConstructor(new Type[] { typeof(bool), typeof(bool) }), new object[] { true, JVM.Debug });
			assemblyBuilder.SetCustomAttribute(debugAttr);
			return saveDebugImage ? assemblyBuilder.DefineDynamicModule("ikvmdump.exe", "ikvmdump.exe", JVM.Debug) : assemblyBuilder.DefineDynamicModule(name.Name, JVM.Debug);
#endif // STATIC_COMPILER
		}
	}
}
#endif //COMPACT_FRAMEWORK
