namespace TorchLiteInstrumenter
{
    using System.Collections.Generic;
    using System.Linq;
    using Mono.Cecil;
    using Mono.Cecil.Cil;
    using Mono.Cecil.Rocks;

    /// <summary>
    /// Instruments code to track field accesses.
    /// The instrumentation logic is simple, but it uses two local variables for each access.
    /// </summary>
    internal class FieldInstrumenter : IInstrumenter
    {
        private readonly MethodReference beforeFieldWriteCallbackRef;
        private readonly MethodReference afterFieldWriteCallbackRef;
        private readonly MethodReference beforeFieldReadCallbackRef;
        private readonly MethodReference beforeMethodCallCallbackRef;
        private readonly MethodReference afterMethodCallCallbackRef;

        private readonly TypeReference objectType;

        private readonly string callbackTypeName = "Callbacks";
        private readonly string beforeFieldReadCallbackName = "BeforeFieldRead";
        private readonly string beforeFieldWriteCallbackName = "BeforeFieldWrite";
        private readonly string afterFieldWriteCallbackName = "AfterFieldWrite";
        private readonly string beforeMethodCallCallbackName = "BeforeMethodCall";
        private readonly string afterMethodCallCallbackName = "AfterMethodCall";

        public FieldInstrumenter(ModuleDefinition moduleToBeInstrumented, ModuleDefinition callbackModule)
        {
            // Get references to callback methods
            var callbackTypeDef = callbackModule.Types.Single(t => t.Name == this.callbackTypeName);
            this.beforeFieldWriteCallbackRef = moduleToBeInstrumented.ImportReference(
                callbackTypeDef.Methods.Single(x => x.Name == this.beforeFieldWriteCallbackName).Resolve());
            this.afterFieldWriteCallbackRef = moduleToBeInstrumented.ImportReference(
                callbackTypeDef.Methods.Single(x => x.Name == this.afterFieldWriteCallbackName).Resolve());
            this.beforeFieldReadCallbackRef = moduleToBeInstrumented.ImportReference(
                callbackTypeDef.Methods.Single(x => x.Name == this.beforeFieldReadCallbackName).Resolve());
            this.beforeMethodCallCallbackRef = moduleToBeInstrumented.ImportReference(
                callbackTypeDef.Methods.Single(x => x.Name == this.beforeMethodCallCallbackName).Resolve());
            this.afterMethodCallCallbackRef = moduleToBeInstrumented.ImportReference(
                callbackTypeDef.Methods.Single(x => x.Name == this.afterMethodCallCallbackName).Resolve());

            // Get reference to type System.Object, to be used later for creating local variables
            this.objectType = moduleToBeInstrumented.ImportReference(typeof(object));
        }

        /// <inheritdoc/>
        public bool Instrument(IEnumerable<MethodDefinition> methods)
        {
            bool instrumented = false;
            foreach (var method in methods)
            {
                instrumented |= this.Instrument(method);
            }

            return instrumented;
        }

        /// <summary>
        /// Instrument one method.
        /// </summary>
        /// <param name="method">Method to be instrumented.</param>
        /// <returns>True if the method has been instrumented, false otherwise.</returns>
        public bool Instrument(MethodDefinition method)
        {
            bool instrumented = false;
            method.Body.SimplifyMacros();
            method.Body.InitLocals = true;

            foreach (var instruction in method.Body.Instructions.ToList())
            {
                if (instruction.OpCode == OpCodes.Stfld || instruction.OpCode == OpCodes.Stsfld)
                {
                    instrumented |= this.InstrumentFieldWriteInstruction(method, instruction);
                }
                else if (instruction.OpCode == OpCodes.Ldfld || instruction.OpCode == OpCodes.Ldsfld)
                {
                    instrumented |= this.InstrumentFieldReadInstruction(method, instruction);
                }
                else if (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt || instruction.OpCode == OpCodes.Newobj)
                {
                    instrumented |= this.InstrumentMethodCallInstruction(method, instruction);
                }
            }

            method.Body.OptimizeMacros();
            return instrumented;
        }

        private bool InstrumentFieldReadInstruction(MethodDefinition method, Instruction instruction)
        {
            var methodName = $"{method.DeclaringType.FullName}::{method.Name}";
            ILProcessor processor = method.Body.GetILProcessor();
            FieldDefinition fieldDef = instruction.Operand as FieldDefinition;
            FieldReference fieldRef = instruction.Operand as FieldReference;

            if (fieldDef == null && fieldRef != null)
            {
                try
                {
                    fieldDef = fieldRef.Resolve();
                }
                catch
                {
                }
            }

            if (fieldDef == null)
            {
                return false;
            }

            if (fieldDef.Name.Contains("<>"))
            {
                return false; // compiler generated fields
            }

            if (fieldDef.DeclaringType.IsValueType)
            {
                return false; // struct field
            }

            var fieldType = fieldDef.FieldType;

            // TODO: support generics
            if (fieldType.IsGenericParameter || fieldType.IsGenericInstance)
            {
                return false;
            }

            if (fieldType.IsGenericParameter)
            {
                var declaringType = (GenericInstanceType)fieldRef.DeclaringType;
                fieldType = declaringType.GenericArguments[((GenericParameter)fieldRef.FieldType).Position];
            }

            if (fieldType.ToString().Contains("System.Runtime.CompilerServices"))
            {
                return false; // compiler generated
            }

            // create a local variable to store the instance object
            VariableDefinition objVariableDef = null;
            if (!fieldDef.IsStatic)
            {
                objVariableDef = new VariableDefinition(this.objectType);
                method.Body.Variables.Add(objVariableDef);
            }

            var patch = this.GetPatchForFieldRead(
                processor,
                fieldDef,
                fieldRef,
                objVariableDef,
                methodName,
                instruction.Offset);
            processor.InsertBeforeAndUpdateReference(method, instruction, patch);

            return true;
        }

        private bool InstrumentFieldWriteInstruction(MethodDefinition method, Instruction instruction)
        {
            var methodName = $"{method.DeclaringType.FullName}::{method.Name}"; 
            ILProcessor processor = method.Body.GetILProcessor();
            FieldDefinition fieldDef = instruction.Operand as FieldDefinition;
            FieldReference fieldRef = instruction.Operand as FieldReference;
            // break here as well on fieldDef.Name.Contains(...)

            if (fieldDef == null && fieldRef != null)
            {
                try
                {
                    fieldDef = fieldRef.Resolve();
                }
                catch
                {
                }
            }

            if (fieldDef == null)
            {
                return false;
            }

            if (fieldDef.DeclaringType.IsValueType) // maybe it fails here
            {
                //if (!fieldDef.Name.Contains("NetMq.Msg")) 
                    return false; // struct field
            }

            if (fieldDef.Name.Contains("<>"))  // put a breakpoint here
            {
                return false; // compiler generated fields
            }

            var fieldType = fieldDef.FieldType;

            // TODO: support generics
            if (fieldType.IsGenericParameter || fieldType.IsGenericInstance)
            {
                if (!fieldDef.Name.Contains("valueFormatterDetectionMode")) // instrument enum types
                {
                    return false;
                }
            }

            if (fieldType.IsGenericParameter)
            {
                var declaringType = (GenericInstanceType)fieldRef.DeclaringType;
                fieldType = declaringType.GenericArguments[((GenericParameter)fieldRef.FieldType).Position];
            }

            if (fieldType.ToString().Contains("System.Runtime.CompilerServices"))
            {
                return false; // compiler generated
            }

            // create two local variables: one to store the new value, and one for the object instance
            var newValueVariableDef = new VariableDefinition(this.objectType);
            method.Body.Variables.Add(newValueVariableDef);

            VariableDefinition objVariableDef = null;
            if (!fieldDef.IsStatic)
            {
                objVariableDef = new VariableDefinition(this.objectType);
                method.Body.Variables.Add(objVariableDef);
            }

            var patch = this.GetPatchForBeforeFieldWrite(
                processor,
                fieldDef,
                fieldRef,
                objVariableDef,
                newValueVariableDef,
                methodName,
                instruction.Offset);

            processor.InsertBeforeAndUpdateReference(method, instruction, patch);

            patch = this.GetPatchForAfterFieldWrite(
                processor,
                fieldDef,
                fieldRef,
                objVariableDef,
                methodName,
                instruction.Offset);

            processor.InsertAfter(instruction, patch);
            method.UpdateInstructionReferences(instruction, patch.Last(), true);

            return true;
        }

        private bool InstrumentMethodCallInstruction(MethodDefinition method, Instruction instruction)
        {
            var methodName = $"{method.DeclaringType.FullName}::{method.Name}";
            var processor = method.Body.GetILProcessor();

            var calleeRef = (MethodReference)instruction.Operand;
            var calleeName = $"{calleeRef.DeclaringType.FullName}::{calleeRef.Name}";

            if (Constants.MethodPrefixBlackList.Any(x => calleeName.StartsWith(x)))
            {
                return false;
            }

            if (calleeRef.DeclaringType.IsValueType)
            {
                if (!methodName.Contains("ValueFormatterDetectionMode") /*&& !methodName.Contains("Msg")*/) // instrument enum types
                {
                    return false; // method in struct
                }
            }

            var patchTarget = instruction;
            var isValueType = false;
            if (patchTarget.Previous != null && patchTarget.Previous.OpCode == OpCodes.Constrained)
            {
                patchTarget = patchTarget.Previous;
                if (patchTarget.Operand is GenericInstanceType)
                {
                    isValueType |= ((GenericInstanceType)patchTarget.Operand).IsValueType;
                }
                else if (patchTarget.Operand is TypeReference)
                {
                    var typeRef = (TypeReference)patchTarget.Operand;
                    isValueType |= typeRef.IsValueType;
                }
            }

            if (isValueType)
            {
                if (!methodName.Contains("ValueFormatterDetectionMode")) // instrument enum types
                {
                    return false; // method in struct
                }
            }

            var signature = calleeRef.GetResolvedMethodSignature();
            var isNewObj = instruction.OpCode == OpCodes.Newobj;
            Instruction patchStart = null;
            VariableDefinition instanceVarDef = null;

            if (!isNewObj && calleeRef.HasThis)
            {
                instanceVarDef = new VariableDefinition(method.Module.TypeSystem.Object);
                method.Body.Variables.Add(instanceVarDef);

                var loadThisInstruction = MSILHelper.LocateLoadThisInstruction(processor, instruction);
                if (loadThisInstruction == null)
                {
                    return false;
                }

                var patch1 = new List<Instruction>()
                        {
                            processor.Create(OpCodes.Dup),
                            processor.Create(OpCodes.Stloc, instanceVarDef),
                        };
                processor.InsertAfter(loadThisInstruction, patch1);
                patchStart = patch1[0];
            }

            VariableDefinition contextVarDef = null;
            var afterMethodCallbackWillBeCalled = calleeRef.Name.Equals("Dispose");
            if (afterMethodCallbackWillBeCalled)
            {
                contextVarDef = new VariableDefinition(method.Module.TypeSystem.Object);
                method.Body.Variables.Add(contextVarDef);
            }

            var patch = this.GetPatchForBeforeMethodCall(
                processor,
                instanceVarDef,
                methodName,
                calleeRef,
                instruction.Offset,
                isValueType,
                contextVarDef);

            if (patchStart == null)
            {
                patchStart = patch[0];
            }

            processor.InsertBefore(patchTarget, patch);
            method.UpdateInstructionReferences(patchTarget, patchStart, true);

            if (contextVarDef != null)
            {
                patch = this.GetPatchForAfterMethodCall(processor, contextVarDef);
                processor.InsertAfter(instruction, patch);
            }

            return true;
        }

        private List<Instruction> GetPatchForBeforeFieldWrite(
            ILProcessor processor,
            FieldDefinition fieldDef,
            FieldReference fieldRef,
            VariableDefinition objVariableDef,
            VariableDefinition newValueVariableDef,
            string callerMethodName,
            int ilOffset)
        {
            var fieldType = fieldRef != null ? fieldRef.FieldType : fieldDef.FieldType;
            if (fieldType.IsGenericParameter)
            {
                var dt = (GenericInstanceType)fieldRef.DeclaringType;
                fieldType = dt.GenericArguments[((GenericParameter)fieldRef.FieldType).Position];
            }

            var isStaticField = fieldDef.IsStatic;
            var needsBoxing = fieldType.IsValueType;// || fieldDef.ContainsGenericParameter;
            var declaringType = fieldRef != null ? fieldRef.DeclaringType : fieldDef.DeclaringType;
            var fieldName = $"{fieldDef.DeclaringType.FullName}::{fieldDef.Name}";

            fieldType = fieldType.ResolveGenericParameter(declaringType);

            List<Instruction> patch = new List<Instruction>()
                    {
                    // top of the stack has the new value.
                        needsBoxing ? processor.Create(OpCodes.Box, fieldType) : processor.Create(OpCodes.Nop),
                        processor.Create(OpCodes.Stloc, newValueVariableDef),

                        // for non-static field, top of stack has the instance object.
                        isStaticField ? processor.Create(OpCodes.Nop) : processor.Create(OpCodes.Stloc, objVariableDef),

                        // Now call the callback.
                        // arg0: instance: object
                        isStaticField ? processor.Create(OpCodes.Ldnull) : processor.Create(OpCodes.Ldloc, objVariableDef),

                        // arg: field name: string
                        processor.Create(OpCodes.Ldstr, fieldName),

                        // arg: old value: object
                        isStaticField ? processor.Create(OpCodes.Nop) : processor.Create(OpCodes.Ldloc, objVariableDef),
                        isStaticField ? processor.Create(OpCodes.Nop) : processor.Create(OpCodes.Castclass, declaringType),
                        processor.Create(isStaticField ? OpCodes.Ldsfld : OpCodes.Ldfld, fieldRef ?? fieldDef),
                        needsBoxing ? processor.Create(OpCodes.Box, fieldType) : processor.Create(OpCodes.Nop),

                        // arg: new value : object
                        processor.Create(OpCodes.Ldloc, newValueVariableDef),

                        // arg: caller method name: string
                        processor.Create(OpCodes.Ldstr, callerMethodName),

                        // arg: ILOffset
                        processor.Create(OpCodes.Ldc_I4, ilOffset),

                        // call the callback
                        processor.Create(OpCodes.Call, this.beforeFieldWriteCallbackRef),

                        // finally, restore the stack
                        isStaticField ? processor.Create(OpCodes.Nop) : processor.Create(OpCodes.Ldloc, objVariableDef),
                        isStaticField ? processor.Create(OpCodes.Nop) : processor.Create(OpCodes.Castclass, declaringType),
                        processor.Create(OpCodes.Ldloc, newValueVariableDef),
                        needsBoxing ? processor.Create(OpCodes.Unbox_Any, fieldType) : processor.Create(OpCodes.Castclass, fieldType),
                    };

            return patch;
        }

        private List<Instruction> GetPatchForAfterFieldWrite(
            ILProcessor ilProcessor,
            FieldDefinition fieldDef,
            FieldReference fieldRef,
            VariableDefinition objVariableDef,
            string callerMethodName,
            int ilOffset)
        {
            var fieldType = fieldRef != null ? fieldRef.FieldType : fieldDef.FieldType;
            if (fieldType.IsGenericParameter)
            {
                var dt = (GenericInstanceType)fieldRef.DeclaringType;
                fieldType = dt.GenericArguments[((GenericParameter)fieldRef.FieldType).Position];
            }

            var isStaticField = fieldDef.IsStatic;
            var needsBoxing = fieldType.IsValueType;
            var declaringType = fieldRef != null ? fieldRef.DeclaringType : fieldDef.DeclaringType;
            var fieldName = $"{fieldDef.DeclaringType.FullName}::{fieldDef.Name}";
            fieldType = fieldType.ResolveGenericParameter(declaringType);

            var patch = new List<Instruction>()
            {
                // parent object
                isStaticField ? ilProcessor.Create(OpCodes.Ldnull) : ilProcessor.Create(OpCodes.Ldloc, objVariableDef),

                // param1: field name
                ilProcessor.Create(OpCodes.Ldstr, fieldName),

                // param: field value
                isStaticField ? ilProcessor.Create(OpCodes.Nop) : ilProcessor.Create(OpCodes.Ldloc, objVariableDef),
                isStaticField ? ilProcessor.Create(OpCodes.Nop) : ilProcessor.Create(OpCodes.Castclass, declaringType),
                ilProcessor.Create(isStaticField ? OpCodes.Ldsfld : OpCodes.Ldfld, fieldRef ?? fieldDef),
                needsBoxing ? ilProcessor.Create(OpCodes.Box, fieldType) : ilProcessor.Create(OpCodes.Nop),

                // param3: caller method name
                ilProcessor.Create(OpCodes.Ldstr, callerMethodName),

                // param: ILOffset
                ilProcessor.Create(OpCodes.Ldc_I4, ilOffset),

                // finally, call the callback
                ilProcessor.Create(OpCodes.Call, this.afterFieldWriteCallbackRef),
            };
            return patch;
        }

        private List<Instruction> GetPatchForFieldRead(
            ILProcessor processor,
            FieldDefinition fieldDef,
            FieldReference fieldRef,
            VariableDefinition objVariableDef,
            string callerMethodName,
            int iloffset)
        {
            var fieldType = fieldRef != null ? fieldRef.FieldType : fieldDef.FieldType;
            if (fieldType.IsGenericParameter)
            {
                var dt = (GenericInstanceType)fieldRef.DeclaringType;
                fieldType = dt.GenericArguments[((GenericParameter)fieldRef.FieldType).Position];
            }

            var isStaticField = fieldDef.IsStatic;
            var needsBoxing = fieldDef.FieldType.IsValueType;
            var declaringType = fieldRef != null ? fieldRef.DeclaringType : fieldDef.DeclaringType;
            string fieldName = $"{fieldDef.DeclaringType.FullName}::{fieldDef.Name}";
            fieldType = fieldType.ResolveGenericParameter(declaringType);

            var patch = new List<Instruction>()
                    {
                        isStaticField ? processor.Create(OpCodes.Nop) : processor.Create(OpCodes.Stloc, objVariableDef),
                        isStaticField ? processor.Create(OpCodes.Ldnull) : processor.Create(OpCodes.Ldloc, objVariableDef),
                        ////isStaticField ? processor.Create(OpCodes.Nop) : processor.Create(OpCodes.Castclass, fieldDef.DeclaringType),
                        processor.Create(OpCodes.Ldstr, fieldName),
                        isStaticField ? processor.Create(OpCodes.Nop) : processor.Create(OpCodes.Ldloc, objVariableDef),
                        isStaticField ? processor.Create(OpCodes.Nop) : processor.Create(OpCodes.Castclass, declaringType),
                        processor.Create(isStaticField ? OpCodes.Ldsfld : OpCodes.Ldfld, fieldRef != null ? fieldRef : fieldDef),
                        needsBoxing ? processor.Create(OpCodes.Box, fieldType) : processor.Create(OpCodes.Nop),
                        processor.Create(OpCodes.Ldstr, callerMethodName),
                        processor.Create(OpCodes.Ldc_I4, iloffset),
                        processor.Create(OpCodes.Call, this.beforeFieldReadCallbackRef),
                        isStaticField ? processor.Create(OpCodes.Nop) : processor.Create(OpCodes.Ldloc, objVariableDef),
                        isStaticField ? processor.Create(OpCodes.Nop) : processor.Create(OpCodes.Castclass, declaringType),
                    };

            return patch;
        }

        private List<Instruction> GetPatchForBeforeMethodCall(
            ILProcessor processor,
            VariableDefinition instanceVarDef,
            string callerMethodName,
            MethodReference calleeRef,
            int ilOffset,
            bool needsBoxing,
            VariableDefinition contextVarDef)
        {
            var calleeName = $"{calleeRef.DeclaringType.FullName}::{calleeRef.Name}";
            needsBoxing |= calleeRef.DeclaringType.IsValueType;

            var patch = new List<Instruction>()
                        {
                            instanceVarDef == null ? processor.Create(OpCodes.Ldnull)
                                : processor.Create(needsBoxing ? OpCodes.Ldloca : OpCodes.Ldloc, instanceVarDef),
                            processor.Create(OpCodes.Ldstr, callerMethodName),
                            processor.Create(OpCodes.Ldc_I4, ilOffset),
                            processor.Create(OpCodes.Ldstr, calleeName),
                            processor.Create(OpCodes.Call, this.beforeMethodCallCallbackRef),
                            contextVarDef == null ? processor.Create(OpCodes.Pop) : processor.Create(OpCodes.Stloc, contextVarDef),
                        };

            return patch;
        }

        private List<Instruction> GetPatchForAfterMethodCall(
                    ILProcessor processor,
                    VariableDefinition contextVarDef)
        {
            var patch = new List<Instruction>()
            {
                contextVarDef == null ? processor.Create(OpCodes.Ldnull) : processor.Create(OpCodes.Ldloc, contextVarDef),
                processor.Create(OpCodes.Call, this.afterMethodCallCallbackRef),
            };

            return patch;
        }
    }
}
