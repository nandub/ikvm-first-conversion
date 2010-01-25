/*
  Copyright (C) 2008, 2009 Jeroen Frijters

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
using System.IO;
using System.Diagnostics;
using System.Diagnostics.SymbolStore;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using IKVM.Reflection.Impl;
using IKVM.Reflection.Metadata;
using IKVM.Reflection.Writer;

namespace IKVM.Reflection.Emit
{
	public sealed class ModuleBuilder : Module, ITypeOwner
	{
		private static readonly bool usePublicKeyAssemblyReference = false;
		private readonly Guid mvid = Guid.NewGuid();
		private long imageBaseAddress = 0x00400000;
		private readonly AssemblyBuilder asm;
		internal readonly string moduleName;
		internal readonly string fileName;
		internal readonly ISymbolWriterImpl symbolWriter;
		private readonly TypeBuilder moduleType;
		private readonly List<TypeBuilder> types = new List<TypeBuilder>();
		private readonly Dictionary<Type, int> typeTokens = new Dictionary<Type, int>();
		private readonly Dictionary<string, TypeBuilder> fullNameToType = new Dictionary<string, TypeBuilder>();
		internal readonly ByteBuffer methodBodies = new ByteBuffer(128 * 1024);
		internal readonly List<int> tokenFixupOffsets = new List<int>();
		internal readonly ByteBuffer initializedData = new ByteBuffer(512);
		internal readonly ByteBuffer manifestResources = new ByteBuffer(512);
		internal byte[] unmanagedResources;
		private readonly Dictionary<MemberInfo, int> importedMembers = new Dictionary<MemberInfo, int>();
		private readonly Dictionary<AssemblyName, int> referencedAssemblies = new Dictionary<AssemblyName, int>();
		private int nextPseudoToken = -1;
		private readonly List<int> resolvedTokens = new List<int>();
		internal readonly TableHeap Tables = new TableHeap();
		internal readonly StringHeap Strings = new StringHeap();
		internal readonly UserStringHeap UserStrings = new UserStringHeap();
		internal readonly GuidHeap Guids = new GuidHeap();
		internal readonly BlobHeap Blobs = new BlobHeap();

		internal ModuleBuilder(AssemblyBuilder asm, string moduleName, string fileName, bool emitSymbolInfo)
			: base(asm.universe)
		{
			this.asm = asm;
			this.moduleName = moduleName;
			this.fileName = fileName;
			if (emitSymbolInfo)
			{
				symbolWriter = SymbolSupport.CreateSymbolWriterFor(this);
			}
			// <Module> must be the first record in the TypeDef table
			moduleType = new TypeBuilder(this, "<Module>", null, 0);
			types.Add(moduleType);
		}

		internal void WriteTypeDefTable(MetadataWriter mw)
		{
			int fieldList = 1;
			int methodList = 1;
			foreach (TypeBuilder type in types)
			{
				type.WriteTypeDefRecord(mw, ref fieldList, ref methodList);
			}
		}

		internal void WriteMethodDefTable(int baseRVA, MetadataWriter mw)
		{
			int paramList = 1;
			foreach (TypeBuilder type in types)
			{
				type.WriteMethodDefRecords(baseRVA, mw, ref paramList);
			}
		}

		internal void WriteParamTable(MetadataWriter mw)
		{
			foreach (TypeBuilder type in types)
			{
				type.WriteParamRecords(mw);
			}
		}

		internal void WriteFieldTable(MetadataWriter mw)
		{
			foreach (TypeBuilder type in types)
			{
				type.WriteFieldRecords(mw);
			}
		}

		internal int AllocPseudoToken()
		{
			return nextPseudoToken--;
		}

		public TypeBuilder DefineType(string name)
		{
			return DefineType(name, TypeAttributes.Class);
		}

		public TypeBuilder DefineType(string name, TypeAttributes attr)
		{
			return DefineType(name, attr, null);
		}

		public TypeBuilder DefineType(string name, TypeAttributes attr, Type parent)
		{
			return DefineType(name, attr, parent, PackingSize.Unspecified, 0);
		}

		public TypeBuilder DefineType(string name, TypeAttributes attr, Type parent, int typesize)
		{
			return DefineType(name, attr, parent, PackingSize.Unspecified, typesize);
		}

		public TypeBuilder DefineType(string name, TypeAttributes attr, Type parent, PackingSize packsize)
		{
			return DefineType(name, attr, parent, packsize, 0);
		}

		public TypeBuilder DefineType(string name, TypeAttributes attr, Type parent, Type[] interfaces)
		{
			TypeBuilder tb = DefineType(name, attr, parent);
			foreach (Type iface in interfaces)
			{
				tb.AddInterfaceImplementation(iface);
			}
			return tb;
		}

		public TypeBuilder DefineType(string name, TypeAttributes attr, Type parent, PackingSize packingSize, int typesize)
		{
			if (parent == null && (attr & TypeAttributes.Interface) == 0)
			{
				parent = universe.System_Object;
			}
			TypeBuilder typeBuilder = new TypeBuilder(this, name, parent, attr);
			PostDefineType(typeBuilder, packingSize, typesize);
			return typeBuilder;
		}

		public EnumBuilder DefineEnum(string name, TypeAttributes visibility, Type underlyingType)
		{
			TypeBuilder tb = DefineType(name, (visibility & TypeAttributes.VisibilityMask) | TypeAttributes.Sealed, universe.System_Enum);
			FieldBuilder fb = tb.DefineField("value__", underlyingType, FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName);
			return new EnumBuilder(tb, fb);
		}

		internal TypeBuilder DefineNestedTypeHelper(TypeBuilder enclosingType, string name, TypeAttributes attr, Type parent, PackingSize packingSize, int typesize)
		{
			if (parent == null && (attr & TypeAttributes.Interface) == 0)
			{
				parent = universe.System_Object;
			}
			TypeBuilder typeBuilder = new TypeBuilder(enclosingType, name, parent, attr);
			PostDefineType(typeBuilder, packingSize, typesize);
			if (enclosingType != null)
			{
				NestedClassTable.Record rec = new NestedClassTable.Record();
				rec.NestedClass = typeBuilder.MetadataToken;
				rec.EnclosingClass = enclosingType.MetadataToken;
				this.NestedClass.AddRecord(rec);
			}
			return typeBuilder;
		}

		private void PostDefineType(TypeBuilder typeBuilder, PackingSize packingSize, int typesize)
		{
			types.Add(typeBuilder);
			fullNameToType.Add(typeBuilder.FullName, typeBuilder);
			if (packingSize != PackingSize.Unspecified || typesize != 0)
			{
				ClassLayoutTable.Record rec = new ClassLayoutTable.Record();
				rec.PackingSize = (short)packingSize;
				rec.ClassSize = typesize;
				rec.Parent = typeBuilder.MetadataToken;
				this.ClassLayout.AddRecord(rec);
			}
		}

		public FieldBuilder __DefineField(string name, Type type, Type[] requiredCustomModifiers, Type[] optionalCustomModifiers, FieldAttributes attributes)
		{
			return moduleType.DefineField(name, type, requiredCustomModifiers, optionalCustomModifiers, attributes);
		}

		public ConstructorBuilder __DefineModuleInitializer(MethodAttributes visibility)
		{
			return moduleType.DefineConstructor(visibility | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, CallingConventions.Standard, Type.EmptyTypes);
		}

		public FieldBuilder DefineUninitializedData(string name, int size, FieldAttributes attributes)
		{
			return moduleType.DefineUninitializedData(name, size, attributes);
		}

		public FieldBuilder DefineInitializedData(string name, byte[] data, FieldAttributes attributes)
		{
			return moduleType.DefineInitializedData(name, data, attributes);
		}

		public MethodBuilder DefineGlobalMethod(string name, MethodAttributes attributes, Type returnType, Type[] parameterTypes)
		{
			return moduleType.DefineMethod(name, attributes, returnType, parameterTypes);
		}

		public MethodBuilder DefineGlobalMethod(string name, MethodAttributes attributes, CallingConventions callingConvention, Type returnType, Type[] parameterTypes)
		{
			return moduleType.DefineMethod(name, attributes, callingConvention, returnType, parameterTypes);
		}

		public MethodBuilder DefineGlobalMethod(string name, MethodAttributes attributes, CallingConventions callingConvention, Type returnType, Type[] requiredReturnTypeCustomModifiers, Type[] optionalReturnTypeCustomModifiers, Type[] parameterTypes, Type[][] requiredParameterTypeCustomModifiers, Type[][] optionalParameterTypeCustomModifiers)
		{
			return moduleType.DefineMethod(name, attributes, callingConvention, returnType, requiredReturnTypeCustomModifiers, optionalReturnTypeCustomModifiers, parameterTypes, requiredParameterTypeCustomModifiers, optionalParameterTypeCustomModifiers);
		}

		public MethodBuilder DefinePInvokeMethod(string name, string dllName, MethodAttributes attributes, CallingConventions callingConvention, Type returnType, Type[] parameterTypes, CallingConvention nativeCallConv, CharSet nativeCharSet)
		{
			return moduleType.DefinePInvokeMethod(name, dllName, attributes, callingConvention, returnType, parameterTypes, nativeCallConv, nativeCharSet);
		}

		public MethodBuilder DefinePInvokeMethod(string name, string dllName, string entryName, MethodAttributes attributes, CallingConventions callingConvention, Type returnType, Type[] parameterTypes, CallingConvention nativeCallConv, CharSet nativeCharSet)
		{
			return moduleType.DefinePInvokeMethod(name, dllName, entryName, attributes, callingConvention, returnType, parameterTypes, nativeCallConv, nativeCharSet);
		}

		public void CreateGlobalFunctions()
		{
			moduleType.CreateType();
		}

		private void AddTypeForwarder(Type type)
		{
			ExportType(type);
			foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
			{
				// we export all nested types (i.e. even the private ones)
				// (this behavior is the same as the C# compiler)
				AddTypeForwarder(nested);
			}
		}

		private int ExportType(Type type)
		{
			ExportedTypeTable.Record rec = new ExportedTypeTable.Record();
			rec.TypeDefId = type.MetadataToken;
			rec.TypeName = this.Strings.Add(type.Name);
			if (type.IsNested)
			{
				rec.Flags = 0;
				rec.TypeNamespace = 0;
				rec.Implementation = ExportType(type.DeclaringType);
			}
			else
			{
				rec.Flags = 0x00200000;	// CorTypeAttr.tdForwarder
				string ns = type.Namespace;
				rec.TypeNamespace = ns == null ? 0 : this.Strings.Add(ns);
				rec.Implementation = ImportAssemblyRef(type.Assembly.GetName());
			}
			return 0x27000000 | this.ExportedType.FindOrAddRecord(rec);
		}

		internal void SetAssemblyCustomAttribute(CustomAttributeBuilder customBuilder)
		{
			if (customBuilder.Constructor.DeclaringType == universe.System_Runtime_CompilerServices_TypeForwardedToAttribute)
			{
				customBuilder = customBuilder.DecodeBlob(this.Assembly);
				AddTypeForwarder((Type)customBuilder.GetConstructorArgument(0));
			}
			else
			{
				SetCustomAttribute(0x20000001, customBuilder);
			}
		}

		public void SetCustomAttribute(ConstructorInfo con, byte[] binaryAttribute)
		{
			SetCustomAttribute(new CustomAttributeBuilder(con, binaryAttribute));
		}

		public void SetCustomAttribute(CustomAttributeBuilder customBuilder)
		{
			SetCustomAttribute(0x00000001, customBuilder);
		}

		internal void SetCustomAttribute(int token, CustomAttributeBuilder customBuilder)
		{
			Debug.Assert(!customBuilder.IsPseudoCustomAttribute);
			CustomAttributeTable.Record rec = new CustomAttributeTable.Record();
			rec.Parent = token;
			rec.Type = this.GetConstructorToken(customBuilder.Constructor).Token;
			rec.Value = customBuilder.WriteBlob(this);
			this.CustomAttribute.AddRecord(rec);
		}

		internal void AddDeclarativeSecurity(int token, System.Security.Permissions.SecurityAction securityAction, System.Security.PermissionSet permissionSet)
		{
			DeclSecurityTable.Record rec = new DeclSecurityTable.Record();
			rec.Action = (short)securityAction;
			rec.Parent = token;
			// like Ref.Emit, we're using the .NET 1.x xml format
			rec.PermissionSet = this.Blobs.Add(ByteBuffer.Wrap(System.Text.Encoding.Unicode.GetBytes(permissionSet.ToXml().ToString())));
			this.DeclSecurity.AddRecord(rec);
		}

		internal void AddDeclarativeSecurity(int token, List<CustomAttributeBuilder> declarativeSecurity)
		{
			Dictionary<int, List<CustomAttributeBuilder>> ordered = new Dictionary<int, List<CustomAttributeBuilder>>();
			foreach (CustomAttributeBuilder cab in declarativeSecurity)
			{
				int action;
				// check for HostProtectionAttribute without SecurityAction
				if (cab.ConstructorArgumentCount == 0)
				{
					action = (int)System.Security.Permissions.SecurityAction.LinkDemand;
				}
				else
				{
					action = (int)cab.GetConstructorArgument(0);
				}
				List<CustomAttributeBuilder> list;
				if (!ordered.TryGetValue(action, out list))
				{
					list = new List<CustomAttributeBuilder>();
					ordered.Add(action, list);
				}
				list.Add(cab);
			}
			foreach (KeyValuePair<int, List<CustomAttributeBuilder>> kv in ordered)
			{
				DeclSecurityTable.Record rec = new DeclSecurityTable.Record();
				rec.Action = (short)kv.Key;
				rec.Parent = token;
				rec.PermissionSet = WriteDeclSecurityBlob(kv.Value);
				this.DeclSecurity.AddRecord(rec);
			}
		}

		private int WriteDeclSecurityBlob(List<CustomAttributeBuilder> list)
		{
			ByteBuffer namedArgs = new ByteBuffer(100);
			ByteBuffer bb = new ByteBuffer(list.Count * 100);
			bb.Write((byte)'.');
			bb.WriteCompressedInt(list.Count);
			foreach (CustomAttributeBuilder cab in list)
			{
				bb.Write(cab.Constructor.DeclaringType.AssemblyQualifiedName);
				namedArgs.Clear();
				cab.WriteNamedArgumentsForDeclSecurity(this, namedArgs);
				bb.WriteCompressedInt(namedArgs.Length);
				bb.Write(namedArgs);
			}
			return this.Blobs.Add(bb);
		}

		public void DefineManifestResource(string name, Stream stream, ResourceAttributes attribute)
		{
			ManifestResourceTable.Record rec = new ManifestResourceTable.Record();
			rec.Offset = manifestResources.Position;
			rec.Flags = (int)attribute;
			rec.Name = this.Strings.Add(name);
			rec.Implementation = 0;
			this.ManifestResource.AddRecord(rec);
			manifestResources.Write(0);	// placeholder for the length
			manifestResources.Write(stream);
			int savePosition = manifestResources.Position;
			manifestResources.Position = rec.Offset;
			manifestResources.Write(savePosition - (manifestResources.Position + 4));
			manifestResources.Position = savePosition;
		}

		public override Assembly Assembly
		{
			get { return asm; }
		}

		internal override Type GetTypeImpl(string typeName)
		{
			TypeBuilder type;
			fullNameToType.TryGetValue(typeName, out type);
			return type;
		}

		internal override void GetTypesImpl(List<Type> list)
		{
			foreach (Type type in types)
			{
				if (type != moduleType)
				{
					list.Add(type);
				}
			}
		}

		public ISymbolDocumentWriter DefineDocument(string url, Guid language, Guid languageVendor, Guid documentType)
		{
			return symbolWriter.DefineDocument(url, language, languageVendor, documentType);
		}

		public TypeToken GetTypeToken(string name)
		{
			return new TypeToken(GetType(name, true, false).MetadataToken);
		}

		public TypeToken GetTypeToken(Type type)
		{
			if (type.Module == this)
			{
				return new TypeToken(type.GetModuleBuilderToken());
			}
			else
			{
				return new TypeToken(ImportType(type));
			}
		}

		internal int GetTypeTokenForMemberRef(Type type)
		{
			if (type.IsGenericTypeDefinition)
			{
				// this could be optimized, but since this is a very infrequent operation, we don't care
				return GetTypeToken(type.MakeGenericType(type.GetGenericArguments())).Token;
			}
			else if (type.IsModulePseudoType)
			{
				return 0x1A000000 | this.ModuleRef.Add(this.Strings.Add(type.Module.ScopeName));
			}
			else
			{
				return GetTypeToken(type).Token;
			}
		}

		private static bool IsFromGenericTypeDefinition(MemberInfo member)
		{
			Type decl = member.DeclaringType;
			return decl != null && decl.IsGenericTypeDefinition;
		}

		public FieldToken GetFieldToken(FieldInfo field)
		{
			// NOTE for some reason, when TypeBuilder.GetFieldToken() is used on a field in a generic type definition,
			// a memberref token is returned (confirmed on .NET) unlike for Get(Method|Constructor)Token which always
			// simply returns the MethodDef token (if the method is from the same module).
			FieldBuilder fb = field as FieldBuilder;
			if (fb != null && fb.Module == this && !IsFromGenericTypeDefinition(fb))
			{
				return new FieldToken(fb.MetadataToken);
			}
			else
			{
				return new FieldToken(ImportMember(field));
			}
		}

		public MethodToken GetMethodToken(MethodInfo method)
		{
			MethodBuilder mb = method as MethodBuilder;
			if (mb != null && mb.ModuleBuilder == this)
			{
				return new MethodToken(mb.MetadataToken);
			}
			else
			{
				return new MethodToken(ImportMember(method));
			}
		}

		// when we refer to a method on a generic type definition in the IL stream,
		// we need to use a MemberRef (even if the method is in the same module)
		internal MethodToken GetMethodTokenForIL(MethodInfo method)
		{
			if (IsFromGenericTypeDefinition(method))
			{
				return new MethodToken(ImportMember(method));
			}
			else
			{
				return GetMethodToken(method);
			}
		}

		public MethodToken GetConstructorToken(ConstructorInfo constructor)
		{
			if (constructor.Module == this && constructor.GetMethodInfo() is MethodBuilder)
			{
				return new MethodToken(constructor.MetadataToken);
			}
			else
			{
				return new MethodToken(ImportMember(constructor));
			}
		}

		internal int ImportMember(MethodBase member)
		{
			int token;
			if (!importedMembers.TryGetValue(member, out token))
			{
				token = member.ImportTo(this);
				importedMembers.Add(member, token);
			}
			return token;
		}

		internal int ImportMember(FieldInfo member)
		{
			int token;
			if (!importedMembers.TryGetValue(member, out token))
			{
				token = member.ImportTo(this);
				importedMembers.Add(member, token);
			}
			return token;
		}

		internal int ImportMethodSpec(MethodInfo method, ByteBuffer instantiation)
		{
			MethodSpecTable.Record rec = new MethodSpecTable.Record();
			rec.Method = GetMethodToken(method.GetGenericMethodDefinition()).Token;
			rec.Instantiation = this.Blobs.Add(instantiation);
			return 0x2B000000 | this.MethodSpec.AddRecord(rec);
		}

		internal int ImportMethodOrField(Type declaringType, string name, Signature sig)
		{
			MemberRefTable.Record rec = new MemberRefTable.Record();
			rec.Class = GetTypeTokenForMemberRef(declaringType);
			rec.Name = this.Strings.Add(name);
			ByteBuffer bb = new ByteBuffer(16);
			sig.WriteSig(this, bb);
			rec.Signature = this.Blobs.Add(bb);
			return 0x0A000000 | this.MemberRef.AddRecord(rec);
		}

		internal int ImportType(Type type)
		{
			int token;
			if (!typeTokens.TryGetValue(type, out token))
			{
				if (type.HasElementType || (type.IsGenericType && !type.IsGenericTypeDefinition))
				{
					ByteBuffer spec = new ByteBuffer(5);
					Signature.WriteTypeSpec(this, spec, type);
					token = 0x1B000000 | this.TypeSpec.AddRecord(this.Blobs.Add(spec));
				}
				else
				{
					TypeRefTable.Record rec = new TypeRefTable.Record();
					if (type.IsNested)
					{
						rec.ResolutionScope = GetTypeToken(type.DeclaringType).Token;
						rec.TypeName = this.Strings.Add(TypeNameParser.Unescape(type.Name));
						rec.TypeNameSpace = 0;
					}
					else
					{
						rec.ResolutionScope = ImportAssemblyRef(type.Assembly.GetName());
						rec.TypeName = this.Strings.Add(TypeNameParser.Unescape(type.Name));
						string ns = type.Namespace;
						rec.TypeNameSpace = ns == null ? 0 : this.Strings.Add(TypeNameParser.Unescape(ns));
					}
					token = 0x01000000 | this.TypeRef.AddRecord(rec);
				}
				typeTokens.Add(type, token);
			}
			return token;
		}

		private int ImportAssemblyRef(AssemblyName asm)
		{
			int token;
			if (!referencedAssemblies.TryGetValue(asm, out token))
			{
				AssemblyRefTable.Record rec = new AssemblyRefTable.Record();
				Version ver = asm.Version;
				rec.MajorVersion = (short)ver.Major;
				rec.MinorVersion = (short)ver.Minor;
				rec.BuildNumber = (short)ver.Build;
				rec.RevisionNumber = (short)ver.Revision;
				rec.Flags = 0;
				byte[] publicKeyOrToken = null;
				if (usePublicKeyAssemblyReference)
				{
					publicKeyOrToken = asm.GetPublicKey();
				}
				if (publicKeyOrToken == null || publicKeyOrToken.Length == 0)
				{
					publicKeyOrToken = asm.GetPublicKeyToken();
				}
				else
				{
					const int PublicKey = 0x0001;
					rec.Flags |= PublicKey;
				}
				rec.PublicKeyOrToken = this.Blobs.Add(ByteBuffer.Wrap(publicKeyOrToken));
				rec.Name = this.Strings.Add(asm.Name);
				if (asm.CultureInfo != null)
				{
					rec.Culture = this.Strings.Add(asm.CultureInfo.Name);
				}
				else
				{
					rec.Culture = 0;
				}
				rec.HashValue = 0;
				token = 0x23000000 | this.AssemblyRef.AddRecord(rec);
				referencedAssemblies.Add(asm, token);
			}
			return token;
		}

		internal void WriteSymbolTokenMap()
		{
			for (int i = 0; i < resolvedTokens.Count; i++)
			{
				int newToken = resolvedTokens[i];
				// The symbol API doesn't support remapping arbitrary integers, the types have to be the same,
				// so we copy the type from the newToken, because our pseudo tokens don't have a type.
				// (see MethodToken.SymbolToken)
				int oldToken = (i + 1) | (newToken & ~0xFFFFFF);
				SymbolSupport.RemapToken(symbolWriter, oldToken, newToken);
			}
		}

		internal void RegisterTokenFixup(int pseudoToken, int realToken)
		{
			int index = -(pseudoToken + 1);
			while (resolvedTokens.Count <= index)
			{
				resolvedTokens.Add(0);
			}
			resolvedTokens[index] = realToken;
		}

		internal bool IsPseudoToken(int token)
		{
			return token < 0;
		}

		internal int ResolvePseudoToken(int pseudoToken)
		{
			int index = -(pseudoToken + 1);
			return resolvedTokens[index];
		}

		internal void FixupMethodBodyTokens()
		{
			int methodToken = 0x06000001;
			int fieldToken = 0x04000001;
			int parameterToken = 0x08000001;
			foreach (TypeBuilder type in types)
			{
				type.ResolveMethodAndFieldTokens(ref methodToken, ref fieldToken, ref parameterToken);
			}
			foreach (int offset in tokenFixupOffsets)
			{
				methodBodies.Position = offset;
				int pseudoToken = methodBodies.GetInt32AtCurrentPosition();
				methodBodies.Write(ResolvePseudoToken(pseudoToken));
			}
		}

		internal int MetadataLength
		{
			get
			{
				return (Blobs.IsEmpty ? 92 : 108 + Blobs.Length) + Tables.Length + Strings.Length + UserStrings.Length + Guids.Length;
			}
		}

		internal void WriteMetadata(MetadataWriter mw)
		{
			mw.Write(0x424A5342);			// Signature ("BSJB")
			mw.Write((ushort)1);			// MajorVersion
			mw.Write((ushort)1);			// MinorVersion
			mw.Write(0);					// Reserved
			byte[] version = StringToPaddedUTF8(asm.ImageRuntimeVersion);
			mw.Write(version.Length);		// Length
			mw.Write(version);
			mw.Write((ushort)0);			// Flags
			int offset;
			// #Blob is the only optional heap
			if (Blobs.IsEmpty)
			{
				mw.Write((ushort)4);		// Streams
				offset = 92;
			}
			else
			{
				mw.Write((ushort)5);		// Streams
				offset = 108;
			}

			// Streams
			mw.Write(offset);				// Offset
			mw.Write(Tables.Length);		// Size
			mw.Write(StringToPaddedUTF8("#~"));
			offset += Tables.Length;

			mw.Write(offset);				// Offset
			mw.Write(Strings.Length);		// Size
			mw.Write(StringToPaddedUTF8("#Strings"));
			offset += Strings.Length;

			mw.Write(offset);				// Offset
			mw.Write(UserStrings.Length);	// Size
			mw.Write(StringToPaddedUTF8("#US"));
			offset += UserStrings.Length;

			mw.Write(offset);				// Offset
			mw.Write(Guids.Length);			// Size
			mw.Write(StringToPaddedUTF8("#GUID"));
			offset += Guids.Length;

			if (!Blobs.IsEmpty)
			{
				mw.Write(offset);				// Offset
				mw.Write(Blobs.Length);			// Size
				mw.Write(StringToPaddedUTF8("#Blob"));
			}

			Tables.Write(mw);
			Strings.Write(mw);
			UserStrings.Write(mw);
			Guids.Write(mw);
			if (!Blobs.IsEmpty)
			{
				Blobs.Write(mw);
			}
		}

		private static byte[] StringToPaddedUTF8(string str)
		{
			byte[] buf = new byte[(System.Text.Encoding.UTF8.GetByteCount(str) + 4) & ~3];
			System.Text.Encoding.UTF8.GetBytes(str, 0, str.Length, buf, 0);
			return buf;
		}

		internal void ExportTypes(int fileToken, ModuleBuilder manifestModule)
		{
			Dictionary<Type, int> declaringTypes = new Dictionary<Type, int>();
			foreach (TypeBuilder type in types)
			{
				if (type != moduleType && IsVisible(type))
				{
					ExportedTypeTable.Record rec = new ExportedTypeTable.Record();
					rec.Flags = (int)type.Attributes;
					rec.TypeDefId = type.MetadataToken & 0xFFFFFF;
					rec.TypeName = manifestModule.Strings.Add(type.Name);
					string ns = type.Namespace;
					rec.TypeNamespace = ns == null ? 0 : manifestModule.Strings.Add(ns);
					if (type.IsNested)
					{
						rec.Implementation = declaringTypes[type.DeclaringType];
					}
					else
					{
						rec.Implementation = fileToken;
					}
					int exportTypeToken = 0x27000000 | manifestModule.ExportedType.AddRecord(rec);
					declaringTypes.Add(type, exportTypeToken);
				}
			}
		}

		private static bool IsVisible(Type type)
		{
			// NOTE this is not the same as Type.IsVisible, because that doesn't take into account family access
			return type.IsPublic || ((type.IsNestedFamily || type.IsNestedFamORAssem || type.IsNestedPublic) && IsVisible(type.DeclaringType));
		}

		internal void AddConstant(int parentToken, object defaultValue)
		{
			ConstantTable.Record rec = new ConstantTable.Record();
			rec.Parent = parentToken;
			ByteBuffer val = new ByteBuffer(16);
			if (defaultValue == null)
			{
				rec.Type = Signature.ELEMENT_TYPE_CLASS;
				val.Write((int)0);
			}
			else if (defaultValue is bool)
			{
				rec.Type = Signature.ELEMENT_TYPE_BOOLEAN;
				val.Write((bool)defaultValue ? (byte)1 : (byte)0);
			}
			else if (defaultValue is char)
			{
				rec.Type = Signature.ELEMENT_TYPE_CHAR;
				val.Write((char)defaultValue);
			}
			else if (defaultValue is sbyte)
			{
				rec.Type = Signature.ELEMENT_TYPE_I1;
				val.Write((sbyte)defaultValue);
			}
			else if (defaultValue is byte)
			{
				rec.Type = Signature.ELEMENT_TYPE_U1;
				val.Write((byte)defaultValue);
			}
			else if (defaultValue is short)
			{
				rec.Type = Signature.ELEMENT_TYPE_I2;
				val.Write((short)defaultValue);
			}
			else if (defaultValue is ushort)
			{
				rec.Type = Signature.ELEMENT_TYPE_U2;
				val.Write((ushort)defaultValue);
			}
			else if (defaultValue is int)
			{
				rec.Type = Signature.ELEMENT_TYPE_I4;
				val.Write((int)defaultValue);
			}
			else if (defaultValue is uint)
			{
				rec.Type = Signature.ELEMENT_TYPE_U4;
				val.Write((uint)defaultValue);
			}
			else if (defaultValue is long)
			{
				rec.Type = Signature.ELEMENT_TYPE_I8;
				val.Write((long)defaultValue);
			}
			else if (defaultValue is ulong)
			{
				rec.Type = Signature.ELEMENT_TYPE_U8;
				val.Write((ulong)defaultValue);
			}
			else if (defaultValue is float)
			{
				rec.Type = Signature.ELEMENT_TYPE_R4;
				val.Write((float)defaultValue);
			}
			else if (defaultValue is double)
			{
				rec.Type = Signature.ELEMENT_TYPE_R8;
				val.Write((double)defaultValue);
			}
			else if (defaultValue is string)
			{
				rec.Type = Signature.ELEMENT_TYPE_STRING;
				foreach (char c in (string)defaultValue)
				{
					val.Write(c);
				}
			}
			else if (defaultValue is DateTime)
			{
				rec.Type = Signature.ELEMENT_TYPE_I8;
				val.Write(((DateTime)defaultValue).Ticks);
			}
			else
			{
				throw new ArgumentException();
			}
			rec.Value = this.Blobs.Add(val);
			this.Constant.AddRecord(rec);
		}

		ModuleBuilder ITypeOwner.ModuleBuilder
		{
			get { return this; }
		}

		public override Type ResolveType(int metadataToken, Type[] genericTypeArguments, Type[] genericMethodArguments)
		{
			if (genericTypeArguments != null || genericMethodArguments != null)
			{
				throw new NotImplementedException();
			}
			return types[(metadataToken & 0xFFFFFF) - 1];
		}

		public override MethodBase ResolveMethod(int metadataToken, Type[] genericTypeArguments, Type[] genericMethodArguments)
		{
			if (genericTypeArguments != null || genericMethodArguments != null)
			{
				throw new NotImplementedException();
			}
			// this method is inefficient, but since it isn't used we don't care
			if ((metadataToken >> 24) == MemberRefTable.Index)
			{
				foreach (KeyValuePair<MemberInfo, int> kv in importedMembers)
				{
					if (kv.Value == metadataToken)
					{
						return (MethodBase)kv.Key;
					}
				}
			}
			// HACK if we're given a SymbolToken, we need to convert back
			if ((metadataToken & 0xFF000000) == 0x06000000)
			{
				metadataToken = -(metadataToken & 0x00FFFFFF);
			}
			foreach (Type type in types)
			{
				MethodBase method = ((TypeBuilder)type).LookupMethod(metadataToken);
				if (method != null)
				{
					return method;
				}
			}
			return ((TypeBuilder)moduleType).LookupMethod(metadataToken);
		}

		public override FieldInfo ResolveField(int metadataToken, Type[] genericTypeArguments, Type[] genericMethodArguments)
		{
			throw new NotImplementedException();
		}

		public override MemberInfo ResolveMember(int metadataToken, Type[] genericTypeArguments, Type[] genericMethodArguments)
		{
			throw new NotImplementedException();
		}

		public override string ResolveString(int metadataToken)
		{
			throw new NotImplementedException();
		}

		public override string FullyQualifiedName
		{
			get { return Path.GetFullPath(Path.Combine(asm.dir, fileName)); }
		}

		public override string Name
		{
			get { return fileName; }
		}

		public override Guid ModuleVersionId
		{
			get { return mvid; }
		}

		public override Type[] __ResolveOptionalParameterTypes(int metadataToken)
		{
			throw new NotImplementedException();
		}

		public override string ScopeName
		{
			get { return moduleName; }
		}

		public ISymbolWriter GetSymWriter()
		{
			return symbolWriter;
		}

		public void DefineUnmanagedResource(string resourceFileName)
		{
			// This method reads the specified resource file (Win32 .res file) and converts it into the appropriate format and embeds it in the .rsrc section,
			// also setting the Resource Directory entry.
			this.unmanagedResources = System.IO.File.ReadAllBytes(resourceFileName);
		}

		public bool IsTransient()
		{
			return false;
		}

		public void SetUserEntryPoint(MethodInfo entryPoint)
		{
			int token = entryPoint.MetadataToken;
			if (token < 0)
			{
				token = -token | 0x06000000;
			}
			if (symbolWriter != null)
			{
				symbolWriter.SetUserEntryPoint(new SymbolToken(token));
			}
		}

		public StringToken GetStringConstant(string str)
		{
			return new StringToken(this.UserStrings.Add(str) | (0x70 << 24));
		}

		public SignatureToken GetSignatureToken(SignatureHelper sigHelper)
		{
			return new SignatureToken(this.StandAloneSig.Add(this.Blobs.Add(sigHelper.GetSignature(this))) | (StandAloneSigTable.Index << 24));
		}

		public SignatureToken GetSignatureToken(byte[] sigBytes, int sigLength)
		{
			return new SignatureToken(this.StandAloneSig.Add(this.Blobs.Add(ByteBuffer.Wrap(sigBytes, sigLength))) | (StandAloneSigTable.Index << 24));
		}

		public MethodInfo GetArrayMethod(Type arrayClass, string methodName, CallingConventions callingConvention, Type returnType, Type[] parameterTypes)
		{
			throw new NotImplementedException();
		}

		public MethodToken GetArrayMethodToken(Type arrayClass, string methodName, CallingConventions callingConvention, Type returnType, Type[] parameterTypes)
		{
			return GetMethodToken(GetArrayMethod(arrayClass, methodName, callingConvention, returnType, parameterTypes));
		}

		internal override Type GetModuleType()
		{
			return moduleType;
		}

		internal override IKVM.Reflection.Reader.ByteReader GetBlob(int blobIndex)
		{
			return Blobs.GetBlob(blobIndex);
		}

		internal int GetSignatureBlobIndex(Signature sig)
		{
			ByteBuffer bb = new ByteBuffer(16);
			sig.WriteSig(this, bb);
			return this.Blobs.Add(bb);
		}

		// non-standard API
		public long __ImageBase
		{
			get { return imageBaseAddress; }
			set { imageBaseAddress = value; }
		}

		public override int MDStreamVersion
		{
			get { return asm.mdStreamVersion; }
		}
	}
}
