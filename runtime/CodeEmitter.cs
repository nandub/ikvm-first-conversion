/*
  Copyright (C) 2002, 2004, 2005, 2006, 2008 Jeroen Frijters

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
using System.Collections.Generic;
using System.Reflection;
#if IKVM_REF_EMIT
using IKVM.Reflection.Emit;
#else
using System.Reflection.Emit;
#endif
using System.Runtime.InteropServices;
using System.Diagnostics.SymbolStore;
using System.Diagnostics;

namespace IKVM.Internal
{
	class CodeEmitterLabel
	{
		private Label label;
		private int offset = -1;

		internal CodeEmitterLabel(Label label)
		{
			this.label = label;
		}

		internal Label Label
		{
			get
			{
				return label;
			}
		}

		internal int Offset
		{
			get
			{
				return offset;
			}
			set
			{
				offset = value;
			}
		}
	}

	class CodeEmitter
	{
		private ILGenerator ilgen_real;
		private int offset;
		private Stack<bool> exceptionStack = new Stack<bool>();
		private bool inFinally;
#if STATIC_COMPILER
		private IKVM.Attributes.LineNumberTableAttribute.LineNumberWriter linenums;
#endif // STATIC_COMPILER
		private Expr[] stackArray = new Expr[8];
		private int topOfStack;
		private CodeEmitterLabel lazyBranch;
		private LocalBuilder[] tempLocals = new LocalBuilder[32];
#if LABELCHECK
		private Dictionary<Label, System.Diagnostics.StackFrame> labels = new Dictionary<Label, System.Diagnostics.StackFrame>();
#endif

		internal static CodeEmitter Create(MethodBuilder mb)
		{
			return new CodeEmitter(mb.GetILGenerator());
		}

		internal static CodeEmitter Create(ConstructorBuilder cb)
		{
			return new CodeEmitter(cb.GetILGenerator());
		}

#if !STATIC_COMPILER
		internal static CodeEmitter Create(DynamicMethod dm)
		{
			return new CodeEmitter(dm.GetILGenerator());
		}
#endif

		private CodeEmitter(ILGenerator ilgen)
		{
			this.ilgen_real = ilgen;
		}

		private void PushStack(Expr expr)
		{
			Debug.Assert(expr != null);
			if (topOfStack == stackArray.Length)
			{
				Array.Resize(ref stackArray, stackArray.Length * 2);
			}
			stackArray[topOfStack++] = expr;
		}

		private void PushStackMayBeNull(Expr expr)
		{
			if (expr != null)
			{
				PushStack(expr);
			}
		}

		private Expr PopStack()
		{
			if (topOfStack == 0)
			{
				return null;
			}
			return stackArray[--topOfStack];
		}

		private Expr PeekStack()
		{
			if (topOfStack == 0)
			{
				return null;
			}
			return stackArray[topOfStack - 1];
		}

		internal LocalBuilder UnsafeAllocTempLocal(Type type)
		{
			int free = -1;
			for (int i = 0; i < tempLocals.Length; i++)
			{
				LocalBuilder lb = tempLocals[i];
				if (lb == null)
				{
					if (free == -1)
					{
						free = i;
					}
				}
				else if (lb.LocalType == type)
				{
					return lb;
				}
			}
			LocalBuilder lb1 = DeclareLocal(type);
			if (free != -1)
			{
				tempLocals[free] = lb1;
			}
			return lb1;
		}

		internal LocalBuilder AllocTempLocal(Type type)
		{
			for (int i = 0; i < tempLocals.Length; i++)
			{
				LocalBuilder lb = tempLocals[i];
				if (lb != null && lb.LocalType == type)
				{
					tempLocals[i] = null;
					return lb;
				}
			}
			return DeclareLocal(type);
		}

		internal void ReleaseTempLocal(LocalBuilder lb)
		{
			for (int i = 0; i < tempLocals.Length; i++)
			{
				if (tempLocals[i] == null)
				{
					tempLocals[i] = lb;
					break;
				}
			}
		}

		internal bool IsStackEmpty
		{
			get
			{
				return topOfStack == 0;
			}
		}

		internal int GetILOffset()
		{
			LazyGen();
			return offset;
		}

		internal void BeginCatchBlock(Type exceptionType)
		{
			LazyGen();
			offset += 5;
			ilgen_real.BeginCatchBlock(exceptionType);
		}

		internal void BeginExceptFilterBlock()
		{
			LazyGen();
			offset += 5;
			ilgen_real.BeginExceptFilterBlock();
		}

		internal Label BeginExceptionBlock()
		{
			LazyGen();
			exceptionStack.Push(inFinally);
			inFinally = false;
			return ilgen_real.BeginExceptionBlock();
		}

		internal void BeginFaultBlock()
		{
			LazyGen();
			inFinally = true;
			offset += 5;
			ilgen_real.BeginFaultBlock();
		}

		internal void BeginFinallyBlock()
		{
			LazyGen();
			inFinally = true;
			offset += 5;
			ilgen_real.BeginFinallyBlock();
		}

		internal void BeginScope()
		{
			LazyGen();
			ilgen_real.BeginScope();
		}

		internal LocalBuilder DeclareLocal(Type localType)
		{
			return ilgen_real.DeclareLocal(localType);
		}

		internal CodeEmitterLabel DefineLabel()
		{
			Label label = ilgen_real.DefineLabel();
#if LABELCHECK
			labels.Add(label, new System.Diagnostics.StackFrame(1, true));
#endif
			return new CodeEmitterLabel(label);
		}

		internal void Emit(OpCode opcode)
		{
			LazyGen();
			offset += opcode.Size;
			ilgen_real.Emit(opcode);
		}

		internal void Emit(OpCode opcode, byte arg)
		{
			LazyGen();
			offset += opcode.Size + 1;
			ilgen_real.Emit(opcode, arg);
		}

		internal void Emit(OpCode opcode, ConstructorInfo con)
		{
			LazyGen();
			offset += opcode.Size + 4;
			ilgen_real.Emit(opcode, con);
		}

		internal void Emit(OpCode opcode, double arg)
		{
			LazyGen();
			offset += opcode.Size + 8;
			ilgen_real.Emit(opcode, arg);
		}

		internal void Emit(OpCode opcode, FieldInfo field)
		{
			LazyGen();
			offset += opcode.Size + 4;
			ilgen_real.Emit(opcode, field);
		}

		internal void Emit(OpCode opcode, short arg)
		{
			LazyGen();
			offset += opcode.Size + 2;
			ilgen_real.Emit(opcode, arg);
		}

		internal void Emit(OpCode opcode, int arg)
		{
			LazyGen();
			offset += opcode.Size + 4;
			ilgen_real.Emit(opcode, arg);
		}

		internal void Emit(OpCode opcode, long arg)
		{
			LazyGen();
			offset += opcode.Size + 8;
			ilgen_real.Emit(opcode, arg);
		}

		internal void Emit(OpCode opcode, CodeEmitterLabel label)
		{
			LazyGen();
			if(label.Offset == -1)
			{
				if(opcode.Value == OpCodes.Br.Value)
				{
					lazyBranch = label;
					return;
				}
			}
			else if(offset - label.Offset < 126)
			{
				if(opcode.Value == OpCodes.Brtrue.Value)
				{
					opcode = OpCodes.Brtrue_S;
				}
				else if(opcode.Value == OpCodes.Brfalse.Value)
				{
					opcode = OpCodes.Brfalse_S;
				}
				else if(opcode.Value == OpCodes.Br.Value)
				{
					opcode = OpCodes.Br_S;
				}
				else if(opcode.Value == OpCodes.Beq.Value)
				{
					opcode = OpCodes.Beq_S;
				}
				else if(opcode.Value == OpCodes.Bne_Un.Value)
				{
					opcode = OpCodes.Bne_Un_S;
				}
				else if(opcode.Value == OpCodes.Ble.Value)
				{
					opcode = OpCodes.Ble_S;
				}
				else if(opcode.Value == OpCodes.Blt.Value)
				{
					opcode = OpCodes.Blt_S;
				}
				else if(opcode.Value == OpCodes.Bge.Value)
				{
					opcode = OpCodes.Bge_S;
				}
				else if(opcode.Value == OpCodes.Bgt.Value)
				{
					opcode = OpCodes.Bgt_S;
				}
			}
			offset += opcode.Size;
			ilgen_real.Emit(opcode, label.Label);
			switch(opcode.OperandType)
			{
				case OperandType.InlineBrTarget:
					offset += 4;
					break;
				case OperandType.ShortInlineBrTarget:
					offset += 1;
					break;
				default:
					throw new NotImplementedException();
			}
		}

		internal void Emit(OpCode opcode, CodeEmitterLabel[] labels)
		{
			LazyGen();
			offset += 5 + labels.Length * 4;
			Label[] real = new Label[labels.Length];
			for(int i = 0; i < labels.Length; i++)
			{
				real[i] = labels[i].Label;
			}
			ilgen_real.Emit(opcode, real);
		}

		internal void Emit(OpCode opcode, LocalBuilder local)
		{
			LazyGen();
			ilgen_real.Emit(opcode, local);
			int index = local.LocalIndex;
			if(index < 4 && opcode.Value != OpCodes.Ldloca.Value && opcode.Value != OpCodes.Ldloca_S.Value)
			{
				offset += 1;
			}
			else if(index < 256)
			{
				offset += 2;
			}
			else
			{
				offset += 4;
			}
		}

		internal void Emit(OpCode opcode, MethodInfo meth)
		{
			LazyGen();
			offset += opcode.Size + 4;
			ilgen_real.Emit(opcode, meth);
		}

		internal void Emit(OpCode opcode, sbyte arg)
		{
			LazyGen();
			offset += opcode.Size + 1;
			ilgen_real.Emit(opcode, arg);
		}

		internal void Emit(OpCode opcode, SignatureHelper signature)
		{
			LazyGen();
			offset += opcode.Size;
			ilgen_real.Emit(opcode, signature);
			throw new NotImplementedException();
		}

		internal void Emit(OpCode opcode, float arg)
		{
			LazyGen();
			offset += opcode.Size + 4;
			ilgen_real.Emit(opcode, arg);
		}

		internal void Emit(OpCode opcode, string arg)
		{
			LazyGen();
			offset += opcode.Size + 4;
			ilgen_real.Emit(opcode, arg);
		}

		internal void Emit(OpCode opcode, Type cls)
		{
			LazyGen();
			offset += opcode.Size + 4;
			ilgen_real.Emit(opcode, cls);
		}

		internal void EmitCall(OpCode opcode, MethodInfo methodInfo, Type[] optionalParameterTypes)
		{
			LazyGen();
			offset += opcode.Size;
			ilgen_real.EmitCall(opcode, methodInfo, optionalParameterTypes);
			throw new NotImplementedException();
		}

		internal void EmitCalli(OpCode opcode, CallingConvention unmanagedCallConv, Type returnType, Type[] parameterTypes)
		{
			LazyGen();
			offset += 5;
			ilgen_real.EmitCalli(opcode, unmanagedCallConv, returnType, parameterTypes);
		}

		internal void EmitCalli(OpCode opcode, CallingConventions callingConvention, Type returnType, Type[] parameterTypes, Type[] optionalParameterTypes)
		{
			LazyGen();
			offset += 5;
			ilgen_real.EmitCalli(opcode, callingConvention, returnType, parameterTypes, optionalParameterTypes);
		}

		internal void EmitWriteLine(FieldInfo fld)
		{
			LazyGen();
			ilgen_real.EmitWriteLine(fld);
			throw new NotImplementedException();
		}

		internal void EmitWriteLine(LocalBuilder localBuilder)
		{
			LazyGen();
			ilgen_real.EmitWriteLine(localBuilder);
			throw new NotImplementedException();
		}

		internal void EmitWriteLine(string value)
		{
			LazyGen();
			offset += 10;
			ilgen_real.EmitWriteLine(value);
		}

		internal void EndExceptionBlock()
		{
			LazyGen();
			if(inFinally)
			{
				offset += 1;
			}
			else
			{
				offset += 5;
			}
			inFinally = exceptionStack.Pop();
			ilgen_real.EndExceptionBlock();
		}

		internal void EndScope()
		{
			LazyGen();
			ilgen_real.EndScope();
		}

		internal void MarkLabel(CodeEmitterLabel loc)
		{
			if(lazyBranch == loc)
			{
				lazyBranch = null;
			}
			LazyGen();
#if LABELCHECK
			labels.Remove(loc.Label);
#endif
			ilgen_real.MarkLabel(loc.Label);
			loc.Offset = offset;
		}

		internal void MarkSequencePoint(ISymbolDocumentWriter document, int startLine, int startColumn, int endLine, int endColumn)
		{
			LazyGen();
			ilgen_real.MarkSequencePoint(document, startLine, startColumn, endLine, endColumn);
		}

		internal void ThrowException(Type excType)
		{
			LazyGen();
			offset += 6;
			ilgen_real.ThrowException(excType);
		}

		internal void UsingNamespace(string usingNamespace)
		{
			LazyGen();
			ilgen_real.UsingNamespace(usingNamespace);
		}

#if STATIC_COMPILER
		internal void SetLineNumber(ushort line)
		{
			LazyGen();
			if(linenums == null)
			{
				linenums = new IKVM.Attributes.LineNumberTableAttribute.LineNumberWriter(32);
			}
			linenums.AddMapping(offset, line);
		}

		internal void EmitLineNumberTable(MethodBase mb)
		{
			if(linenums != null)
			{
				AttributeHelper.SetLineNumberTable(mb, linenums);
			}
		}
#endif // STATIC_COMPILER

		internal void LazyEmitPop()
		{
			Expr exp = PeekStack();
			if (exp == null || exp.HasSideEffect)
			{
				Emit(OpCodes.Pop);
			}
			else
			{
				PopStack();
			}
		}

		internal void LazyEmitLoadClass(Type type)
		{
			PushStack(new ClassLiteralExpr(type));
		}

		internal void LazyEmitBox(Type type)
		{
			PushStack(new BoxExpr(PopStack(), type));
		}

		internal void LazyEmitUnbox(Type type)
		{
			BoxExpr box = PeekStack() as BoxExpr;
			if(box != null)
			{
				PopStack();
				PushStack(new BoxUnboxExpr(box.Expr, type));
			}
			else
			{
				PushStack(new UnboxExpr(PopStack(), type));
			}
		}

		internal void LazyEmitLdobj(Type type)
		{
			BoxUnboxExpr boxunbox = PeekStack() as BoxUnboxExpr;
			if(boxunbox != null)
			{
				PopStack();
				// box/unbox+ldobj annihilate each other
				PushStackMayBeNull(boxunbox.Expr);
			}
			else
			{
				PushStack(new LdobjExpr(PopStack(), type));
			}
		}

		internal void LazyEmitUnboxSpecial(Type type)
		{
			BoxExpr box = PeekStack() as BoxExpr;
			if(box != null)
			{
				PopStack();
				// the unbox and lazy box cancel each other out
				PushStackMayBeNull(box.Expr);
			}
			else
			{
				// NOTE if the reference is null, we treat it as a default instance of the value type.
				Emit(OpCodes.Dup);
				CodeEmitterLabel label1 = DefineLabel();
				Emit(OpCodes.Brtrue_S, label1);
				Emit(OpCodes.Pop);
				Emit(OpCodes.Ldloc, DeclareLocal(type));
				CodeEmitterLabel label2 = DefineLabel();
				Emit(OpCodes.Br_S, label2);
				MarkLabel(label1);
				Emit(OpCodes.Unbox, type);
				Emit(OpCodes.Ldobj, type);
				MarkLabel(label2);
			}
		}

		internal void LazyEmitLdnull()
		{
			PushStack(new NullExpr());
		}

		internal void LazyEmitLdc_I4(int i)
		{
			PushStack(new ConstIntExpr(i));
		}

		internal void LazyEmitLdc_I8(long l)
		{
			PushStack(new ConstLongExpr(l));
		}

		internal void LazyEmitLdstr(string str)
		{
			PushStack(new ConstStringExpr(str));
		}

		internal void LazyEmit_idiv()
		{
			// we need to special case dividing by -1, because the CLR div instruction
			// throws an OverflowException when dividing Int32.MinValue by -1, and
			// Java just silently overflows
			ConstIntExpr v = PeekStack() as ConstIntExpr;
			if(v != null)
			{
				if(v.i == -1)
				{
					PopStack();
					Emit(OpCodes.Neg);
				}
				else
				{
					Emit(OpCodes.Div);
				}
			}
			else
			{
				Emit(OpCodes.Dup);
				Emit(OpCodes.Ldc_I4_M1);
				CodeEmitterLabel label = DefineLabel();
				Emit(OpCodes.Bne_Un_S, label);
				Emit(OpCodes.Pop);
				Emit(OpCodes.Neg);
				CodeEmitterLabel label2 = DefineLabel();
				Emit(OpCodes.Br_S, label2);
				MarkLabel(label);
				Emit(OpCodes.Div);
				MarkLabel(label2);
			}
		}

		internal void LazyEmit_ldiv()
		{
			// we need to special case dividing by -1, because the CLR div instruction
			// throws an OverflowException when dividing Int32.MinValue by -1, and
			// Java just silently overflows
			ConstLongExpr v = PeekStack() as ConstLongExpr;
			if(v != null)
			{
				if(v.l == -1)
				{
					PopStack();
					Emit(OpCodes.Neg);
				}
				else
				{
					Emit(OpCodes.Div);
				}
			}
			else
			{
				Emit(OpCodes.Dup);
				Emit(OpCodes.Ldc_I4_M1);
				Emit(OpCodes.Conv_I8);
				CodeEmitterLabel label = DefineLabel();
				Emit(OpCodes.Bne_Un_S, label);
				Emit(OpCodes.Pop);
				Emit(OpCodes.Neg);
				CodeEmitterLabel label2 = DefineLabel();
				Emit(OpCodes.Br_S, label2);
				MarkLabel(label);
				Emit(OpCodes.Div);
				MarkLabel(label2);
			}
		}

		internal void LazyEmit_instanceof(Type type)
		{
			PushStack(new InstanceOfExpr(type));
		}

		internal void LazyEmit_ifeq(CodeEmitterLabel label)
		{
			LazyEmit_if_ne_eq(label, false);
		}

		internal void LazyEmit_ifne(CodeEmitterLabel label)
		{
			LazyEmit_if_ne_eq(label, true);
		}

		private void LazyEmit_if_ne_eq(CodeEmitterLabel label, bool brtrue)
		{
			InstanceOfExpr instanceof = PeekStack() as InstanceOfExpr;
			if (instanceof != null)
			{
				PopStack();
				Emit(OpCodes.Isinst, instanceof.Type);
			}
			else
			{
				CmpExpr cmp = PeekStack() as CmpExpr;
				if (cmp != null)
				{
					PopStack();
					Emit(brtrue ? OpCodes.Bne_Un : OpCodes.Beq, label);
					return;
				}
			}
			Emit(brtrue ? OpCodes.Brtrue : OpCodes.Brfalse, label);
		}

		internal enum Comparison
		{
			LessOrEqual,
			LessThan,
			GreaterOrEqual,
			GreaterThan
		}

		private void EmitBcc(Comparison comp, CodeEmitterLabel label)
		{
			switch (comp)
			{
				case Comparison.LessOrEqual:
					Emit(OpCodes.Ble, label);
					break;
				case Comparison.LessThan:
					Emit(OpCodes.Blt, label);
					break;
				case Comparison.GreaterOrEqual:
					Emit(OpCodes.Bge, label);
					break;
				case Comparison.GreaterThan:
					Emit(OpCodes.Bgt, label);
					break;
			}
		}

		internal void LazyEmit_if_le_lt_ge_gt(Comparison comp, CodeEmitterLabel label)
		{
			CmpExpr cmp = PeekStack() as CmpExpr;
			if (cmp != null)
			{
				PopStack();
				cmp.EmitBcc(this, comp, label);
			}
			else
			{
				Emit(OpCodes.Ldc_I4_0);
				EmitBcc(comp, label);
			}
		}

		internal void LazyEmit_lcmp()
		{
			PushStack(new LCmpExpr());
		}

		internal void LazyEmit_fcmpl()
		{
			PushStack(new FCmplExpr());
		}

		internal void LazyEmit_fcmpg()
		{
			PushStack(new FCmpgExpr());
		}

		internal void LazyEmit_dcmpl()
		{
			PushStack(new DCmplExpr());
		}

		internal void LazyEmit_dcmpg()
		{
			PushStack(new DCmpgExpr());
		}

		internal void LazyEmitAnd_I4(int v2)
		{
			ConstIntExpr v1 = PeekStack() as ConstIntExpr;
			if (v1 != null)
			{
				PopStack();
				LazyEmitLdc_I4(v1.i & v2);
			}
			else
			{
				LazyEmitLdc_I4(v2);
				Emit(OpCodes.And);
			}
		}

		internal string PopLazyLdstr()
		{
			ConstStringExpr str = PeekStack() as ConstStringExpr;
			if(str != null)
			{
				PopStack();
				return str.str;
			}
			return null;
		}

		private void LazyGen()
		{
			if(lazyBranch != null)
			{
				offset += OpCodes.Br.Size + 4;
				ilgen_real.Emit(OpCodes.Br, lazyBranch.Label);
				lazyBranch = null;
			}
			int len = topOfStack;
			topOfStack = 0;
			for(int i = 0; i < len; i++)
			{
				stackArray[i].Emit(this);
			}
		}

		internal void Finish()
		{
#if LABELCHECK
			foreach(System.Diagnostics.StackFrame frame in labels.Values)
			{
				string name = frame.GetFileName() + ":" + frame.GetFileLineNumber();
				IKVM.Internal.JVM.CriticalFailure("Label failure: " + name, null);
			}
#endif
		}

		abstract class Expr
		{
			internal bool HasSideEffect { get { return false; } }	// for now we only have side-effect free expressions

			internal abstract void Emit(CodeEmitter ilgen);
		}

		abstract class ExprWithExprAndType : Expr
		{
			internal readonly Expr Expr;
			internal readonly Type Type;

			protected ExprWithExprAndType(Expr expr, Type type)
			{
				this.Expr = expr;
				this.Type = type;
			}

			internal override void Emit(CodeEmitter ilgen)
			{
				if (Expr != null)
				{
					Expr.Emit(ilgen);
				}
			}
		}

		class BoxExpr : ExprWithExprAndType
		{
			internal BoxExpr(Expr expr, Type type)
				: base(expr, type)
			{
			}

			internal override void Emit(CodeEmitter ilgen)
			{
				base.Emit(ilgen);
				ilgen.Emit(OpCodes.Box, Type);
			}
		}

		class UnboxExpr : ExprWithExprAndType
		{
			internal UnboxExpr(Expr expr, Type type)
				: base(expr, type)
			{
			}

			internal override void Emit(CodeEmitter ilgen)
			{
				base.Emit(ilgen);
				ilgen.Emit(OpCodes.Unbox, Type);
			}
		}

		class BoxUnboxExpr : ExprWithExprAndType
		{
			internal BoxUnboxExpr(Expr expr, Type type)
				: base(expr, type)
			{
			}

			internal override void Emit(CodeEmitter ilgen)
			{
				base.Emit(ilgen);
				// unbox leaves a pointer to the value of the stack (instead of the value)
				// so we have to copy the value into a local variable and load the address
				// of the local onto the stack
				LocalBuilder local = ilgen.DeclareLocal(Type);
				ilgen.Emit(OpCodes.Stloc, local);
				ilgen.Emit(OpCodes.Ldloca, local);
			}
		}

		class LdobjExpr : ExprWithExprAndType
		{
			internal LdobjExpr(Expr expr, Type type)
				: base(expr, type)
			{
			}

			internal override void Emit(CodeEmitter ilgen)
			{
				base.Emit(ilgen);
				ilgen.Emit(OpCodes.Ldobj, Type);
			}
		}

		class NullExpr : Expr
		{
			internal override void Emit(CodeEmitter ilgen)
			{
				ilgen.Emit(OpCodes.Ldnull);
			}
		}

		class ConstIntExpr : Expr
		{
			internal readonly int i;

			internal ConstIntExpr(int i)
			{
				this.i = i;
			}

			internal override void Emit(CodeEmitter ilgen)
			{
				switch(i)
				{
					case -1:
						ilgen.Emit(OpCodes.Ldc_I4_M1);
						break;
					case 0:
						ilgen.Emit(OpCodes.Ldc_I4_0);
						break;
					case 1:
						ilgen.Emit(OpCodes.Ldc_I4_1);
						break;
					case 2:
						ilgen.Emit(OpCodes.Ldc_I4_2);
						break;
					case 3:
						ilgen.Emit(OpCodes.Ldc_I4_3);
						break;
					case 4:
						ilgen.Emit(OpCodes.Ldc_I4_4);
						break;
					case 5:
						ilgen.Emit(OpCodes.Ldc_I4_5);
						break;
					case 6:
						ilgen.Emit(OpCodes.Ldc_I4_6);
						break;
					case 7:
						ilgen.Emit(OpCodes.Ldc_I4_7);
						break;
					case 8:
						ilgen.Emit(OpCodes.Ldc_I4_8);
						break;
					default:
						if(i >= -128 && i <= 127)
						{
							ilgen.Emit(OpCodes.Ldc_I4_S, (sbyte)i);
						}
						else
						{
							ilgen.Emit(OpCodes.Ldc_I4, i);
						}
						break;
				}
			}
		}

		sealed class ConstLongExpr : Expr
		{
			internal readonly long l;

			internal ConstLongExpr(long l)
			{
				this.l = l;
			}

			internal override void Emit(CodeEmitter ilgen)
			{
				switch (l)
				{
					case -1:
						ilgen.Emit(OpCodes.Ldc_I4_M1);
						ilgen.Emit(OpCodes.Conv_I8);
						break;
					case 0:
						ilgen.Emit(OpCodes.Ldc_I4_0);
						ilgen.Emit(OpCodes.Conv_I8);
						break;
					case 1:
						ilgen.Emit(OpCodes.Ldc_I4_1);
						ilgen.Emit(OpCodes.Conv_I8);
						break;
					case 2:
						ilgen.Emit(OpCodes.Ldc_I4_2);
						ilgen.Emit(OpCodes.Conv_I8);
						break;
					case 3:
						ilgen.Emit(OpCodes.Ldc_I4_3);
						ilgen.Emit(OpCodes.Conv_I8);
						break;
					case 4:
						ilgen.Emit(OpCodes.Ldc_I4_4);
						ilgen.Emit(OpCodes.Conv_I8);
						break;
					case 5:
						ilgen.Emit(OpCodes.Ldc_I4_5);
						ilgen.Emit(OpCodes.Conv_I8);
						break;
					case 6:
						ilgen.Emit(OpCodes.Ldc_I4_6);
						ilgen.Emit(OpCodes.Conv_I8);
						break;
					case 7:
						ilgen.Emit(OpCodes.Ldc_I4_7);
						ilgen.Emit(OpCodes.Conv_I8);
						break;
					case 8:
						ilgen.Emit(OpCodes.Ldc_I4_8);
						ilgen.Emit(OpCodes.Conv_I8);
						break;
					default:
						if (l >= -2147483648L && l <= 4294967295L)
						{
							if (l >= -128 && l <= 127)
							{
								ilgen.Emit(OpCodes.Ldc_I4_S, (sbyte)l);
							}
							else
							{
								ilgen.Emit(OpCodes.Ldc_I4, (int)l);
							}
							if (l < 0)
							{
								ilgen.Emit(OpCodes.Conv_I8);
							}
							else
							{
								ilgen.Emit(OpCodes.Conv_U8);
							}
						}
						else
						{
							ilgen.Emit(OpCodes.Ldc_I8, l);
						}
						break;
				}
			}
		}

		sealed class ConstStringExpr : Expr
		{
			internal readonly string str;

			internal ConstStringExpr(string str)
			{
				this.str = str;
			}

			internal override void Emit(CodeEmitter ilgen)
			{
				ilgen.Emit(OpCodes.Ldstr, str);
			}
		}

		sealed class InstanceOfExpr : Expr
		{
			internal readonly Type Type;

			internal InstanceOfExpr(Type type)
			{
				this.Type = type;
			}

			internal override void Emit(CodeEmitter ilgen)
			{
				ilgen.Emit(OpCodes.Isinst, this.Type);
				ilgen.Emit(OpCodes.Ldnull);
				ilgen.Emit(OpCodes.Cgt_Un);
			}
		}

		abstract class CmpExpr : Expr
		{
			internal CmpExpr()
			{
			}

			internal abstract void EmitBcc(CodeEmitter ilgen, Comparison comp, CodeEmitterLabel label);
		}

		sealed class LCmpExpr : CmpExpr
		{
			internal LCmpExpr()
			{
			}

			internal sealed override void Emit(CodeEmitter ilgen)
			{
				LocalBuilder value1 = ilgen.AllocTempLocal(typeof(long));
				LocalBuilder value2 = ilgen.AllocTempLocal(typeof(long));
				ilgen.Emit(OpCodes.Stloc, value2);
				ilgen.Emit(OpCodes.Stloc, value1);
				ilgen.Emit(OpCodes.Ldloc, value1);
				ilgen.Emit(OpCodes.Ldloc, value2);
				ilgen.Emit(OpCodes.Cgt);
				ilgen.Emit(OpCodes.Ldloc, value1);
				ilgen.Emit(OpCodes.Ldloc, value2);
				ilgen.Emit(OpCodes.Clt);
				ilgen.Emit(OpCodes.Sub);
				ilgen.ReleaseTempLocal(value2);
				ilgen.ReleaseTempLocal(value1);
			}

			internal sealed override void EmitBcc(CodeEmitter ilgen, Comparison comp, CodeEmitterLabel label)
			{
				ilgen.EmitBcc(comp, label);
			}
		}

		class FCmplExpr : CmpExpr
		{
			protected virtual Type FloatOrDouble()
			{
				return typeof(float);
			}

			internal sealed override void Emit(CodeEmitter ilgen)
			{
				LocalBuilder value1 = ilgen.AllocTempLocal(FloatOrDouble());
				LocalBuilder value2 = ilgen.AllocTempLocal(FloatOrDouble());
				ilgen.Emit(OpCodes.Stloc, value2);
				ilgen.Emit(OpCodes.Stloc, value1);
				ilgen.Emit(OpCodes.Ldloc, value1);
				ilgen.Emit(OpCodes.Ldloc, value2);
				ilgen.Emit(OpCodes.Cgt);
				ilgen.Emit(OpCodes.Ldloc, value1);
				ilgen.Emit(OpCodes.Ldloc, value2);
				ilgen.Emit(OpCodes.Clt_Un);
				ilgen.Emit(OpCodes.Sub);
				ilgen.ReleaseTempLocal(value1);
				ilgen.ReleaseTempLocal(value2);
			}

			internal sealed override void EmitBcc(CodeEmitter ilgen, Comparison comp, CodeEmitterLabel label)
			{
				switch (comp)
				{
					case Comparison.LessOrEqual:
						ilgen.Emit(OpCodes.Ble_Un, label);
						break;
					case Comparison.LessThan:
						ilgen.Emit(OpCodes.Blt_Un, label);
						break;
					case Comparison.GreaterOrEqual:
						ilgen.Emit(OpCodes.Bge, label);
						break;
					case Comparison.GreaterThan:
						ilgen.Emit(OpCodes.Bgt, label);
						break;
				}
			}
		}

		class FCmpgExpr : CmpExpr
		{
			protected virtual Type FloatOrDouble()
			{
				return typeof(float);
			}

			internal sealed override void Emit(CodeEmitter ilgen)
			{
				LocalBuilder value1 = ilgen.AllocTempLocal(FloatOrDouble());
				LocalBuilder value2 = ilgen.AllocTempLocal(FloatOrDouble());
				ilgen.Emit(OpCodes.Stloc, value2);
				ilgen.Emit(OpCodes.Stloc, value1);
				ilgen.Emit(OpCodes.Ldloc, value1);
				ilgen.Emit(OpCodes.Ldloc, value2);
				ilgen.Emit(OpCodes.Cgt_Un);
				ilgen.Emit(OpCodes.Ldloc, value1);
				ilgen.Emit(OpCodes.Ldloc, value2);
				ilgen.Emit(OpCodes.Clt);
				ilgen.Emit(OpCodes.Sub);
				ilgen.ReleaseTempLocal(value1);
				ilgen.ReleaseTempLocal(value2);
			}

			internal sealed override void EmitBcc(CodeEmitter ilgen, Comparison comp, CodeEmitterLabel label)
			{
				switch (comp)
				{
					case Comparison.LessOrEqual:
						ilgen.Emit(OpCodes.Ble, label);
						break;
					case Comparison.LessThan:
						ilgen.Emit(OpCodes.Blt, label);
						break;
					case Comparison.GreaterOrEqual:
						ilgen.Emit(OpCodes.Bge_Un, label);
						break;
					case Comparison.GreaterThan:
						ilgen.Emit(OpCodes.Bgt_Un, label);
						break;
				}
			}
		}

		class DCmplExpr : FCmplExpr
		{
			protected override Type FloatOrDouble()
			{
				return typeof(double);
			}
		}

		class DCmpgExpr : FCmpgExpr
		{
			protected override Type FloatOrDouble()
			{
				return typeof(double);
			}
		}

		class ClassLiteralExpr : Expr
		{
			private readonly Type type;

			internal ClassLiteralExpr(Type type)
			{
				this.type = type;
			}

			internal override void Emit(CodeEmitter ilgen)
			{
				ilgen.Emit(OpCodes.Ldtoken, type);
				Compiler.getClassFromTypeHandle.EmitCall(ilgen);
			}
		}
	}
}
#endif
