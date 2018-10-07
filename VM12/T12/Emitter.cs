using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace T12
{
    using ConstMap = Dictionary<string, ASTConstDirective>;
    using FunctionMap = Dictionary<string, ASTFunction>;
    using GlobalMap = Dictionary<string, ASTGlobalDirective>;
    using TypeMap = Dictionary<string, ASTType>;
    using VarList = List<(string Name, int Offset, ASTType Type)>;
    using VarMap = Dictionary<string, (int Offset, ASTType Type)>;

    public struct FunctionConext
    {
        public readonly string FunctionName;
        public readonly ASTType ReturnType;

        public FunctionConext(string functionName, ASTType returnType)
        {
            FunctionName = functionName;
            ReturnType = returnType;
        }
    }

    public struct LoopContext
    {
        public static readonly LoopContext Empty = new LoopContext();

        public readonly bool InLoop;
        public readonly string ContinueLabel;
        public readonly string EndLabel;

        public LoopContext(string ContinueLabel, string BreakLabel)
        {
            this.InLoop = true;
            this.ContinueLabel = ContinueLabel;
            this.EndLabel = BreakLabel;
        }
    }
    
    public static class Emitter
    {
        private static void Fail(string error)
        {
            throw new InvalidOperationException(error);
        }
        
        private static ASTType TypeOfVariable(string variableName, VarMap scope, TypeMap typeMap)
        {
            if (scope.TryGetValue(variableName, out var varType) == false)
                Fail($"No variable called '{variableName}'!");

            return ResolveType(varType.Type, typeMap);
        }

        private static int SizeOfType(ASTType type, TypeMap map)
        {
            if (type is ASTPointerType)
            {
                return ASTPointerType.Size;
            }
            else if (type is ASTFixedArrayType)
            {
                ASTFixedArrayType array = type as ASTFixedArrayType;
                return array.Size.IntValue * SizeOfType(array.BaseType, map);
            }
            else if (type is ASTArrayType)
            {
                return ASTArrayType.Size;
            }
            else if (map.TryGetValue(type.TypeName, out ASTType outType))
            {
                if (outType is ASTBaseType)
                {
                    return (outType as ASTBaseType).Size;
                }
                else if (outType is ASTStructType)
                {
                    ASTStructType sType = outType as ASTStructType;

                    int size = 0;
                    foreach (var member in sType.Members)
                    {
                        size += SizeOfType(member.Type, map);
                    }

                    return size;
                }
                else
                {
                    Fail($"We don't support this type of type atm! ({outType} of type {outType.GetType()})");
                    return default;
                }
            }
            else
            {
                Fail($"Could not find type named '{type.TypeName}'");
                return default;
            }
        }

        private static ASTType CalcReturnType(ASTExpression expression, VarMap scope, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap)
        {
            switch (expression)
            {
                case ASTLitteral litteral:
                    return litteral.Type;
                case ASTVariableExpression variableExpression:
                    {
                        if (scope.TryGetValue(variableExpression.Name, out var varType))
                            return varType.Type;
                        else if (constMap.TryGetValue(variableExpression.Name, out var constDirective))
                            return constDirective.Type;
                        else if (globalMap.TryGetValue(variableExpression.Name, out var globalDirective))
                            return globalDirective.Type;
                        Fail($"Could not find variable called '{variableExpression.Name}'!");
                        break;
                    }
                case ASTUnaryOp unaryOp:
                    var retType = CalcReturnType(unaryOp.Expr, scope, typeMap, functionMap, constMap, globalMap);

                    if (unaryOp.OperatorType == ASTUnaryOp.UnaryOperationType.Dereference)
                    {
                        if (retType is ASTPointerType pointerType)
                            retType = ResolveType(pointerType.BaseType, typeMap);
                        else
                            Fail($"Cannot derefernece non-pointer type '{retType}'!");
                    }

                    return retType;
                case ASTBinaryOp binaryOp:
                    {
                        ASTType left = CalcReturnType(binaryOp.Left, scope, typeMap, functionMap, constMap, globalMap);
                        ASTType right = CalcReturnType(binaryOp.Right, scope, typeMap, functionMap, constMap, globalMap);

                        // TODO!! Merge types!
                        return left;
                    }
                case ASTConditionalExpression conditional:
                    {
                        ASTType left = CalcReturnType(conditional.IfTrue, scope, typeMap, functionMap, constMap, globalMap);
                        ASTType right = CalcReturnType(conditional.IfFalse, scope, typeMap, functionMap, constMap, globalMap);

                        if (left != right) Fail("Differing return types!");

                        return left;
                    }
                case ASTPointerExpression pointerExpression:
                    {
                        var targetType = ResolveType(CalcReturnType(pointerExpression.Pointer, scope, typeMap, functionMap, constMap, globalMap), typeMap);

                        // TODO: Array types?
                        if (targetType is ASTPointerType == false)
                            Fail($"Cannot dereference non-pointer type '{targetType}'!");

                        return (targetType as ASTPointerType).BaseType;
                    }
                case ASTFunctionCall functionCall:
                    {
                        if (functionMap.TryGetValue(functionCall.FunctionName, out ASTFunction function) == false)
                            Fail($"No function called '{functionCall.FunctionName}'!");

                        return function.ReturnType;
                    }
                case ASTMemberExpression memberExpression:
                    {
                        var targetType = CalcReturnType(memberExpression.TargetExpr, scope, typeMap, functionMap, constMap, globalMap); ;

                        if (targetType is ASTTypeRef)
                            if (typeMap.TryGetValue((targetType as ASTTypeRef).Name, out var reffedType))
                                targetType = reffedType;
                            else
                                Fail($"There is no type called '{(targetType as ASTTypeRef).Name}'!");
                                

                        if (targetType is ASTStructType == false)
                            Fail($"Type '{targetType}' does not have members!");


                        var (type, name) = (targetType as ASTStructType).Members.Find(m => m.Name == memberExpression.MemberName);
                        if (type == null)
                            Fail($"Type '{targetType}' does not contain a member '{memberExpression.MemberName}'!");

                        return type;
                    }
                case ASTCastExpression cast:
                    // We assume all casts will work. Because if they are in the AST they shuold work!
                    return cast.To;
                case ASTSizeofTypeExpression sizeExpr:
                    int size = SizeOfType(sizeExpr.Type, typeMap);
                    return size > ASTWordLitteral.WORD_MAX_VALUE ? ASTBaseType.DoubleWord : ASTBaseType.Word;
                default:
                    Fail($"Unknown expression type {expression}, this is a compiler bug!");
                    break;
            }

            return default;
        }

        private static bool TryGenerateImplicitCast(ASTExpression expression, ASTType targetType, VarMap scope, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap, out ASTExpression result, out string error)
        {
            ASTType exprType = CalcReturnType(expression, scope, typeMap, functionMap, constMap, globalMap);

            // NOTE: Should we always resolve? Will this result in unexpected casts?
            
            if (exprType == targetType)
            {
                result = expression;
                error = default;
                return true;
            }
            else if (exprType is ASTFixedArrayType && targetType is ASTArrayType)
            {
                // We can always cast a fixed size array to a non-fixed array.
                result = new ASTFixedArrayToArrayCast(expression, exprType as ASTFixedArrayType, targetType as ASTArrayType);
                error = null;
                return true;
            }
            else if (expression is ASTWordLitteral && targetType == ASTBaseType.DoubleWord)
            {
                // Here there is a special case where we can optimize the loading of words and dwords
                ASTWordLitteral litteral = expression as ASTWordLitteral;
                // NOTE: Is adding the 'd' to the litteral the right thing to do?
                result = new ASTDoubleWordLitteral(litteral.Value + "d", litteral.IntValue);
                error = default;
                return true;
            }
            else if (exprType is ASTPointerType && targetType == ASTPointerType.Of(ASTBaseType.Void))
            {
                result = new ASTPointerToVoidPointerCast(expression, exprType as ASTPointerType);
                error = default;
                return true;
            }
            else if (exprType == ASTBaseType.String && targetType == ASTPointerType.Of(ASTBaseType.Void))
            {
                // FIXME!!! This is a ugly hack!! When we go over to struct strings this will have to change
                // So we just say that we can conver this. We rely on the fact that we never actually check
                // to see if the expression results in a pointer when generating the cast
                result = new ASTPointerToVoidPointerCast(expression, ASTPointerType.Of(ASTBaseType.Word));
                error = default;
                return true;
            }
            else
            {
                if (exprType is ASTBaseType && targetType is ASTBaseType)
                {
                    int exprSize = (exprType as ASTBaseType).Size;
                    int targetSize = (targetType as ASTBaseType).Size;

                    if (exprSize < targetSize)
                    {
                        result = new ASTImplicitCast(expression, exprType as ASTBaseType, targetType as ASTBaseType);
                        error = default;
                        return true;
                    }
                    else
                    {
                        //
                        result = default;
                        error = "This cast would lead to loss of information, do an explicit cast!";
                        return false;
                    }
                }
                else
                {
                    result = default;
                    error = "Can only cast base types atm!";
                    return false;
                }
            }
        }
        
        /// <summary>
        /// This method will generate the most optimized jump based on the type of the binary operation condition.
        /// </summary>
        private static void GenerateOptimizedBinaryOpJump(StringBuilder builder, ASTBinaryOp condition, string jmpLabel, VarMap scope, VarList varList, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap)
        {
            var leftType = CalcReturnType(condition.Left, scope, typeMap, functionMap, constMap, globalMap);
            var rightType = CalcReturnType(condition.Right, scope, typeMap, functionMap, constMap, globalMap);
            // Try and cast the right type to the left type so we can apply the binary operation.
            if (TryGenerateImplicitCast(condition.Right, leftType, scope, typeMap, functionMap, constMap, globalMap, out ASTExpression typedRight, out string error) == false)
                Fail($"Cannot apply binary operation '{condition.OperatorType}' on differing types '{leftType}' and '{rightType}'!");

            int typeSize = SizeOfType(leftType, typeMap);
            
            switch (condition.OperatorType)
            {
                case ASTBinaryOp.BinaryOperatorType.Equal:
                    EmitExpression(builder, condition.Left, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                    EmitExpression(builder, typedRight, scope, varList, typeMap, functionMap, constMap, globalMap, true);

                    // We use jneq and jneql here because we want to not jump and execute the body if they are equal.

                    if (typeSize == 1)
                    {
                        builder.AppendLine($"\tjneq {jmpLabel}");
                    }
                    else if (typeSize == 2)
                    {
                        builder.AppendLine($"\tjneql {jmpLabel}");
                    }
                    else
                    {
                        Fail($"We only support types with a max size of 2 right now! Got type {leftType} size {typeSize}");
                    }
                    break;
                case ASTBinaryOp.BinaryOperatorType.Not_equal:
                    EmitExpression(builder, condition.Left, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                    EmitExpression(builder, typedRight, scope, varList, typeMap, functionMap, constMap, globalMap, true);

                    // We use jeq and jeql here because we want to not jump and execute the body if they are not equal.

                    if (typeSize == 1)
                    {
                        builder.AppendLine($"\tjeq {jmpLabel}");
                    }
                    else if (typeSize == 2)
                    {
                        builder.AppendLine($"\tjeql {jmpLabel}");
                    }
                    else
                    {
                        Fail($"We only support types with a max size of 2 right now! Got type {leftType} size {typeSize}");
                    }
                    break;
                case ASTBinaryOp.BinaryOperatorType.Less_than:
                    // left < right
                    // We do:
                    // right - left > 0
                    // If the result is positive left was strictly less than right
                    
                    EmitExpression(builder, condition.Left, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                    EmitExpression(builder, typedRight, scope, varList, typeMap, functionMap, constMap, globalMap, true);

                    // We want to jump past the body if left >= right and not jump if left < right
                    // -> left - right >= 0
                    // This is why we use jge and jgel
                    
                    if (typeSize == 1)
                    {
                        builder.AppendLine($"\tsub");
                        builder.AppendLine($"\tjge {jmpLabel}");
                    }
                    else if (typeSize == 2)
                    {
                        builder.AppendLine($"\tlsub");
                        builder.AppendLine($"\tjgel {jmpLabel}");
                    }
                    else
                    {
                        Fail($"We only support types with a max size of 2 right now! Got type {leftType} size {typeSize}");
                    }
                    break;
                case ASTBinaryOp.BinaryOperatorType.Less_than_or_equal:
                    throw new NotImplementedException();

                    EmitExpression(builder, condition.Left, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                    EmitExpression(builder, typedRight, scope, varList, typeMap, functionMap, constMap, globalMap, true);

                    if (typeSize == 1)
                    {
                        builder.AppendLine($"\tjeq {jmpLabel}");
                    }
                    else if (typeSize == 2)
                    {
                        builder.AppendLine($"\tjeql {jmpLabel}");
                    }
                    else
                    {
                        Fail($"We only support types with a max size of 2 right now! Got type {leftType} size {typeSize}");
                    }
                    break;
                case ASTBinaryOp.BinaryOperatorType.Greater_than:
                    throw new NotImplementedException();
                    EmitExpression(builder, condition.Left, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                    EmitExpression(builder, typedRight, scope, varList, typeMap, functionMap, constMap, globalMap, true);

                    if (typeSize == 1)
                    {
                        builder.AppendLine($"\tjeq {jmpLabel}");
                    }
                    else if (typeSize == 2)
                    {
                        builder.AppendLine($"\tjeql {jmpLabel}");
                    }
                    else
                    {
                        Fail($"We only support types with a max size of 2 right now! Got type {leftType} size {typeSize}");
                    }
                    break;
                case ASTBinaryOp.BinaryOperatorType.Greater_than_or_equal:
                    throw new NotImplementedException();
                    EmitExpression(builder, condition.Left, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                    EmitExpression(builder, typedRight, scope, varList, typeMap, functionMap, constMap, globalMap, true);

                    if (typeSize == 1)
                    {
                        builder.AppendLine($"\tjeq {jmpLabel}");
                    }
                    else if (typeSize == 2)
                    {
                        builder.AppendLine($"\tjeql {jmpLabel}");
                    }
                    else
                    {
                        Fail($"We only support types with a max size of 2 right now! Got type {leftType} size {typeSize}");
                    }
                    break;
                default:
                    // We cant do something smart here :(
                    EmitExpression(builder, condition, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                    builder.AppendLine($"\tjz {jmpLabel}");
                    break;
            }
        }
        
        private struct VariableRef
        {
            public VariableType VariableType;
            
            /// <summary>
            /// This member is valid of VariableType is local!
            /// </summary>
            public int LocalAddress;
            // NOTE: What is this?!
            public ASTPointerExpression Pointer;
            public string GlobalName;
            public string ConstantName;
            public ASTType Type;
            public string Comment;
        }

        // NOTE: Should we use this instead?
        private enum VariableType
        {
            Local,
            Pointer,
            Global,
            Constant,
        }

        private static bool TryResolveVariable(string name, VarMap scope, GlobalMap globalMap, ConstMap constMap, TypeMap typeMap, out VariableRef variable)
        {
            if (scope.TryGetValue(name, out var local))
            {
                variable = new VariableRef
                {
                    VariableType = VariableType.Local,
                    LocalAddress = local.Offset,
                    Type = ResolveType(local.Type, typeMap),
                };

                return true;
            }
            else if (globalMap.TryGetValue(name, out var global))
            {
                // FIXME!! Is there a way to convert the constant to a valid address?
                variable = new VariableRef
                {
                    VariableType = VariableType.Global,
                    GlobalName = global.Name,
                    Type = ResolveType(global.Type, typeMap),

                };

                return true;
            }
            else if (constMap.TryGetValue(name, out var constant))
            {
                variable = new VariableRef
                {
                    VariableType = VariableType.Constant,
                    ConstantName = constant.Name,
                    Type = ResolveType(constant.Type, typeMap),
                };

                return true;
            }
            else
            {
                variable = default;
                return false;
            }
        }

        private static void LoadVariable(StringBuilder builder, VariableRef var, TypeMap typeMap)
        {
            int typeSize = SizeOfType(var.Type, typeMap);
            switch (var.VariableType)
            {
                case VariableType.Local:
                    switch (typeSize)
                    {
                        case 1:
                            builder.AppendLine($"\tload {var.LocalAddress}{(var.Comment == null ? "" : $"\t; {var.Comment}")}");
                            break;
                        case 2:
                            builder.AppendLine($"\tloadl {var.LocalAddress}{(var.Comment == null ? "" : $"\t; {var.Comment}")}");
                            break;
                        default:
                            builder.AppendLine($"\t; {var.Comment} ({typeSize})");
                            for (int i = 0; i < typeSize; i++)
                            {
                                builder.AppendLine($"\tload {var.LocalAddress + i}\t; {var.Comment}:{i}");
                            }
                            break;
                    }
                    break;
                case VariableType.Pointer:
                    // If we are loading a pointer we assume the pointer is on the stack!
                    // We might what to change this!
                    switch (typeSize)
                    {
                        case 1:
                            builder.AppendLine($"\tload [SP]{(var.Comment == null ? "" : $"\t; {var.Comment}")}");
                            break;
                        case 2:
                            builder.AppendLine($"\tloadl [SP]{(var.Comment == null ? "" : $"\t; {var.Comment}")}");
                            break;
                        default:
                            builder.AppendLine($"\t; {var.Comment} ({typeSize})");
                            for (int i = 0; i < typeSize; i++)
                            {
                                // TODO: This could probably be done better
                                // Duplicate the pointer, load the one word, swap the word with the pointer underneath, increment the pointer
                                // Only save the pointer if it is not the last value we are loading

                                bool lastValue = i == typeSize - 1;

                                if (lastValue == false)
                                {
                                    builder.AppendLine("\tldup");
                                }

                                builder.AppendLine("\tload [SP]");

                                if (lastValue == false)
                                {
                                    builder.AppendLine("\tslswap slswap\t; Swap the loaded value with the pointer underneath");
                                    builder.AppendLine("\tlinc\t; Increment pointer");
                                }
                            }
                            break;
                    }
                    break;
                case VariableType.Global:
                    throw new NotImplementedException();
                case VariableType.Constant:
                    switch (typeSize)
                    {
                        case 1:
                            builder.AppendLine($"\tload #{var.ConstantName}{(var.Comment == null ? "" : $"\t; {var.Comment}")}");
                            break;
                        case 2:
                            builder.AppendLine($"\tloadl #{var.ConstantName}{(var.Comment == null ? "" : $"\t; {var.Comment}")}");
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                    break;
                default:
                    Fail($"Unknown variable type '{var.VariableType}'!");
                    break;
            }
        }

        private static void StoreVariable(StringBuilder builder, VariableRef var, TypeMap typeMap)
        {
            int typeSize = SizeOfType(var.Type, typeMap);
            switch (var.VariableType)
            {
                case VariableType.Local:
                    switch (typeSize)
                    {
                        case 1:
                            builder.AppendLine($"\tstore {var.LocalAddress}{(var.Comment == null ? "" : $"\t; {var.Comment}")}");
                            break;
                        case 2:
                            builder.AppendLine($"\tstorel {var.LocalAddress}{(var.Comment == null ? "" : $"\t; {var.Comment}")}");
                            break;
                        default:
                            builder.AppendLine($"\t; {var.Comment} ({typeSize})");

                            // We assume the value is on the stack
                            // Because the stack is fifo the first value on the stack will be the last value of the type.
                            for (int i = typeSize - 1; i >= 0; i--)
                            {
                                builder.AppendLine($"\tstore {var.LocalAddress + i}\t; {var.Comment}:{i}");
                            }
                            break;
                    }
                    break;
                case VariableType.Pointer:
                    // NOTE: Here we assume the pointer is already on the stack
                    // It will be hard to implement storing of things larger than 2 words!
                    switch (typeSize)
                    {
                        case 1:
                            builder.AppendLine($"\tstore [SP]{(var.Comment == null ? "" : $"\t; {var.Comment}")}");
                            break;
                        case 2:
                            builder.AppendLine($"\tstorel [SP]{(var.Comment == null ? "" : $"\t; {var.Comment}")}");
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                    break;
                case VariableType.Global:
                    // What do we do here!?
                    throw new NotImplementedException();
                case VariableType.Constant:
                    Fail($"Cannot modify constant '{var.ConstantName}'!");
                    break;
                default:
                    Fail($"Unknown variable type '{var.VariableType}'!");
                    break;
            }
        }

        private static ASTType ResolveType(ASTType type, TypeMap typeMap)
        {
            if (type is ASTTypeRef)
                return ResolveType(type.TypeName, typeMap);

            return type;
        }

        private static ASTType ResolveType(string typeName, TypeMap typeMap)
        {
            if (typeMap.TryGetValue(typeName, out ASTType type) == false)
                Fail($"There is no type called '{typeName}'");

            // FIXME: Detect reference loops
            if (type is ASTTypeRef)
            {
                type = ResolveType(type.TypeName, typeMap);
            }

            return type;
        }

        private static int MemberOffset(ASTStructType type, string memberName,  TypeMap typeMap, out ASTType memberType)
        {
            int memberIndex = type.Members.FindIndex(m => m.Name == memberName);
            if (memberIndex < 0) Fail($"No member called '{memberName}' in struct '{type}'");

            memberType = ResolveType(type.Members[memberIndex].Type, typeMap);

            // Calculate the offset
            int memberOffset = 0;
            for (int i = 0; i < memberIndex; i++)
            {
                memberOffset += SizeOfType(type.Members[i].Type, typeMap);
            }

            return memberOffset;
        }
        
        /*
        private static bool TryResolveConstantValue(ASTExpression constExpression, out int constant)
        {
            switch (constExpression)
            {
                case ASTNumericLitteral:
                    break;
                default:
                    break;
            }
        }
        */

        public static string EmitAsem(AST ast)
        {
            StringBuilder builder = new StringBuilder();
            
            TypeMap typeMap = ASTBaseType.BaseTypeMap.ToDictionary(kvp => kvp.Key, kvp => (ASTType) kvp.Value);

            ConstMap constMap = new ConstMap();
            
            // NOTE: This might not be the best solution
            // because when you look for variables you might forget to check the globals
            // This might be fixed with a function to do this.
            // But that might not be desirable either.
            GlobalMap globalMap = new GlobalMap();

            FunctionMap functionMap = ast.Program.Functions.ToDictionary(func => func.Name, func => func);
            
            foreach (var directive in ast.Program.Directives)
            {
                EmitDirective(builder, directive, typeMap, functionMap, constMap, globalMap);
            }

            builder.AppendLine();

            foreach (var func in ast.Program.Functions)
            {
                EmitFunction(builder, func, typeMap, functionMap, constMap, globalMap);
                builder.AppendLine();
            }
            
            return builder.ToString();
        }
        
        private static void EmitDirective(StringBuilder builder, ASTDirective directive, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap)
        {
            switch (directive)
            {
                case ASTUseDirective use:
                    {
                        builder.AppendLine($"& {Path.GetFileNameWithoutExtension(use.FileName)} {use.FileName}");
                        break;
                    }
                case ASTExternFunctionDirective externFunc:
                    {
                        // Create a new ASTFunction without body
                        ASTFunction func = new ASTFunction(externFunc.FunctionName, externFunc.ReturnType, externFunc.Parameters, null);
                        // Add that function to the function map
                        functionMap.Add(externFunc.FunctionName, func);
                        break;
                    }
                case ASTConstDirective constDirective:
                    {
                        // TODO: Is constant value?
                        // TODO: Resolve constant value
                        // TODO: Resolve conflicts
                        constMap[constDirective.Name] = constDirective;

                        if (constDirective.Value is ASTStringLitteral)
                        {
                            // We do nothing as we handle the case when we need the constant
                        }
                        else
                        {

                            // FIXME: Proper constant folding!!!!!!!
                            builder.AppendLine($"<{constDirective.Name} = {(constDirective.Value as ASTLitteral).Value}>");
                        }
                        break;
                    }
                case ASTGlobalDirective globalDirective:
                    {
                        globalMap[globalDirective.Name] = globalDirective;
                        builder.AppendLine($"<{globalDirective.Name} = auto({SizeOfType(globalDirective.Type, typeMap)})>");
                        break;
                    }
                case ASTStructDeclarationDirective structDeclaration:
                    {
                        string name = structDeclaration.Name;

                        if (typeMap.ContainsKey(name))
                            Fail($"Cannot declare struct '{name}' as there already exists a struct with that name!");

                        typeMap.Add(name, structDeclaration.DeclaredType);
                        break;
                    }
                default:
                    Fail($"Unknown directive {directive}, this is a compiler bug!");
                    break;
            }
        }

        private static void EmitFunction(StringBuilder builder, ASTFunction func, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap)
        {
            // FIXME: Do control flow analysis to check that the function returns!

            VarList variableList = new VarList();
            FunctionConext functionConext = new FunctionConext(func.Name, func.ReturnType);
            int local_index = 0;

            foreach (var param in func.Parameters)
            {
                variableList.Add((param.Name, local_index, param.Type));
                local_index += SizeOfType(param.Type, typeMap);
            }

            VarMap scope = variableList.ToDictionary(var => var.Name, var => (var.Offset, var.Type));

            if (func is ASTInterrupt)
            {
                // NOTE: We might want to use constants here...
                VM12_Opcode.InterruptType type = (func as ASTInterrupt).Type;
                builder.AppendLine($":{func.Name}_interrupt\t@0x{(int)type:X6}\t; {type} interrupt");

                // Load all variables so we can send executrion over to delegate.
                int index = 0;
                foreach (var param in ASTInterrupt.InterruptToParameterList(type))
                {
                    ASTType paramType = TypeOfVariable(param.Name, scope, typeMap);

                    VariableRef paramVariable = new VariableRef()
                    {
                        VariableType = VariableType.Local,
                        LocalAddress = index,
                        Type = paramType,
                    };

                    LoadVariable(builder, paramVariable, typeMap);

                    index += SizeOfType(paramType, typeMap);
                }

                builder.AppendLine($"\t::{func.Name}");
                builder.AppendLine($"\tret");
                builder.AppendLine();
                // We could make factor this last statement out of the if-statement, 
                // but then it would be harder to implement the comment
                builder.AppendLine($":{func.Name}\t; {type} interrupt implementation");
            }
            else
            {
                builder.AppendLine($":{func.Name}");
            }
            
            int param_index = builder.Length;

            // FIXME!
            //builder.AppendLine("\t0 0");

            int @params = func.Parameters.Sum(param => SizeOfType(param.Type, typeMap));

            // We add a return statement if the function returns void and there is not a return statement at the end
            // FIXME: We do not yet validate that a function acutally returns the said value!
            // NOTE: This should be done better
            if (functionConext.ReturnType == ASTBaseType.Void && (func.Body.Count <= 0 || func.Body.Last() is ASTReturnStatement == false))
            {
                var returnStatement = new ASTReturnStatement(null);
                func.Body.Add(returnStatement);
            }

            foreach (var blockItem in func.Body)
            {
                EmitBlockItem(builder, blockItem, scope, variableList, ref local_index, typeMap, functionConext, LoopContext.Empty, functionMap, constMap, globalMap);
            }
            
            int locals = local_index;

            string params_string = string.Join(", ", func.Parameters.Select(param => $"/{param.Name} {param.Type.TypeName}"));

            string locals_string = string.Join(", ", variableList.Skip(func.Parameters.Count).Select(var => (var.Type, var.Name)).Select(local => $"/{local.Name} {local.Type.TypeName}"));
            
            // Create the proc comment. FIXME: This could probably be done better.
            string combined_string = "";
            if (string.IsNullOrEmpty(params_string) && string.IsNullOrEmpty(locals_string))
            {
                combined_string = "";
            }
            else if (string.IsNullOrEmpty(params_string) && !string.IsNullOrEmpty(locals_string))
            {
                combined_string = locals_string;
            }
            else if (!string.IsNullOrEmpty(params_string) && string.IsNullOrEmpty(locals_string))
            {
                combined_string = $"({params_string})";
            }
            else if (!string.IsNullOrEmpty(params_string) && !string.IsNullOrEmpty(locals_string))
            {
                combined_string = $"({params_string}), {locals_string}";
            }
            
            // FIXME: We can probably precompute this value. 
            // So we already know these values before emitting any code
            builder.Insert(param_index, $"\t{@params} {locals}\t; {combined_string}\n");
        }

        private static void EmitBlockItem(StringBuilder builder, ASTBlockItem blockItem, VarMap scope, VarList varList, ref int local_index, TypeMap typeMap, FunctionConext functionConext, LoopContext loopContext, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap)
        {
            switch (blockItem)
            {
                case ASTDeclaration declaration:
                    EmitDeclaration(builder, declaration, scope, varList, ref local_index, typeMap, functionMap, constMap, globalMap);
                    break;
                case ASTStatement statement:
                    // @TODO: Make this cleaner, like using an imutable map or other datastructure for handling scopes
                    // Make a copy of the scope so that the statement does not modify the current scope
                    var new_scope = new VarMap(scope);
                    EmitStatement(builder, statement, new_scope, varList, ref local_index, typeMap, functionConext, loopContext, functionMap, constMap, globalMap);
                    break;
                default:
                    Fail($"Unknown block item {blockItem}, this is a compiler bug!");
                    break;
            }
        }

        private static void EmitDeclaration(StringBuilder builder, ASTDeclaration declaration, VarMap scope, VarList varList, ref int local_index, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap)
        {
            switch (declaration)
            {
                case ASTVariableDeclaration variableDeclaration:
                    {
                        string varName = variableDeclaration.VariableName;
                        if (scope.ContainsKey(varName)) Fail($"Cannot declare the variable '{varName}' more than once!");

                        int varOffset = local_index;
                        scope.Add(varName, (varOffset, variableDeclaration.Type));
                        varList.Add((varName, varOffset, variableDeclaration.Type));
                        local_index += SizeOfType(variableDeclaration.Type, typeMap);

                        if (variableDeclaration.Initializer != null)
                        {
                            var initExpr = variableDeclaration.Initializer;
                            var initType = CalcReturnType(initExpr, scope, typeMap, functionMap, constMap, globalMap);

                            if (TryGenerateImplicitCast(initExpr, variableDeclaration.Type, scope, typeMap, functionMap, constMap, globalMap, out ASTExpression typedExpression, out string error) == false)
                                Fail($"Cannot assign expression of type '{initType}' to variable ('{variableDeclaration.VariableName}') of type '{variableDeclaration.Type}'");
                            
                            EmitExpression(builder, typedExpression, scope, varList, typeMap, functionMap, constMap, globalMap, true);

                            VariableRef var = new VariableRef
                            {
                                VariableType = VariableType.Local,
                                LocalAddress = varOffset,
                                Type = variableDeclaration.Type,
                                Comment = $"[{varName}]",
                            };

                            StoreVariable(builder, var, typeMap);
                            // AppendTypedStore(builder, $"{varOffset}\t; [{varName}]", variableDeclaration.Type, typeMap);
                        }
                        break;
                    }
                default:
                    Fail($"Unknown declaration {declaration}, this is a compiler bug!");
                    break;
            }
        }

        private static void EmitStatement(StringBuilder builder, ASTStatement statement, VarMap scope, VarList varList, ref int local_index, TypeMap typeMap, FunctionConext functionConext, LoopContext loopContext, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap)
        {
            switch (statement)
            {
                case ASTEmptyStatement _:
                    builder.AppendLine("\tnop");
                    break;
                case ASTReturnStatement returnStatement:
                    {
                        if (returnStatement.ReturnValueExpression != null)
                        {
                            var retType = CalcReturnType(returnStatement.ReturnValueExpression, scope, typeMap, functionMap, constMap, globalMap);

                            if (retType != functionConext.ReturnType) Fail($"Cannot return expression of type '{retType}' in a function that returns type '{functionConext.ReturnType}'");

                            EmitExpression(builder, returnStatement.ReturnValueExpression, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                            // FIXME: Handle the size of the return type!

                            int retSize = SizeOfType(retType, typeMap);
                            if (retSize == 1)
                            {
                                builder.AppendLine("\tret1");
                            }
                            else if (retSize == 2)
                            {
                                builder.AppendLine("\tret2");
                            }
                            else
                            {
                                builder.AppendLine($"\tretv {retSize}\t; {retType}");
                            }
                        }
                        else
                        {
                            builder.AppendLine("\tret");
                        }
                        break;
                    }
                case ASTAssignmentStatement assignment:
                    {
                        // This is not used!
                        Fail($"We don't use this AST node type! {assignment}");
                        string varName = assignment.VariableNames[0];
                        EmitExpression(builder, assignment.AssignmentExpression, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                        builder.AppendLine($"\tstore {scope[varName].Offset}\t; [{varName}]");
                        break;
                    }
                case ASTIfStatement ifStatement:
                    {
                        // FIXME: There could be hash collisions!
                        int hash = ifStatement.GetHashCode();
                        if (ifStatement.IfFalse == null)
                        {
                            // If-statement without else
                            if (ifStatement.Condition is ASTBinaryOp)
                            {
                                // Here we can optimize the jump.
                                GenerateOptimizedBinaryOpJump(builder, ifStatement.Condition as ASTBinaryOp, $":post_{hash}", scope, varList, typeMap, functionMap, constMap, globalMap);
                            }
                            else
                            {
                                // FIXME: We do not handle return types with a size > 1
                                // We don't know how to optimize this so we eval the whole expression
                                EmitExpression(builder, ifStatement.Condition, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                                builder.AppendLine($"\tjz :post_{hash}");
                            }

                            // Generate the if true block.
                            EmitStatement(builder, ifStatement.IfTrue, scope, varList, ref local_index, typeMap, functionConext, loopContext, functionMap, constMap, globalMap);
                            builder.AppendLine($"\t:post_{hash}");
                        }
                        else
                        {
                            if (ifStatement.Condition is ASTBinaryOp)
                            {
                                // Here we can optimize the jump.
                                GenerateOptimizedBinaryOpJump(builder, ifStatement.Condition as ASTBinaryOp, $":else_{hash}", scope, varList, typeMap, functionMap, constMap, globalMap);
                            }
                            else
                            {
                                // We don't know how to optimize this so we eval the whole expression
                                EmitExpression(builder, ifStatement.Condition, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                                builder.AppendLine($"\tjz :else_{hash}");
                            }

                            EmitStatement(builder, ifStatement.IfTrue, scope, varList, ref local_index, typeMap, functionConext, loopContext, functionMap, constMap, globalMap);
                            builder.AppendLine($"\tjmp :post_{hash}");
                            builder.AppendLine($"\t:else_{hash}");
                            EmitStatement(builder, ifStatement.IfFalse, scope, varList, ref local_index, typeMap, functionConext, loopContext, functionMap, constMap, globalMap);
                            builder.AppendLine($"\t:post_{hash}");
                        }
                        break;
                    }
                case ASTCompoundStatement compoundStatement:
                    foreach (var blockItem in compoundStatement.Block)
                    {
                        EmitBlockItem(builder, blockItem, scope, varList, ref local_index, typeMap, functionConext, loopContext, functionMap, constMap, globalMap);
                    }
                    break;
                case ASTExpressionStatement expression:
                    EmitExpression(builder, expression.Expr, scope, varList, typeMap, functionMap, constMap, globalMap, false);
                    break;
                case ASTForWithDeclStatement forWithDecl:
                    {
                        int hash = forWithDecl.GetHashCode();
                        LoopContext newLoopContext = new LoopContext($":post_statement_{hash}", $":for_end_{hash}");

                        builder.AppendLine($"\t; For loop {forWithDecl.Condition} {hash}");
                        
                        VarMap new_scope = new VarMap(scope);
                        EmitDeclaration(builder, forWithDecl.Declaration, new_scope, varList, ref local_index, typeMap, functionMap, constMap, globalMap);
                        
                        // We are now in the new scope.
                        scope = new_scope;

                        builder.AppendLine($"\t:for_cond_{hash}");

                        var cond = forWithDecl.Condition;
                        if (cond is ASTBinaryOp)
                        {
                            // This will generate a jump depending on the type of binary op
                            GenerateOptimizedBinaryOpJump(builder, cond as ASTBinaryOp, newLoopContext.EndLabel, scope, varList, typeMap, functionMap, constMap, globalMap);
                        }
                        else
                        {
                            // Here we can't really optimize.
                            EmitExpression(builder, cond, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                            builder.AppendLine($"\tjz {newLoopContext.EndLabel}");
                        }
                        
                        EmitStatement(builder, forWithDecl.Body, new_scope, varList, ref local_index, typeMap, functionConext, newLoopContext, functionMap, constMap, globalMap);

                        builder.AppendLine($"\t{newLoopContext.ContinueLabel}");
                        EmitExpression(builder, forWithDecl.PostExpression, new_scope, varList, typeMap, functionMap, constMap, globalMap, false);

                        builder.AppendLine($"\tjmp :for_cond_{hash}");
                        builder.AppendLine($"\t{newLoopContext.EndLabel}");

                        break;
                    }
                case ASTWhileStatement whileStatement:
                    {
                        int hash = whileStatement.GetHashCode();
                        LoopContext newLoopContext = new LoopContext($":while_condition_{hash}", $":while_end_{hash}");

                        builder.AppendLine($"\t; While loop {whileStatement.Condition} {hash}");
                        builder.AppendLine($"\t{newLoopContext.ContinueLabel}");

                        EmitExpression(builder, whileStatement.Condition, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                        builder.AppendLine($"\tjz {newLoopContext.EndLabel}");

                        EmitStatement(builder, whileStatement.Body, scope, varList, ref local_index, typeMap, functionConext, loopContext, functionMap, constMap, globalMap);

                        builder.AppendLine($"\tjmp {newLoopContext.ContinueLabel}");
                        builder.AppendLine($"\t{newLoopContext.EndLabel}");
                        break;
                    }
                case ASTDoWhileStatement doWhile:
                    {
                        int hash = doWhile.GetHashCode();
                        LoopContext newLoopContext = new LoopContext($":do_while_condition_{hash}", $":do_while_end_{hash}");

                        builder.AppendLine($"\t; Do while loop {doWhile.Condition} {hash}");
                        builder.AppendLine($"\t:do_while_{hash}");

                        EmitStatement(builder, doWhile.Body, scope, varList, ref local_index, typeMap, functionConext, newLoopContext, functionMap, constMap, globalMap);

                        builder.AppendLine($"\t{newLoopContext.ContinueLabel}");
                        EmitExpression(builder, doWhile.Condition, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                        builder.AppendLine($"\tjnz :do_while_{hash}");
                        builder.AppendLine($"\t{newLoopContext.EndLabel}");

                        break;
                    }
                case ASTContinueStatement continueStatement:
                    builder.AppendLine($"\tjmp {loopContext.ContinueLabel}");
                    break;
                case ASTBreakStatement breakStatement:
                    builder.AppendLine($"\tjmp {loopContext.EndLabel}");
                    break;
                case ASTInlineAssemblyStatement assemblyStatement:
                    foreach (var line in assemblyStatement.Assembly)
                    {
                        builder.AppendLine($"\t{line.Contents}");
                    }
                    break;
                default:
                    Fail($"Could not emit code for statement {statement}, this is a compiler bug!");
                    break;
            }
        }

        private static void EmitExpression(StringBuilder builder, ASTExpression expression, VarMap scope, VarList varList, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap, bool produceResult)
        {
            switch (expression)
            {
                case ASTLitteral litteral:
                    EmitLitteral(builder, litteral);
                    break;
                case ASTVariableExpression variableExpr:
                    {
                        if (TryResolveVariable(variableExpr.Name, scope, globalMap, constMap, typeMap, out VariableRef variable) == false)
                            Fail($"No variable called '{variableExpr.Name}'!");
                        
                        variable.Comment = $"[{variableExpr.Name}]";

                        switch (variable.VariableType)
                        {
                            case VariableType.Local:
                                {
                                    var variableType = TypeOfVariable(variableExpr.Name, scope, typeMap);

                                    if (variableExpr.AssignmentExpression != null)
                                    {
                                        var assignmentType = ResolveType(CalcReturnType(variableExpr.AssignmentExpression, scope, typeMap, functionMap, constMap, globalMap), typeMap);

                                        if (TryGenerateImplicitCast(variableExpr.AssignmentExpression, variableType, scope, typeMap, functionMap, constMap, globalMap, out var typedAssignment, out var error) == false)
                                            Fail($"Cannot assign expression of type '{assignmentType}' to variable '{variableExpr.Name}' of type '{variableType}'! (Implicit cast error: '{error}')");

                                        EmitExpression(builder, typedAssignment, scope, varList, typeMap, functionMap, constMap, globalMap, true);

                                        StoreVariable(builder, variable, typeMap);
                                    }

                                    if (produceResult)
                                    {
                                        LoadVariable(builder, variable, typeMap);
                                    }
                                    break;
                                }
                            case VariableType.Global:
                                {
                                    // This is fine because of TryResolveVariable
                                    var globalType = globalMap[variableExpr.Name].Type;

                                    // So we can load and store to the pointers we are going to emit
                                    variable.VariableType = VariableType.Pointer;

                                    if (variableExpr.AssignmentExpression != null)
                                    {
                                        var assignmentType = ResolveType(CalcReturnType(variableExpr.AssignmentExpression, scope, typeMap, functionMap, constMap, globalMap), typeMap);

                                        if (TryGenerateImplicitCast(variableExpr.AssignmentExpression, globalType, scope, typeMap, functionMap, constMap, globalMap, out var typedAssignment, out var error) == false)
                                            Fail($"Cannot assign expression of type '{assignmentType}' to global variable '{variableExpr.Name}' of type '{globalType}'! (Implicit cast error: '{error}')");

                                        // We are loading a pointer so 'loadl' is fine
                                        builder.AppendLine($"\tloadl #{variableExpr.Name}");

                                        EmitExpression(builder, typedAssignment, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                                        
                                        StoreVariable(builder, variable, typeMap);
                                    }

                                    if (produceResult)
                                    {
                                        // We are loading a pointer so 'loadl' is fine
                                        builder.AppendLine($"\tloadl #{variableExpr.Name}");
                                        LoadVariable(builder, variable, typeMap);
                                    }
                                    break;
                                }
                            case VariableType.Constant:
                                {
                                    if (variableExpr.AssignmentExpression != null)
                                        Fail($"Cannot assign to const '{variableExpr.Name}'!");
                                    
                                    if (produceResult)
                                    {
                                        // We know this works because TryResolveVariable has done it
                                        var constDirective = constMap[variableExpr.Name];

                                        // FIXME: This does not feel good
                                        // We are comparing the type of the constant against string
                                        // So we can load a label instead of a litteral
                                        if (constDirective.Value is ASTStringLitteral)
                                        {
                                            builder.AppendLine($"\tload {(constDirective.Value as ASTStringLitteral).Value}");
                                        }
                                        else
                                        {
                                            LoadVariable(builder, variable, typeMap);
                                        }
                                    }
                                    break;
                                }
                            case VariableType.Pointer:
                                Fail("Something is wrong as TryResolveVariable should not return a Pointer");
                                break;
                            default:
                                Fail($"Unknown variable type '{variable.VariableType}'!");
                                break;
                        }
                        break;
                    }
                case ASTUnaryOp unaryOp:
                    {
                        var type = CalcReturnType(unaryOp.Expr, scope, typeMap, functionMap, constMap, globalMap);
                        int type_size = SizeOfType(type, typeMap);

                        // TODO: Check to see if this expression has side-effects. This way we can avoid poping at the end
                        EmitExpression(builder, unaryOp.Expr, scope, varList, typeMap, functionMap, constMap, globalMap, true);

                        // FIXME: Handle differing type sizes!!!

                        switch (unaryOp.OperatorType)
                        {
                            case ASTUnaryOp.UnaryOperationType.Identity:
                                // Do nothing
                                break;
                            case ASTUnaryOp.UnaryOperationType.Negation:
                                // FIXME: Consider the size of the result of the expression
                                builder.AppendLine("\tneg");
                                break;
                            case ASTUnaryOp.UnaryOperationType.Compliment:
                                builder.AppendLine("\tnot");
                                break;
                            case ASTUnaryOp.UnaryOperationType.Logical_negation:
                                // FIXME: Make this better, we can probably avoid the swaps
                                builder.AppendLine("\tload #0");
                                builder.AppendLine("\tswap");
                                builder.AppendLine("\tload #1");
                                builder.AppendLine("\tswap");
                                builder.AppendLine("\tselz");
                                break;
                            case ASTUnaryOp.UnaryOperationType.Dereference:
                                if (type is ASTPointerType == false) Fail($"Cannot dereference non-pointer type '{type}'!");
                                
                                VariableRef variable = new VariableRef
                                {
                                    VariableType = VariableType.Pointer,
                                    Type = (type as ASTPointerType).BaseType,
                                    Comment = $"*[{unaryOp.Expr}]",
                                };

                                LoadVariable(builder, variable, typeMap);
                                break;
                            default:
                                Fail($"Unknown unary operator type {unaryOp.OperatorType}, this is a compiler bug!");
                                break;
                        }

                        if (produceResult == false)
                        {
                            // FIXME: Do this depending on the type size!!!
                            builder.AppendLine("\tpop");
                        }
                        break;
                    }
                case ASTBinaryOp binaryOp:
                    {
                        var leftType = ResolveType(CalcReturnType(binaryOp.Left, scope, typeMap, functionMap, constMap, globalMap), typeMap);
                        var rightType = CalcReturnType(binaryOp.Right, scope, typeMap, functionMap, constMap, globalMap);
                        // Try and cast the right type to the left type so we can apply the binary operation.
                        if (TryGenerateImplicitCast(binaryOp.Right, leftType, scope, typeMap, functionMap, constMap, globalMap, out ASTExpression typedRight, out string error) == false)
                            Fail($"Cannot apply binary operation '{binaryOp.OperatorType}' on differing types '{leftType}' and '{rightType}'!");

                        int type_size = SizeOfType(leftType, typeMap);

                        EmitExpression(builder, binaryOp.Left, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                        EmitExpression(builder, typedRight, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                        // FIXME: Consider the size of the result of the expression
                        switch (binaryOp.OperatorType)
                        {
                            case ASTBinaryOp.BinaryOperatorType.Addition:
                                if (type_size == 1)
                                {
                                    builder.AppendLine("\tadd");
                                }
                                else if (type_size == 2)
                                {
                                    builder.AppendLine("\tladd");
                                }
                                else
                                {
                                    Fail($"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Subtraction:
                                if (type_size == 1)
                                {
                                    builder.AppendLine("\tsub");
                                }
                                else if (type_size == 2)
                                {
                                    builder.AppendLine("\tlsub");
                                }
                                else
                                {
                                    Fail($"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Multiplication:
                                if (type_size == 1)
                                {
                                    builder.AppendLine("\tmul");
                                }
                                else if (type_size == 2)
                                {
                                    builder.AppendLine("\tlmul");
                                }
                                else
                                {
                                    Fail($"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Division:
                                if (type_size == 1)
                                {
                                    builder.AppendLine("\tdiv");
                                }
                                else if (type_size == 2)
                                {
                                    builder.AppendLine("\tldiv");
                                }
                                else
                                {
                                    Fail($"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Modulo:
                                if (type_size == 1)
                                {
                                    builder.AppendLine("\tmod");
                                }
                                else if (type_size == 2)
                                {
                                    builder.AppendLine("\tlmod");
                                }
                                else
                                {
                                    Fail($"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Bitwise_And:
                                if (type_size == 1)
                                {
                                    builder.AppendLine("\tand");
                                }
                                else if (type_size == 2)
                                {
                                    throw new NotImplementedException();
                                }
                                else
                                {
                                    Fail($"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Bitwise_Or:
                                if (type_size == 1)
                                {
                                    builder.AppendLine("\tor");
                                }
                                else if (type_size == 2)
                                {
                                    throw new NotImplementedException();
                                }
                                else
                                {
                                    Fail($"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Bitwise_Xor:
                                if (type_size == 1)
                                {
                                    builder.AppendLine("\txor");
                                }
                                else if (type_size == 2)
                                {
                                    throw new NotImplementedException();
                                }
                                else
                                {
                                    Fail($"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Equal:
                                if (type_size == 1)
                                {
                                    // TODO: Better handling?
                                    builder.AppendLine("\txor ; Equals");
                                }
                                else if (type_size == 2)
                                {
                                    throw new NotImplementedException();
                                }
                                else
                                {
                                    Fail($"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Less_than:
                                if (type_size == 1)
                                {
                                    // FIXME: This is really inefficient
                                    builder.AppendLine("\tswap ; Less than");
                                    builder.AppendLine("\tsub");
                                    builder.AppendLine("\tload #0");
                                    builder.AppendLine("\tswap");
                                    builder.AppendLine("\tload #1");
                                    builder.AppendLine("\tswap");
                                    builder.AppendLine("\tselgz ; If b - a > 0 signed");
                                }
                                else if (type_size == 2)
                                {
                                    throw new NotImplementedException();
                                }
                                else
                                {
                                    Fail($"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Greater_than:
                                if (type_size == 1)
                                {
                                    // FIXME: This is really inefficient
                                    builder.AppendLine("\tsub ; Greater than");
                                    builder.AppendLine("\tload #0");
                                    builder.AppendLine("\tswap");
                                    builder.AppendLine("\tload #1");
                                    builder.AppendLine("\tswap");
                                    builder.AppendLine("\tselgz ; If b - a > 0 signed");
                                }
                                else if (type_size == 2)
                                {
                                    throw new NotImplementedException();
                                }
                                else
                                {
                                    Fail($"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Logical_And:
                                if (type_size == 1)
                                {
                                    // TODO: Fix this!!
                                    builder.AppendLine("\tand");
                                }
                                else if (type_size == 2)
                                {
                                    throw new NotImplementedException();
                                }
                                else
                                {
                                    Fail($"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Logical_Or:
                                if (type_size == 1)
                                {
                                    // TODO: Fix this!!
                                    builder.AppendLine("\tor");
                                }
                                else if (type_size == 2)
                                {
                                    throw new NotImplementedException();
                                }
                                else
                                {
                                    Fail($"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
                                }
                                break;
                            default:
                                Fail($"Unknown binary operator type {binaryOp.OperatorType}, this is a compiler bug!");
                                break;
                        }

                        if (produceResult == false)
                        {
                            builder.AppendLine("\tpop");
                        }
                        break;
                    }
                case ASTConditionalExpression conditional:
                    {
                        int hash = conditional.GetHashCode();
                        // builder.AppendLine($"\t; Ternary {conditional.Condition.GetType()} ({hash})");

                        var ifTrueType = CalcReturnType(conditional.IfTrue, scope, typeMap, functionMap, constMap, globalMap);
                        var ifFalseType = CalcReturnType(conditional.IfFalse, scope, typeMap, functionMap, constMap, globalMap);

                        if (TryGenerateImplicitCast(conditional.IfFalse, ifTrueType, scope, typeMap, functionMap, constMap, globalMap, out ASTExpression typedIfFalse, out string error) == false)
                            Fail($"Cannot return two different types {ifTrueType} and {ifFalseType} from a conditional operator!");

                        if (conditional.Condition is ASTBinaryOp)
                        {
                            // Optimize jump for binary operations
                            GenerateOptimizedBinaryOpJump(builder, conditional.Condition as ASTBinaryOp, $":else_cond_{hash}", scope, varList, typeMap, functionMap, constMap, globalMap);
                        }
                        else
                        {
                            EmitExpression(builder, conditional.Condition, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                            builder.AppendLine($"\tjz :else_cond_{hash}");
                        }
                        
                        // We propagate the produce results to the ifTrue and ifFalse emits.
                        builder.AppendLine($"\t:if_cond_{hash}");
                        EmitExpression(builder, conditional.IfTrue, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                        builder.AppendLine($"\tjmp :post_cond_{hash}");
                        builder.AppendLine($"\t:else_cond_{hash}");
                        EmitExpression(builder, conditional.IfFalse, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                        builder.AppendLine($"\t:post_cond_{hash}");
                        break;
                    }
                case ASTFunctionCall functionCall:
                    {
                        int hash = functionCall.GetHashCode();
                        
                        if (functionMap.TryGetValue(functionCall.FunctionName, out ASTFunction function) == false)
                        {
                            Fail($"No function called '{functionCall.FunctionName}'");
                        }

                        // FIXME!!! Check types!!!
                        if (functionCall.Arguments.Count != function.Parameters.Count)
                            Fail($"Missmaching number of arguments for function {function.Name}! Calling with {functionCall.Arguments.Count} expected {function.Parameters.Count}");

                        // FIXME!! Implement implicit casting!!
                        for (int i = 0; i < function.Parameters.Count; i++)
                        {
                            ASTType targetType = function.Parameters[i].Type;
                            ASTType argumentType = CalcReturnType(functionCall.Arguments[i], scope, typeMap, functionMap, constMap, globalMap);

                            // Try and cast the arguemnt
                            if (TryGenerateImplicitCast(functionCall.Arguments[i], targetType, scope, typeMap, functionMap, constMap, globalMap, out ASTExpression typedArg, out string error) == false)
                                Fail($"Missmatching types on parameter '{function.Parameters[i].Name}' ({i}), expected '{function.Parameters[i].Type}' got '{argumentType}'! (Cast error: '{error}')");

                            // We don't need to check the result as it will have the desired type.

                            // Switch the old argument for the new casted one
                            functionCall.Arguments[i] = typedArg;
                        }

                        if (functionCall.Arguments.Count > 0)
                            builder.AppendLine($"\t; Args to function call ::{functionCall.FunctionName} {hash}");
                        
                        // This means adding a result type to expressions
                        foreach (var arg in functionCall.Arguments)
                        {
                            EmitExpression(builder, arg, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                        }

                        builder.AppendLine($"\t::{function.Name}");

                        if (produceResult == false)
                        {
                            int retSize = SizeOfType(function.ReturnType, typeMap);

                            if (retSize > 0)
                            {
                                // TODO: This can be done in a more optimized way
                                builder.Append("\t");
                                for (int i = 0; i < retSize; i++)
                                {
                                    builder.Append("pop ");
                                }
                                builder.AppendLine();
                            }
                        }
                        break;
                    }
                case ASTPointerExpression pointerExpression:
                    {
                        // We use this to deref a pointer that is loaded to the stack
                        void DerefPointer(ASTPointerType pointerType)
                        {
                            ASTType baseType = pointerType.BaseType;
                            
                            if (baseType == ASTBaseType.Void)
                                Fail("Cannot deference void pointer! Cast to a valid pointer type!");

                            var offsetType = CalcReturnType(pointerExpression.Offset, scope, typeMap, functionMap, constMap, globalMap);
                            // Try to cast the offset to a dword
                            if (TryGenerateImplicitCast(pointerExpression.Offset, ASTBaseType.DoubleWord, scope, typeMap, functionMap, constMap, globalMap, out ASTExpression dwordOffset, out string error) == false)
                                Fail($"Could not generate implicit cast for pointer offset of type {offsetType} to {ASTBaseType.DoubleWord}: {error}");

                            // Emit the casted offset
                            EmitExpression(builder, dwordOffset, scope, varList, typeMap, functionMap, constMap, globalMap, true);

                            int baseTypeSize = SizeOfType(baseType, typeMap);
                            // Multiply by pointer base type size!
                            if (baseTypeSize > 1)
                            {
                                builder.AppendLine($"\tloadl #{baseTypeSize}\t; {pointerType} base type size ({baseTypeSize})");

                                builder.AppendLine($"\tlmul");
                            }

                            VariableRef pointerRef = new VariableRef()
                            {
                                VariableType = VariableType.Pointer,
                                Type = baseType,
                                Comment = $"{pointerExpression.Pointer}[{pointerExpression.Offset}]",
                            };

                            // Add the offset to the pointer
                            builder.AppendLine($"\tladd");
                            
                            if (pointerExpression.Assignment != null)
                            {
                                var assign_type = CalcReturnType(pointerExpression.Assignment, scope, typeMap, functionMap, constMap, globalMap);

                                if (TryGenerateImplicitCast(pointerExpression.Assignment, baseType, scope, typeMap, functionMap, constMap, globalMap, out ASTExpression typedAssign, out error) == false)
                                    Fail($"Cannot assign expression of type '{assign_type}' to pointer to type '{baseType}'! (Implicit cast error: '{error}')");

                                if (produceResult)
                                {
                                    // Copy the pointer address
                                    builder.AppendLine($"\tldup");
                                }

                                EmitExpression(builder, typedAssign, scope, varList, typeMap, functionMap, constMap, globalMap, true);

                                StoreVariable(builder, pointerRef, typeMap);
                                // AppendTypedStore(builder, $"[SP]\t; {pointerExpression.Name}[{pointerExpression.Offset}]", baseType, typeMap);
                            }

                            if (produceResult)
                            {
                                LoadVariable(builder, pointerRef, typeMap);
                                // AppendTypedLoad(builder, $"[SP]\t; {pointerExpression.Name}[{pointerExpression.Offset}]", baseType, typeMap);
                            }
                        }

                        if (pointerExpression.Pointer is ASTVariableExpression variableExpr)
                        {
                            if (TryResolveVariable(variableExpr.Name, scope, globalMap, constMap, typeMap, out VariableRef variable) == false)
                                Fail($"No variable called '{variableExpr.Name}'!");
                            
                            switch (variable.VariableType)
                            {
                                case VariableType.Local:
                                    {
                                        if ((variable.Type is ASTPointerType) == false)
                                            Fail("Cannot dereference a non-pointer type!");
                                        
                                        // Load the local variable. Here we are loading a pointer, so we know we should loadl
                                        builder.AppendLine($"\tloadl {variable.LocalAddress}\t; [{variableExpr.Name}]");

                                        // This does the rest!
                                        DerefPointer(variable.Type as ASTPointerType);
                                        break;
                                    }
                                case VariableType.Global:
                                    {
                                        // This is checked so that is exists in TryResolveVariable
                                        var global = globalMap[variable.GlobalName];

                                        if (global.Type is ASTPointerType)
                                        {
                                            // Load the global variable, because we are loading it as a pointer we are using loadl
                                            builder.AppendLine($"\tloadl #{global.Name}\t; {global.Name}[{pointerExpression.Offset}]");

                                            // This does the rest!
                                            DerefPointer(global.Type as ASTPointerType);
                                        }
                                        else if (global.Type is ASTArrayType)
                                        {
                                            ASTArrayType arrayType = global.Type as ASTArrayType;
                                            
                                            // FIXME: Do bounds check!
                                            // TODO: Handle FixedArray!
                                            builder.AppendLine($"\tloadl #{global.Name}\t; {global.Name}[{pointerExpression.Offset}]");

                                            // This does the rest!
                                            DerefPointer(global.Type as ASTPointerType);
                                        }
                                        else
                                        {
                                            Fail($"Cannot dereference a non-pointer global '{global.Name}'!");
                                        }
                                        break;
                                    }
                                case VariableType.Pointer:
                                    // NOTE: Is this really true? Can we have constant pointers? Or would you need to cast first?
                                    Fail("This should not happen because TryResolveVariable should not return pointers!");
                                    break;
                                case VariableType.Constant:
                                    Fail("Cannot dereference constant!");
                                    break;
                                default:
                                    Fail($"Unknown variable type '{variable.VariableType}'!");
                                    break;
                            }

                        }
                        else
                        {
                            // Here the expression results in a pointer.
                            // We emit the pointer and then dereference it

                            var type = ResolveType(CalcReturnType(pointerExpression.Pointer, scope, typeMap, functionMap, constMap, globalMap), typeMap);

                            if (type is ASTPointerType == false)
                                Fail($"Cannot dereference non-pointer type '{type}'!");

                            var pointerType = type as ASTPointerType;

                            EmitExpression(builder, pointerExpression.Pointer, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);

                            if (produceResult)
                            {
                                DerefPointer(pointerType);
                            }
                        }
                        break;
                    }
                case ASTPointerToVoidPointerCast cast:
                    // We really don't need to do anything as the cast is just for type-safety
                    EmitExpression(builder, cast.From, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                    break;
                case ASTFixedArrayToArrayCast cast:
                    {
                        Fail("We don't have fixed array to array type of cast yet!!");

                        if (cast.From is ASTVariableExpression)
                        {

                        }

                        builder.AppendLine($"\tloadl #{cast.FromType.Size}\t; Size of {cast.FromType} in elements");
                        // We want a pointer to the value
                        // NOTE: We might not want to create AST nodes while emitting assembly because debugging might become harder
                        EmitExpression(builder, cast.From, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                        break;
                    }
                case ASTImplicitCast cast:
                    {
                        if (cast.FromType.Size + 1 == cast.ToType.Size)
                        {
                            if (produceResult) builder.AppendLine($"\tload #0\t; Cast from '{cast.FromType}' to '{cast.To}'");
                            EmitExpression(builder, cast.From, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                        }
                        else
                        {
                            Fail($"We don't know how to cast {cast.FromType} to {cast.ToType} right now!");
                        }
                        break;
                    }
                case ASTExplicitCast cast:
                    {
                        ASTType fromType = CalcReturnType(cast.From, scope, typeMap, functionMap, constMap, globalMap);
                        ASTType toType = ResolveType(cast.To, typeMap);

                        if (TryGenerateImplicitCast(cast.From, toType, scope, typeMap, functionMap, constMap, globalMap, out ASTExpression implicitCast, out string _))
                        {
                            // There existed an implicit cast! Use that!
                            EmitExpression(builder, implicitCast, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                        }
                        else if (cast.From is ASTDoubleWordLitteral && toType == ASTBaseType.Word)
                        {
                            // This is an optimization for dword litterals casted to words. We can just compile time truncate the litteral to 12-bits.
                            ASTDoubleWordLitteral dwordLit = cast.From as ASTDoubleWordLitteral;
                            int truncatedValue = dwordLit.IntValue & 0xFFF;
                            ASTWordLitteral wordLit = new ASTWordLitteral(truncatedValue.ToString(), truncatedValue);
                            EmitExpression(builder, wordLit, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                        }
                        else
                        {
                            // There was no implicit way to do it.
                            // How do we cast structs?

                            if (ResolveType(fromType, typeMap) == ResolveType(toType, typeMap))
                            {
                                // They are the same type behind the scenes, so we just don't do anything
                                EmitExpression(builder, cast.From, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                                
                            }

                            // TODO: Should we hardcode these casts?
                            // Atm we have them hardcoded
                            else if (fromType == ASTBaseType.DoubleWord && toType == ASTBaseType.Word)
                            {
                                // This cast is easy
                                EmitExpression(builder, cast.From, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                                if (produceResult) builder.AppendLine($"\tswap pop\t; cast({toType})");
                            }
                            else if (fromType is ASTPointerType && toType is ASTPointerType)
                            {
                                // We don't have to do anything to swap base type for pointer types!
                                EmitExpression(builder, cast.From, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                            }
                            else
                            {
                                Fail($"There is no explicit cast from {fromType} to {toType}!");
                            }
                        }

                        break;
                    }
                case ASTMemberExpression memberExpression:
                    {
                        ASTType targetType = ResolveType(CalcReturnType(memberExpression.TargetExpr, scope, typeMap, functionMap, constMap, globalMap), typeMap);
                        
                        if (targetType is ASTStructType == false || (memberExpression.Dereference && (targetType as ASTPointerType)?.BaseType is ASTStructType))
                            Fail($"Type {targetType} does not have any members!");

                        var members = (targetType as ASTStructType).Members;
                        int memberIndex = members.FindIndex(m => m.Name == memberExpression.MemberName);
                        if (memberIndex < 0) Fail($"No member called '{memberExpression.MemberName}' in struct '{targetType}'");

                        var memberType = ResolveType(members[memberIndex].Type, typeMap);

                        // Calculate the offset
                        int memberOffset = 0;
                        for (int i = 0; i < memberIndex; i++)
                        {
                            memberOffset += SizeOfType(members[i].Type, typeMap);
                        }

                        var test = memberExpression;

                        ASTExpression typedAssigmnent = null;
                        if (memberExpression.Assignment != null)
                        {
                            var retType = ResolveType(CalcReturnType(memberExpression.Assignment, scope, typeMap, functionMap, constMap, globalMap), typeMap);

                            if (TryGenerateImplicitCast(memberExpression.Assignment, memberType, scope, typeMap, functionMap, constMap, globalMap, out typedAssigmnent, out var error) == false)
                                Fail($"Can't generate implicit cast from type '{retType}' to type '{memberType}'! (Cast error: {error})");
                        }

                        // We look at the expression we should get the memeber from
                        // If that expression is another member expression that does not dereference
                        // Then we can just calculate an offset directly instead of loading the the whole target expression
                        
                        Stack<string> membersComment = new Stack<string>();
                        
                        ASTMemberExpression target = memberExpression;
                        while (target.TargetExpr is ASTMemberExpression next && next.Dereference == false)
                        {
                            ASTType nextType = ResolveType(CalcReturnType(next.TargetExpr, scope, typeMap, functionMap, constMap, globalMap), typeMap);

                            if (nextType is ASTStructType == false)
                                Fail($"Type '{nextType}' does not have any members!");

                            membersComment.Push($"{target.MemberName}");

                            memberOffset += MemberOffset(nextType as ASTStructType, next.MemberName, typeMap, out nextType);

                            target = target.TargetExpr as ASTMemberExpression;
                        }

                        membersComment.Push($"{target.MemberName}");
                        membersComment.Push($"{target.TargetExpr}");

                        string comment = $"[{membersComment.Aggregate((s1, s2) => $"{s1}.{s2}")}]";

                        if (target.TargetExpr is ASTVariableExpression)
                        {
                            ASTVariableExpression varExpr = target.TargetExpr as ASTVariableExpression;

                            if (TryResolveVariable(varExpr.Name, scope, globalMap, constMap, typeMap, out VariableRef variable) == false)
                                Fail($"There is no variable called '{varExpr.Name}'!");

                            if (variable.VariableType == VariableType.Constant)
                                Fail("We don't do complex constants!");

                            if (variable.VariableType == VariableType.Pointer)
                                Fail("Pointers don't have members! Something is weird here because we should not get pointers from 'TryResolveVariable'...");

                            //FIXME: Can we do this better?
                            if (variable.VariableType == VariableType.Local)
                            {
                                VariableRef member = new VariableRef()
                                {
                                    VariableType = VariableType.Local,
                                    LocalAddress = variable.LocalAddress + memberOffset,
                                    Type = memberType,
                                    Comment = comment,
                                };

                                if (typedAssigmnent != null)
                                {
                                    // Load the assignment value
                                    EmitExpression(builder, typedAssigmnent, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                                    // Store that value into local
                                    StoreVariable(builder, member, typeMap);
                                    // AppendTypedStore(builder, $"{variable.Offset + offset}", memberType, typeMap);
                                }

                                if (produceResult)
                                {
                                    LoadVariable(builder, member, typeMap);
                                    // AppendTypedLoad(builder, $"{variable.Offset + offset}", memberType, typeMap);
                                }
                            }
                            else if (variable.VariableType == VariableType.Global)
                            {
                                VariableRef member = new VariableRef
                                {
                                    // NOTE: This might not be the right thing to do...
                                    VariableType = VariableType.Pointer,
                                    Type = memberType,
                                    Comment = comment,
                                };

                                if (typedAssigmnent != null)
                                {
                                    // Can we do this?
                                    builder.AppendLine($"\tloadl #(#{variable.GlobalName} {memberOffset} +)");
                                    EmitExpression(builder, typedAssigmnent, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                                    StoreVariable(builder, member, typeMap);
                                }

                                if (produceResult)
                                {
                                    builder.AppendLine($"\tloadl #(#{variable.GlobalName} {memberOffset} +)");
                                    LoadVariable(builder, member, typeMap);
                                }
                            }
                            else
                            {
                                Fail($"This should not happen! We have a weird VariableType '{variable.VariableType}'!");
                            }
                        }
                        else
                        {
                            // We don't have a way to optimize this yet...
                            // We'll just emit the whole struct, isolate the member, and possibly assign to it...

                            int targetSize = SizeOfType(targetType, typeMap);
                            int memberSize = SizeOfType(memberType, typeMap);
                            
                            EmitExpression(builder, target.TargetExpr, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);

                            if (produceResult)
                            {
                                for (int i = 0; i < memberOffset; i++)
                                {
                                    builder.AppendLine($"\tpop\t; [{target.TargetExpr}]:{targetSize - i - 1}");
                                }

                                for (int i = memberOffset + memberSize; i < targetSize; i++)
                                {
                                    builder.AppendLine($"\tswap pop\t; [{target.TargetExpr}]:{targetSize - i - 1}");
                                }
                            }
                        }
                        break;
                    }
                case ASTSizeofTypeExpression sizeofTypeExpression:
                    {
                        var sizeOfType = ResolveType(sizeofTypeExpression.Type, typeMap);
                        int size = SizeOfType(sizeOfType, typeMap);
                        var resultType = CalcReturnType(sizeofTypeExpression, scope, typeMap, functionMap, constMap, globalMap);

                        if (produceResult)
                        {
                            VariableRef variable = new VariableRef
                            {
                                VariableType = VariableType.Constant,
                                ConstantName = $"{size}",
                                Type = resultType,
                                Comment = $"sizeof({sizeofTypeExpression.Type})"
                            };

                            LoadVariable(builder, variable, typeMap);
                        }
                        break;
                    }
                default:
                    Fail($"Unknown expression type {expression}, this is a compiler bug!");
                    break;
            }
        }

        private static void EmitLitteral(StringBuilder builder, ASTLitteral litteral)
        {
            switch (litteral)
            {
                case ASTWordLitteral wordLitteral:
                    builder.AppendLine($"\tload #{wordLitteral.Value}");
                    break;
                case ASTDoubleWordLitteral dwordLitteral:
                    builder.AppendLine($"\tloadl #{dwordLitteral.Value.Substring(0, dwordLitteral.Value.Length - 1)}");
                    break;
                case ASTBoolLitteral boolLitteral:
                    // NOTE: Should we load the constants instead?
                    builder.AppendLine($"\tload #{(boolLitteral == ASTBoolLitteral.True ? 1 : 0)}\t; {(boolLitteral == ASTBoolLitteral.True ? "true" : "false")}");
                    break;
                case ASTCharLitteral charLitteral:
                    builder.AppendLine($"\tload {charLitteral.Value}");
                    break;
                case ASTStringLitteral stringLitteral:
                    // FIXME: Figure out how to do string constants and string structs!
                    builder.AppendLine($"\tload {stringLitteral.Value}");
                    break;
                default:
                    Fail($"Unknown litteral type {litteral.GetType()}, this is a compiler bug!");
                    break;
            }
        }
    }
}
 