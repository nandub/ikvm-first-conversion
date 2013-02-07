/*
  Copyright (C) 2002-2013 Jeroen Frijters

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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
#if STATIC_COMPILER
using IKVM.Reflection;
using IKVM.Reflection.Emit;
using Type = IKVM.Reflection.Type;
using ProtectionDomain = System.Object;
#else
using System.Reflection;
using System.Reflection.Emit;
using ProtectionDomain = java.security.ProtectionDomain;
#endif

namespace IKVM.Internal
{
	sealed class DynamicClassLoader : TypeWrapperFactory
	{
		// this PublicKey must be the same as the byte array in ForgedKeyPair
		internal const string DynamicAssemblySuffixAndPublicKey = "-ikvm-runtime-injected, PublicKey=00240000048000009400000006020000002400005253413100040000010001009D674F3D63B8D7A4C428BD7388341B025C71AA61C6224CD53A12C21330A3159D300051FE2EED154FE30D70673A079E4529D0FD78113DCA771DA8B0C1EF2F77B73651D55645B0A4294F0AF9BF7078432E13D0F46F951D712C2FCF02EB15552C0FE7817FC0AED58E0984F86661BF64D882F29B619899DD264041E7D4992548EB9E";
#if !STATIC_COMPILER
		private static List<AssemblyBuilder> saveDebugAssemblies;
		private static List<DynamicClassLoader> saveClassLoaders;
		private static int dumpCounter;
#endif // !STATIC_COMPILER
#if STATIC_COMPILER || CLASSGC
		private readonly Dictionary<string, TypeWrapper> dynamicTypes = new Dictionary<string, TypeWrapper>();
#else
		private static readonly Dictionary<string, TypeWrapper> dynamicTypes = new Dictionary<string, TypeWrapper>();
#endif
		private readonly ModuleBuilder moduleBuilder;
		private readonly bool hasInternalAccess;
#if STATIC_COMPILER
		private TypeBuilder proxiesContainer;
		private List<TypeBuilder> proxies;
#endif // STATIC_COMPILER
		private Dictionary<string, TypeBuilder> unloadables;
		private TypeBuilder unloadableContainer;
#if !STATIC_COMPILER && !CLASSGC
		private static DynamicClassLoader instance = new DynamicClassLoader(CreateModuleBuilder(), false);
#endif
#if CLASSGC
		private List<string> friends = new List<string>();
#endif

		[System.Security.SecuritySafeCritical]
		static DynamicClassLoader()
		{
#if !STATIC_COMPILER
			// TODO AppDomain.TypeResolve requires ControlAppDomain permission, but if we don't have that,
			// we should handle that by disabling dynamic class loading
			AppDomain.CurrentDomain.TypeResolve += new ResolveEventHandler(OnTypeResolve);
#if !CLASSGC
			// Ref.Emit doesn't like the "<Module>" name for types
			// (since it already defines a pseudo-type named <Module> for global methods and fields)
			dynamicTypes.Add("<Module>", null);
#endif // !CLASSGC
#endif // !STATIC_COMPILER
		}

		internal DynamicClassLoader(ModuleBuilder moduleBuilder, bool hasInternalAccess)
		{
			this.moduleBuilder = moduleBuilder;
			this.hasInternalAccess = hasInternalAccess;

#if !STATIC_COMPILER
			if (JVM.IsSaveDebugImage)
			{
				if (saveClassLoaders == null)
				{
					System.Threading.Interlocked.CompareExchange(ref saveClassLoaders, new List<DynamicClassLoader>(), null);
				}
				lock (saveClassLoaders)
				{
					saveClassLoaders.Add(this);
				}
			}
#endif

#if STATIC_COMPILER || CLASSGC
			// Ref.Emit doesn't like the "<Module>" name for types
			// (since it already defines a pseudo-type named <Module> for global methods and fields)
			dynamicTypes.Add("<Module>", null);
#endif
		}

#if CLASSGC
		internal override void AddInternalsVisibleTo(Assembly friend)
		{
			string name = friend.GetName().Name;
			lock (friends)
			{
				if (!friends.Contains(name))
				{
					friends.Add(name);
					AttributeHelper.SetInternalsVisibleToAttribute((AssemblyBuilder)moduleBuilder.Assembly, name);
				}
			}
		}
#endif // CLASSGC

#if !STATIC_COMPILER
		private static Assembly OnTypeResolve(object sender, ResolveEventArgs args)
		{
			TypeWrapper type;
#if CLASSGC
			DynamicClassLoader instance;
			ClassLoaderWrapper loader = ClassLoaderWrapper.GetClassLoaderForDynamicJavaAssembly(args.RequestingAssembly);
			if(loader == null)
			{
				return null;
			}
			instance = (DynamicClassLoader)loader.GetTypeWrapperFactory();
			instance.dynamicTypes.TryGetValue(args.Name, out type);
#else
			dynamicTypes.TryGetValue(args.Name, out type);
#endif
			if (type == null)
			{
				return null;
			}
			try
			{
				type.Finish();
			}
			catch(RetargetableJavaException x)
			{
				throw x.ToJava();
			}
			// NOTE We used to remove the type from the hashtable here, but that creates a race condition if
			// another thread also fires the OnTypeResolve event while we're baking the type.
			// I really would like to remove the type from the hashtable, but at the moment I don't see
			// any way of doing that that wouldn't cause this race condition.
			// UPDATE since we now also use the dynamicTypes hashtable to keep track of type names that
			// have been used already, we cannot remove the keys.
			return type.TypeAsTBD.Assembly;
		}
#endif // !STATIC_COMPILER

		internal override bool ReserveName(string name)
		{
			lock(dynamicTypes)
			{
				if(dynamicTypes.ContainsKey(name))
				{
					return false;
				}
				dynamicTypes.Add(name, null);
				return true;
			}
		}

		internal override string AllocMangledName(DynamicTypeWrapper tw)
		{
			lock(dynamicTypes)
			{
				return TypeNameMangleImpl(dynamicTypes, tw.Name, tw);
			}
		}

		internal static string TypeNameMangleImpl(Dictionary<string, TypeWrapper> dict, string name, TypeWrapper tw)
		{
			// the CLR maximum type name length is 1023 characters,
			// but we need to leave some room for the suffix that we
			// may need to append to make the name unique
			const int MaxLength = 1000;
			if (name.Length > MaxLength)
			{
				name = name.Substring(0, MaxLength) + "/truncated";
			}
			string mangledTypeName = TypeNameUtil.ReplaceIllegalCharacters(name);
			// FXBUG the CLR (both 1.1 and 2.0) doesn't like type names that end with a single period,
			// it loses the trailing period in the name that gets passed in the TypeResolve event.
			if (dict.ContainsKey(mangledTypeName) || mangledTypeName.EndsWith("."))
			{
#if STATIC_COMPILER
					Tracer.Warning(Tracer.Compiler, "Class name clash: {0}", mangledTypeName);
#endif
				// Java class names cannot contain slashes (since they are converted into periods),
				// so we take advantage of that fact to create a unique name.
				string baseName = mangledTypeName;
				int instanceId = 0;
				do
				{
					mangledTypeName = baseName + "/" + (++instanceId);
				} while (dict.ContainsKey(mangledTypeName));
			}
			dict.Add(mangledTypeName, tw);
			return mangledTypeName;
		}

		internal sealed override TypeWrapper DefineClassImpl(Dictionary<string, TypeWrapper> types, ClassFile f, ClassLoaderWrapper classLoader, ProtectionDomain protectionDomain)
		{
#if STATIC_COMPILER
			AotTypeWrapper type = new AotTypeWrapper(f, (CompilerClassLoader)classLoader);
			type.CreateStep1();
			types[f.Name] = type;
			return type;
#else
			// this step can throw a retargettable exception, if the class is incorrect
			DynamicTypeWrapper type = new DynamicTypeWrapper(f, classLoader, protectionDomain);
			// This step actually creates the TypeBuilder. It is not allowed to throw any exceptions,
			// if an exception does occur, it is due to a programming error in the IKVM or CLR runtime
			// and will cause a CriticalFailure and exit the process.
			type.CreateStep1();
			type.CreateStep2();
			lock(types)
			{
				// in very extreme conditions another thread may have beaten us to it
				// and loaded (not defined) a class with the same name, in that case
				// we'll leak the the Reflection.Emit defined type. Also see the comment
				// in ClassLoaderWrapper.RegisterInitiatingLoader().
				TypeWrapper race;
				types.TryGetValue(f.Name, out race);
				if(race == null)
				{
					types[f.Name] = type;
#if !FIRST_PASS
					java.lang.Class clazz = new java.lang.Class(null);
#if __MonoCS__
					TypeWrapper.SetTypeWrapperHack(clazz, type);
#else
					clazz.typeWrapper = type;
#endif
					clazz.pd = protectionDomain;
					type.SetClassObject(clazz);
#endif
				}
				else
				{
					throw new LinkageError("duplicate class definition: " + f.Name);
				}
			}
			return type;
#endif // STATIC_COMPILER
		}

#if STATIC_COMPILER
		internal TypeBuilder DefineProxy(TypeWrapper proxyClass, TypeWrapper[] interfaces)
		{
			if (proxiesContainer == null)
			{
				proxiesContainer = moduleBuilder.DefineType("__<Proxies>", TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.Abstract);
				AttributeHelper.HideFromJava(proxiesContainer);
				AttributeHelper.SetEditorBrowsableNever(proxiesContainer);
				proxies = new List<TypeBuilder>();
			}
			Type[] ifaces = new Type[interfaces.Length];
			for (int i = 0; i < ifaces.Length; i++)
			{
				ifaces[i] = interfaces[i].TypeAsBaseType;
			}
			TypeBuilder tb = proxiesContainer.DefineNestedType(GetProxyNestedName(interfaces), TypeAttributes.NestedPublic | TypeAttributes.Class | TypeAttributes.Sealed, proxyClass.TypeAsBaseType, ifaces);
			proxies.Add(tb);
			return tb;
		}
#endif

		private static string GetProxyNestedName(TypeWrapper[] interfaces)
		{
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			foreach (TypeWrapper tw in interfaces)
			{
				sb.Append(tw.Name.Length).Append('|').Append(tw.Name);
			}
			return TypeNameUtil.MangleNestedTypeName(sb.ToString());
		}

		internal static string GetProxyName(TypeWrapper[] interfaces)
		{
			return "__<Proxies>+" + GetProxyNestedName(interfaces);
		}

		internal override Type DefineUnloadable(string name)
		{
			lock(this)
			{
				if(unloadables == null)
				{
					unloadables = new Dictionary<string, TypeBuilder>();
				}
				TypeBuilder type;
				if(unloadables.TryGetValue(name, out type))
				{
					return type;
				}
				if(unloadableContainer == null)
				{
					unloadableContainer = moduleBuilder.DefineType(UnloadableTypeWrapper.ContainerTypeName, TypeAttributes.Interface | TypeAttributes.Abstract);
					AttributeHelper.HideFromJava(unloadableContainer);
				}
				type = unloadableContainer.DefineNestedType(TypeNameUtil.MangleNestedTypeName(name), TypeAttributes.NestedPrivate | TypeAttributes.Interface | TypeAttributes.Abstract);
				unloadables.Add(name, type);
				return type;
			}
		}

		internal override bool HasInternalAccess
		{
			get { return hasInternalAccess; }
		}

		internal void FinishAll()
		{
			Dictionary<TypeWrapper, TypeWrapper> done = new Dictionary<TypeWrapper, TypeWrapper>();
			bool more = true;
			while(more)
			{
				more = false;
				List<TypeWrapper> l = new List<TypeWrapper>(dynamicTypes.Values);
				foreach(TypeWrapper tw in l)
				{
					if(tw != null && !done.ContainsKey(tw))
					{
						more = true;
						done.Add(tw, tw);
						Tracer.Info(Tracer.Runtime, "Finishing {0}", tw.TypeAsTBD.FullName);
						tw.Finish();
					}
				}
			}
			if(unloadableContainer != null)
			{
				unloadableContainer.CreateType();
				foreach(TypeBuilder tb in unloadables.Values)
				{
					tb.CreateType();
				}
			}
#if STATIC_COMPILER
			if(proxiesContainer != null)
			{
				proxiesContainer.CreateType();
				foreach(TypeBuilder tb in proxies)
				{
					tb.CreateType();
				}
			}
#endif // STATIC_COMPILER
		}

#if !STATIC_COMPILER
		internal static void SaveDebugImages()
		{
			Console.Error.WriteLine("Saving dynamic assemblies...");
			JVM.FinishingForDebugSave = true;
			if (saveClassLoaders != null)
			{
				foreach (DynamicClassLoader instance in saveClassLoaders)
				{
					instance.FinishAll();
					AssemblyBuilder ab = (AssemblyBuilder)instance.ModuleBuilder.Assembly;
					SaveDebugAssembly(ab);
				}
			}
			if (saveDebugAssemblies != null)
			{
				foreach (AssemblyBuilder ab in saveDebugAssemblies)
				{
					SaveDebugAssembly(ab);
				}
			}
			Console.Error.WriteLine("Saving done.");
		}

		private static void SaveDebugAssembly(AssemblyBuilder ab)
		{
			Console.Error.WriteLine("Saving '{0}'", ab.GetName().Name + ".dll");
			ab.Save(ab.GetName().Name + ".dll");
		}

		internal static void RegisterForSaveDebug(AssemblyBuilder ab)
		{
			if(saveDebugAssemblies == null)
			{
				saveDebugAssemblies = new List<AssemblyBuilder>();
			}
			saveDebugAssemblies.Add(ab);
		}
#endif

		internal sealed override ModuleBuilder ModuleBuilder
		{
			get
			{
				return moduleBuilder;
			}
		}

		[System.Security.SecuritySafeCritical]
		internal static DynamicClassLoader Get(ClassLoaderWrapper loader)
		{
#if STATIC_COMPILER
			return new DynamicClassLoader(((CompilerClassLoader)loader).CreateModuleBuilder(), false);
#else
			AssemblyClassLoader acl = loader as AssemblyClassLoader;
			if (acl != null && ForgedKeyPair.Instance != null)
			{
				string name = acl.MainAssembly.GetName().Name + DynamicAssemblySuffixAndPublicKey;
				foreach (InternalsVisibleToAttribute attr in acl.MainAssembly.GetCustomAttributes(typeof(InternalsVisibleToAttribute), false))
				{
					if (attr.AssemblyName == name)
					{
						AssemblyName n = new AssemblyName(name);
						n.KeyPair = ForgedKeyPair.Instance;
						return new DynamicClassLoader(CreateModuleBuilder(n), true);
					}
				}
			}
#if CLASSGC
			DynamicClassLoader instance = new DynamicClassLoader(CreateModuleBuilder());
#endif
			return instance;
#endif
		}

#if !STATIC_COMPILER
		sealed class ForgedKeyPair : StrongNameKeyPair
		{
			internal static readonly StrongNameKeyPair Instance;

			static ForgedKeyPair()
			{
				try
				{
					// this public key byte array must be the same as the public key in DynamicAssemblySuffixAndPublicKey
					Instance = new ForgedKeyPair(new byte[] {
						0x00, 0x24, 0x00, 0x00, 0x04, 0x80, 0x00, 0x00, 0x94, 0x00, 0x00,
						0x00, 0x06, 0x02, 0x00, 0x00, 0x00, 0x24, 0x00, 0x00, 0x52, 0x53,
						0x41, 0x31, 0x00, 0x04, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x9D,
						0x67, 0x4F, 0x3D, 0x63, 0xB8, 0xD7, 0xA4, 0xC4, 0x28, 0xBD, 0x73,
						0x88, 0x34, 0x1B, 0x02, 0x5C, 0x71, 0xAA, 0x61, 0xC6, 0x22, 0x4C,
						0xD5, 0x3A, 0x12, 0xC2, 0x13, 0x30, 0xA3, 0x15, 0x9D, 0x30, 0x00,
						0x51, 0xFE, 0x2E, 0xED, 0x15, 0x4F, 0xE3, 0x0D, 0x70, 0x67, 0x3A,
						0x07, 0x9E, 0x45, 0x29, 0xD0, 0xFD, 0x78, 0x11, 0x3D, 0xCA, 0x77,
						0x1D, 0xA8, 0xB0, 0xC1, 0xEF, 0x2F, 0x77, 0xB7, 0x36, 0x51, 0xD5,
						0x56, 0x45, 0xB0, 0xA4, 0x29, 0x4F, 0x0A, 0xF9, 0xBF, 0x70, 0x78,
						0x43, 0x2E, 0x13, 0xD0, 0xF4, 0x6F, 0x95, 0x1D, 0x71, 0x2C, 0x2F,
						0xCF, 0x02, 0xEB, 0x15, 0x55, 0x2C, 0x0F, 0xE7, 0x81, 0x7F, 0xC0,
						0xAE, 0xD5, 0x8E, 0x09, 0x84, 0xF8, 0x66, 0x61, 0xBF, 0x64, 0xD8,
						0x82, 0xF2, 0x9B, 0x61, 0x98, 0x99, 0xDD, 0x26, 0x40, 0x41, 0xE7,
						0xD4, 0x99, 0x25, 0x48, 0xEB, 0x9E
					});
				}
				catch
				{
				}
			}

			private ForgedKeyPair(byte[] publicKey)
				: base(ToInfo(publicKey), new StreamingContext())
			{
			}

			private static SerializationInfo ToInfo(byte[] publicKey)
			{
				byte[] privateKey = publicKey;
				if (JVM.IsSaveDebugImage)
				{
					CspParameters cspParams = new CspParameters();
					cspParams.KeyContainerName = null;
					cspParams.Flags = CspProviderFlags.UseArchivableKey;
					cspParams.KeyNumber = 2;
					RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(1024, cspParams);
					privateKey = rsa.ExportCspBlob(true);
				}
				SerializationInfo info = new SerializationInfo(typeof(StrongNameKeyPair), new FormatterConverter());
				info.AddValue("_keyPairExported", true);
				info.AddValue("_keyPairArray", privateKey);
				info.AddValue("_keyPairContainer", null);
				info.AddValue("_publicKey", publicKey);
				return info;
			}
		}

		private static ModuleBuilder CreateModuleBuilder()
		{
			AssemblyName name = new AssemblyName();
			if(JVM.IsSaveDebugImage)
			{
				name.Name = "ikvmdump-" + System.Threading.Interlocked.Increment(ref dumpCounter);
			}
			else
			{
				name.Name = "ikvm_dynamic_assembly__" + (uint)Environment.TickCount;
			}
			return CreateModuleBuilder(name);
		}

		private static ModuleBuilder CreateModuleBuilder(AssemblyName name)
		{
			DateTime now = DateTime.Now;
			name.Version = new Version(now.Year, (now.Month * 100) + now.Day, (now.Hour * 100) + now.Minute, (now.Second * 1000) + now.Millisecond);
			List<CustomAttributeBuilder> attribs = new List<CustomAttributeBuilder>();
			AssemblyBuilderAccess access;
			if(JVM.IsSaveDebugImage)
			{
				access = AssemblyBuilderAccess.RunAndSave;
			}
#if CLASSGC
			else if(JVM.classUnloading
				// DefineDynamicAssembly(..., RunAndCollect, ...) does a demand for PermissionSet(Unrestricted), so we want to avoid that in partial trust scenarios
				&& AppDomain.CurrentDomain.IsFullyTrusted)
			{
				access = AssemblyBuilderAccess.RunAndCollect;
			}
#endif
			else
			{
				access = AssemblyBuilderAccess.Run;
			}
#if NET_4_0
			if(!AppDomain.CurrentDomain.IsFullyTrusted)
			{
				attribs.Add(new CustomAttributeBuilder(typeof(System.Security.SecurityTransparentAttribute).GetConstructor(Type.EmptyTypes), new object[0]));
			}
#endif
			AssemblyBuilder assemblyBuilder =
#if NET_4_0
				AppDomain.CurrentDomain.DefineDynamicAssembly(name, access, null, true, attribs);
#else
				AppDomain.CurrentDomain.DefineDynamicAssembly(name, access, null, null, null, null, null, true, attribs);
#endif
			AttributeHelper.SetRuntimeCompatibilityAttribute(assemblyBuilder);
			bool debug = JVM.EmitSymbols;
			CustomAttributeBuilder debugAttr = new CustomAttributeBuilder(typeof(DebuggableAttribute).GetConstructor(new Type[] { typeof(bool), typeof(bool) }), new object[] { true, debug });
			assemblyBuilder.SetCustomAttribute(debugAttr);
			ModuleBuilder moduleBuilder = JVM.IsSaveDebugImage ? assemblyBuilder.DefineDynamicModule(name.Name, name.Name + ".dll", debug) : assemblyBuilder.DefineDynamicModule(name.Name, debug);
			moduleBuilder.SetCustomAttribute(new CustomAttributeBuilder(typeof(IKVM.Attributes.JavaModuleAttribute).GetConstructor(Type.EmptyTypes), new object[0]));
			return moduleBuilder;
		}
#endif // !STATIC_COMPILER
	}
}
