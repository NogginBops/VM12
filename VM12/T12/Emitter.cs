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
    using ImportMap = Dictionary<string, ASTFile>;
    using TypeMap = Dictionary<string, ASTType>;
    using VarList = List<(string Name, int Offset, ASTType Type)>;
    using VarMap = Dictionary<string, (int Offset, ASTType Type)>;

    public struct Assembly
    {
        public string assembly;
        public string funcDebug;
        public AST ast;
    }

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
        private static void Fail(TraceData trace, string error)
        {
            if (trace.StartLine == trace.EndLine)
            {
                throw new InvalidOperationException($"Error in file '{Path.GetFileName(trace.File)}' on line {trace.StartLine}: '{error}'");
            }
            else
            {
                throw new InvalidOperationException($"Error in file '{Path.GetFileName(trace.File)}' on lines {trace.StartLine}-{trace.EndLine}: '{error}'");
            }
        }
        
        // TODO: Should we really include the trace?
        private static ASTType TypeOfVariable(TraceData trace, string variableName, VarMap scope, TypeMap typeMap)
        {
            if (scope.TryGetValue(variableName, out var varType) == false)
                Fail(trace, $"No variable called '{variableName}'!");

            return ResolveType(varType.Type, typeMap);
        }

        private static int SizeOfType(ASTType type, TypeMap typeMap)
        {
            // NOTE: Here we don't check that the underlying types are valid types
            // E.g. We can have a pointer to some type and that type does not have to exist for this type to work
            switch (type)
            {
                case ASTBaseType baseType:
                    return baseType.Size;
                case ASTStructType structType:
                    return structType.Members.Select(member => SizeOfType(member.Type, typeMap)).Sum();
                case ASTPointerType pointerType:
                    return ASTPointerType.Size;
                case ASTFixedArrayType fixedArrayType:
                    return fixedArrayType.Size.IntValue * SizeOfType(fixedArrayType.BaseType, typeMap);
                case ASTArrayType arrayType:
                    return ASTArrayType.Size;
                case ASTFunctionPointerType functionType:
                    return ASTFunctionPointerType.Size;
                case ASTExternType externType:
                    if (typeMap.TryGetValue(externType.TypeName, out var outType))
                        if (outType is ASTExternType outExternType)
                        {
                            return SizeOfType(outExternType.Type, typeMap);
                        }
                        else
                        {
                            return SizeOfType(outType, typeMap);
                        }
                    else
                        return SizeOfType(externType.Type, typeMap);
                default:
                    // We don't fully know the size of the type yet so we consult the TypeMap
                    // FIXME: We can get stuck looping here!
                    return SizeOfType(ResolveType(type, typeMap), typeMap);
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
                        else if (functionMap.TryGetValue(variableExpression.Name, out var function))
                            return ASTFunctionPointerType.Of(expression.Trace, function);
                        else
                            Fail(variableExpression.Trace, $"Could not find variable called '{variableExpression.Name}'!");
                        break;
                    }
                case ASTUnaryOp unaryOp:
                    var retType = CalcReturnType(unaryOp.Expr, scope, typeMap, functionMap, constMap, globalMap);

                    if (unaryOp.OperatorType == ASTUnaryOp.UnaryOperationType.Dereference)
                    {
                        if (retType is ASTPointerType pointerType)
                            retType = ResolveType(pointerType.BaseType, typeMap);
                        else
                            Fail(unaryOp.Trace, $"Cannot derefernece non-pointer type '{retType}'!");
                    }

                    return retType;
                case ASTBinaryOp binaryOp:
                    {
                        ASTType left = CalcReturnType(binaryOp.Left, scope, typeMap, functionMap, constMap, globalMap);
                        ASTType right = CalcReturnType(binaryOp.Right, scope, typeMap, functionMap, constMap, globalMap);

                        if (ASTBinaryOp.IsBooleanOpType(binaryOp.OperatorType))
                        {
                            return ASTBaseType.Bool;
                        }

                        if (TryGenerateImplicitCast(binaryOp.Right, left, scope, typeMap, functionMap, constMap, globalMap, out _, out _))
                        {
                            // We where able to cast the right expression to the left one! Great.
                            return left;
                        }
                        else if (TryGenerateImplicitCast(binaryOp.Left, right, scope, typeMap, functionMap, constMap, globalMap, out _, out _))
                        {
                            return right;
                        }
                        else
                        {
                            Fail(binaryOp.Trace, $"Cannot apply binary op '{binaryOp.OperatorType}' to types '{left}' and '{right}'");
                            return default;
                        }
                    }
                case ASTConditionalExpression conditional:
                    {
                        ASTType left = CalcReturnType(conditional.IfTrue, scope, typeMap, functionMap, constMap, globalMap);
                        ASTType right = CalcReturnType(conditional.IfFalse, scope, typeMap, functionMap, constMap, globalMap);
                        
                        if (TryGenerateImplicitCast(conditional.IfFalse, left, scope, typeMap, functionMap, constMap, globalMap, out _, out _))
                        {
                            // We where able to cast the right expression to the left one! Great.
                            return left;
                        }
                        else if (TryGenerateImplicitCast(conditional.IfTrue, right, scope, typeMap, functionMap, constMap, globalMap, out _, out _))
                        {
                            return right;
                        }
                        else
                        {
                            Fail(conditional.Trace, $"Cannot return differing types '{left}' and '{right}' from conditional operator!");
                            return default;
                        }
                    }
                case ASTContainsExpression containsExpression:
                    return ASTBaseType.Bool;
                case ASTPointerExpression pointerExpression:
                    {
                        var targetType = ResolveType(CalcReturnType(pointerExpression.Pointer, scope, typeMap, functionMap, constMap, globalMap), typeMap);

                        if (targetType is ASTArrayType)
                            return (targetType as ASTArrayType).BaseType;
                        else if (targetType is ASTPointerType)
                            return (targetType as ASTPointerType).BaseType;
                        else if (targetType is ASTFixedArrayType)
                            return (targetType as ASTFixedArrayType).BaseType;

                        Fail(targetType.Trace, $"Cannot dereference non-pointer type '{targetType}'!");
                        return default;
                    }
                case ASTFunctionCall functionCall:
                    {
                        // First check if we have a local variable that maches
                        if (scope.TryGetValue(functionCall.FunctionName, out var local) && local.Type is ASTFunctionPointerType functionPointerType)
                            return functionPointerType.ReturnType;

                        if (functionMap.TryGetValue(functionCall.FunctionName, out ASTFunction function) == false)
                            Fail(functionCall.Trace, $"No function called '{functionCall.FunctionName}'!");

                        return function.ReturnType;
                    }
                case ASTMemberExpression memberExpression:
                    {
                        var targetType = ResolveType(CalcReturnType(memberExpression.TargetExpr, scope, typeMap, functionMap, constMap, globalMap), typeMap);

                        var test = memberExpression;

                        if (memberExpression.Dereference && targetType is ASTPointerType pointerType && pointerType.BaseType is ASTStructType)
                        {
                            targetType = pointerType.BaseType;
                        }

                        // FIXME: This is a hack for now?
                        // We should implement this for normal arrays too
                        if (targetType is ASTFixedArrayType fixedArrayType)
                        {
                            switch (memberExpression.MemberName)
                            {
                                case "length":
                                    return ASTBaseType.DoubleWord;
                                case "data":
                                    return ASTPointerType.Of(fixedArrayType.BaseType);
                                case "end":
                                    // We return a pointer to void to avoid struct size problems
                                    // This is the last valid address of the fixed array
                                    return ASTPointerType.Of(ASTBaseType.Void);
                                default:
                                    Fail(memberExpression.Trace, $"Fixed array type '{targetType}' does not have a memeber '{memberExpression.MemberName}'");
                                    break;
                            }
                        }

                        // TODO: Add length and data members to arrays!
                        /*
                        if (targetType is ASTArrayType || targetType is ASTFixedArrayType)
                        {
                            var arrayType = targetType as ASTArrayType;
                            var fixedArrayType = targetType as ASTFixedArrayType;
                            
                            
                            if (memberExpression.MemberName == "length")
                                return ASTBaseType.DoubleWord;
                            else if (memberExpression.MemberName == "data")
                                return ASTPointerType.Of(arrayType?.BaseType ?? fixedArrayType?.BaseType);
                        }
                        else */
                        if (targetType is ASTStructType)
                        {
                            var (type, name) = (targetType as ASTStructType).Members.Find(m => m.Name == memberExpression.MemberName);
                            if (type == null)
                                Fail(memberExpression.TargetExpr.Trace, $"Type '{targetType}' does not contain a member '{memberExpression.MemberName}'!");

                            return type;
                        }

                        Fail(memberExpression.TargetExpr.Trace, $"Type '{targetType}' does not have members!");
                        return default;
                    }
                case ASTCastExpression cast:
                    // We assume all casts will work. Because if they are in the AST they shuold work!
                    return cast.To;
                case ASTSizeofTypeExpression sizeExpr:
                    int size = SizeOfType(sizeExpr.Type, typeMap);
                    return size > ASTWordLitteral.WORD_MAX_VALUE ? ASTBaseType.DoubleWord : ASTBaseType.Word;
                case ASTAddressOfExpression addressOfExpression:
                    // Address of resturns a pointer to the type of the expression.
                    return ASTPointerType.Of(CalcReturnType(addressOfExpression.Expr, scope, typeMap, functionMap, constMap, globalMap));
                default:
                    Fail(expression.Trace, $"Unknown expression type {expression}, this is a compiler bug!");
                    break;
            }

            return default;
        }

        // FIXME: Redesign this to be able to consider both expressions we want to cast to each other
        // Or rather we want a way to describe unary implicit casts and binary implicit casts.
        // So we can say that an expression must result in a type
        // Or we can say that two expressions must result in the same type
        private static bool TryGenerateImplicitCast(ASTExpression expression, ASTType targetType, VarMap scope, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap, out ASTExpression result, out string error)
        {
            ASTType exprType = CalcReturnType(expression, scope, typeMap, functionMap, constMap, globalMap);

            // NOTE: Should we always resolve? Will this result in unexpected casts?
            
            // TODO: Add optimization for sizeof(x) being casted to dword!

            if (exprType == targetType)
            {
                result = expression;
                error = default;
                return true;
            }
            else if (exprType is ASTFixedArrayType && targetType is ASTArrayType)
            {
                // We can always cast a fixed size array to a non-fixed array.
                result = new ASTFixedArrayToArrayCast(expression.Trace, expression, exprType as ASTFixedArrayType, targetType as ASTArrayType);
                error = null;
                return true;
            }
            else if (expression is ASTWordLitteral && targetType == ASTBaseType.DoubleWord)
            {
                // Here there is a special case where we can optimize the loading of words and dwords
                ASTWordLitteral litteral = expression as ASTWordLitteral;
                // NOTE: Is adding the 'd' to the litteral the right thing to do?
                result = new ASTDoubleWordLitteral(litteral.Trace, litteral.Value + "d", litteral.IntValue);
                error = default;
                return true;
            }
            else if (exprType is ASTPointerType && targetType == ASTPointerType.Of(ASTBaseType.Void))
            {
                result = new ASTPointerToVoidPointerCast(expression.Trace, expression, exprType as ASTPointerType);
                error = default;
                return true;
            }
            else if (exprType == ASTBaseType.String && targetType == ASTPointerType.Of(ASTBaseType.Void))
            {
                // FIXME!!! This is a ugly hack!! When we go over to struct strings this will have to change
                // So we just say that we can conver this. We rely on the fact that we never actually check
                // to see if the expression results in a pointer when generating the cast
                result = new ASTPointerToVoidPointerCast(expression.Trace, expression, ASTPointerType.Of(ASTBaseType.Word));
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
                        result = new ASTImplicitCast(expression.Trace, expression, exprType as ASTBaseType, targetType as ASTBaseType);
                        error = default;
                        return true;
                    }
                    else
                    {
                        result = default;
                        error = "This cast would lead to loss of information, do an explicit cast!";
                        return false;
                    }
                }
                else
                {
                    result = default;
                    error = $"Did not find a cast from '{exprType}' to '{targetType}'!";
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

            ASTExpression typedLeft = condition.Left;
            ASTExpression typedRight = condition.Right;

            // Try and cast the right type to the left type and vise versa so we can apply the binary operation.
            if (TryGenerateImplicitCast(condition.Right, leftType, scope, typeMap, functionMap, constMap, globalMap, out typedRight, out string rightError)) ;
            else if (TryGenerateImplicitCast(condition.Left, rightType, scope, typeMap, functionMap, constMap, globalMap, out typedLeft, out string leftError));
            else Fail(condition.Trace, $"Cannot apply binary operation '{condition.OperatorType}' on differing types '{leftType}' and '{rightType}'!");

            // The out param can set these to null
            typedLeft = typedLeft ?? condition.Left;
            typedRight = typedRight ?? condition.Right;

            var resultType = CalcReturnType(typedLeft, scope, typeMap, functionMap, constMap, globalMap);
            int typeSize = SizeOfType(resultType, typeMap);
            
            // TODO: We can optimize even more if one of the operands is a constant zero!!

            switch (condition.OperatorType)
            {
                case ASTBinaryOp.BinaryOperatorType.Equal:
                    EmitExpression(builder, typedLeft, scope, varList, typeMap, functionMap, constMap, globalMap, true);
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
                        Fail(condition.Trace, $"We only support types with a max size of 2 right now! Got type {resultType} size {typeSize}");
                    }
                    break;
                case ASTBinaryOp.BinaryOperatorType.Not_equal:
                    EmitExpression(builder, typedLeft, scope, varList, typeMap, functionMap, constMap, globalMap, true);
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
                        Fail(condition.Trace, $"We only support types with a max size of 2 right now! Got type {resultType} size {typeSize}");
                    }
                    break;
                case ASTBinaryOp.BinaryOperatorType.Less_than:
                    // left < right
                    // We do:
                    // right - left > 0
                    // If the result is positive left was strictly less than right
                    
                    EmitExpression(builder, typedLeft, scope, varList, typeMap, functionMap, constMap, globalMap, true);
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
                        Fail(condition.Trace, $"We only support types with a max size of 2 right now! Got type {resultType} size {typeSize}");
                    }
                    break;
                case ASTBinaryOp.BinaryOperatorType.Less_than_or_equal:
                    throw new NotImplementedException();

                    EmitExpression(builder, typedLeft, scope, varList, typeMap, functionMap, constMap, globalMap, true);
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
                        Fail(condition.Trace, $"We only support types with a max size of 2 right now! Got type {resultType} size {typeSize}");
                    }
                    break;
                case ASTBinaryOp.BinaryOperatorType.Greater_than:
                    EmitExpression(builder, typedLeft, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                    EmitExpression(builder, typedRight, scope, varList, typeMap, functionMap, constMap, globalMap, true);

                    // We want to jump past the body if left <= right and not jump if left > right
                    // -> left - right <= 0
                    // This is why we use jle and jlel

                    if (typeSize == 1)
                    {
                        builder.AppendLine($"\tsub");
                        builder.AppendLine($"\tjle {jmpLabel}");
                    }
                    else if (typeSize == 2)
                    {
                        builder.AppendLine($"\tlsub");
                        builder.AppendLine($"\tjlel {jmpLabel}");
                    }
                    else
                    {
                        Fail(condition.Trace, $"We only support types with a max size of 2 right now! Got type {resultType} size {typeSize}");
                    }
                    break;
                case ASTBinaryOp.BinaryOperatorType.Greater_than_or_equal:
                    EmitExpression(builder, typedLeft, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                    EmitExpression(builder, typedRight, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                    
                    // Jump if left < right
                    if (typeSize == 1)
                    {
                        builder.AppendLine($"\tsub");
                        builder.AppendLine($"\tjlz {jmpLabel}");
                    }
                    else if (typeSize == 2)
                    {
                        builder.AppendLine($"\tlsub");
                        builder.AppendLine($"\tjlzl {jmpLabel}");
                    }
                    else
                    {
                        Fail(condition.Trace, $"We only support types with a max size of 2 right now! Got type {resultType} size {typeSize}");
                    }
                    break;
                default:
                    // We can't do something smart here :(
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
            public string GlobalName;
            public string ConstantName;
            public string FunctionName;
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
            Function,
        }

        private static bool TryGetLocalVariableRef(string name, VarMap scope, TypeMap typeMap, out VariableRef variable)
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
            else
            {
                variable = default;
                return false;
            }
        }

        private static bool TryResolveVariable(string name, VarMap scope, GlobalMap globalMap, ConstMap constMap, FunctionMap functionMap, TypeMap typeMap, out VariableRef variable)
        {
            if (TryGetLocalVariableRef(name, scope, typeMap, out variable))
            {
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

                // NOTE: This is not the cleanest solution!
                if (global is ASTExternGlobalDirective externGlobalDirective)
                {
                    variable.GlobalName = externGlobalDirective.GlobalDirective.Name;
                }

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
            else if (functionMap.TryGetValue(name, out var function))
            {
                variable = new VariableRef
                {
                    VariableType = VariableType.Function,
                    FunctionName = function.Name,
                    Type = ASTFunctionPointerType.Of(function.Trace, function),
                };

                if (function is ASTExternFunction externFunc)
                {
                    variable.FunctionName = externFunc.Func.Name;
                }

                return true;
            }
            else
            {
                variable = default;
                return false;
            }
        }

        private static void LoadSP(StringBuilder builder, int typeSize, string comment = "")
        {
            switch (typeSize)
            {
                case 1:
                    builder.AppendLine($"\tload [SP]{(comment == null ? "" : $"\t; {comment}")}");
                    break;
                case 2:
                    builder.AppendLine($"\tloadl [SP]{(comment == null ? "" : $"\t; {comment}")}");
                    break;
                default:
                    builder.AppendLine($"\t; {comment} ({typeSize})");

                    int wordsLeft = typeSize;
                    while (wordsLeft >= 2)
                    {
                        wordsLeft -= 2;

                        if (wordsLeft != 0)
                        {
                            builder.AppendLine("\tldup");
                        }

                        builder.AppendLine("\tloadl [SP]");

                        if (wordsLeft != 0)
                        {
                            builder.AppendLine("\tlswap\t; Swap the loaded value with the pointer underneath");
                            builder.AppendLine("\tlinc linc\t; Increment pointer");
                        }
                    }

                    if (typeSize % 2 != 0)
                    {
                        builder.AppendLine("\tloadl [SP]");
                    }

                    //for (int i = 0; i < typeSize / 2; i++)
                    //{
                    //    // TODO: This could probably be done better
                    //    // Duplicate the pointer, load the one word, swap the word with the pointer underneath, increment the pointer
                    //    // Only save the pointer if it is not the last value we are loading

                    //    bool lastValue = (i * 2) == typeSize;

                    //    if (lastValue == false)
                    //    {
                    //        builder.AppendLine("\tldup");
                    //    }

                    //    builder.AppendLine("\tloadl [SP]");

                    //    if (lastValue == false)
                    //    {
                    //        builder.AppendLine("\tlswap\t; Swap the loaded value with the pointer underneath");
                    //        builder.AppendLine("\tlinc linc\t; Increment pointer");
                    //    }
                    //}

                    //if (typeSize % 2 != 0)
                    //{
                    //    builder.AppendLine("\tloadl [SP]");
                    //}
                    break;
            }
        }

        private static void LoadVariable(StringBuilder builder, TraceData trace, VariableRef var, TypeMap typeMap)
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
                    LoadSP(builder, typeSize, var.Comment);
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
                    Fail(trace, $"Unknown variable type '{var.VariableType}'!");
                    break;
            }
        }

        private static void StoreSP(StringBuilder builder, int typeSize, string comment = "")
        {
            // It will be hard to implement storing of things larger than 2 words!
            switch (typeSize)
            {
                case 1:
                    builder.AppendLine($"\tstore [SP]{(comment == null ? "" : $"\t; {comment}")}");
                    break;
                case 2:
                    builder.AppendLine($"\tstorel [SP]{(comment == null ? "" : $"\t; {comment}")}");
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private static void StoreVariable(StringBuilder builder, TraceData trace, VariableRef var, TypeMap typeMap)
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
                    StoreSP(builder, typeSize, var.Comment);
                    break;
                case VariableType.Global:
                    // What do we do here!?
                    throw new NotImplementedException();
                case VariableType.Constant:
                    Fail(trace, $"Cannot modify constant '{var.ConstantName}'!");
                    break;
                default:
                    Fail(trace, $"Unknown variable type '{var.VariableType}'!");
                    break;
            }
        }

        private static ASTType ResolveType(ASTType type, TypeMap typeMap)
        {
            if (type is ASTTypeRef)
                return ResolveType(type.Trace, type.TypeName, typeMap);

            if (type is ASTExternType externType)
            {
                if (externType.Type is ASTTypeRef)
                {
                    if (typeMap.TryGetValue(type.TypeName, out var outType) == false)
                        Fail(type.Trace, $"No type called '{type.TypeName}'!");

                    // The type we will have gotten now will be the imported one with the full type info
                    
                    // FIXME: Do proper checks to ensure that we handle extern type refs 
                    // NOTE: We could check that outType actually is a ASTExternType
                    return (outType as ASTExternType)?.Type ?? outType;
                }
                else
                {
                    return externType.Type;
                }
            }

            if (type is ASTPointerType)
                return ASTPointerType.Of(ResolveType((type as ASTPointerType).BaseType, typeMap));

            return type;
        }

        private static ASTType ResolveType(TraceData trace, string typeName, TypeMap typeMap)
        {
            if (typeMap.TryGetValue(typeName, out ASTType type) == false)
                Fail(trace, $"There is no type called '{typeName}'");

            // FIXME: Detect reference loops
            // NOTE: Does this ever run?
            if (type is ASTTypeRef)
            {
                type = ResolveType(type.Trace, type.TypeName, typeMap);
            }

            return type;
        }

        private static int MemberOffset(ASTStructType type, string memberName,  TypeMap typeMap, out ASTType memberType)
        {
            int memberIndex = type.Members.FindIndex(m => m.Name == memberName);
            if (memberIndex < 0) Fail(type.Trace, $"No member called '{memberName}' in struct '{type}'");

            memberType = ResolveType(type.Members[memberIndex].Type, typeMap);

            // Calculate the offset
            int memberOffset = 0;
            for (int i = 0; i < memberIndex; i++)
            {
                memberOffset += SizeOfType(type.Members[i].Type, typeMap);
            }

            return memberOffset;
        }
        
        private static bool TryConstantFolding(StringBuilder builder, ASTExpression expr, TypeMap typeMap, ConstMap constMap)
        {
            // FIXME: Make this work and use it!

            switch (expr)
            {
                /*
                case ASTLitteral litteral:
                    builder.Append($"{litteral.Value} ");
                    return true;
                */
                case ASTBinaryOp binaryOp:
                    {
                        // If both operands are const we can do the binary op
                        

                        
                        return false;
                    }
                case ASTUnaryOp unaryOp:
                    {
                        
                        return false;
                    }
                case ASTConditionalExpression conditionalExpression:
                    {
                        return false;
                    }
                case ASTContainsExpression containsExpression:
                    {
                        return false;
                    }
                case ASTExternVariableExpression variableExpression:
                    {
                        return false;
                    }
                case ASTSizeofTypeExpression sizeofTypeExpression:
                    {
                        var sizeOfType = ResolveType(sizeofTypeExpression.Type, typeMap);
                        int size = SizeOfType(sizeOfType, typeMap);

                        builder.Append($"{size} ");
                        return true;
                    }
                case ASTAddressOfExpression addressOfExpression:
                    {
                        // If we are taking the address of a global var we can know the pointer?!
                        return false;
                    }
                default:
                    Fail(expr.Trace, $"Unknown expression type '{expr.GetType()}'! This is a compiler bug!!");
                    return false;
            }
        }

        public static Assembly EmitAsem(ASTFile file, AST ast)
        {
            StringBuilder builder = new StringBuilder();

            TypeMap typeMap = ASTBaseType.BaseTypeMap.ToDictionary(kvp => kvp.Key, kvp => (ASTType)kvp.Value);

            ConstMap constMap = new ConstMap();

            // NOTE: This might not be the best solution
            // because when you look for variables you might forget to check the globals
            // This might be fixed with a function to do this.
            // But that might not be desirable either.
            GlobalMap globalMap = new GlobalMap();

            FunctionMap functionMap = file.Functions.ToDictionary(func => func.Name, func => func);

            ImportMap importMap = new ImportMap();
            foreach (var import in file.Directives.Where(d => d is ASTImportDirective).Cast<ASTImportDirective>())
            {
                if (ast.Files.TryGetValue(import.File, out var importFile) == false)
                    Fail(import.Trace, $"Could not find import file '{import.File}'!");
                
                importMap.Add(import.ImportName, importFile.File);
            }

            foreach (var directive in file.Directives)
            {
                EmitDirective(builder, directive, typeMap, functionMap, constMap, globalMap, importMap);
            }

            builder.AppendLine();

            StringBuilder debugBuilder = new StringBuilder();

            foreach (var func in file.Functions)
            {
                EmitFunction(builder, func, typeMap, functionMap, constMap, globalMap, debugBuilder);
                builder.AppendLine();
            }

            Assembly assembly = new Assembly
            {
                assembly = builder.ToString(),
                funcDebug = debugBuilder.ToString(),
                ast = ast,
            };

            return assembly;
        }
        
        private static void EmitDirective(StringBuilder builder, ASTDirective directive, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap, ImportMap importMap)
        {
            switch (directive)
            {
                case ASTVisibilityDirective visibilityDirective:
                    {
                        builder.AppendLine($"{(visibilityDirective.IsPublic ? "!global" : "!private")}");
                        break;
                    }
                case ASTUseDirective use:
                    {
                        builder.AppendLine($"& {Path.GetFileNameWithoutExtension(use.FileName)} {use.FileName}");
                        break;
                    }
                case ASTImportDirective import:
                    {
                        if (importMap.TryGetValue(import.ImportName, out ASTFile file) == false)
                            Fail(import.Trace, $"Could not resolve import of type '{import.ImportName}' and file '{import.File}'!");

                        // FIXME: If we import multiple files into the same name

                        // FIXME: When one file uses a type from another file and that other file is using a type from the first
                        
                        ASTType ImportType(ASTType type)
                        {
                            // Fast path for base types
                            if (type is ASTBaseType) return type;

                            Stack<ASTDereferenceableType> indirections = new Stack<ASTDereferenceableType>();

                            ASTType baseType = type;
                            while (baseType is ASTDereferenceableType dereferenceableType)
                            {
                                indirections.Push(dereferenceableType);
                                baseType = dereferenceableType.DerefType;
                            }

                            // Fast path for base types
                            if (baseType is ASTBaseType) return type;
                            
                            ASTType externType = new ASTExternType(type.Trace, import.ImportName, baseType);
                            
                            // TODO: Fix this fast path!
                            // FIXME: When two file declare the same struct name!!
                            // Here the base type is a type we know! This means it is fine to use it just like it is!
                            //if (typeMap.TryGetValue(externType.TypeName, out ASTType existingType)) return type;

                            // The type to return, with all levels of indirection
                            ASTType returnType = externType;

                            // Here we imnport the extern type and then add back all levels of indirection to the type!
                            foreach (var indirType in indirections)
                            {
                                switch (indirType)
                                {
                                    case ASTPointerType pointerType:
                                        returnType = new ASTPointerType(pointerType.Trace, returnType);
                                        break;
                                    case ASTArrayType arrayType:
                                        returnType = new ASTArrayType(arrayType.Trace, returnType);
                                        break;
                                    case ASTFixedArrayType fixedArrayType:
                                        returnType = new ASTFixedArrayType(fixedArrayType.Trace, returnType, fixedArrayType.Size);
                                        break;
                                    default:
                                        Fail(type.Trace, $"Unknown indirection '{indirType}'! This is a compiler bug!");
                                        break;
                                }
                            }

                            return returnType;
                        }

                        builder.AppendLine($"& {import.ImportName} {Path.ChangeExtension(import.File, ".12asm")}");

                        bool visible = false;
                        foreach (var direct in file.Directives)
                        {
                            if (visible == false && (direct is ASTVisibilityDirective == false))
                                continue;

                            // NOTE: We are duplicating code here

                            switch (direct)
                            {
                                case ASTVisibilityDirective visibilityDirective:
                                    visible = visibilityDirective.IsPublic;
                                    break;
                                case ASTConstDirective constDirective:
                                    {
                                        // FIXME: Implement!!!
                                    }
                                    break;
                                case ASTGlobalDirective globalDirective:
                                    {
                                        var global = new ASTExternGlobalDirective(globalDirective.Trace, import.ImportName, ImportType(globalDirective.Type), globalDirective.Name, globalDirective);
                                        globalMap.Add(global.Name, global);

                                        builder.AppendLine($"<{globalDirective.Name} = extern> ; {global.Name}");
                                    }
                                    break;
                                case ASTStructDeclarationDirective structDecl:
                                    {
                                        string name = $"{import.ImportName}::{structDecl.Name}";

                                        if (typeMap.ContainsKey(name))
                                            Fail(structDecl.Trace, $"Cannot declare struct '{name}' as there already exists a struct with that name!");
                                        
                                        typeMap.Add(name, ImportType(structDecl.DeclaredType));
                                        break;
                                    }
                                default:
                                    break;
                            }
                        }

                        foreach (var func in file.Functions)
                        {
                            var @params = func.Parameters.Select(p => { p.Type = ImportType(p.Type); return p; }).ToList();
                            var importFunc = new ASTExternFunction(func.Trace, import.ImportName, func.Name, ImportType(func.ReturnType), @params, func.Body, func);
                            functionMap.Add(importFunc.Name, importFunc);
                        }
                        
                        break;
                    }
                case ASTExternFunctionDirective externFunc:
                    {
                        // Create a new ASTFunction without body
                        ASTFunction func = new ASTFunction(externFunc.Trace, externFunc.FunctionName, externFunc.ReturnType, externFunc.Parameters, null);
                        // Add that function to the function map
                        functionMap.Add(externFunc.FunctionName, func);
                        break;
                    }
                case ASTExternConstantDirective externConstDirective:
                    {
                        constMap[externConstDirective.Name] = new ASTConstDirective(externConstDirective.Trace, externConstDirective.Type, externConstDirective.Name, null);

                        builder.AppendLine($"<{externConstDirective.Name} = extern>");

                        break;
                    }
                case ASTConstDirective constDirective:
                    {
                        // TODO: Is constant value?
                        // TODO: Resolve constant value
                        // TODO: Resolve conflicts
                        constMap[constDirective.Name] = constDirective;

                        var constType = ResolveType(constDirective.Type, typeMap);

                        if (constType is ASTStructType)
                            Fail(constDirective.Type.Trace, "We don't do constant structs yet? Or there cannot be constant structs!");
                        
                        if (constDirective.Value is ASTStringLitteral)
                        {
                            // FIXME: This is risky, and does not feel super good...
                            // We do nothing as we handle the case when we need the constant
                        }
                        else
                        {
                            // We send in an empty scope as there is no scope
                            var valueType = ResolveType(CalcReturnType(constDirective.Value, new VarMap(), typeMap, functionMap, constMap, globalMap), typeMap);

                            // If we are casting from a (d)word to pointer of those types there is no problem
                            if (constType is ASTPointerType)
                                if (valueType != ASTBaseType.Word && valueType != ASTBaseType.DoubleWord)
                                    Fail(constDirective.Value.Trace, $"Can't convert constant expression of type '{valueType}' to type '{constType}'!");

                            //if (TryConstantFolding(constDirective.Value, typeMap, constMap, out ASTLitteral foldedConst) == false)
                            //    Fail(constDirective.Value.Trace, $"Cannot assign a non-costant value '{constDirective.Value}' to constant '{constDirective.Name}'");
                            
                            // FIXME: Proper constant folding!!!!!!!
                            builder.AppendLine($"<{constDirective.Name} = {(constDirective.Value as ASTLitteral).Value.TrimEnd('d', 'D', 'w', 'W')}>");
                        }
                        break;
                    }
                case ASTGlobalDirective globalDirective:
                    {
                        globalMap[globalDirective.Name] = globalDirective;
                        builder.AppendLine($"<{globalDirective.Name} = auto({SizeOfType(globalDirective.Type, typeMap)})> ; {globalDirective.Type}");
                        break;
                    }
                case ASTStructDeclarationDirective structDeclaration:
                    {
                        string name = structDeclaration.Name;

                        if (typeMap.ContainsKey(name))
                            Fail(structDeclaration.Trace, $"Cannot declare struct '{name}' as there already exists a struct with that name!");
                        
                        builder.AppendLine($"<{name.ToLowerInvariant()}_struct_size = {SizeOfType(structDeclaration.DeclaredType, typeMap)}>");

                        typeMap.Add(name, structDeclaration.DeclaredType);
                        break;
                    }
                default:
                    Fail(directive.Trace, $"Unknown directive {directive}, this is a compiler bug!");
                    break;
            }
        }

        private static void EmitFunction(StringBuilder builder, ASTFunction func, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap, StringBuilder debugBuilder)
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
                    VariableRef paramVariable = new VariableRef()
                    {
                        VariableType = VariableType.Local,
                        LocalAddress = index,
                        Type = param.Type,
                    };

                    LoadVariable(builder, func.Trace, paramVariable, typeMap);

                    index += SizeOfType(param.Type, typeMap);
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
                var trace = new TraceData
                {
                    File = func.Trace.File,
                    StartLine = func.Trace.EndLine,
                    EndLine = func.Trace.EndLine,
                };

                var returnStatement = new ASTReturnStatement(trace, null);
                func.Body.Add(returnStatement);
            }
            else if (func.Body.Last() is ASTReturnStatement == false)
            {
                Console.WriteLine($"WARNING: The function '{func.Name}' does not end with a return statement, because we don't do control-flow analasys we don't know it the function actually returns!");
            }

            foreach (var blockItem in func.Body)
            {
                EmitBlockItem(builder, blockItem, scope, variableList, ref local_index, typeMap, functionConext, LoopContext.Empty, functionMap, constMap, globalMap);
            }
            
            int locals = local_index;

            string params_string = string.Join(", ", func.Parameters.Select(param => $"/{param.Name} {param.Type.TypeName}"));

            string locals_string = string.Join(", ", variableList.Skip(func.Parameters.Count).Select(var => (var.Type, var.Name)).Select(local => $"/{local.Name} {local.Type.TypeName}"));
            
            // Here we generate debug data
            {
                debugBuilder.AppendLine($":{func.Name}");

                int index = 0;
                foreach (var var in variableList)
                {
                    int varSize = SizeOfType(var.Type, typeMap);

                    for (int i = 0; i < varSize; i++)
                    {
                        // TODO: Do _H and _L for dword args? Or do member names like list.count?
                        debugBuilder.AppendLine($"[local:{index}|{var.Name}{(varSize > 1 ? $"_{i}" : "")}]");
                        index++;
                    }
                }

                debugBuilder.AppendLine();
            }

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
                    Fail(blockItem.Trace, $"Unknown block item {blockItem}, this is a compiler bug!");
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
                        if (scope.ContainsKey(varName)) Fail(variableDeclaration.Trace, $"Cannot declare the variable '{varName}' more than once!");

                        int varOffset = local_index;
                        scope.Add(varName, (varOffset, variableDeclaration.Type));
                        varList.Add((varName, varOffset, variableDeclaration.Type));
                        local_index += SizeOfType(variableDeclaration.Type, typeMap);

                        if (variableDeclaration.Initializer != null)
                        {
                            var initExpr = variableDeclaration.Initializer;
                            var initType = CalcReturnType(initExpr, scope, typeMap, functionMap, constMap, globalMap);

                            if (TryGenerateImplicitCast(initExpr, variableDeclaration.Type, scope, typeMap, functionMap, constMap, globalMap, out ASTExpression typedExpression, out string error) == false)
                                Fail(variableDeclaration.Initializer.Trace, $"Cannot assign expression of type '{initType}' to variable ('{variableDeclaration.VariableName}') of type '{variableDeclaration.Type}'");
                            
                            EmitExpression(builder, typedExpression, scope, varList, typeMap, functionMap, constMap, globalMap, true);

                            VariableRef var = new VariableRef
                            {
                                VariableType = VariableType.Local,
                                LocalAddress = varOffset,
                                Type = variableDeclaration.Type,
                                Comment = $"[{varName}]",
                            };

                            StoreVariable(builder, variableDeclaration.Trace, var, typeMap);
                            // AppendTypedStore(builder, $"{varOffset}\t; [{varName}]", variableDeclaration.Type, typeMap);
                        }
                        break;
                    }
                default:
                    Fail(declaration.Trace, $"Unknown declaration {declaration}, this is a compiler bug!");
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
                            var retExpr = returnStatement.ReturnValueExpression;

                            if (TryGenerateImplicitCast(retExpr, functionConext.ReturnType, scope, typeMap, functionMap, constMap, globalMap, out var typedReturn, out string error) == false)
                                Fail(returnStatement.Trace, $"Cannot return expression of type '{retType}' in a function that returns type '{functionConext.ReturnType}'");
                            
                            EmitExpression(builder, typedReturn, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                            
                            int retSize = SizeOfType(functionConext.ReturnType, typeMap);
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
                            if (functionConext.ReturnType != ASTBaseType.Void)
                                Fail(returnStatement.Trace, $"Cannot return without a value in a function that returns {functionConext.ReturnType}");

                            builder.AppendLine("\tret");
                        }
                        break;
                    }
                case ASTAssignmentStatement assignment:
                    {
                        // This is not used!
                        Fail(assignment.Trace, $"We don't use this AST node type! {assignment}");
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
                        var test = forWithDecl;

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

                        if (whileStatement.Condition is ASTBinaryOp binaryCond)
                        {
                            GenerateOptimizedBinaryOpJump(builder, binaryCond, newLoopContext.EndLabel, scope, varList, typeMap, functionMap, constMap, globalMap);
                        }
                        else
                        {
                            EmitExpression(builder, whileStatement.Condition, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                            builder.AppendLine($"\tjz {newLoopContext.EndLabel}");
                        }
                        
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
                        // Here we need to invert the condition before we try and do an optimized jump!
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
                    Fail(statement.Trace, $"Could not emit code for statement {statement}, this is a compiler bug!");
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
                        if (TryResolveVariable(variableExpr.Name, scope, globalMap, constMap, functionMap, typeMap, out VariableRef variable) == false)
                            Fail(variableExpr.Trace, $"No variable called '{variableExpr.Name}'!");
                        
                        variable.Comment = $"[{variableExpr.Name}]";

                        switch (variable.VariableType)
                        {
                            case VariableType.Local:
                                {
                                    var variableType = TypeOfVariable(variableExpr.Trace, variableExpr.Name, scope, typeMap);

                                    if (variableExpr.AssignmentExpression != null)
                                    {
                                        var assignmentType = ResolveType(CalcReturnType(variableExpr.AssignmentExpression, scope, typeMap, functionMap, constMap, globalMap), typeMap);

                                        if (TryGenerateImplicitCast(variableExpr.AssignmentExpression, variableType, scope, typeMap, functionMap, constMap, globalMap, out var typedAssignment, out var error) == false)
                                            Fail(variableExpr.AssignmentExpression.Trace, $"Cannot assign expression of type '{assignmentType}' to variable '{variableExpr.Name}' of type '{variableType}'! (Implicit cast error: '{error}')");

                                        EmitExpression(builder, typedAssignment, scope, varList, typeMap, functionMap, constMap, globalMap, true);

                                        StoreVariable(builder, variableExpr.AssignmentExpression.Trace, variable, typeMap);
                                    }

                                    if (produceResult)
                                    {
                                        LoadVariable(builder, variableExpr.Trace, variable, typeMap);
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
                                            Fail(variableExpr.AssignmentExpression.Trace, $"Cannot assign expression of type '{assignmentType}' to global variable '{variableExpr.Name}' of type '{globalType}'! (Implicit cast error: '{error}')");

                                        // We are loading a pointer so 'loadl' is fine
                                        builder.AppendLine($"\tloadl #{variableExpr.Name}");

                                        EmitExpression(builder, typedAssignment, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                                        
                                        StoreVariable(builder, variableExpr.Trace, variable, typeMap);
                                    }

                                    if (produceResult)
                                    {
                                        // We are loading a pointer so 'loadl' is fine
                                        builder.AppendLine($"\tloadl #{variable.GlobalName}");
                                        LoadVariable(builder, variableExpr.Trace, variable, typeMap);
                                    }
                                    break;
                                }
                            case VariableType.Constant:
                                {
                                    if (variableExpr.AssignmentExpression != null)
                                        Fail(variableExpr.AssignmentExpression.Trace, $"Cannot assign to const '{variableExpr.Name}'!");
                                    
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
                                            LoadVariable(builder, variableExpr.Trace, variable, typeMap);
                                        }
                                    }
                                    break;
                                }
                            case VariableType.Function:
                                {
                                    if (variableExpr.AssignmentExpression != null)
                                        Fail(variableExpr.AssignmentExpression.Trace, $"Cannot assign to function '{variable.FunctionName}'!");

                                    if (produceResult)
                                    {
                                        builder.AppendLine($"\tloadl :{variable.FunctionName}");
                                    }

                                    break;
                                }
                            case VariableType.Pointer:
                                Fail(variableExpr.Trace, "Something is wrong as TryResolveVariable should not return a Pointer");
                                break;
                            default:
                                Fail(variableExpr.Trace, $"Unknown variable type '{variable.VariableType}'!");
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
                                if (type is ASTPointerType == false) Fail(unaryOp.Trace, $"Cannot dereference non-pointer type '{type}'!");
                                
                                VariableRef variable = new VariableRef
                                {
                                    VariableType = VariableType.Pointer,
                                    Type = (type as ASTPointerType).BaseType,
                                    Comment = $"*[{unaryOp.Expr}]",
                                };

                                LoadVariable(builder, unaryOp.Trace, variable, typeMap);
                                break;
                            default:
                                Fail(unaryOp.Trace, $"Unknown unary operator type {unaryOp.OperatorType}, this is a compiler bug!");
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
                        //if (TryGenerateImplicitCast(binaryOp.Right, leftType, scope, typeMap, functionMap, constMap, globalMap, out ASTExpression typedRight, out string error) == false)
                        //    Fail(binaryOp.Trace, $"Cannot apply binary operation '{binaryOp.OperatorType}' on differing types '{leftType}' and '{rightType}'!");
                        
                        ASTExpression typedLeft = binaryOp.Left;
                        ASTExpression typedRight = binaryOp.Right;

                        // Try and cast the right type to the left type and vise versa so we can apply the binary operation.
                        if (TryGenerateImplicitCast(binaryOp.Right, leftType, scope, typeMap, functionMap, constMap, globalMap, out typedRight, out string rightError)) ;
                        else if (TryGenerateImplicitCast(binaryOp.Left, rightType, scope, typeMap, functionMap, constMap, globalMap, out typedLeft, out string leftError)) ;
                        else Fail(binaryOp.Trace, $"Cannot apply binary operation '{binaryOp.OperatorType}' on differing types '{leftType}' and '{rightType}'!");

                        // The out param can set these to null
                        typedLeft = typedLeft ?? binaryOp.Left;
                        typedRight = typedRight ?? binaryOp.Right;

                        var resultType = CalcReturnType(typedLeft, scope, typeMap, functionMap, constMap, globalMap);
                        int type_size = SizeOfType(resultType, typeMap);

                        EmitExpression(builder, typedLeft, scope, varList, typeMap, functionMap, constMap, globalMap, true);
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
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
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
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
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
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
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
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
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
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Bitwise_And:
                                if (type_size == 1)
                                {
                                    builder.AppendLine("\tand");
                                }
                                else if (type_size == 2)
                                {
                                    // FIXME: Make this better!
                                    builder.AppendLine("\tslswap and");
                                    builder.AppendLine("\tslswap slswap");
                                    builder.AppendLine("\tand");
                                    builder.AppendLine("\tswap");
                                }
                                else
                                {
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
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
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
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
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Equal:
                                if (type_size == 1)
                                {
                                    builder.AppendLine("\tsub setz\t; Equals cmp");
                                }
                                else if (type_size == 2)
                                {
                                    builder.AppendLine("\tlsub lsetz swap pop\t; Equals cmp");
                                }
                                else
                                {
                                    Fail(binaryOp.Trace, $"Cannot compare types larger than 2 words right now! Got type {leftType} with size {type_size}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Not_equal:
                                if (type_size == 1)
                                {
                                    builder.AppendLine("\tsub setnz\t; Not equals cmp");
                                }
                                else if (type_size == 2)
                                {
                                    builder.AppendLine("\tlsub lsetnz swap pop\t; Equals cmp");
                                }
                                else
                                {
                                    Fail(binaryOp.Trace, $"Cannot compare types larger than 2 words right now! Got type {leftType} with size {type_size}");
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
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Greater_than:
                                if (type_size == 1)
                                {
                                    // 1 if left > right
                                    // left - right > 0
                                    builder.AppendLine("\tsub setgz\t; Greater than");
                                }
                                else if (type_size == 2)
                                {
                                    builder.AppendLine("\tlsub lsetgz swap pop\t; Greater than");
                                }
                                else
                                {
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
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
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
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
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {leftType} with size {type_size}");
                                }
                                break;
                            default:
                                Fail(binaryOp.Trace, $"Unknown binary operator type {binaryOp.OperatorType}, this is a compiler bug!");
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

                        // NOTE: We can do a optimization for words with sel instruction

                        var ifTrueType = CalcReturnType(conditional.IfTrue, scope, typeMap, functionMap, constMap, globalMap);
                        var ifFalseType = CalcReturnType(conditional.IfFalse, scope, typeMap, functionMap, constMap, globalMap);

                        if (TryGenerateImplicitCast(conditional.IfFalse, ifTrueType, scope, typeMap, functionMap, constMap, globalMap, out ASTExpression typedIfFalse, out string error) == false)
                            Fail(conditional.Trace, $"Cannot return two different types {ifTrueType} and {ifFalseType} from a conditional operator!");

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
                case ASTContainsExpression containsExpression:
                    {
                        var valueType = ResolveType(CalcReturnType(containsExpression.Value, scope, typeMap, functionMap, constMap, globalMap), typeMap);
                        var lowerType = ResolveType(CalcReturnType(containsExpression.LowerBound, scope, typeMap, functionMap, constMap, globalMap), typeMap);
                        var upperType = ResolveType(CalcReturnType(containsExpression.UpperBound, scope, typeMap, functionMap, constMap, globalMap), typeMap);

                        if (valueType is ASTBaseType == false)
                            Fail(containsExpression.Value.Trace, $"Can only do contains expressions on number types! Got '{valueType}'!");

                        // TODO: Try cast them to each other!!!
                        if (valueType != lowerType)
                            Fail(containsExpression.LowerBound.Trace, $"Lower bound must be a number type! Got '{lowerType}'!");

                        if (valueType != upperType)
                            Fail(containsExpression.UpperBound.Trace, $"Upper bound must be a number type! Got '{upperType}'!");

                        // All types are the same!

                        var value = containsExpression.Value;
                        var lower = containsExpression.LowerBound;
                        var upper = containsExpression.UpperBound;

                        int typeSize = SizeOfType(valueType, typeMap);
                        switch (typeSize)
                        {
                            case 1:
                                // load min
                                // load value
                                // over sub swap
                                // load max 
                                // swap sub
                                // inc sub pop
                                builder.AppendLine($"\t; Contains");
                                EmitExpression(builder, lower, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                                EmitExpression(builder, value, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                                builder.AppendLine($"\tover sub swap");
                                EmitExpression(builder, upper, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                                builder.AppendLine($"\tswap sub");
                                builder.AppendLine($"\tinc sub");
                                if (produceResult) builder.AppendLine($"\tsetc\t; Set to one if the value is contained in the range");
                                else builder.AppendLine($"\tpop");
                                break;
                            case 2:
                                builder.AppendLine($"\t; Contains");
                                EmitExpression(builder, lower, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                                EmitExpression(builder, value, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                                builder.AppendLine($"\tlover lsub lswap");
                                EmitExpression(builder, upper, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                                builder.AppendLine($"\tlswap lsub");
                                builder.AppendLine($"\tlinc lsub pop");
                                if (produceResult) builder.AppendLine($"\tsetc\t; Set to one if the value is contained in the range");
                                else builder.AppendLine($"\tpop");
                                break;
                            default:
                                Fail(containsExpression.Trace, $"This is weird because we shouldn't get types over 2 in size here! Got '{valueType}' with size '{typeSize}'!");
                                break;
                        }
                        break;
                    }
                case ASTFunctionCall functionCall:
                    {
                        int hash = functionCall.GetHashCode();

                        string name;
                        List<(ASTType Type, string Name)> parameters;
                        ASTType returnType;

                        bool virtualCall = false;
                        string functionLabel;

                        // FIXME: Function calls should really just be working on expressions! Or something like that
                        // Then we could do a function that returns a function pointer and then call that function directly

                        // Is there is something in our scope that is a better match for this function we use that and make this a virtual call
                        if (scope.TryGetValue(functionCall.FunctionName, out var variable) && variable.Type is ASTFunctionPointerType functionPointerType)
                        {
                            // This function call is referencing a local var
                            virtualCall = true;

                            name = $"{functionCall.FunctionName}({string.Join(", ", functionPointerType.ParamTypes)}) -> {functionPointerType.ReturnType}";
                            parameters = functionPointerType.ParamTypes.Select(p => (p, p.TypeName)).ToList();
                            returnType = functionPointerType.ReturnType;

                            functionLabel = "[SP]";
                        }
                        else if (functionMap.TryGetValue(functionCall.FunctionName, out ASTFunction function))
                        {
                            name = function.Name;
                            parameters = function.Parameters;
                            returnType = function.ReturnType;

                            functionLabel = function.Name;

                            if (function is ASTExternFunction externFunction)
                                functionLabel = externFunction.Func.Name;
                        }
                        else
                        {
                            Fail(functionCall.Trace, $"No function called '{functionCall.FunctionName}'");
                            return;
                        }
                        
                        // FIXME!!! Check types!!!
                        if (functionCall.Arguments.Count != parameters.Count)
                            Fail(functionCall.Trace, $"Missmaching number of arguments for function {name}! Calling with {functionCall.Arguments.Count} expected {parameters.Count}");

                        // FIXME!! Implement implicit casting!!
                        for (int i = 0; i < parameters.Count; i++)
                        {
                            ASTType targetType = parameters[i].Type;
                            ASTType argumentType = CalcReturnType(functionCall.Arguments[i], scope, typeMap, functionMap, constMap, globalMap);

                            // Try and cast the arguemnt
                            if (TryGenerateImplicitCast(functionCall.Arguments[i], targetType, scope, typeMap, functionMap, constMap, globalMap, out ASTExpression typedArg, out string error) == false)
                                Fail(functionCall.Arguments[i].Trace, $"Missmatching types on parameter '{parameters[i].Name}' ({i}), expected '{parameters[i].Type}' got '{argumentType}'! (Cast error: '{error}')");

                            // We don't need to check the result as it will have the desired type.

                            // Switch the old argument for the new casted one
                            functionCall.Arguments[i] = typedArg;
                        }

                        if (functionCall.Arguments.Count > 0)
                            builder.AppendLine($"\t; Args to function call ::{name} {hash}");
                        
                        // This means adding a result type to expressions
                        foreach (var arg in functionCall.Arguments)
                        {
                            EmitExpression(builder, arg, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                        }

                        if (virtualCall)
                        {
                            // If this is a virtual call
                            // Load the function pointer
                            // Then jump to that!

                            if (TryGetLocalVariableRef(functionCall.FunctionName, scope, typeMap, out var local) == false)
                                Fail(functionCall.Trace, $"Could not find local variable '{functionCall.FunctionName}'! Something weird has happened as we must have found a local earlier!?");
                            if (local.Type is ASTFunctionPointerType == false)
                                Fail(functionCall.Trace, $"Local is not a function pointer!? This is weird because it should have been checked earlier!");

                            local.Comment = $"[{functionCall.FunctionName}]";

                            // Load the pointer and then call the function!
                            LoadVariable(builder, functionCall.Trace, local, typeMap);
                            builder.AppendLine($"\t::[SP]\t; {name} {hash}");
                        }
                        else
                        {
                            // Just call the function
                            builder.AppendLine($"\t::{functionLabel}\t; {hash}");
                        }
                        
                        if (produceResult == false)
                        {
                            int retSize = SizeOfType(returnType, typeMap);

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
                case ASTVirtualFucntionCall virtualFucntionCall:
                    {
                        var targetType = ResolveType(CalcReturnType(virtualFucntionCall.FunctionPointer, scope, typeMap, functionMap, constMap, globalMap), typeMap);

                        if (targetType is ASTFunctionPointerType == false)
                            Fail(virtualFucntionCall.FunctionPointer.Trace, $"Cannot call non-function pointer type '{targetType}'!");

                        ASTFunctionPointerType functionPointerType = targetType as ASTFunctionPointerType;

                        // Check the parameter types
                        // Call the pointer

                        if (functionPointerType.ParamTypes.Count != virtualFucntionCall.Arguments.Count)
                            Fail(virtualFucntionCall.Trace, $"Missmaching number of arguments for type {functionPointerType}! Calling with {virtualFucntionCall.Arguments.Count} expected {functionPointerType.ParamTypes.Count}");

                        List<ASTType> parameters = functionPointerType.ParamTypes;

                        for (int i = 0; i < parameters.Count; i++)
                        {
                            ASTType paramType = parameters[i];
                            ASTType argumentType = CalcReturnType(virtualFucntionCall.Arguments[i], scope, typeMap, functionMap, constMap, globalMap);

                            // Try and cast the arguemnt
                            if (TryGenerateImplicitCast(virtualFucntionCall.Arguments[i], paramType, scope, typeMap, functionMap, constMap, globalMap, out ASTExpression typedArg, out string error) == false)
                                Fail(virtualFucntionCall.Arguments[i].Trace, $"Missmatching types on parameter '{i}', expected '{parameters[i]}' got '{argumentType}'! (Cast error: '{error}')");

                            // We don't need to check the result as it will have the desired type.

                            // NOTE: Should we really modify the AST like this?
                            // Switch the old argument for the new casted one
                            virtualFucntionCall.Arguments[i] = typedArg;
                        }

                        if (virtualFucntionCall.Arguments.Count > 0)
                            builder.AppendLine($"\t; Args to virtual function call to [{virtualFucntionCall.FunctionPointer}]");

                        // This means adding a result type to expressions
                        foreach (var arg in virtualFucntionCall.Arguments)
                        {
                            EmitExpression(builder, arg, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                        }

                        EmitExpression(builder, virtualFucntionCall.FunctionPointer, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                        builder.AppendLine($"\t::[SP]\t; Virtual call to [{virtualFucntionCall.FunctionPointer}]");

                        if (produceResult == false)
                        {
                            int retSize = SizeOfType(functionPointerType.ReturnType, typeMap);

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
                        var test = pointerExpression;

                        // We use this to deref a pointer that is loaded to the stack
                        void DerefPointer<T>(T pointerType) where T : ASTDereferenceableType
                        {
                            ASTType baseType = pointerType.DerefType;
                            
                            if (baseType == ASTBaseType.Void)
                                Fail(pointerType.Trace, "Cannot deference void pointer! Cast to a valid pointer type!");

                            var offsetType = CalcReturnType(pointerExpression.Offset, scope, typeMap, functionMap, constMap, globalMap);
                            // Try to cast the offset to a dword
                            if (TryGenerateImplicitCast(pointerExpression.Offset, ASTBaseType.DoubleWord, scope, typeMap, functionMap, constMap, globalMap, out ASTExpression dwordOffset, out string error) == false)
                                Fail(pointerExpression.Offset.Trace, $"Could not generate implicit cast for pointer offset of type {offsetType} to {ASTBaseType.DoubleWord}: {error}");

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
                                var assignType = CalcReturnType(pointerExpression.Assignment, scope, typeMap, functionMap, constMap, globalMap);

                                if (TryGenerateImplicitCast(pointerExpression.Assignment, baseType, scope, typeMap, functionMap, constMap, globalMap, out ASTExpression typedAssign, out error) == false)
                                    Fail(pointerExpression.Assignment.Trace, $"Cannot assign expression of type '{assignType}' to pointer to type '{baseType}'! (Implicit cast error: '{error}')");

                                if (produceResult)
                                {
                                    // Copy the pointer address
                                    builder.AppendLine($"\tldup");
                                }

                                EmitExpression(builder, typedAssign, scope, varList, typeMap, functionMap, constMap, globalMap, true);

                                StoreVariable(builder, pointerExpression.Trace, pointerRef, typeMap);
                            }

                            if (produceResult)
                            {
                                LoadVariable(builder, pointerExpression.Trace, pointerRef, typeMap);
                            }
                        }
                        
                        if (pointerExpression.Pointer is ASTVariableExpression variableExpr)
                        {
                            if (TryResolveVariable(variableExpr.Name, scope, globalMap, constMap, functionMap, typeMap, out VariableRef variable) == false)
                                Fail(variableExpr.Trace, $"No variable called '{variableExpr.Name}'!");
                            
                            switch (variable.VariableType)
                            {
                                case VariableType.Local:
                                    {
                                        if ((variable.Type is ASTDereferenceableType) == false)
                                            Fail(variableExpr.Trace, "Cannot dereference a non-pointer type!");
                                        
                                        // Load the local variable. Here we are loading a pointer, so we know we should loadl
                                        builder.AppendLine($"\tloadl {variable.LocalAddress}\t; [{variableExpr.Name}]");

                                        // FIXME: We are losing the line number here
                                        // so DerefPointer can't give us a proper error message

                                        // This does the rest!
                                        DerefPointer(variable.Type as ASTDereferenceableType);
                                        break;
                                    }
                                case VariableType.Global:
                                    {
                                        // This is checked so that is exists in TryResolveVariable
                                        var global = globalMap[variable.GlobalName];

                                        if (global.Type is ASTDereferenceableType == false)
                                            Fail(variableExpr.Trace, $"Cannot dereference a non-pointer global '{global.Name}'!");

                                        // Load the global variable, because we are loading it as a pointer we are using loadl
                                        builder.AppendLine($"\tloadl #{global.Name}\t; {global.Name}[{pointerExpression.Offset}]");

                                        // This does the rest!
                                        DerefPointer(global.Type as ASTDereferenceableType);
                                        break;
                                    }
                                case VariableType.Pointer:
                                    // NOTE: Is this really true? Can we have constant pointers? Or would you need to cast first?
                                    Fail(variableExpr.Trace, "This should not happen because TryResolveVariable should not return pointers!");
                                    break;
                                case VariableType.Constant:
                                    {
                                        if (variable.Type is ASTDereferenceableType == false)
                                            Fail(variableExpr.Trace, $"Cannot dereference constant of type '{variable.Type}'!");

                                        // We can dereference constant pointers!
                                        builder.AppendLine($"\tloadl #{variable.ConstantName}\t; {variable.ConstantName}[{pointerExpression.Offset}]");

                                        DerefPointer(variable.Type as ASTDereferenceableType);
                                        break;
                                    }
                                case VariableType.Function:
                                    {
                                        Fail(variableExpr.Trace, $"Cannot deference function pointer '{variable.Type}'");
                                        break;
                                    }
                                default:
                                    Fail(variableExpr.Trace, $"Unknown variable type '{variable.VariableType}'!");
                                    break;
                            }

                        }
                        else
                        {
                            // Here the expression results in a pointer.
                            // We emit the pointer and then dereference it

                            var type = ResolveType(CalcReturnType(pointerExpression.Pointer, scope, typeMap, functionMap, constMap, globalMap), typeMap);
                            
                            if (type is ASTDereferenceableType == false)
                                Fail(pointerExpression.Pointer.Trace, $"Cannot dereference non-pointer type '{type}'!");

                            var pointerType = type as ASTDereferenceableType;
                            
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
                        Fail(cast.Trace, "We don't have fixed array to array type of cast yet!!");

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
                        if (cast.FromType.Size == cast.ToType.Size)
                        {
                            // Do nothing fancy, they have the same size
                            EmitExpression(builder, cast.From, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                        }
                        else if (cast.FromType.Size + 1 == cast.ToType.Size)
                        {
                            if (produceResult) builder.AppendLine($"\tload #0\t; Cast from '{cast.FromType}' to '{cast.To}'");
                            EmitExpression(builder, cast.From, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                        }
                        else
                        {
                            Fail(cast.Trace, $"We don't know how to cast {cast.FromType} to {cast.ToType} right now!");
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
                            ASTWordLitteral wordLit = new ASTWordLitteral(dwordLit.Trace, truncatedValue.ToString(), truncatedValue);
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
                            // TODO: Should we hardcode these casts? The list is getting pretty long, I don't think we should hard code them like this...
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
                            else if (fromType is ASTPointerType && toType == ASTBaseType.DoubleWord)
                            {
                                // We don't have to do anything to convert a pointer to a dword!
                                EmitExpression(builder, cast.From, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType is ASTFunctionPointerType && toType == ASTBaseType.DoubleWord)
                            {
                                // These will have the same size just emit the expression
                                EmitExpression(builder, cast.From, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType == ASTBaseType.Word && toType is ASTPointerType)
                            {
                                if (produceResult) builder.AppendLine($"\tload #0\t; Cast from '{fromType}' to '{toType}'");
                                EmitExpression(builder, cast.From, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType == ASTBaseType.Word && toType is ASTFunctionPointerType)
                            {
                                if (produceResult) builder.AppendLine($"\tload #0\t; Cast from '{fromType}' to '{toType}'");
                                EmitExpression(builder, cast.From, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType == ASTBaseType.DoubleWord && toType is ASTPointerType)
                            {
                                // We don't have to do anything to convert a dword to a pointer!
                                EmitExpression(builder, cast.From, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType == ASTBaseType.DoubleWord && toType is ASTFunctionPointerType)
                            {
                                // We don't have to do anything to convert a dword to a function pointer!
                                EmitExpression(builder, cast.From, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType == ASTBaseType.String && toType == ASTPointerType.Of(ASTBaseType.Word))
                            {
                                // FIXME: Make proper strings! Now we are doing different things for different casts!!
                                // TODO: Proper strings!
                                // Take the string pointer and increment it by two
                                EmitExpression(builder, cast.From, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                                if (produceResult) builder.AppendLine($"\tlinc linc");
                            }
                            else if (fromType == ASTBaseType.String && toType == ASTPointerType.Of(ASTBaseType.DoubleWord))
                            {
                                // FIXME! FIXME! FIXME! FIXME! FIXME!
                                // For now we just cast to the raw pointer and not the data pointer
                                // This is because it is convenient in code while we don't have proper strings
                                EmitExpression(builder, cast.From, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType == ASTBaseType.String && toType == ASTBaseType.DoubleWord)
                            {
                                // FIXME: Make proper strings! Now we are doing different things for different casts!!
                                // Because a string is just a pointer ATM just emit the expresion
                                EmitExpression(builder, cast.From, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType is ASTPointerType && toType == ASTBaseType.String)
                            {
                                // FIXME: Make proper strings! Now we are doing different things for different casts!!
                                // Because a string is just a pointer ATM just emit the expresion
                                EmitExpression(builder, cast.From, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType == ASTPointerType.Of(ASTBaseType.Void) && toType is ASTArrayType arrayType)
                            {
                                // We want a *void to become a []something. That should be fine?
                                // Not really as it would need a length, but that is in the future!
                                // FIXME: Proper arrays with length and stuff!!!

                                // They have the same size so we just emit
                                EmitExpression(builder, cast.From, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType is ASTFixedArrayType fixedArrayType && (toType == ASTPointerType.Of(fixedArrayType.BaseType) || toType == ASTPointerType.Of(ASTBaseType.Void)))
                            {
                                // We take the "data" pointer of the fixed array and use that
                                var data_member = new ASTMemberExpression(cast.From.Trace, cast.From, "data", null, false);
                                EmitExpression(builder, data_member, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                            }
                            else
                            {
                                Fail(cast.Trace, $"There is no explicit cast from {fromType} to {toType}!");
                            }
                        }

                        break;
                    }
                case ASTMemberExpression memberExpression:
                    {
                        var test = memberExpression;

                        ASTType targetType = ResolveType(CalcReturnType(memberExpression.TargetExpr, scope, typeMap, functionMap, constMap, globalMap), typeMap);

                        if (memberExpression.Dereference && (targetType is ASTDereferenceableType == false))
                            Fail(memberExpression.Trace, $"The type '{targetType}' is not a reference type so we can't dereference it! Use '.' instead of '->'.");

                        if (targetType is ASTStructType == false)
                        {
                            if (memberExpression.Dereference && targetType is ASTPointerType pointerType && pointerType.BaseType is ASTStructType)
                            {
                                // FIXME: Do proper dereferencing!!!
                                targetType = pointerType.BaseType;
                            }
                            else if (targetType is ASTFixedArrayType fixedArrayType)
                            {
                                switch (memberExpression.MemberName)
                                {
                                    case "length":
                                        {
                                            // We know the length at compile time! Just put it in there
                                            if (produceResult) builder.AppendLine($"\tloadl #{fixedArrayType.Size}\t; length of [{memberExpression.TargetExpr}] {fixedArrayType}");
                                            return;
                                        }
                                    case "data":
                                        {
                                            // Here we just load the expression that results in the fixedArray, all we are really doing here is changing the type
                                            var dataExpr = new ASTAddressOfExpression(memberExpression.TargetExpr.Trace, memberExpression.TargetExpr);
                                            EmitExpression(builder, dataExpr, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                                            return;
                                        }
                                    case "end":
                                        {
                                            // Here we just load the expression that results in the fixedArray, all we are really doing here is changing the type
                                            // Then add the length - 1 to that pointer
                                            var dataExpr = new ASTAddressOfExpression(memberExpression.TargetExpr.Trace, memberExpression.TargetExpr);
                                            EmitExpression(builder, dataExpr, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);
                                            if (produceResult) builder.AppendLine($"\tloadl #{fixedArrayType.Size}\t; length of [{memberExpression.TargetExpr}] {fixedArrayType}");
                                            if (produceResult) builder.AppendLine($"\tloadl #{SizeOfType(fixedArrayType.BaseType, typeMap)}\t; size of type {fixedArrayType.BaseType}");
                                            if (produceResult) builder.AppendLine($"\tlmul ldec ladd\t; Multiply the length by the size decrement and add to the pointer");
                                            return;
                                        }
                                    default:
                                        Fail(memberExpression.Trace, $"Fixed array type '{targetType}' does not have a memeber '{memberExpression.MemberName}'");
                                        break;
                                }
                            }
                            else
                            {
                                Fail(memberExpression.TargetExpr.Trace, $"Type {targetType} does not have any members!");
                            }
                        }

                        var members = (targetType as ASTStructType).Members;
                        int memberIndex = members.FindIndex(m => m.Name == memberExpression.MemberName);
                        if (memberIndex < 0) Fail(memberExpression.Trace, $"No member called '{memberExpression.MemberName}' in struct '{targetType}'");

                        var memberType = ResolveType(members[memberIndex].Type, typeMap);
                        int memberSize = SizeOfType(memberType, typeMap);

                        // Calculate the offset
                        int memberOffset = 0;
                        for (int i = 0; i < memberIndex; i++)
                        {
                            memberOffset += SizeOfType(members[i].Type, typeMap);
                        }
                        
                        ASTExpression typedAssigmnent = null;
                        if (memberExpression.Assignment != null)
                        {
                            var retType = ResolveType(CalcReturnType(memberExpression.Assignment, scope, typeMap, functionMap, constMap, globalMap), typeMap);

                            if (TryGenerateImplicitCast(memberExpression.Assignment, memberType, scope, typeMap, functionMap, constMap, globalMap, out typedAssigmnent, out var error) == false)
                                Fail(memberExpression.Assignment.Trace, $"Can't generate implicit cast from type '{retType}' to type '{memberType}'! (Cast error: {error})");
                        }

                        // We look at the expression we should get the memeber from
                        // If that expression is another member expression that does not dereference
                        // Then we can just calculate an offset directly instead of loading the the whole target expression
                        
                        Stack<string> membersComment = new Stack<string>();
                        
                        ASTMemberExpression target = memberExpression;

                        // TODO: We don't do this optimization for now. It's somewhat complex, easier to do without
                        // Optimization for when chaining member accesses without dereferencing (i.e. just calc the actual offset)
                        /**
                        while (target.TargetExpr is ASTMemberExpression next && next.Dereference == false)
                        {
                            ASTType nextType = ResolveType(CalcReturnType(next.TargetExpr, scope, typeMap, functionMap, constMap, globalMap), typeMap);

                            if (nextType is ASTStructType == false)
                                Fail(next.TargetExpr.Trace, $"Type '{nextType}' does not have any members!");

                            membersComment.Push($"{target.MemberName}");

                            memberOffset += MemberOffset(nextType as ASTStructType, next.MemberName, typeMap, out nextType);

                            target = target.TargetExpr as ASTMemberExpression;
                        }
                        */

                        membersComment.Push($"{target.MemberName}");
                        membersComment.Push($"{target.TargetExpr}");

                        string comment = $"[{membersComment.Aggregate((s1, s2) => $"{s1}.{s2}")}]";

                        if (target.TargetExpr is ASTVariableExpression varExpr)
                        {
                            if (TryResolveVariable(varExpr.Name, scope, globalMap, constMap, functionMap, typeMap, out VariableRef variable) == false)
                                Fail(varExpr.Trace, $"There is no variable called '{varExpr.Name}'!");

                            // FIXME: Type checking when dereferencing?

                            switch (variable.VariableType)
                            {
                                case VariableType.Local:
                                    {
                                        if (target.Dereference)
                                        {
                                            // The local variable pointer
                                            VariableRef member = new VariableRef()
                                            {
                                                VariableType = VariableType.Local,
                                                LocalAddress = variable.LocalAddress,
                                                Type = memberType,
                                                Comment = comment,
                                            };

                                            // Load the target pointer
                                            LoadVariable(builder, target.Trace, variable, typeMap);
                                            // Add the member offset
                                            builder.AppendLine($"\tloadl #{memberOffset} ladd\t; {target.MemberName} offset");
                                            
                                            if (typedAssigmnent != null)
                                            {
                                                // Duplicate the pointer if we are going to produce a result
                                                if (produceResult) builder.AppendLine("ldup");
                                                // Load the value to store
                                                EmitExpression(builder, typedAssigmnent, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                                                // Store the loaded value at the pointer
                                                StoreSP(builder, memberSize, $"{target.TargetExpr}->{target.MemberName} = {target.Assignment}");
                                            }
                                            
                                            if (produceResult)
                                            {
                                                // Load the result from the pointer
                                                LoadSP(builder, memberSize, comment);
                                            }
                                        }
                                        else
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
                                                StoreVariable(builder, varExpr.Trace, member, typeMap);
                                            }

                                            if (produceResult)
                                            {
                                                LoadVariable(builder, varExpr.Trace, member, typeMap);

                                                if (target.Dereference)
                                                {
                                                    throw new NotImplementedException("FIXME");
                                                    // FIXME
                                                    // Add the member offset
                                                    // Load the variable
                                                }
                                            }
                                        }
                                        break;
                                    }
                                case VariableType.Global:
                                    {
                                        if (target.Dereference)
                                        {

                                            // Load the global pointer
                                            builder.AppendLine($"\tloadl #{variable.GlobalName}\t; [{variable.GlobalName}]");
                                            // Deref that pointer
                                            LoadSP(builder, SizeOfType(variable.Type, typeMap), $"<< [{variable.GlobalName}]");
                                            // Add the member offset
                                            builder.AppendLine($"\tloadl #{memberOffset} ladd\t; {target.MemberName} offset");

                                            if (typedAssigmnent != null)
                                            {
                                                // Duplicate the pointer if we are going to produce a result
                                                if (produceResult) builder.AppendLine("ldup");
                                                // Load the value to store
                                                EmitExpression(builder, typedAssigmnent, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                                                // Store the loaded value at the pointer
                                                StoreSP(builder, memberSize, $"{target.TargetExpr}->{target.MemberName} = {target.Assignment}");
                                            }

                                            if (produceResult)
                                            {
                                                // Load the result from the pointer
                                                LoadSP(builder, memberSize, $"[{target.TargetExpr}->{target.MemberName}]");
                                            }
                                        }
                                        else
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
                                                StoreVariable(builder, varExpr.Trace, member, typeMap);
                                            }

                                            if (produceResult)
                                            {
                                                builder.AppendLine($"\tloadl #(#{variable.GlobalName} {memberOffset} +)");
                                                LoadVariable(builder, varExpr.Trace, member, typeMap);
                                            }
                                        }
                                        break;
                                    }
                                case VariableType.Constant:
                                    Fail(varExpr.Trace, "We don't do complex constants!");
                                    break;
                                case VariableType.Pointer:
                                    Fail(varExpr.Trace, "Pointers don't have members! Something is weird here because we should not get pointers from 'TryResolveVariable'...");
                                    break;
                                case VariableType.Function:
                                    Fail(varExpr.Trace, "Function pointers don't have members! (yet?)");
                                    break;
                                default:
                                    Fail(varExpr.Trace, $"This should not happen! We have a weird VariableType '{variable.VariableType}'!");
                                    break;
                            }
                        }
                        else if (target.Dereference == false && target.TargetExpr is ASTPointerExpression pointerExpression && pointerExpression.Assignment == null)
                        {
                            // Here we are derefing something and just taking one thing from the result.
                            // Then we can just get the pointer that points to the member
                            // We don't do this if we are dereferencing once again becase then we can't just
                            // add to the pointer

                            // If we are assigning to the pointer this becomes harder, so we just don't do this atm

                            if (pointerExpression.Assignment != null) Fail(pointerExpression.Assignment.Trace, $"Assigning to the pointer expression should not happen here!");

                            var pointerType = CalcReturnType(pointerExpression.Pointer, scope, typeMap, functionMap, constMap, globalMap);

                            // Load the pointer value
                            if (pointerType is ASTFixedArrayType fixedArray)
                            {
                                // If this is a fixed array, loading it will mean loading the full array, we just want a pointer to it
                                // So we load the "data" member
                                var fixedArrayPointerExpr = new ASTMemberExpression(pointerExpression.Trace, pointerExpression.Pointer, "data", null, false);
                                EmitExpression(builder, fixedArrayPointerExpr, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                            }
                            else
                            {
                                EmitExpression(builder, pointerExpression.Pointer, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                            }

                            // If the member has a offset, add that offset
                            if (memberOffset != 0) builder.AppendLine($"\tloadl #{memberOffset} ladd\t; Offset to member {memberExpression.MemberName}");

                            VariableRef variable = new VariableRef
                            {
                                VariableType = VariableType.Pointer,
                                Type = memberType,
                                Comment = $"[{memberExpression.MemberName}]",
                            };

                            LoadVariable(builder, memberExpression.Trace, variable, typeMap);
                        }
                        else
                        {
                            // We don't have a way to optimize this yet...
                            // We'll just emit the whole struct, isolate the member, and possibly assign to it...
                            int targetSize = SizeOfType(targetType, typeMap);
                            
                            EmitExpression(builder, target.TargetExpr, scope, varList, typeMap, functionMap, constMap, globalMap, produceResult);

                            if (memberExpression.Dereference)
                            {
                                throw new NotImplementedException("We don't do this type of member dereferencing yet!");
                            }

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

                            LoadVariable(builder, sizeofTypeExpression.Trace, variable, typeMap);
                        }
                        break;
                    }
                case ASTAddressOfExpression addressOfExpression:
                    {
                        if (addressOfExpression.Expr is ASTVariableExpression variableExpression)
                        {
                            if (TryResolveVariable(variableExpression.Name, scope, globalMap, constMap, functionMap, typeMap, out var variable) == false)
                                Fail(addressOfExpression.Expr.Trace, $"No variable called '{variableExpression.Name}'!");
                            
                            switch (variable.VariableType)
                            {
                                case VariableType.Local:
                                    {
                                        builder.AppendLine($"\t[FP]");
                                        // Load the number of locals from the frame pointer
                                        builder.AppendLine($"\t[FP] loadl [SP]\t; Load the number of locals");
                                        builder.AppendLine($"\tloadl #{variable.LocalAddress}");
                                        builder.AppendLine($"\tlsub\t; Subtract the local index");
                                        builder.AppendLine($"\tlsub\t; &{addressOfExpression.Expr}");
                                        break;
                                    }
                                case VariableType.Global:
                                    {
                                        builder.AppendLine($"\tloadl #{variable.GlobalName}\t; &[{variable.GlobalName}]");
                                        break;
                                    }
                                case VariableType.Pointer:
                                    Fail(addressOfExpression.Trace, $"TryResolveVariable should not return variable of type pointer!");
                                    break;
                                case VariableType.Constant:
                                    Fail(addressOfExpression.Trace, $"Cannot take address of constant '{variable.ConstantName}'!");
                                    break;
                                case VariableType.Function:
                                    {
                                        builder.AppendLine($"\tloadl :{variable.FunctionName}");
                                        break;
                                    }
                                default:
                                    Fail(addressOfExpression.Trace, $"Unknown variable type: '{variable.VariableType}'!");
                                    break;
                            }
                        }
                        else if (addressOfExpression.Expr is ASTPointerExpression pointerExpression)
                        {
                            // FIXME: This is doing a lot of duplicate code from ASTPointerExpression!!!
                            // We could fix this by factoring this out of the ASTPointerExpression thing.

                            // When we have the address of a pointer expression
                            // We just calculate the pointer plus the offset and don't dereference

                            var pointerType = CalcReturnType(pointerExpression.Pointer, scope, typeMap, functionMap, constMap, globalMap);
                            if (pointerType is ASTDereferenceableType == false)
                                Fail(pointerExpression.Pointer.Trace, $"Cannot dereference type '{pointerType}'!");

                            var baseType = (pointerType as ASTDereferenceableType).DerefType;

                            // So if the pointer is a fixed size array, and we know the pointer to that array
                            
                            // NOTE: Here we do special behaviour for fixed arrays to not load the whole fixed array.
                            // I don't know if it would actually work without this.... yikes
                            if (pointerType is ASTFixedArrayType fixedArray)
                            {
                                var dataMember = new ASTMemberExpression(addressOfExpression.Trace, pointerExpression.Pointer, "data", null, false);
                                EmitExpression(builder, dataMember, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                            }
                            else
                            {
                                EmitExpression(builder, pointerExpression.Pointer, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                            }
                            
                            var offsetType = CalcReturnType(pointerExpression.Offset, scope, typeMap, functionMap, constMap, globalMap);
                            // Try to cast the offset to a dword
                            if (TryGenerateImplicitCast(pointerExpression.Offset, ASTBaseType.DoubleWord, scope, typeMap, functionMap, constMap, globalMap, out ASTExpression dwordOffset, out string error) == false)
                                Fail(pointerExpression.Offset.Trace, $"Could not generate implicit cast for pointer offset of type {offsetType} to {ASTBaseType.DoubleWord}: {error}");

                            // Emit the casted offset
                            EmitExpression(builder, dwordOffset, scope, varList, typeMap, functionMap, constMap, globalMap, true);

                            int baseTypeSize = SizeOfType(baseType, typeMap);
                            // Multiply by pointer base type size!
                            if (baseTypeSize > 1)
                            {
                                builder.AppendLine($"\tloadl #{baseTypeSize}\t; {pointerType} base type size ({baseTypeSize})");

                                builder.AppendLine($"\tlmul");
                            }

                            // Add the offset to the pointer
                            builder.AppendLine($"\tladd");

                            // This will be the address where the element is stored
                        }
                        else
                        {
                            Fail(addressOfExpression.Trace, $"Unsupported or invalid type for address of: '{addressOfExpression.Expr}'");
                        }
                        break;
                    }
                default:
                    Fail(expression.Trace, $"Unknown expression type {expression}, this is a compiler bug!");
                    break;
            }
        }

        private static void EmitLitteral(StringBuilder builder, ASTLitteral litteral)
        {
            switch (litteral)
            {
                case ASTWordLitteral wordLitteral:
                    builder.AppendLine($"\tload #{litteral}");
                    break;
                case ASTDoubleWordLitteral dwordLitteral:
                    builder.AppendLine($"\tloadl #{litteral}");
                    break;
                case ASTCharLitteral charLitteral:
                    builder.AppendLine($"\tload {litteral}");
                    break;
                case ASTStringLitteral stringLitteral:
                    builder.AppendLine($"\tload {litteral}");
                    break;
                case ASTBoolLitteral boolLitteral:
                    // NOTE: Should we load the constants instead?
                    // We have moved to litterals just needing ToString to be valid for emitting,
                    // Bools don't follow this for now but should probably be changed
                    builder.AppendLine($"\tload #{(boolLitteral.BoolValue ? 1 : 0)}\t; {(boolLitteral.BoolValue ? "true" : "false")}");
                    break;
                case ASTNullLitteral nullLitteral:
                    // The same goes for null as for bool, its not directly emittable for now!
                    builder.AppendLine($"\tloadl #0\t; null");
                    break;
                default:
                    Fail(litteral.Trace, $"Unknown litteral type {litteral.GetType()}, this is a compiler bug!");
                    break;
            }
        }
    }
}
 