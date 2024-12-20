//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
// Copyright (C) Lluis Sanchez Gual, 2004
//

#if !MONOTOUCH
using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;

namespace Mono.CodeGeneration
{
	[RequiresUnreferencedCode (MonoAndroidExport.DynamicFeatures)]
	internal class CodeFieldReference: CodeValueReference
	{
		CodeExpression target;
		FieldInfo field;
		
		public CodeFieldReference (CodeExpression target, FieldInfo field)
		{
			if (field.IsStatic)
				throw new InvalidOperationException ("Static member '" + field.Name + "' cannot be accessed with an instance reference.");
			this.target = target;
			this.field = field;		
		}
		
		public CodeFieldReference (FieldInfo field)
		{
			if (!field.IsStatic)
				throw new InvalidOperationException ("An object reference is required for the non-static field '" + field.Name + "'.");
				
			this.field = field;		
		}
		
		public override void Generate (ILGenerator gen)
		{
			if (field.IsStatic) {
				gen.Emit (OpCodes.Ldsfld, field);
			}
			else {
				target.Generate (gen);
				gen.Emit (OpCodes.Ldfld, field);
			}
		}
		
		public override void GenerateSet (ILGenerator gen, CodeExpression value)
		{
			if (field.IsStatic) {
				value.Generate (gen);
				CodeGenerationHelper.GenerateSafeConversion (gen, field.FieldType, value.GetResultType ());
				gen.Emit (OpCodes.Stsfld, field);
			}
			else {
				target.Generate (gen);
				value.Generate (gen);
				CodeGenerationHelper.GenerateSafeConversion (gen, field.FieldType, value.GetResultType ());
				gen.Emit (OpCodes.Stfld, field);
			}
		}
		
		public override void PrintCode (CodeWriter cp)
		{
			if (!field.IsStatic)
				target.PrintCode (cp);
			else
				cp.Write (field.DeclaringType.Name);
			cp.Write (".");
			cp.Write (field.Name);
		}
		
		public override Type GetResultType ()
		{
			return field.FieldType;
		}
	}
}
#endif
