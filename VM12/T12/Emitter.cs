using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using VM12Util;

namespace T12
{
    using ConstMap = Dictionary<string, ASTConstDirective>;
    using FunctionMap = Dictionary<string, List<ASTFunction>>;
    using GlobalMap = Dictionary<string, ASTGlobalDirective>;
    using ImportMap = Dictionary<string, ASTFile>;
    using TypeMap = Dictionary<string, ASTType>;
    using VarList = List<(string Name, int Offset, ASTType Type)>;
    using VarMap = Dictionary<string, (int Offset, ASTType Type)>;
    using SpecializationList = List<(ASTFunction Specialization, List<ASTType> GenericTypes)>;
    using SpecializationMap = Dictionary<ASTGenericFunction, List<(ASTFunction Specialization, List<ASTType> GenericTypes)>>;

    using static ConstantFolding;

    public struct Assembly
    {
        public StringBuilder assembly;
        public StringBuilder funcDebug;
        public AST ast;
    }

    public class Context
    {
        public readonly FunctionConext FunctionConext;
        public readonly LoopContext LoopContext;

        // This is not a struct to make it easier to increment the values.
        // This breaks the immulabillity of this object but oh well...
        public readonly LabelContext LabelContext;

        public Context(FunctionConext functionConext, LoopContext loopContext, LabelContext labelContext)
        {
            this.FunctionConext = functionConext;
            this.LoopContext = loopContext;
            this.LabelContext = labelContext;
        }

        public Context With(FunctionConext functionConext)
        {
            return new Context(functionConext, LoopContext, LabelContext);
        }

        public Context With(LoopContext loopContext)
        {
            return new Context(FunctionConext, loopContext, LabelContext);
        }

        public Context With(LabelContext labelContext)
        {
            return new Context(FunctionConext, LoopContext, labelContext);
        }
    }

    public class LabelContext
    {
        public int IfLabels;
        public int ConditionalLabels;
        public int BoundsLabels;
        public int WhileLabel;
        public int DoWhileLabel;
        public int ForLabel;
        public int FunctionCalls;
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
    
    public static partial class Emitter
    {
        internal static void Fail(TraceData trace, string error)
        {
            Compiler.CurrentErrorHandler?.Invoke(Compiler.MessageData.FromError(trace, error));

            if (trace.StartLine == trace.EndLine)
            {
                throw new InvalidOperationException($"Error in file '{Path.GetFileName(trace.File)}' on line {trace.StartLine}: '{error}'");
            }
            else
            {
                throw new InvalidOperationException($"Error in file '{Path.GetFileName(trace.File)}' on lines {trace.StartLine}-{trace.EndLine}: '{error}'");
            }
        }

        internal static void Warning(TraceData trace, string warning)
        {
            Compiler.CurrentErrorHandler?.Invoke(Compiler.MessageData.FromWarning(trace, warning));
            
            if (trace.StartLine == trace.EndLine)
            {
                Console.WriteLine($"WARNING ({Path.GetFileName(trace.File)}:{trace.StartLine}): '{warning}'");
            }
            else
            {
                Console.WriteLine($"WARNING ({Path.GetFileName(trace.File)}:{trace.StartLine}-{trace.EndLine}): '{warning}'");
            }
        }

        // TODO: Should we really include the trace?
        internal static ASTType TypeOfVariable(TraceData trace, string variableName, VarMap scope, TypeMap typeMap)
        {
            if (scope.TryGetValue(variableName, out var varType) == false)
                Fail(trace, $"No variable called '{variableName}'!");

            return ResolveType(varType.Type, typeMap);
        }

        internal static int SizeOfType(ASTType type, TypeMap typeMap)
        {
            // NOTE: Here we don't check that the underlying types are valid types
            // E.g. We can have a pointer to some type and that type does not have to exist for this type to work
            switch (type)
            {
                case ASTBaseType baseType:
                    return baseType.Size;
                case ASTStructType structType:
                    return structType.Members.Select(member => SizeOfType(member.Type, typeMap)).Sum();
                case ASTGenericType genericType:
                    Fail(genericType.Trace, "Generic types do note have a size!");
                    return -1;
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
                case ASTAliasedType aliasedType:
                    return SizeOfType(aliasedType.RealType, typeMap);
                default:
                    // We don't fully know the size of the type yet so we consult the TypeMap
                    // FIXME: We can get stuck looping here!
                    // Why do we get stuck in a loop here??? - 2019-10-15
                    return SizeOfType(ResolveType(type, typeMap), typeMap);
            }
        }

        internal static ASTType CalcReturnType(ASTExpression expression, VarMap scope, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap)
        {
            switch (expression)
            {
                case ASTLitteral litteral:
                    return litteral.Type;
                case ASTStructLitteral structLitteral:
                    return structLitteral.StructType;
                case ASTVariableExpression variableExpression:
                    {
                        if (scope.TryGetValue(variableExpression.Name, out var varType))
                            return varType.Type;
                        else if (constMap.TryGetValue(variableExpression.Name, out var constDirective))
                            return constDirective.Type;
                        else if (globalMap.TryGetValue(variableExpression.Name, out var globalDirective))
                            return globalDirective.Type;
                        else if (functionMap.TryGetValue(variableExpression.Name, out var functions))
                            if (functions.Count > 1) Fail(default, $"We cannot take function pointer to the overloaded function '{variableExpression.Name}'! ");
                            else return ASTFunctionPointerType.Of(expression.Trace, functions[0]);
                        else
                            Fail(variableExpression.Trace, $"Could not find variable called '{variableExpression.Name}'!");
                        break;
                    }
                case ASTUnaryOp unaryOp:
                    {
                        var retType = CalcReturnType(unaryOp.Expr, scope, typeMap, functionMap, constMap, globalMap);

                        if (unaryOp.OperatorType == ASTUnaryOp.UnaryOperationType.Dereference)
                        {
                            if (retType is ASTPointerType pointerType)
                                retType = ResolveType(pointerType.BaseType, typeMap);
                            else
                                Fail(unaryOp.Trace, $"Cannot derefernece non-pointer type '{retType}'!");
                        }

                        return retType;
                    }
                case ASTBinaryOp binaryOp:
                    {
                        ASTType left = CalcReturnType(binaryOp.Left, scope, typeMap, functionMap, constMap, globalMap);
                        ASTType right = CalcReturnType(binaryOp.Right, scope, typeMap, functionMap, constMap, globalMap);

                        if (TypesCompatibleWithBinaryOp(left, right, binaryOp.OperatorType, typeMap, out ASTType result) == false)
                            Fail(binaryOp.Trace, $"Cannot apply binary op '{binaryOp.OperatorType}' to types '{left}' and '{right}'");

                        return result;
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
                    return ASTBaseType.Bool.WithTrace(containsExpression.Trace);
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

                        if (functionMap.TryGetValue(functionCall.FunctionName, out var functions) == false)
                            Fail(functionCall.Trace, $"No function called '{functionCall.FunctionName}'!");

                        List<ASTType> argumentTypes = functionCall.Arguments.Select(a => CalcReturnType(a, scope, typeMap, functionMap, constMap, globalMap)).ToList();

                        if(TryFindBestFunctionMatch(functionCall.Trace, functions,  argumentTypes, typeMap, out var func) == false)
                            Fail(functionCall.Trace, $"No overload for function '{functionCall.FunctionName}' taking arguments of types {string.Join(", ",  argumentTypes)}!");

                        ASTType returnType = func.ReturnType;
                        if (functionCall is ASTGenericFunctionCall genericFunctionCall)
                        {
                            if (func is ASTGenericFunction genFunc)
                            {
                                var genMap = GenerateGenericMap(genericFunctionCall.Trace, genFunc.GenericNames, genericFunctionCall.GenericTypes);
                                
                                returnType = SpecializeType(functionCall.Trace, returnType, genMap);

                                if (returnType is ASTTypeRef typeRef)
                                {
                                    // Here we try to substitue the return type
                                    int index = genFunc.GenericNames.IndexOf(typeRef.Name);
                                    if (index != -1)
                                    {
                                        returnType = genericFunctionCall.GenericTypes[index];
                                    }
                                }
                            }
                            else
                                Fail(genericFunctionCall.Trace, "Cannot call a non-generic function with generic arguments");
                        }

                        // NOTE: For now we don't check the case where func is a generic function and the call is not generic!

                        returnType = ResolveType(returnType, typeMap);
                        return returnType;
                    }
                case ASTVirtualFunctionCall virtualFucntionCall:
                    {
                        var funcPointerType = CalcReturnType(virtualFucntionCall.FunctionPointer, scope, typeMap, functionMap, constMap, globalMap);

                        if (funcPointerType is ASTFunctionPointerType == false)
                            Fail(virtualFucntionCall.FunctionPointer.Trace, $"Type '{funcPointerType}' is not a function pointer and cannot be called as such!");

                        return (funcPointerType as ASTFunctionPointerType).ReturnType;
                    }
                case ASTMemberExpression memberExpression:
                    {
                        var targetType = ResolveType(CalcReturnType(memberExpression.TargetExpr, scope, typeMap, functionMap, constMap, globalMap), typeMap);

                        var test = memberExpression;

                        if (memberExpression.Dereference && targetType is ASTPointerType pointerType && pointerType.BaseType is ASTStructType)
                        {
                            targetType = pointerType.BaseType;
                        }
                        
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
                        else if (targetType is ASTArrayType arrayType)
                        {
                            switch (memberExpression.MemberName)
                            {
                                case "length":
                                    return ASTBaseType.DoubleWord;
                                case "data":
                                    return ASTPointerType.Of(arrayType.BaseType);
                                default:
                                    Fail(memberExpression.Trace, $"Fixed array type '{targetType}' does not have a memeber '{memberExpression.MemberName}'");
                                    break;
                            }
                        }
                        
                        if (targetType is ASTStructType structType)
                        {
                            if (TryGetStructMember(structType, memberExpression.MemberName, typeMap, out var member) == false)
                                Fail(memberExpression.TargetExpr.Trace, $"Type '{targetType}' does not contain a member '{memberExpression.MemberName}'!");

                            return member.Type;
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
                case ASTTypeOfExpression typeExpr:
                    // FIXME: We actually want to return a pointer to a struct...? Or do we want a offset into the type table...?
                    // Anyways we want a more proper type! For now we are doing an offset into the type table
                    return ASTAliasedType.Of("TypeID", ASTBaseType.DoubleWord);
                case ASTDefaultExpression defaultExpression:
                    return defaultExpression.Type;
                case ASTInlineAssemblyExpression assemblyExpression:
                    // Just trust that the programmer is right.
                    return assemblyExpression.ResultType;
                case ASTInternalCompoundExpression compoundExpression:
                    // Just trust that the compiler is right.
                    return compoundExpression.ResultType;
                default:
                    Fail(expression.Trace, $"Unknown expression type {expression}, this is a compiler bug!");
                    break;
            }

            return default;
        }

        internal static bool TypesCompatibleWithBinaryOp(ASTType left, ASTType right, ASTBinaryOp.BinaryOperatorType opType, TypeMap typeMap, out ASTType resultType)
        {
            // We might want a special error message for when we try to add something that is not a numeric type like a string
            if ((left is ASTPointerType && ASTBaseType.IsNumericType(right as ASTBaseType)) ||
                (right is ASTPointerType && ASTBaseType.IsNumericType(left as ASTBaseType)))
            {
                ASTPointerType pType = left as ASTPointerType ?? right as ASTPointerType;

                if (ASTBinaryOp.IsPointerCompatibleOpType(opType))
                {
                    resultType = pType;
                    return true;
                }
                else
                {
                    resultType = default;
                    return false;
                }
            }

            // TODO: Will this work always or what?
            if (ASTBinaryOp.IsBooleanOpType(opType))
            {
                resultType = ASTBaseType.Bool;
                return true;
            }

            if (HasImplicitCast(right, left, typeMap))
            {
                // We where able to cast the right expression to the left one! Great.
                resultType = left;
                return true;
            }
            else if (HasImplicitCast(left, right, typeMap))
            {
                // We where able to cast the left expression to the right one! Great.
                resultType = right;
                return true;
            }
            else
            {
                resultType = default;
                return false;
            }
        }

        internal static bool HasImplicitCast(ASTType from, ASTType to, TypeMap typeMap)
        {
            if (from == to)
            {
                return true;
            }
            else if (from is ASTTypeRef typeRef && typeMap.TryGetValue(typeRef.Name, out ASTType actType) && actType is ASTBaseType baseType && to == baseType)
            {
                return true;
            }
            else if (from is ASTFixedArrayType && to is ASTArrayType)
            {
                return true;
            }
            else if (from is ASTFixedArrayType fixedArray && fixedArray.BaseType == ASTBaseType.Char && to == ASTBaseType.String)
            {
                return true;
            }
            else if (from is ASTDereferenceableType && to == ASTPointerType.Of(ASTBaseType.Void))
            {
                return true;
            }
            else if (from == ASTBaseType.String && to == ASTPointerType.Of(ASTBaseType.Void))
            {
                return true;
            }
            else if (from == ASTBaseType.String && to == ASTArrayType.Of(ASTBaseType.Char))
            {
                return true;
            }
            else if (from is ASTBaseType && to == ASTBaseType.Bool)
            {
                return true;
            }
            else if (from is ASTBaseType fromBase && to is ASTBaseType toBase)
            {
                if (fromBase.Size == toBase.Size)
                {
                    // FIXME: This is not entierly correct!
                    // When is this not correct? - 2019-10-15
                    return true;
                }

                // FIXME: This should not allow word -> string casts...
                // I think that we should just allow all casts of this type and have a
                // well defined meaning. Something like zero padding or sign extending
                else if (fromBase.Size < toBase.Size)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }
        
        // FIXME: This is generating double casts to word.....
        // FIXME: TODO: NOTE: We should really just use the HasImplicitCast fucntion and wrap the expression in an implicit cast
        // That way we really don't have to 
        internal static bool TryGenerateImplicitCast(ASTExpression expression, ASTType targetType, VarMap scope, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap, out ASTExpression result, out string error)
        {
            ASTType exprType = CalcReturnType(expression, scope, typeMap, functionMap, constMap, globalMap);

            exprType = ResolveGenericType(exprType, typeMap);

            // NOTE: Should we always resolve? Will this result in unexpected casts?

            if (exprType == targetType)
            {
                result = expression;
                error = default;
                return true;
            }
            else if (ResolveType(exprType, typeMap) is ASTAliasedType alias && ResolveType(alias.RealType, typeMap) == targetType)
            {
                // If the type is an alias to a base type
                result = expression;
                error = default;
                return true;
            }
            else if (targetType is ASTAliasedType targetAlias && TryGenerateImplicitCast(expression, targetAlias.RealType, scope, typeMap, functionMap, constMap, globalMap, out result, out error))
            {
                // The target was an alias for another type that we knew how to cast to
                return true;
            }
            else if (expression is ASTNullLitteral && targetType is ASTPointerType pType)
            {
                // If it's a null litteral, we can cast that to anything
                result = expression;
                error = null;
                return true;
            }
            else if (exprType is ASTFixedArrayType && targetType is ASTArrayType)
            {
                // We can always cast a fixed size array to a non-fixed array.
                result = new ASTFixedArrayToArrayCast(expression.Trace, expression, exprType as ASTFixedArrayType, targetType as ASTArrayType);
                error = null;
                return true;
            }
            else if (exprType is ASTFixedArrayType fixedArrayType && targetType == ASTPointerType.Of(fixedArrayType.BaseType))
            {
                // This is just getting the data member of the fixed array
                // NOTE: This might mean we try to edit ROM by accident
                // but for now we allow this implicit convertion
                result = new ASTMemberExpression(expression.Trace, expression, "data", null, false);
                error = null;
                return true;
            }
            else if (exprType is ASTFixedArrayType && targetType == ASTPointerType.Of(ASTBaseType.Void))
            {
                // This is just getting the data member of the fixed array
                // NOTE: This might mean we try to edit ROM by accident
                // but for now we allow this implicit convertion
                result = new ASTMemberExpression(expression.Trace, expression, "data", null, false);
                error = null;
                return true;
            }
            else if (expression is ASTWordLitteral && targetType == ASTBaseType.DoubleWord)
            {
                // Here there is a special case where we can optimize the loading of words and dwords
                ASTWordLitteral litteral = expression as ASTWordLitteral;
                // NOTE: Is adding the 'd' to the litteral the right thing to do?
                result = ASTDoubleWordLitteral.From(litteral.Trace, litteral.IntValue, litteral.NumberFromat);
                error = default;
                return true;
            }
            else if (exprType is ASTPointerType && targetType == ASTBaseType.DoubleWord)
            {
                // FIXME: Actually change the type!
                // What is the reason we can't change the type?? - 2019-10-15
                result = expression;
                error = default;
                return true;
            }
            else if (exprType is ASTPointerType && targetType == ASTPointerType.Of(ASTBaseType.Void))
            {
                result = new ASTPointerToVoidPointerCast(expression.Trace, expression, exprType as ASTPointerType);
                error = default;
                return true;
            }
            else if (exprType is ASTArrayType && targetType == ASTPointerType.Of(ASTBaseType.Void))
            {
                result = new ASTMemberExpression(expression.Trace, expression, "data", null, false);
                //result = new ASTPointerToVoidPointerCast(expression.Trace, expression, exprType as ASTPointerType);
                error = default;
                return true;
            }
            else if (exprType == ASTBaseType.String && targetType == ASTPointerType.Of(ASTBaseType.Void))
            {
                // FIXME!!! This is a ugly hack!! When we go over to struct strings this will have to change
                // So we just say that we can conver this. We rely on the fact that we never actually check
                // to see if the expression results in a pointer when generating the cast.
                result = new ASTPointerToVoidPointerCast(expression.Trace, expression, ASTPointerType.Of(ASTBaseType.Word));
                error = default;
                return true;
            }
            else if (exprType == ASTBaseType.String && targetType == ASTArrayType.Of(ASTBaseType.Char))
            {
                result = new ASTStringToArrayCast(expression.Trace, expression, targetType as ASTArrayType);
                error = default;
                return true;
            }
            else
            {
                if (exprType is ASTBaseType && targetType == ASTBaseType.Bool)
                {
                    // This is a special case for converting too bool as many things can do that
                    result = new ASTImplicitCast(expression.Trace, expression, exprType as ASTBaseType, targetType as ASTBaseType);
                    error = default;
                    return true;
                }
                else if (UnAliasType(ResolveType(exprType, typeMap)) is ASTBaseType exprBaseType && targetType is ASTBaseType targetBaseType)
                {
                    int exprSize = exprBaseType.Size;
                    int targetSize = targetBaseType.Size;

                    // FIXME!!!
                    // Special case for word to char implicit cast...
                    if ((exprType == ASTBaseType.Word && targetType == ASTBaseType.Char) ||
                        (exprType == ASTBaseType.Char && targetType == ASTBaseType.Word))
                    {
                        result = new ASTImplicitCast(expression.Trace, expression, exprBaseType, targetBaseType);
                        error = default;
                        return true;
                    }
                    // FIXME: This should not allow word -> string cast
                    // We should allow all casts, they just need well defined behaviour.
                    if (exprSize < targetSize)
                    {
                        result = new ASTImplicitCast(expression.Trace, expression, exprBaseType, targetBaseType);
                        error = default;
                        return true;
                    }
                    else
                    {
                        result = default;
                        error = "This cast would lead to loss of information or the types are not implicitly compatible, do an explicit cast!";
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
        
        internal struct TypedExpressionPair
        {
            public ASTExpression Left;
            public ASTExpression Right;
            public ASTType Type;

            public TypedExpressionPair(ASTExpression left, ASTExpression right, ASTType type)
            {
                Left = left;
                Right = right;
                Type = type;
            }
        }
        
        internal static TypedExpressionPair GenerateBinaryCast(ASTBinaryOp binaryOp, VarMap scope, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap)
        {
            var left = binaryOp.Left;
            var right = binaryOp.Right;
            var opType = binaryOp.OperatorType;

            var leftType = ResolveType(CalcReturnType(left, scope, typeMap, functionMap, constMap, globalMap), typeMap);
            var rightType = ResolveType(CalcReturnType(right, scope, typeMap, functionMap, constMap, globalMap), typeMap);

            // If the types are aliased, get the real types
            if (leftType is ASTAliasedType leftAliasedType) leftType = leftAliasedType.RealType;
            if (rightType is ASTAliasedType rightAliasedType) rightType = rightAliasedType.RealType;

            var leftSize = SizeOfType(leftType, typeMap);
            var rightSize = SizeOfType(rightType, typeMap);
            
            if (leftType is ASTPointerType leftPType && ASTBinaryOp.IsPointerCompatibleOpType(opType))
            {
                // We are doing pointer arithmetic
                if (ASTBaseType.IsNumericType(rightType) == false)
                    Fail(right.Trace, $"Cannot add the non-numeric type '{rightType}' to a the pointer '{leftPType}'");

                if (TryGenerateImplicitCast(right, ASTBaseType.DoubleWord, scope, typeMap, functionMap, constMap, globalMap, out var typedRight, out _) == false)
                    Fail(right.Trace, $"Could not cast '{right}' to dword as needed to be able to apply operator '{opType}' to pointer of type '{leftPType}'!");

                int pointerTypeSize = SizeOfType(leftPType.BaseType, typeMap);

                // NOTE: Here we might wan't to change the type of the expression to indicate that we have multiplied the size of the pointer!
                // Here we transform the right thing to a mult of the pointer size
                typedRight = new ASTBinaryOp(right.Trace, ASTBinaryOp.BinaryOperatorType.Multiplication, ASTDoubleWordLitteral.From(left.Trace, pointerTypeSize), typedRight);

                return new TypedExpressionPair(left, typedRight, leftType);
            }
            else if (ASTBaseType.IsNumericType(leftType) && ASTBaseType.IsNumericType(rightType))
            {
                // Do normal addition
                var resultType = leftSize >= rightSize ? leftType : rightType;

                if (TryGenerateImplicitCast(left, resultType, scope, typeMap, functionMap, constMap, globalMap, out var typedLeft, out _) == false)
                    Fail(left.Trace, $"Could not implicitly cast left term of type '{leftType}' to type '{resultType}'");

                if (TryGenerateImplicitCast(right, resultType, scope, typeMap, functionMap, constMap, globalMap, out var typedRight, out _) == false)
                    Fail(left.Trace, $"Could not implicitly cast right term of type '{rightType}' to type '{resultType}'");

                // If we get here the cast worked!
                return new TypedExpressionPair(typedLeft, typedRight, resultType);
            }
            else if (leftType is ASTPointerType && rightType is ASTPointerType && ASTBinaryOp.IsComparisonOp(opType))
            {
                // Here we allow comparisons of pointer types.
                // For now we allow comparisons for differing base types

                // We don't have to cast because all pointers are the same size
                return new TypedExpressionPair(left, right, leftType);
            }
            else if (leftType is ASTBaseType && rightType == ASTBaseType.Bool)
            {
                // We can compare all base types to bool
                if (TryGenerateImplicitCast(left, ASTBaseType.Bool, scope, typeMap, functionMap, constMap, globalMap, out var typedLeft, out _) == false)
                    Fail(left.Trace, $"Could not cast expression '{left}' of type '{leftType}' to type '{ASTBaseType.Bool}'!");

                return new TypedExpressionPair(typedLeft, right, ASTBaseType.Bool);
            }
            else if (leftType == ASTBaseType.Bool && rightType is ASTBaseType)
            {
                // We can compare all base types to bool
                if (TryGenerateImplicitCast(right, ASTBaseType.Bool, scope, typeMap, functionMap, constMap, globalMap, out var typedRight, out _) == false)
                    Fail(left.Trace, $"Could not cast expression '{right}' of type '{rightType}' to type '{ASTBaseType.Bool}'!");

                return new TypedExpressionPair(left, typedRight, ASTBaseType.Bool);
            }
            else if (leftType == ASTBaseType.String && rightType == ASTPointerType.Of(ASTBaseType.Void) && ASTBinaryOp.IsEqualsOp(opType))
            {
                // Special case for comparing a string to null or any void pointer...?
                // TODO: Should really only be comparison to null

                // FIXME: This will need to change when we get proper strings!
                // But for now a pointer and a string are the same
                return new TypedExpressionPair(left, right, ASTBaseType.String);
            }
            else if (leftType is ASTFunctionPointerType leftFP && rightType is ASTFunctionPointerType rightFP)
            {
                if (leftFP != rightFP)
                    Fail(binaryOp.Trace, $"Cannot compare function pointer of differing types {leftFP} and {rightFP}");

                return new TypedExpressionPair(left, right, leftFP);
            }
            else
            {
                Fail(binaryOp.Trace, $"Can not apply binary operation '{opType}' on types '{leftType}' and '{rightType}'.");
                return default;
            }
        }

        /// <summary>
        /// This method will generate the most optimized jump based on the type of the binary operation condition.
        /// </summary>
        private static void GenerateOptimizedBinaryOpJump(StringBuilder builder, ASTBinaryOp condition, string jmpLabel, VarMap scope, VarList varList, TypeMap typeMap, Context context, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap)
        {
            var leftType = CalcReturnType(condition.Left, scope, typeMap, functionMap, constMap, globalMap);
            var rightType = CalcReturnType(condition.Right, scope, typeMap, functionMap, constMap, globalMap);

            TypedExpressionPair exprPair = GenerateBinaryCast(condition, scope, typeMap, functionMap, constMap, globalMap);

            var resultType = exprPair.Type;

            int typeSize = SizeOfType(resultType, typeMap);

            var typedLeft = exprPair.Left;
            var typedRight = exprPair.Right;

            // TODO: We can optimize even more if one of the operands is a constant zero!!

            switch (condition.OperatorType)
            {
                case ASTBinaryOp.BinaryOperatorType.Equal:
                    EmitExpression(builder, typedLeft, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                    EmitExpression(builder, typedRight, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);

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
                    EmitExpression(builder, typedLeft, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                    EmitExpression(builder, typedRight, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);

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
                    
                    EmitExpression(builder, typedLeft, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                    EmitExpression(builder, typedRight, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);

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
                    EmitExpression(builder, typedLeft, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                    EmitExpression(builder, typedRight, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);

                    // We want to jump past the body if left > right and not jump if left <= right
                    // -> left - right > 0
                    // This is why we use jgz and jgzl

                    if (typeSize == 1)
                    {
                        builder.AppendLine($"\tsub");
                        builder.AppendLine($"\tjgz {jmpLabel}");
                    }
                    else if (typeSize == 2)
                    {
                        builder.AppendLine($"\tlsub");
                        builder.AppendLine($"\tjgzl {jmpLabel}");
                    }
                    else
                    {
                        Fail(condition.Trace, $"We only support types with a max size of 2 right now! Got type {resultType} size {typeSize}");
                    }
                    break;
                case ASTBinaryOp.BinaryOperatorType.Greater_than:
                    EmitExpression(builder, typedLeft, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                    EmitExpression(builder, typedRight, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);

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
                    EmitExpression(builder, typedLeft, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                    EmitExpression(builder, typedRight, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                    
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
                    if (typeSize == 1)
                    {
                        EmitExpression(builder, condition, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                        builder.AppendLine($"\tjz {jmpLabel}");
                    }
                    else if (typeSize == 2)
                    {
                        EmitExpression(builder, condition, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                        builder.AppendLine($"\tjzl {jmpLabel}");
                    }
                    else
                    {
                        Fail(condition.Trace, $"We only support types with a max size of 2 right now! Got type {resultType} size {typeSize}");
                    }
                    
                    break;
            }
        }
        
        internal enum VariableType
        {
            Local,
            Pointer,
            Global,
            Constant,
            Function,
        }
        
        internal struct VariableRef
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

        private static bool TryGetLocalVariableRef(string name, VarMap scope, TypeMap typeMap, out VariableRef variable)
        {
            if (scope.TryGetValue(name, out var local))
            {
                variable = new VariableRef
                {
                    VariableType = VariableType.Local,
                    LocalAddress = local.Offset,
                    Type = ResolveType(local.Type, typeMap),
                    Comment = $"[{name}]"
                };

                return true;
            }
            else
            {
                variable = default;
                return false;
            }
        }

        internal static bool TryResolveVariable(string name, VarMap scope, GlobalMap globalMap, ConstMap constMap, FunctionMap functionMap, TypeMap typeMap, out VariableRef variable)
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
            else if (functionMap.TryGetValue(name, out var functions))
            {
                // FIXME: This is really not the trace we want to use...
                // We want a special error message for this but this method shouldn't really create an error...
                // FIXME: What if this function becomes overloaded later on in the programs lifetime...?
                // NOTE: We could try solve the type by looking at the context of where this variable is located but the compiler is not really made for that...
                if (functions.Count > 1) Fail(functions[0].Trace, $"Cannnot take a function pointer to the overloaded function '{name}'");

                variable = new VariableRef
                {
                    VariableType = VariableType.Function,
                    FunctionName = functions[0].Name,
                    Type = ASTFunctionPointerType.Of(functions[0].Trace, functions[0]),
                };
                
                return true;
            }
            else
            {
                variable = default;
                return false;
            }
        }

        internal static ASTType UnAliasType(ASTType type)
        {
            while (type is ASTAliasedType alias) type = alias.RealType;
            return type;
        }

        internal static ASTType ResolveGenericType(ASTType type, TypeMap typeMap)
        {
            // TODO: Make sure we are doing the right resolving for generic arrays!
            if (type is ASTFixedArrayType fixedArrayType)
                return new ASTFixedArrayType(fixedArrayType.Trace, ResolveGenericType(fixedArrayType.BaseType, typeMap), fixedArrayType.Size);

            if (type is ASTArrayType arrayType)
                return new ASTArrayType(arrayType.Trace, ResolveGenericType(arrayType.BaseType, typeMap));

            if (type is ASTGenericTypeRef genericRef)
                return ResolveType(type.Trace, genericRef, typeMap);

            if (type is ASTPointerType)
                return ASTPointerType.Of(ResolveGenericType((type as ASTPointerType).BaseType, typeMap));

            return type;
        }

        internal static ASTType ResolveType(ASTType type, TypeMap typeMap)
        {
            if (type is ASTGenericTypeRef genericRef)
                return ResolveType(type.Trace, genericRef, typeMap);

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
            if (type is ASTTypeRef tref)
                type = ResolveType(tref.Trace, tref.Name, typeMap);

            if (type is ASTGenericType genType)
                Fail(trace, $"Could not resolve type '{genType.GetFullTypeName()}' to a non-generic type! Consider adding type-parameters to the generic type.");

            return type;
        }

        private static ASTType ResolveType(TraceData trace, ASTGenericTypeRef genRef, TypeMap typeMap)
        {
            if (typeMap.TryGetValue(genRef.Name, out ASTType type) == false)
                Fail(trace, $"There is no type called '{genRef.Name}'");

            // FIXME: Detect reference loops
            // NOTE: Does this ever run?
            if (type is ASTTypeRef tref)
                type = ResolveType(tref.Trace, tref.Name, typeMap);

            if (type is ASTGenericType == false) Fail(trace, $"'{type}' is not a generic type!");
            var genType = type as ASTGenericType;

            // Here we need to contruct a generic map
            var specializedType = SpecializeType(trace, genType, GenerateGenericMap(trace, genType.GenericNames, genRef.GenericTypes));

            // Add the newly spezialized type to the referenced types
            Compiler.AddReferencedType(specializedType);

            if (specializedType is ASTGenericType)
                Fail(trace, $"We did not fully specialize the type '{genType.TypeName}<{string.Join(",", genType.GenericNames)}>' using the type parameters <{string.Join(",", genRef.GenericTypes)}>");

            return specializedType;
        }

        internal struct StructMember
        {
            /// <summary>
            /// The ASTStructType this member is a part of.
            /// </summary>
            public ASTStructType In;
            public ASTType Type;
            public int Size;
            public int Index;
            public int Offset;
        }
        
        internal static bool TryGetStructMember(ASTStructType structType, string memberName, TypeMap typeMap, out StructMember member)
        {
            var members = structType.Members;
            int memberIndex = members.FindIndex(m => m.Name == memberName);

            if (memberIndex < 0)
            {
                member = default;
                return false;
            }

            var memberType = members[memberIndex].Type;
            int memberSize = SizeOfType(memberType, typeMap);

            // Calculate the offset
            int memberOffset = 0;
            for (int i = 0; i < memberIndex; i++)
            {
                memberOffset += SizeOfType(members[i].Type, typeMap);
            }

            member = new StructMember
            {
                In = structType,
                Type = memberType,
                Size = memberSize,
                Index = memberIndex,
                Offset = memberOffset,
            };

            return true;
        }

        private static ASTType DerefType(TraceData trace, ASTType pointerType)
        {
            if (pointerType is ASTDereferenceableType derefType)
            {
                if (derefType.DerefType is ASTStructType == false)
                {
                    // NOTE: Figure out why this is a fail?
                    Fail(trace, $"Type '{derefType.DerefType}' does not have any members!");
                    return default;
                }

                return derefType.DerefType;
            }
            else
            {
                Fail(trace, $"The type '{pointerType}' is not a reference type so we can't dereference it! Use '.' instead of '->'.");
                return default;
            }
        }
        
        internal static bool TryFindBestFunctionMatch(TraceData trace, List<ASTFunction> functions, List<ASTType> argumentTypes, TypeMap typeMap, out ASTFunction func)
        {
            double bestScore = 0;
            func = default;

            // Special case to avoid divison by 0 later
            if (argumentTypes.Count == 0)
            {
                foreach (var function in functions)
                {
                    // There should only be one function that have zero parameters, so return that one
                    if (function.Parameters.Count == 0)
                    {
                        func = function;
                        return true;
                    }
                }

                return false;
            }
            
            // For now we just take the first function matching the types
            // And assume there is only one function that will match that sequence of types
            foreach (var function in functions)
            {
                double score = 0;

                // This will never match
                if (function.Parameters.Count != argumentTypes.Count)
                    continue;

                for (int i = 0; i < argumentTypes.Count; i++)
                {
                    var fType = ResolveGenericType(function.Parameters[i].Type, typeMap);
                    var argType = ResolveGenericType(argumentTypes[i], typeMap);
                    // TODO: Do we need to resolve the types?
                    if (fType == argType)
                    {
                        score += 1;
                    }
                    else if (HasImplicitCast(argumentTypes[i], fType, typeMap))
                    {
                        score += .5f;
                    }
                }

                score /= function.Parameters.Count;

                if (score == 0)
                    continue;

                if (score > bestScore)
                {
                    func = function;
                    bestScore = score;
                }
                else if (score == bestScore)
                {
                    Warning(trace, $"There where two overloads to '{function.Name}' that fit equally good. F1: '{func}', F2: '{function}'");
                }
            }

            // If we found a function we succeeded
            return func != null;
        }

        internal static bool TypesMatchFunctionParameters(ASTFunction func1, List<ASTType> types)
        {
            if (func1.Parameters.Count != types.Count)
                return false;

            for (int i = 0; i < func1.Parameters.Count; i++)
            {
                if (func1.Parameters[i].Type != types[i])
                    return false;
            }

            return true;
        }

        internal static bool FunctionParamsEqual(ASTFunction func1, ASTFunction func2)
        {
            if (func1.Parameters.Count != func2.Parameters.Count)
                return false;

            for (int i = 0; i < func1.Parameters.Count; i++)
            {
                if (func1.Parameters[i].Type != func2.Parameters[i].Type)
                    return false;
            }

            return true;
        }

        // FIXME: When we import something we just parse it, we don't emit it before using it's AST
        // this means that errors like, duplicate function names will be detected first when we import them
        // This will cause the error message to have a weird trace...
        internal static void AddFunctionToMap(TraceData trace, FunctionMap fmap, string name, ASTFunction func)
        {
            if (fmap.TryGetValue(name, out var funcList))
            {
                foreach (var f in funcList)
                {
                    if (FunctionParamsEqual(func, f))
                    {
                        if (f is ASTGenericFunction)
                        {
                            // FIXME: For now we allow this but we should think about this 
                            // so that it does the right thing!!!
                        }
                        else
                        {
                            Fail(trace, $"There already is a function called '{func.Name}'. Declared in '{Path.GetFileName(func.Trace.File)}'");
                        }
                    }
                }

                funcList.Add(func);
            }
            else
            {
                fmap.Add(name, new List<ASTFunction>() { func });
            }
        }

        internal static string GetFunctionLabel(ASTFunction func, TypeMap typeMap, FunctionMap functionMap)
        {
            void AppendTypeToFunctionLabel(StringBuilder label, ASTType type)
            {
                switch (type)
                {
                    case ASTBaseType baseType:
                        label.Append(baseType);
                        break;
                    case ASTPointerType pType:
                        label.Append("P.");
                        AppendTypeToFunctionLabel(label, pType.BaseType);
                        break;
                    case ASTArrayType aType:
                        label.Append("A.");
                        AppendTypeToFunctionLabel(label, aType.BaseType);
                        break;
                    case ASTFixedArrayType fType:
                        label.Append($"F{fType.Size}.");
                        AppendTypeToFunctionLabel(label, fType.BaseType);
                        break;
                    default:
                        label.Append(type.TypeName);
                        break;
                }
            }

            StringBuilder functionLabelBuilder = new StringBuilder(func.Name);

            // FIXME: Generics will affect this generation!
            if (functionMap.TryGetValue(func.Name, out var functions) && functions.Count > 1)
            {
                foreach (var parameter in func.Parameters)
                {
                    functionLabelBuilder.Append("_");
                    AppendTypeToFunctionLabel(functionLabelBuilder, parameter.Type);
                }
            }

            return functionLabelBuilder.ToString();
        }

        internal static string GetGenericFunctionLabel(TraceData trace, ASTGenericFunction sourceFunction, List<ASTType> GenericTypes, TypeMap typeMap, FunctionMap functionMap)
        {
            if (sourceFunction.GenericNames.Count != GenericTypes.Count)
                Fail(trace, $"Missmatching number of generic arguments! Got: {GenericTypes.Count} Expected: {sourceFunction.GenericNames.Count}");

            StringBuilder functionLabelBuilder = new StringBuilder(sourceFunction.Name);

            for (int i = 0; i < sourceFunction.GenericNames.Count; i++)
            {
                functionLabelBuilder.Append("_");
                functionLabelBuilder.Append(sourceFunction.GenericNames[i]);
                AppendTypeToFunctionLabel(functionLabelBuilder, GenericTypes[i]);
            }

            if (functionMap.TryGetValue(sourceFunction.Name, out var functions) && functions.Count > 1)
            {
                foreach (var parameter in sourceFunction.Parameters)
                {
                    functionLabelBuilder.Append("_");
                    AppendTypeToFunctionLabel(functionLabelBuilder, parameter.Type);
                }
            }

            return functionLabelBuilder.ToString();
        }

        internal static TypeMap GenerateDefaultTypeMap()
        {
            // TODO: Error message for duplicate types.
            TypeMap typeMap = ASTBaseType.BaseTypeMap.ToDictionary(kvp => kvp.Key, kvp => (ASTType)kvp.Value);

            // Here we add an alias for the type 'TypeID'
            typeMap.Add("TypeID", ASTAliasedType.Of("TypeID", ASTBaseType.DoubleWord));

            return typeMap;
        }

        public static Assembly EmitAsem(ASTFile file, AST ast)
        {
            StringBuilder builder = new StringBuilder();

            // Generates a default typemap with the base types and global aliases
            TypeMap typeMap = GenerateDefaultTypeMap();

            ConstMap constMap = new ConstMap();

            // NOTE: This might not be the best solution
            // because when you look for variables you might forget to check the globals
            // This might be fixed with a function to do this.
            // But that might not be desirable either.
            GlobalMap globalMap = new GlobalMap();
            
            FunctionMap functionMap = new FunctionMap(file.Functions.Count);
            foreach (var function in file.Functions)
            {
                // This will do duplicate checking
                AddFunctionToMap(function.Trace, functionMap, function.Name, function);
            }

            ImportMap importMap = new ImportMap();
            foreach (var import in file.Directives.Where(d => d is ASTImportDirective).Cast<ASTImportDirective>())
            {
                if (ast.Files.TryGetValue(import.File, out var importFile) == false)
                    Fail(import.Trace, $"Could not find import file '{import.File}'!");
                
                if (importMap.ContainsKey(import.File))
                    Fail(import.Trace, $"File '{import.File}' is already imported!");

                importMap.Add(import.File, importFile.File);
            }

            foreach (var directive in file.Directives)
            {
                EmitDirective(builder, directive, typeMap, functionMap, constMap, globalMap, importMap);
            }

            builder.AppendLine();

            StringBuilder debugBuilder = new StringBuilder();

            foreach (var func in file.Functions)
            {
                // We don't emit anything for intrinsics
                if (func is ASTIntrinsicFunction)
                    continue;

                // We don't output raw generic functions
                if (func is ASTGenericFunction)
                    continue;

                EmitFunction(builder, func, typeMap, functionMap, constMap, globalMap, debugBuilder);
                builder.AppendLine();
            }

            /*
            // We do a for-loop here so that we can modify the list of 
            for (int i = 0; i < specializationsList.Count; i++)
            {
                var func = specializationsList[i].Specialization;

                if (func is ASTGenericFunction || func is ASTIntrinsicFunction)
                    Fail(func.Trace, $"Invalid specialized function of type: {func.GetType()}");

                EmitFunction(builder, func, typeMap, functionMap, constMap, globalMap, debugBuilder);
                builder.AppendLine();
            }
            */

            Assembly assembly = new Assembly
            {
                assembly = builder,
                funcDebug = debugBuilder,
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
                        var test = import;
                        if (importMap.TryGetValue(import.File, out ASTFile file) == false)
                            Fail(import.Trace, $"Could not resolve import of file '{import.File}'!");
                        
                        // FIXME: When one file uses a type from another file and that other file is using a type from the first
                        
                        builder.AppendLine($"& {import.File.Replace(".t12", "")} {Path.ChangeExtension(import.File, ".12asm")}");
                        
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
                                        if (constMap.TryGetValue(constDirective.Name, out var constant))
                                            Fail(import.Trace, $"Could not import constant '{constDirective.Name}' from '{import.File}'. There already is a global called '{constDirective.Name}' in this filescope imported from file '{Path.GetFileName(constant.Trace.File)}'.");

                                        // We only extern it it will be a constant in 12asm
                                        // Array constants will be procs, so we don't extern them
                                        //if (constDirective.Type is ASTFixedArrayType == false)
                                        //    builder.AppendLine($"<{constDirective.Name} = extern>");

                                        constMap.Add(constDirective.Name, constDirective);
                                    }
                                    break;
                                case ASTGlobalDirective globalDirective:
                                    {
                                        if (globalMap.TryGetValue(globalDirective.Name, out var value))
                                            Fail(import.Trace, $"Could not import global '{globalDirective.Name}' from '{import.File}'. There already is a global called '{globalDirective.Name}' in this filescope imported from file '{Path.GetFileName(value.Trace.File)}'.");

                                        // Add the directive as is was our own
                                        globalMap[globalDirective.Name] = globalDirective;

                                        // Then we just include it as extern
                                        // NOTE: With the new assembler this is no longer necessary
                                        //builder.AppendLine($"<{globalDirective.Name} = extern> ; {globalDirective.Name}");
                                    }
                                    break;
                                case ASTStructDeclarationDirective structDecl:
                                    {
                                        if (typeMap.TryGetValue(structDecl.Name, out var value))
                                            Fail(import.Trace, $"Could not import struct '{structDecl.Name}' from '{import.File}'. There already is one in '{Path.GetFileName(value.Trace.File)}'.");
                                            
                                        // We just add the type to the type map and call it done.
                                        // NOTE: There could be something weird going on with types
                                        // that this struct uses that get imported only in the second file.
                                        typeMap.Add(structDecl.Name, structDecl.DeclaredType);
                                        break;
                                    }
                                default:
                                    break;
                            }
                        }

                        foreach (var func in file.Functions)
                        {
                            // Just add the function no modification.
                            AddFunctionToMap(import.Trace, functionMap, func.Name, func);
                        }
                        
                        break;
                    }
                case ASTExternFunctionDirective externFunc:
                    {
                        // Create a new ASTFunction without body
                        ASTFunction func = new ASTFunction(externFunc.Trace, externFunc.FunctionName, externFunc.ReturnType, externFunc.Parameters, null);

                        // Add that function to the function map
                        AddFunctionToMap(externFunc.Trace, functionMap, externFunc.FunctionName, func);
                        break;
                    }
                case ASTExternConstantDirective externConstDirective:
                    {
                        constMap[externConstDirective.Name] = new ASTConstDirective(externConstDirective.Trace, externConstDirective.Type, externConstDirective.Name, null);

                        if (externConstDirective.Type is ASTFixedArrayType == false)
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

                        // FIXME: This is no longer true and we want to try support constant structs!
                        if (constType is ASTStructType)
                            Fail(constDirective.Type.Trace, "We don't do constant structs yet? Or there cannot be constant structs!");
                        
                        if (constDirective.Value is ASTStringLitteral)
                        {
                            // FIXME: This is risky, and does not feel super good...
                            // We do nothing as we handle the case when we need the constant
                        }
                        else if (constDirective.Value is ASTArrayLitteral arrayLitteral)
                        {
                            // FIXME: Here we want to delay the emittion of an array label
                            // This should not emit litterals like normal (that would load them 'load #0')
                            // instead we should emit values directly ('0')

                            if ((constDirective.Type is ASTArrayType == false) && (constDirective.Type is ASTFixedArrayType == false))
                                Fail(constDirective.Trace, $"Cannot define const '{constDirective.Name}' of type '{constType}' as an array!");

                            if (constDirective.Type is ASTFixedArrayType farray && farray.Size.IntValue != arrayLitteral.Values.Count)
                                Fail(constDirective.Trace, $"Missmatching length, '{constDirective.Name}' is an array of '{farray.Size.IntValue}' elements, got '{arrayLitteral.Values.Count}' elements");

                            // TODO: Support nested array litterals
                            var baseType = (constDirective.Type as ASTDereferenceableType).DerefType;

                            // FIXME: Support arrays of strings!!
                            // We need to somehow make a list of string procs before we create the
                            // array proc.
                            // And the array proc should be a list of references to
                            // the string procs
                            if (baseType == ASTBaseType.String)
                            {
                                // Here we generate the string procs needed for the array
                                // We need to manually de-duplicate them
                                // We would actually like this to be done by the assemblers autostring function
                                // but we don't have a facility for that yet.
                                Dictionary<string, string> elementDict = new Dictionary<string, string>();
                                string[] labels = new string[arrayLitteral.Values.Count];

                                int element = 0;
                                foreach (var str in arrayLitteral.Values)
                                {
                                    if (TryGenerateImplicitCast(str, baseType, new VarMap(), typeMap, functionMap, constMap, globalMap, out var casted, out string error) == false)
                                        Fail(str.Trace, $"Cannot cast element in index {element}, '{str} to the type of the array {baseType}!'");

                                    var folded = ConstantFold(casted, new VarMap(), typeMap, functionMap, constMap, globalMap);

                                    if (folded is ASTStringLitteral == false)
                                        Fail(str.Trace, $"Could not evaluate this string as a constant! Got '{folded}'");

                                    string value = (folded as ASTStringLitteral).Value;

                                    if (elementDict.TryGetValue(value, out string label))
                                    {
                                        labels[element] = label;
                                        element++;
                                        continue;
                                    }
                                    else
                                    {
                                        label = $":__{constDirective.Name}_element_{element}";
                                        elementDict[value] = label;
                                        labels[element] = label;
                                        element++;
                                    }

                                    builder.AppendLine($"{label}");
                                    builder.AppendLine($"\t{value}");
                                }

                                builder.AppendLine($":{constDirective.Name}");
                                foreach (var label in labels)
                                {
                                    builder.AppendLine($"\t{label}*");
                                }
                            }
                            else
                            {
                                builder.AppendLine($":{constDirective.Name}");

                                int index = 1;
                                builder.Append("\t");
                                foreach (var lit in arrayLitteral.Values)
                                {
                                    if (TryGenerateImplicitCast(lit, baseType, new VarMap(), typeMap, functionMap, constMap, globalMap, out var casted, out string error) == false)
                                        Fail(lit.Trace, $"Cannot cast element in index {index - 1}, '{lit} to the type of the array {baseType}!'");

                                    var folded = ConstantFold(casted, new VarMap(), typeMap, functionMap, constMap, globalMap);

                                    if (folded is ASTLitteral astlit)
                                    {
                                        string value = astlit.Value;
                                        if (folded is ASTNumericLitteral numLit && numLit.NumberFromat == ASTNumericLitteral.NumberFormat.Decimal)
                                            value = value.TrimEnd('d', 'D', 'w', 'W');
                                        else if (folded is ASTDoubleWordLitteral dwordLit && dwordLit.NumberFromat == ASTNumericLitteral.NumberFormat.Hexadecimal)
                                            value = $"0x{dwordLit.IntValue:X6}";

                                        builder.Append($"{value} ");
                                    }
                                    else if (folded is ASTStructLitteral structLit)
                                    {
                                        ASTType type = ResolveType(structLit.StructType, typeMap);
                                        if (type is ASTStructType == false) Fail(structLit.Trace, "Cannot init a non-struct type using struct litterals!");
                                        ASTStructType stype = type as ASTStructType;

                                        // For a struct litteral to be a constant all of it's initializers need to resolve to constant values
                                        List<ASTExpression> initializers = new List<ASTExpression>();

                                        var inits = structLit.MemberInitializers;
                                        foreach (var member in stype.Members)
                                        {
                                            if (inits.TryGetValue((StringRef)member.Name, out var init) == false)
                                                // If there was not explicit initializer create a default one.
                                                init = new ASTDefaultExpression(structLit.Trace, member.Type);

                                            var initType = CalcReturnType(init, new VarMap(), typeMap, functionMap, constMap, globalMap);

                                            if (TryGenerateImplicitCast(init, member.Type, new VarMap(), typeMap, functionMap, constMap, globalMap, out var typedInit, out error) == false)
                                                Fail(init.Trace, $"Cannot assign expression of type '{initType}' to member '{member.Name}' of type '{member.Type}'! (Implicit cast error: '{error}')");

                                            var foldedInit = ConstantFold(typedInit, new VarMap(), typeMap, functionMap, constMap, globalMap);

                                            // Here we go through all the initializers and make sure that they are either a default expression or a constant value
                                            if (foldedInit is ASTDefaultExpression defaultExpression)
                                            {
                                                // Here we just emit as meny zeros as we need to
                                                int typeSize = SizeOfType(defaultExpression.Type, typeMap);

                                                while (typeSize > 1)
                                                {
                                                    builder.Append($"{0x000_000} ");
                                                    typeSize -= 2;
                                                }

                                                if (typeSize == 1) builder.Append($"{0x000} ");
                                            }
                                            else if (foldedInit is ASTLitteral litteral)
                                            {
                                                string value = litteral.Value;
                                                if (foldedInit is ASTNumericLitteral numLit && numLit.NumberFromat == ASTNumericLitteral.NumberFormat.Decimal)
                                                    value = value.TrimEnd('d', 'D', 'w', 'W');
                                                else if (foldedInit is ASTDoubleWordLitteral dwordLit && dwordLit.NumberFromat == ASTNumericLitteral.NumberFormat.Hexadecimal)
                                                    value = $"0x{dwordLit.IntValue:X6}";

                                                builder.Append($"{value} ");
                                            }
                                            else Fail(foldedInit.Trace, $"Initializer for member '{member.Name}' did not resolve to a constant! Expression: {init}");
                                        }
                                    }
                                    else
                                    {
                                        Fail(lit.Trace, $"Could not evaluate this as a constant! Got '{folded}'");
                                    }

                                    if (index++ % 10 == 0) builder.Append("\n\t");
                                }
                            }

                            builder.AppendLine();
                            builder.AppendLine();
                        }
                        else
                        {
                            // We send in an empty scope as there is no scope
                            var valueType = ResolveType(CalcReturnType(constDirective.Value, new VarMap(), typeMap, functionMap, constMap, globalMap), typeMap);

                            // This is fine, we can have things that resolve to pointer to structs
                            // and are still constant
                            // If we are casting from a (d)word to pointer of those types there is no problem
                            // if (constType is ASTPointerType)
                            //    if (valueType != ASTBaseType.Word && valueType != ASTBaseType.DoubleWord)
                            //        Fail(constDirective.Value.Trace, $"Can't convert constant expression of type '{valueType}' to type '{constType}'!");
                            
                            if (TryGenerateImplicitCast(constDirective.Value, constType, new VarMap(), typeMap, functionMap, constMap, globalMap, out var typedValue, out string error) == false)
                                Fail(constDirective.Value.Trace, $"Cannot define const '{constDirective.Name}' of type '{constType}' as a value of type '{valueType}'");

                            // Constant fold with empty scope
                            var foldedConst = ConstantFold(typedValue, new VarMap(), typeMap, functionMap, constMap, globalMap);

                            if (foldedConst is ASTLitteral == false)
                                Fail(constDirective.Value.Trace, $"Value for constant '{constDirective.Name}' could not be folded to a constant. It got folded to '{foldedConst}'");
                            
                            string value = (foldedConst as ASTLitteral).Value;
                            if (foldedConst is ASTNumericLitteral numLit && numLit.NumberFromat == ASTNumericLitteral.NumberFormat.Decimal)
                                value = value.TrimEnd('d', 'D', 'w', 'W');

                            // FIXME: Proper constant folding!!!!!!!
                            builder.AppendLine($"<{constDirective.Name} = {value}>");
                        }
                        break;
                    }
                case ASTGlobalDirective globalDirective:
                    {
                        if (globalMap.TryGetValue(globalDirective.Name, out var value))
                            Fail(globalDirective.Trace, $"Cannot declare global '' as there already exists a global with that name. {(value.Trace.File != globalDirective.Trace.File ? $"Imported from '{value.Trace.File}'" : "")}");

                        globalMap.Add(globalDirective.Name, globalDirective);
                        builder.AppendLine($"<{globalDirective.Name} = auto({SizeOfType(globalDirective.Type, typeMap)})> ; {globalDirective.Type}");
                        break;
                    }
                case ASTStructDeclarationDirective structDeclaration:
                    {
                        // If we can know the size of this struct we output that constant
                        if (structDeclaration.DeclaredType is ASTGenericType == false)
                        {
                            string name = structDeclaration.Name;

                            if (typeMap.TryGetValue(name, out var value))
                                Fail(structDeclaration.Trace, $"Cannot declare struct '{name}' as there already exists a struct with that name! {(value.Trace.File != structDeclaration.Trace.File ? $"Imported from file '{value.Trace.File}'" : "")}");

                            builder.AppendLine($"<{name.ToLowerInvariant()}_struct_size = {SizeOfType(structDeclaration.DeclaredType, typeMap)}>");

                            
                        }

                        // NOTE: We might want to do something about generic types!
                        typeMap.Add(structDeclaration.Name, structDeclaration.DeclaredType);

                        // FIXME: We need to handle generic types properly!!!
                        Compiler.AddReferencedType(structDeclaration.DeclaredType);
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

            string functionLabel = GetFunctionLabel(func, typeMap, functionMap);

            VarList variableList = new VarList();
            FunctionConext functionContext = new FunctionConext(func.Name, ResolveType(func.ReturnType, typeMap));
            int local_index = 0;

            VarMap scope = new VarMap();

            foreach (var param in func.Parameters)
            {
                var type = ResolveType(param.Type, typeMap);

                variableList.Add((param.Name, local_index, type));
                scope.Add(param.Name, (local_index, type));
                local_index += SizeOfType(type, typeMap);
            }
            
            if (func is ASTInterrupt)
            {
                // NOTE: We might want to use constants here...
                VM12Opcode.InterruptType type = (func as ASTInterrupt).Type;
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
                        Comment = $"[{param.Name}]",
                    };

                    LoadVariable(builder, func.Trace, paramVariable, typeMap, constMap);

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
                builder.AppendLine($":{functionLabel}");
            }
            
            int param_index = builder.Length;

            // FIXME!
            //builder.AppendLine("\t0 0");

            int @params = func.Parameters.Sum(param => SizeOfType(param.Type, typeMap));

            // We add a return statement if the function returns void and there is not a return statement at the end
            // FIXME: We do not yet validate that a function acutally returns the said value!
            // NOTE: This should be done better
            if (functionContext.ReturnType == ASTBaseType.Void && (func.Body.Count <= 0 || func.Body.Last() is ASTReturnStatement == false))
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
            else if (func.Body.Count == 0)
            {
                Fail(func.Trace, $"The function '{func.Name}' must return a value!");
            }
            else if (func.Body.Last() is ASTReturnStatement == false)
            {
                // TODO: Proper control-flow analysis
                bool EndsWithReturn(ASTBlockItem blockItem)
                {
                    switch (blockItem)
                    {
                        case ASTReturnStatement returnStatement:
                            return true;
                        case ASTCompoundStatement compoundStatement:
                            return compoundStatement.Block.Last() is ASTReturnStatement;
                        case ASTIfStatement ifStatement:
                            {
                                if (ifStatement.IfFalse == null) return false;
                                return EndsWithReturn(ifStatement.IfTrue) && EndsWithReturn(ifStatement.IfFalse);
                            }
                        case ASTWhileStatement whileStatement:
                            {
                                // FIXME: Here we need real control-flow analysis
                                Warning(blockItem.Trace, $"We don't do controlflow analysis for while loops! So we can't guarantee that the function '{func.Name}' actaully returns!");
                                return false;
                            }
                        case ASTExpressionStatement expressionStatement:
                            if (expressionStatement.Expr is ASTFunctionCall functionCall)
                            {
                                // HACK!!! This is really ugly but it is the best solution for now
                                // NOTE: Function calls to 'panic' and 'panic_string' won't return. 
                                if (functionCall.FunctionName == "panic" || functionCall.FunctionName == "panic_string")
                                {
                                    return true;
                                }
                            }
                            return false;
                        default:
                            Warning(blockItem.Trace, $"We don't do controlflow analysis on AST nodes of type '{blockItem}' in function '{func.Name}'");
                            return false;
                    }
                }

                if (EndsWithReturn(func.Body.Last()) == false)
                {
                    Warning(func.Trace, $"The function '{func.Name}' does not end with a return statement!");
                }
            }

            Context context = new Context(functionContext, LoopContext.Empty, new LabelContext());

            foreach (var blockItem in func.Body)
            {
                EmitBlockItem(builder, blockItem, scope, variableList, ref local_index, typeMap, context, functionMap, constMap, globalMap);
            }
            
            int locals = local_index;

            string params_string = string.Join(", ", func.Parameters.Select(param => $"/{param.Name} {param.Type.TypeName}"));

            string locals_string = string.Join(", ", variableList.Skip(func.Parameters.Count).Select(var => (var.Type, var.Name)).Select(local => $"/{local.Name} {local.Type.TypeName}"));
            
            // Here we generate debug data
            {
                debugBuilder.AppendLine($":{functionLabel}");

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

        private static void EmitBlockItem(StringBuilder builder, ASTBlockItem blockItem, VarMap scope, VarList varList, ref int local_index, TypeMap typeMap, Context context, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap)
        {
            switch (blockItem)
            {
                case ASTDeclaration declaration:
                    EmitDeclaration(builder, declaration, scope, varList, ref local_index, typeMap, context, functionMap, constMap, globalMap);
                    break;
                case ASTStatement statement:
                    // @TODO: Make this cleaner, like using an imutable map or other datastructure for handling scopes
                    // Make a copy of the scope so that the statement does not modify the current scope
                    var new_scope = new VarMap(scope);
                    EmitStatement(builder, statement, new_scope, varList, ref local_index, typeMap, context, functionMap, constMap, globalMap);
                    break;
                default:
                    Fail(blockItem.Trace, $"Unknown block item {blockItem}, this is a compiler bug!");
                    break;
            }
        }

        private static void EmitDeclaration(StringBuilder builder, ASTDeclaration declaration, VarMap scope, VarList varList, ref int local_index, TypeMap typeMap, Context context, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap)
        {
            switch (declaration)
            {
                case ASTVariableDeclaration variableDeclaration:
                    {
                        string varName = variableDeclaration.VariableName;
                        if (scope.ContainsKey(varName)) Fail(variableDeclaration.Trace, $"Cannot declare the variable '{varName}' more than once!");

                        var varType = ResolveType(variableDeclaration.Type, typeMap);

                        int varOffset = local_index;
                        scope.Add(varName, (varOffset, varType));
                        varList.Add((varName, varOffset, varType));
                        local_index += SizeOfType(varType, typeMap);

                        if (variableDeclaration.Initializer != null)
                        {
                            var initExpr = variableDeclaration.Initializer;
                            var initType = CalcReturnType(initExpr, scope, typeMap, functionMap, constMap, globalMap);

                            if (TryGenerateImplicitCast(initExpr, varType, scope, typeMap, functionMap, constMap, globalMap, out ASTExpression typedExpression, out string error) == false)
                                Fail(variableDeclaration.Initializer.Trace, $"Cannot assign expression of type '{initType}' to variable ('{variableDeclaration.VariableName}') of type '{varType}'");
                            
                            EmitExpression(builder, typedExpression, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);

                            VariableRef var = new VariableRef
                            {
                                VariableType = VariableType.Local,
                                LocalAddress = varOffset,
                                Type = varType,
                                Comment = $"[{varName}]",
                            };

                            StoreVariable(builder, variableDeclaration.Trace, var, typeMap);
                        }
                        break;
                    }
                default:
                    Fail(declaration.Trace, $"Unknown declaration {declaration}, this is a compiler bug!");
                    break;
            }
        }

        private static void EmitStatement(StringBuilder builder, ASTStatement statement, VarMap scope, VarList varList, ref int local_index, TypeMap typeMap, Context context, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap)
        {
            FunctionConext functionConext = context.FunctionConext;
            LoopContext loopContext = context.LoopContext;
            switch (statement)
            {
                case ASTEmptyStatement emptyStatement:
                    builder.AppendLine("\tnop");
                    Warning(emptyStatement.Trace, "Possibly not intended empty statement!");
                    break;
                case ASTReturnStatement returnStatement:
                    {
                        if (returnStatement.ReturnValueExpression != null)
                        {
                            var retType = CalcReturnType(returnStatement.ReturnValueExpression, scope, typeMap, functionMap, constMap, globalMap);
                            var retExpr = returnStatement.ReturnValueExpression;

                            if (TryGenerateImplicitCast(retExpr, functionConext.ReturnType, scope, typeMap, functionMap, constMap, globalMap, out var typedReturn, out string error) == false)
                                Fail(returnStatement.Trace, $"Cannot return expression of type '{retType}' in the function '{functionConext.FunctionName}' that returns type '{functionConext.ReturnType}'");
                            
                            EmitExpression(builder, typedReturn, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                            
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
                        EmitExpression(builder, assignment.AssignmentExpression, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                        builder.AppendLine($"\tstore {scope[varName].Offset}\t; [{varName}]");
                        break;
                    }
                case ASTIfStatement ifStatement:
                    {
                        var condType = CalcReturnType(ifStatement.Condition, scope, typeMap, functionMap, constMap, globalMap);
                        if (TryGenerateImplicitCast(ifStatement.Condition, ASTBaseType.Bool, scope, typeMap, functionMap, constMap, globalMap, out var typedCond, out string error) == false)
                            Fail(ifStatement.Condition.Trace, $"Cannot implicitly convert expression of type {condType} to {ASTBaseType.Bool}! Cast error: '{error}'");

                        // Now we don't have to handle doing jumps on things that are bigger than one word as the condition will be a bool!

                        int ifIndex = context.LabelContext.IfLabels++;
                        if (ifStatement.IfFalse == null)
                        {
                            // If-statement without else
                            // NOTE: Here we are checking if the uncasted condition is a binary op, because optimized jump can do smart things
                            if (ifStatement.Condition is ASTBinaryOp)
                            {
                                // Here we can optimize the jump.
                                GenerateOptimizedBinaryOpJump(builder, ifStatement.Condition as ASTBinaryOp, $":post_if_{ifIndex}", scope, varList, typeMap, context, functionMap, constMap, globalMap);
                            }
                            else
                            {
                                // FIXME: We do not handle return types with a size > 1
                                // We don't know how to optimize this so we eval the whole expression
                                EmitExpression(builder, typedCond, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                                builder.AppendLine($"\tjz :post_if_{ifIndex}");
                            }

                            // Generate the if true block.
                            EmitStatement(builder, ifStatement.IfTrue, scope, varList, ref local_index, typeMap, context, functionMap, constMap, globalMap);
                            builder.AppendLine($"\t:post_if_{ifIndex}");
                        }
                        else
                        {
                            // NOTE: Here we are checking if the uncasted condition is a binary op, because optimized jump can do smart things
                            if (ifStatement.Condition is ASTBinaryOp)
                            {
                                // Here we can optimize the jump.
                                GenerateOptimizedBinaryOpJump(builder, ifStatement.Condition as ASTBinaryOp, $":else_{ifIndex}", scope, varList, typeMap, context, functionMap, constMap, globalMap);
                            }
                            else
                            {
                                // We don't know how to optimize this so we eval the whole expression
                                EmitExpression(builder, typedCond, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                                builder.AppendLine($"\tjz :else_{ifIndex}");
                            }

                            EmitStatement(builder, ifStatement.IfTrue, scope, varList, ref local_index, typeMap, context, functionMap, constMap, globalMap);
                            builder.AppendLine($"\tjmp :post_else_{ifIndex}");
                            builder.AppendLine($"\t:else_{ifIndex}");
                            EmitStatement(builder, ifStatement.IfFalse, scope, varList, ref local_index, typeMap, context, functionMap, constMap, globalMap);
                            builder.AppendLine($"\t:post_else_{ifIndex}");
                        }
                        break;
                    }
                case ASTCompoundStatement compoundStatement:
                    {
                        VarMap newScope = new VarMap(scope);
                        foreach (var blockItem in compoundStatement.Block)
                        {
                            EmitBlockItem(builder, blockItem, newScope, varList, ref local_index, typeMap, context, functionMap, constMap, globalMap);
                        }
                        break;
                    }
                case ASTExpressionStatement expression:
                    EmitExpression(builder, expression.Expr, scope, varList, typeMap, context, functionMap, constMap, globalMap, false);
                    break;
                case ASTForWithDeclStatement forWithDecl:
                    {
                        var test = forWithDecl;

                        int forIndex = context.LabelContext.ForLabel++;
                        LoopContext newLoopContext = new LoopContext($":post_for_statement_{forIndex}", $":for_end_{forIndex}");

                        builder.AppendLine($"\t; For loop {forIndex} ({forWithDecl.Condition})");
                        
                        VarMap new_scope = new VarMap(scope);
                        EmitDeclaration(builder, forWithDecl.Declaration, new_scope, varList, ref local_index, typeMap, context, functionMap, constMap, globalMap);
                        
                        // We are now in the new scope.
                        scope = new_scope;

                        builder.AppendLine($"\t:for_cond_{forIndex}");

                        var cond = forWithDecl.Condition;
                        if (cond is ASTBinaryOp)
                        {
                            // This will generate a jump depending on the type of binary op
                            GenerateOptimizedBinaryOpJump(builder, cond as ASTBinaryOp, newLoopContext.EndLabel, scope, varList, typeMap, context, functionMap, constMap, globalMap);
                        }
                        else
                        {
                            // Here we can't really optimize.
                            EmitExpression(builder, cond, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                            builder.AppendLine($"\tjz {newLoopContext.EndLabel}");
                        }
                        
                        EmitStatement(builder, forWithDecl.Body, new_scope, varList, ref local_index, typeMap, context.With(newLoopContext), functionMap, constMap, globalMap);

                        builder.AppendLine($"\t{newLoopContext.ContinueLabel}");
                        EmitExpression(builder, forWithDecl.PostExpression, new_scope, varList, typeMap, context, functionMap, constMap, globalMap, false);

                        builder.AppendLine($"\tjmp :for_cond_{forIndex}");
                        builder.AppendLine($"\t{newLoopContext.EndLabel}");

                        break;
                    }
                case ASTWhileStatement whileStatement:
                    {
                        int whileIndex = context.LabelContext.WhileLabel++;
                        LoopContext newLoopContext = new LoopContext($":while_condition_{whileIndex}", $":while_end_{whileIndex}");

                        builder.AppendLine($"\t; While loop {whileIndex} ({whileStatement.Condition})");
                        builder.AppendLine($"\t{newLoopContext.ContinueLabel}");

                        if (whileStatement.Condition is ASTBinaryOp binaryCond)
                        {
                            GenerateOptimizedBinaryOpJump(builder, binaryCond, newLoopContext.EndLabel, scope, varList, typeMap, context, functionMap, constMap, globalMap);
                        }
                        else
                        {
                            EmitExpression(builder, whileStatement.Condition, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                            builder.AppendLine($"\tjz {newLoopContext.EndLabel}");
                        }
                        
                        EmitStatement(builder, whileStatement.Body, scope, varList, ref local_index, typeMap, context.With(newLoopContext), functionMap, constMap, globalMap);

                        builder.AppendLine($"\tjmp {newLoopContext.ContinueLabel}");
                        builder.AppendLine($"\t{newLoopContext.EndLabel}");
                        break;
                    }
                case ASTDoWhileStatement doWhile:
                    {
                        int doWhileIndex = context.LabelContext.DoWhileLabel++;
                        LoopContext newLoopContext = new LoopContext($":do_while_condition_{doWhileIndex}", $":do_while_end_{doWhileIndex}");

                        builder.AppendLine($"\t; Do while loop {doWhileIndex} ({doWhile.Condition})");
                        builder.AppendLine($"\t:do_while_{doWhileIndex}");

                        EmitStatement(builder, doWhile.Body, scope, varList, ref local_index, typeMap, context.With(newLoopContext), functionMap, constMap, globalMap);

                        builder.AppendLine($"\t{newLoopContext.ContinueLabel}");

                        if (doWhile.Condition is ASTBinaryOp binaryCond)
                        {
                            // Here we need to invert the condition before we try and do an optimized jump!
                            // FIXME: Ensure that it's properly inverted! We don't handle &&/|| atm!
                            var invertedCond = new ASTBinaryOp(binaryCond.Trace, ASTBinaryOp.InvertBooleanOp(binaryCond.OperatorType), binaryCond.Left, binaryCond.Right);
                            GenerateOptimizedBinaryOpJump(builder, invertedCond, $":do_while_{doWhileIndex}", scope, varList, typeMap, context.With(newLoopContext), functionMap, constMap, globalMap);
                        }
                        else
                        {
                            EmitExpression(builder, doWhile.Condition, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                            builder.AppendLine($"\tjnz :do_while_{doWhileIndex}");
                        }

                        builder.AppendLine($"\t{newLoopContext.EndLabel}");

                        break;
                    }
                case ASTContinueStatement continueStatement:
                    builder.AppendLine($"\tjmp {loopContext.ContinueLabel}");
                    break;
                case ASTBreakStatement breakStatement:
                    builder.AppendLine($"\tjmp {loopContext.EndLabel}");
                    break;
                default:
                    Fail(statement.Trace, $"Could not emit code for statement {statement}, this is a compiler bug!");
                    break;
            }
        }

        private static void EmitExpression(StringBuilder builder, ASTExpression expression, VarMap scope, VarList varList, TypeMap typeMap, Context context, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap, bool produceResult)
        {
            switch (expression)
            {
                case ASTLitteral litteral:
                    {
                        EmitLitteral(builder, litteral);
                        break;
                    }
                case ASTStructLitteral structLitteral:
                    {
                        // FIXME: Make this work for array types!

                        int structSize = SizeOfType(structLitteral.StructType, typeMap);
                        ASTType type = ResolveType(structLitteral.StructType, typeMap);

                        if (type is ASTStructType == false) Fail(structLitteral.StructType.Trace, "Cannot init a non-struct type using struct litterals!");
                        ASTStructType stype = type as ASTStructType;

                        var inits = structLitteral.MemberInitializers;
                        foreach (var member in stype.Members)
                        {
                            // Check to see if this member has a initializer
                            // If not, initialize with zeroes

                            // FIXME: Move over to StringRef!
                            if (inits.TryGetValue((StringRef)member.Name, out var init) == false)
                                // If there was not explicit initializer create a default one.
                                init = new ASTDefaultExpression(structLitteral.Trace, member.Type);

                            var initType = CalcReturnType(init, scope, typeMap, functionMap, constMap, globalMap);

                            if (TryGenerateImplicitCast(init, member.Type, scope, typeMap, functionMap, constMap, globalMap, out var typedInit, out string error) == false)
                                Fail(init.Trace, $"Cannot assign expression of type '{initType}' to member '{member.Name}' of type '{member.Type}'! (Implicit cast error: '{error}')");

                            var foldedInit = ConstantFold(typedInit, scope, typeMap, functionMap, constMap, globalMap);

                            EmitExpression(builder, foldedInit, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                        }
                        break;
                    }
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

                                        EmitExpression(builder, typedAssignment, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);

                                        StoreVariable(builder, variableExpr.AssignmentExpression.Trace, variable, typeMap);
                                    }

                                    if (produceResult)
                                    {
                                        LoadVariable(builder, variableExpr.Trace, variable, typeMap, constMap);
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

                                        EmitExpression(builder, typedAssignment, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                                        
                                        StoreVariable(builder, variableExpr.Trace, variable, typeMap);
                                    }

                                    if (produceResult)
                                    {
                                        // We are loading a pointer so 'loadl' is fine
                                        builder.AppendLine($"\tloadl #{variable.GlobalName}");
                                        LoadVariable(builder, variableExpr.Trace, variable, typeMap, constMap);
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
                                            LoadVariable(builder, variableExpr.Trace, variable, typeMap, constMap);
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
                        int typeSize = SizeOfType(type, typeMap);

                        // TODO: Check to see if this expression has side-effects. This way we can avoid poping at the end
                        switch (unaryOp.OperatorType)
                        {
                            case ASTUnaryOp.UnaryOperationType.Identity:
                                // Do nothing
                                EmitExpression(builder, unaryOp.Expr, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                                break;
                            case ASTUnaryOp.UnaryOperationType.Negation:
                                EmitExpression(builder, unaryOp.Expr, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                                if (produceResult)
                                {
                                    if (typeSize == 1) builder.AppendLine("\tneg");
                                    else if (typeSize == 2) builder.AppendLine("\tlneg");
                                    else Fail(unaryOp.Expr.Trace, $"Cannot negate a type of size {typeSize}!");
                                }
                                break;
                            case ASTUnaryOp.UnaryOperationType.Compliment:
                                EmitExpression(builder, unaryOp.Expr, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                                if (produceResult)
                                {
                                    if (typeSize == 1) builder.AppendLine("\tnot");
                                    else if (typeSize == 2) builder.AppendLine("\tlnot");
                                    else Fail(unaryOp.Expr.Trace, $"Cannot invert a type of size {typeSize}!");
                                }
                                break;
                            case ASTUnaryOp.UnaryOperationType.Logical_negation:
                                // NOTE: For now we don't support this as we should only do this on logical types which are always 1 word!
                                if (typeSize != 1) Fail(unaryOp.Expr.Trace, $"Cannot negate a type of size {typeSize}!");

                                if (unaryOp.Expr is ASTExplicitCast cast && CalcReturnType(cast.From, scope, typeMap, functionMap, constMap, globalMap) == ASTBaseType.DoubleWord)
                                {
                                    // FIXME: This is a very specific optimization that should be implemented more generally
                                    EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                                    if (produceResult) builder.AppendLine("\tor setz");
                                }
                                else
                                {
                                    EmitExpression(builder, unaryOp.Expr, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                                    if (produceResult) builder.AppendLine("\tsetz");
                                }
                                break;
                            case ASTUnaryOp.UnaryOperationType.Dereference:
                                {
                                    EmitExpression(builder, unaryOp.Expr, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);

                                    if (type is ASTPointerType == false) Fail(unaryOp.Trace, $"Cannot dereference non-pointer type '{type}'!");

                                    if (produceResult)
                                    {
                                        VariableRef variable = new VariableRef
                                        {
                                            VariableType = VariableType.Pointer,
                                            Type = (type as ASTPointerType).BaseType,
                                            Comment = $"<< [{unaryOp.Expr}]",
                                        };

                                        LoadVariable(builder, unaryOp.Trace, variable, typeMap, constMap);
                                    }
                                    break;
                                }
                            case ASTUnaryOp.UnaryOperationType.Decrement:
                            case ASTUnaryOp.UnaryOperationType.Increment:
                            case ASTUnaryOp.UnaryOperationType.Increment_post:
                            case ASTUnaryOp.UnaryOperationType.Decrement_post:
                                {
                                    // FIXME: Pointer arithmetic!!!!! Atm we don't handle pointers right!!!
                                    // So if the type is not a word or dword we can't increment!
                                    if ((type == ASTBaseType.Word || type == ASTBaseType.DoubleWord) == false)
                                    {
                                        Fail(unaryOp.Trace, $"We only support incrementing/decrementing variables of type word or dword. (Pointers are on the way). Got type '{type}'");
                                    }
                                    
                                    // If we are not producing a result, this is the same as just doing post operators.
                                    // This makes it easier to deal with not producing a result for the pre operators.
                                    ASTUnaryOp.UnaryOperationType opType = unaryOp.OperatorType;
                                    if (produceResult == false)
                                    {
                                        if (opType == ASTUnaryOp.UnaryOperationType.Increment) opType = ASTUnaryOp.UnaryOperationType.Increment_post;
                                        else if (opType == ASTUnaryOp.UnaryOperationType.Decrement) opType = ASTUnaryOp.UnaryOperationType.Decrement_post;
                                    }

                                    void AppendIncDecOp(string comment = "")
                                    {
                                        switch (opType)
                                        {
                                            case ASTUnaryOp.UnaryOperationType.Increment:
                                            case ASTUnaryOp.UnaryOperationType.Increment_post:
                                                builder.AppendLineWithComment($"\t{(typeSize == 2 ? "l" : "")}inc", comment);
                                                break;
                                            case ASTUnaryOp.UnaryOperationType.Decrement:
                                            case ASTUnaryOp.UnaryOperationType.Decrement_post:
                                                builder.AppendLineWithComment($"\t{(typeSize == 2 ? "l" : "")}dec", comment);
                                                break;
                                            default:
                                                Fail(unaryOp.Trace, $"Unknown increment/decrement operator type '{opType}'!");
                                                break;
                                        }
                                    }
                                    
                                    string OpString()
                                    {
                                        switch (opType)
                                        {
                                            case ASTUnaryOp.UnaryOperationType.Increment:
                                            case ASTUnaryOp.UnaryOperationType.Increment_post:
                                                return "++";
                                            case ASTUnaryOp.UnaryOperationType.Decrement:
                                            case ASTUnaryOp.UnaryOperationType.Decrement_post:
                                                return "--";
                                            default:
                                                return "UNKNOWN";
                                        }
                                    }

                                    if (unaryOp.Expr is ASTVariableExpression varExpr)
                                    {
                                        // Here we can do cool optimized stuff!
                                        if (TryResolveVariable(varExpr.Name, scope, globalMap, constMap, functionMap, typeMap, out var variable) == false)
                                            Fail(varExpr.Trace, $"No variable called '{varExpr.Name}'!");
                                        
                                        switch (variable.VariableType)
                                        {
                                            case VariableType.Local:
                                                {
                                                    // TODO: For now we always produce a result, but we could figure out how to not do that in the future
                                                    EmitExpression(builder, varExpr, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                                                    
                                                    switch (opType)
                                                    {
                                                        case ASTUnaryOp.UnaryOperationType.Increment:
                                                        case ASTUnaryOp.UnaryOperationType.Decrement:
                                                            // NOTE: We only get here if we should produce a result
                                                            AppendIncDecOp($"{OpString()}[{varExpr.Name}]");
                                                            DuplicateSP(builder, unaryOp.Trace, typeSize);
                                                            StoreVariable(builder, unaryOp.Trace, variable, typeMap);
                                                            break;
                                                        case ASTUnaryOp.UnaryOperationType.Increment_post:
                                                            // This works because we have already loaded the value above
                                                            IncrementLocal(builder, unaryOp.Trace, variable.LocalAddress, type, typeSize, $"[{varExpr.Name}]++");
                                                            break;
                                                        case ASTUnaryOp.UnaryOperationType.Decrement_post:
                                                            // This works because we have already loaded the value above
                                                            DecrementLocal(builder, unaryOp.Trace, variable.LocalAddress, type, typeSize, $"[{varExpr.Name}]--");
                                                            break;
                                                        default:
                                                            Fail(unaryOp.Trace, $"This should not happen!!!!!");
                                                            break;
                                                    }
                                                    break;
                                                }
                                            case VariableType.Global:
                                                switch (opType)
                                                {
                                                    case ASTUnaryOp.UnaryOperationType.Increment:
                                                    case ASTUnaryOp.UnaryOperationType.Decrement:
                                                        // NOTE: We only get here if we should produce a result
                                                        // Increment the current value, load the global addr, over and then store
                                                        EmitExpression(builder, varExpr, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                                                        AppendIncDecOp();
                                                        builder.AppendLine($"\tloadl #{variable.GlobalName}");
                                                        OverSP(builder, unaryOp.Trace, typeSize, 2);
                                                        StoreSP(builder, unaryOp.Trace, typeSize, $"{OpString()}[{variable.GlobalName}]");
                                                        break;
                                                    case ASTUnaryOp.UnaryOperationType.Increment_post:
                                                    case ASTUnaryOp.UnaryOperationType.Decrement_post:
                                                        // NOTE: This code might produce a result or not
                                                        if (produceResult)
                                                        {
                                                            EmitExpression(builder, varExpr, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                                                            builder.AppendLine($"\tloadl #{variable.GlobalName}");
                                                            OverSP(builder, unaryOp.Trace, typeSize, 2);
                                                            AppendIncDecOp();
                                                            StoreSP(builder, unaryOp.Trace, typeSize, $"[{unaryOp.Expr}]{OpString()}");
                                                        }
                                                        else
                                                        {
                                                            builder.AppendLine($"\tloadl #{variable.GlobalName}");
                                                            builder.AppendLine($"\tldup");
                                                            LoadSP(builder, typeSize, $"[{unaryOp.Expr}]");
                                                            AppendIncDecOp();
                                                            StoreSP(builder, unaryOp.Trace, typeSize, $"[{unaryOp.Expr}]{OpString()}");
                                                        }

                                                        break;
                                                    default:
                                                        Fail(unaryOp.Trace, $"This should not happen!!!!!");
                                                        break;
                                                }
                                                break;
                                            case VariableType.Pointer:
                                                // NOTE: Is this really true? Can we have constant pointers? Or would you need to cast first?
                                                Fail(varExpr.Trace, "This should not happen because TryResolveVariable should not return pointers!");
                                                break;
                                            case VariableType.Constant:
                                                Fail(unaryOp.Trace, $"Cannot increment/decrement a constant value! (Type: '{variable.Type}')");
                                                break;
                                            case VariableType.Function:
                                                Fail(unaryOp.Trace, $"Cannot increment/decrement a function pointer! (Type: '{variable.Type}')");
                                                break;
                                            default:
                                                Fail(unaryOp.Trace, $"Unknown VariableType '{variable.VariableType}'! This is a compiler bug!");
                                                break;
                                        }
                                    }
                                    else
                                    {
                                        // We want the address of where we should store the value
                                        var addressOf = new ASTAddressOfExpression(unaryOp.Expr.Trace, unaryOp.Expr);
                                        
                                        switch (opType)
                                        {
                                            case ASTUnaryOp.UnaryOperationType.Increment:
                                            case ASTUnaryOp.UnaryOperationType.Decrement:
                                                // NOTE: We only get here if we should produce a result
                                                // Increment the current value, load the global addr, over and then store
                                                EmitExpression(builder, unaryOp.Expr, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                                                AppendIncDecOp();
                                                EmitExpression(builder, addressOf, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                                                OverSP(builder, unaryOp.Trace, typeSize, 2);
                                                StoreSP(builder, unaryOp.Trace, typeSize, $"{OpString()}[{unaryOp.Expr}]");
                                                break;
                                            case ASTUnaryOp.UnaryOperationType.Increment_post:
                                            case ASTUnaryOp.UnaryOperationType.Decrement_post:
                                                // NOTE: This code might produce a result or not
                                                if (produceResult)
                                                {
                                                    EmitExpression(builder, unaryOp.Expr, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                                                    EmitExpression(builder, addressOf, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                                                    OverSP(builder, unaryOp.Trace, typeSize, 2);
                                                    AppendIncDecOp();
                                                    StoreSP(builder, unaryOp.Trace, typeSize, $"[{unaryOp.Expr}]{OpString()}");
                                                }
                                                else
                                                {
                                                    EmitExpression(builder, addressOf, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                                                    builder.AppendLine($"\tldup");
                                                    LoadSP(builder, typeSize, $"[{unaryOp.Expr}]");
                                                    AppendIncDecOp();
                                                    StoreSP(builder, unaryOp.Trace, typeSize, $"[{unaryOp.Expr}]{OpString()}");
                                                }
                                                break;
                                            default:
                                                Fail(unaryOp.Trace, $"This should not happen!!!!!");
                                                break;
                                        }
                                    }
                                    break;
                                }
                            default:
                                Fail(unaryOp.Trace, $"Unknown unary operator type {unaryOp.OperatorType}, this is a compiler bug!");
                                break;
                        }
                        break;
                    }
                case ASTBinaryOp binaryOp:
                    {
                        var leftType = CalcReturnType(binaryOp.Left, scope, typeMap, functionMap, constMap, globalMap);
                        var rightType = CalcReturnType(binaryOp.Right, scope, typeMap, functionMap, constMap, globalMap);

                        TypedExpressionPair exprPair = GenerateBinaryCast(binaryOp, scope, typeMap, functionMap, constMap, globalMap);

                        var resultType = exprPair.Type;

                        int typeSize = SizeOfType(resultType, typeMap);
                        
                        //var typedLeft = exprPair.Left;
                        //var typedRight = exprPair.Right;

                        // FIXME: Make constant fold work properly
                        var typedLeft = ConstantFold(exprPair.Left, scope, typeMap, functionMap, constMap, globalMap);
                        var typedRight = ConstantFold(exprPair.Right, scope, typeMap, functionMap, constMap, globalMap);

                        // Optimization for equals operations where we can use the setX instructions
                        if (ASTBinaryOp.IsEqualsOp(binaryOp.OperatorType))
                        {
                            // Appends the appropriate setX instruction
                            void AppendSetInst(string comment)
                            {
                                switch (binaryOp.OperatorType)
                                {
                                    case ASTBinaryOp.BinaryOperatorType.Equal when typeSize == 1:
                                        builder.AppendLineWithComment("\tsetz", comment);
                                        break;
                                    case ASTBinaryOp.BinaryOperatorType.Equal when typeSize == 2:
                                        builder.AppendLineWithComment("\tlsetz or", comment);
                                        break;

                                    case ASTBinaryOp.BinaryOperatorType.Not_equal when typeSize == 1:
                                        builder.AppendLineWithComment("\tsetnz", comment);
                                        break;
                                    case ASTBinaryOp.BinaryOperatorType.Not_equal when typeSize == 2:
                                        builder.AppendLineWithComment("\tlsetnz or", comment);
                                        break;

                                    default:
                                        Fail(binaryOp.Trace, $"Unknown case for optype {binaryOp.OperatorType} and type size {typeSize}!");
                                        break;
                                }
                            }

                            if (typedRight is ASTNumericLitteral rightLit && rightLit.IntValue == 0)
                            {
                                EmitExpression(builder, typedLeft, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                                AppendSetInst("Eql 0");
                                return;
                            }
                            else if (typedLeft is ASTNumericLitteral leftLit && leftLit.IntValue == 0)
                            {
                                EmitExpression(builder, typedRight, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                                AppendSetInst("Eql 0");
                                return;
                            }
                            else if (typedRight is ASTNullLitteral)
                            {
                                EmitExpression(builder, typedLeft, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                                AppendSetInst("Eql null");
                                return;
                            }
                            else if (typedLeft is ASTNullLitteral)
                            {
                                EmitExpression(builder, typedRight, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                                AppendSetInst("Eql null");
                                return;
                            }
                        }

                        var bin = typedLeft as ASTBinaryOp ?? typedRight as ASTBinaryOp;
                        if (binaryOp.OperatorType == ASTBinaryOp.BinaryOperatorType.Addition && bin != null && bin.OperatorType == ASTBinaryOp.BinaryOperatorType.Multiplication)
                        {
                            // Here we can do a multiply add instruction optimization

                            var other = ReferenceEquals(bin, typedRight) ? typedLeft : typedRight;

                            if (typeSize > 2)
                                Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {resultType} with size {typeSize}");

                            EmitExpression(builder, other, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);

                            // FIXME: Atm a casted binary op will not work here!
                            // So we really want to do the optimization check earlier and do proper casting here!
                            EmitExpression(builder, bin.Left, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            EmitExpression(builder, bin.Right, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);

                            if (produceResult)
                            {
                                if (typeSize == 1)
                                {
                                    builder.AppendLine("\tmuladd");
                                    //builder.AppendLine("\tmul add");
                                }
                                else if (typeSize == 2)
                                {
                                    builder.AppendLine("\tlmuladd");
                                    //builder.AppendLine("\tlmul ladd");
                                }
                            }

                            // We are done!
                            break;
                        }

                        // FIXME: We could optimize the oop at the end away by not sending true at the end here!
                        EmitExpression(builder, typedLeft, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                        EmitExpression(builder, typedRight, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                        // FIXME: Consider the size of the result of the expression
                        switch (binaryOp.OperatorType)
                        {
                            case ASTBinaryOp.BinaryOperatorType.Addition:
                                if (typeSize == 1)
                                {
                                    builder.AppendLine("\tadd");
                                }
                                else if (typeSize == 2)
                                {
                                    builder.AppendLine("\tladd");
                                }
                                else
                                {
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {resultType} with size {typeSize}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Subtraction:
                                if (typeSize == 1)
                                {
                                    builder.AppendLine("\tsub");
                                }
                                else if (typeSize == 2)
                                {
                                    builder.AppendLine("\tlsub");
                                }
                                else
                                {
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {resultType} with size {typeSize}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Multiplication:
                                if (typeSize == 1)
                                {
                                    builder.AppendLine("\tmul");
                                }
                                else if (typeSize == 2)
                                {
                                    builder.AppendLine("\tlmul");
                                }
                                else
                                {
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {resultType} with size {typeSize}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Division:
                                if (typeSize == 1)
                                {
                                    builder.AppendLine("\tdiv");
                                }
                                else if (typeSize == 2)
                                {
                                    builder.AppendLine("\tldiv");
                                }
                                else
                                {
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {resultType} with size {typeSize}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Modulo:
                                if (typeSize == 1)
                                {
                                    builder.AppendLine("\tmod");
                                }
                                else if (typeSize == 2)
                                {
                                    builder.AppendLine("\tlmod");
                                }
                                else
                                {
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {resultType} with size {typeSize}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Bitwise_And:
                                if (typeSize == 1)
                                {
                                    builder.AppendLine("\tand");
                                }
                                else if (typeSize == 2)
                                {
                                    // FIXME: Make this better!
                                    builder.AppendLine("\tslswap and");
                                    builder.AppendLine("\tslswap slswap");
                                    builder.AppendLine("\tand");
                                    builder.AppendLine("\tswap");
                                }
                                else
                                {
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {resultType} with size {typeSize}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Bitwise_Or:
                                if (typeSize == 1)
                                {
                                    builder.AppendLine("\tor");
                                }
                                else if (typeSize == 2)
                                {
                                    builder.AppendLine("\tslswap or");
                                    builder.AppendLine("\tslswap slswap");
                                    builder.AppendLine("\tor");
                                    builder.AppendLine("\tswap");
                                }
                                else
                                {
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {resultType} with size {typeSize}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Bitwise_Xor:
                                if (typeSize == 1)
                                {
                                    builder.AppendLine("\txor");
                                }
                                else if (typeSize == 2)
                                {
                                    builder.AppendLine("\tslswap xor");
                                    builder.AppendLine("\tslswap slswap");
                                    builder.AppendLine("\txor");
                                    builder.AppendLine("\tswap");
                                }
                                else
                                {
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {resultType} with size {typeSize}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Equal:
                                if (typeSize == 1)
                                {
                                    builder.AppendLine("\tsub setz\t; Equals cmp");
                                }
                                else if (typeSize == 2)
                                {
                                    builder.AppendLine("\tlsub lsetz or\t; Equals cmp");
                                }
                                else
                                {
                                    Fail(binaryOp.Trace, $"Cannot compare types larger than 2 words right now! Got type {resultType} with size {typeSize}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Not_equal:
                                if (typeSize == 1)
                                {
                                    builder.AppendLine("\tsub setnz\t; Not equals cmp");
                                }
                                else if (typeSize == 2)
                                {
                                    builder.AppendLine("\tlsub lsetnz or\t; Equals cmp");
                                }
                                else
                                {
                                    Fail(binaryOp.Trace, $"Cannot compare types larger than 2 words right now! Got type {resultType} with size {typeSize}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Less_than:
                                if (typeSize == 1)
                                {
                                    builder.AppendLine("\tswap sub setgz ; Less than");
                                }
                                else if (typeSize == 2)
                                {
                                    //builder.AppendLine("\tlswap lsub lsetgz or ; Less than");
                                    builder.AppendLine("\tccl lswap lsub lsetcz or ; Less than");
                                }
                                else
                                {
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {resultType} with size {typeSize}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Less_than_or_equal:
                                if (typeSize == 1)
                                {
                                    builder.AppendLine("\tswap sub setge ; Less than or equal");
                                }
                                else if (typeSize == 2)
                                {
                                    builder.AppendLine("\tlswap lsub lsetge or ; Less than or equal");
                                }
                                else
                                {
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {resultType} with size {typeSize}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Greater_than:
                                if (typeSize == 1)
                                {
                                    // 1 if left > right
                                    // left - right > 0
                                    builder.AppendLine("\tsub setgz\t; Greater than");
                                }
                                else if (typeSize == 2)
                                {
                                    builder.AppendLine("\tlsub lsetgz or\t; Greater than");
                                }
                                else
                                {
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {resultType} with size {typeSize}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Logical_And:
                                if (typeSize == 1)
                                {
                                    // TODO: Fix this!!
                                    builder.AppendLine("\tand");
                                }
                                else if (typeSize == 2)
                                {
                                    throw new NotImplementedException();
                                }
                                else
                                {
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {resultType} with size {typeSize}");
                                }
                                break;
                            case ASTBinaryOp.BinaryOperatorType.Logical_Or:
                                if (typeSize == 1)
                                {
                                    // TODO: Fix this!!
                                    builder.AppendLine("\tor");
                                }
                                else if (typeSize == 2)
                                {
                                    throw new NotImplementedException();
                                }
                                else
                                {
                                    Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {resultType} with size {typeSize}");
                                }
                                break;
                            default:
                                Fail(binaryOp.Trace, $"Unknown binary operator type {binaryOp.OperatorType}, this is a compiler bug!");
                                break;
                        }

                        // NOTE: We could get away without this!
                        if (produceResult == false)
                        {
                            if (typeSize == 1)
                            {
                                builder.AppendLine("\tpop");
                            }
                            else if (typeSize == 2)
                            {
                                builder.AppendLine("\tpop pop");
                            }
                            else
                            {
                                Fail(binaryOp.Trace, $"We only support types with size up to 2 right now! Got type {resultType} with size {typeSize}");
                            }
                        }
                        break;
                    }
                case ASTConditionalExpression conditional:
                    {
                        // builder.AppendLine($"\t; Ternary {conditional.Condition.GetType()} ({condIndex})");
                        
                        var ifTrueType = CalcReturnType(conditional.IfTrue, scope, typeMap, functionMap, constMap, globalMap);
                        var ifFalseType = CalcReturnType(conditional.IfFalse, scope, typeMap, functionMap, constMap, globalMap);

                        TypedExpressionPair typedExpr = default;

                        // FIXME!!!!! This is really bad!! We should not try-catch!
                        try
                        {
                            var tempBinOp = new ASTBinaryOp(conditional.Trace, ASTBinaryOp.BinaryOperatorType.Unknown, conditional.IfTrue, conditional.IfFalse);
                            typedExpr = GenerateBinaryCast(tempBinOp, scope, typeMap, functionMap, constMap, globalMap);
                        }
                        catch (InvalidOperationException)
                        {
                            Fail(conditional.Trace, $"Cannot return two different types {ifTrueType} and {ifFalseType} from a conditional operator! Got '{ifTrueType}' and '{ifFalseType}'!");
                        }
                        
                        var condType = CalcReturnType(conditional.Condition, scope, typeMap, functionMap, constMap, globalMap);
                        if (TryGenerateImplicitCast(conditional.Condition, ASTBaseType.Bool, scope, typeMap, functionMap, constMap, globalMap, out var typedCond, out string error) == false)
                            Fail(conditional.Condition.Trace, $"Cannot implicitly convert expression of type {condType} to {ASTBaseType.Bool}! Cast error: '{error}'");

                        // Now we don't have to handle doing jumps on things that are bigger than one word as the condition will be a bool!
                        int resultTypeSize = SizeOfType(typedExpr.Type, typeMap);

                        var foldedTrue = ConstantFold(typedExpr.Left, scope, typeMap, functionMap, constMap, globalMap);
                        var foldedFalse = ConstantFold(typedExpr.Right, scope, typeMap, functionMap, constMap, globalMap);
                        var foldedCond = ConstantFold(typedCond, scope, typeMap, functionMap, constMap, globalMap);
                        
                        int condIndex = context.LabelContext.ConditionalLabels++;

                        // NOTE: We check if the un-casted condition is a binary op, then optimized jump can do smart things
                        if (conditional.Condition is ASTBinaryOp)
                        {
                            // Optimize jump for binary operations
                            GenerateOptimizedBinaryOpJump(builder, conditional.Condition as ASTBinaryOp, $":else_cond_{condIndex}", scope, varList, typeMap, context, functionMap, constMap, globalMap);
                        }
                        else
                        {
                            EmitExpression(builder, foldedCond, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                            builder.AppendLine($"\tjz :else_cond_{condIndex}");
                        }

                        // We propagate the produce results to the ifTrue and ifFalse emits.
                        builder.AppendLine($"\t:if_cond_{condIndex}");
                        EmitExpression(builder, foldedTrue, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                        builder.AppendLine($"\tjmp :post_cond_{condIndex}");
                        builder.AppendLine($"\t:else_cond_{condIndex}");
                        EmitExpression(builder, foldedFalse, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                        builder.AppendLine($"\t:post_cond_{condIndex}");
                        break;
                    }
                case ASTContainsExpression containsExpression:
                    {
                        var valueType = ResolveType(CalcReturnType(containsExpression.Value, scope, typeMap, functionMap, constMap, globalMap), typeMap);
                        var lowerType = ResolveType(CalcReturnType(containsExpression.LowerBound, scope, typeMap, functionMap, constMap, globalMap), typeMap);
                        var upperType = ResolveType(CalcReturnType(containsExpression.UpperBound, scope, typeMap, functionMap, constMap, globalMap), typeMap);
                        
                        // FIXME: We can only do this on numeric types!!! Not string, void etc
                        if (valueType is ASTBaseType == false)
                        {
                            if (UnAliasType(valueType) is ASTBaseType)
                            {
                                // This is valid
                            }
                            else if (valueType is ASTPointerType)
                            {
                                // This is valid

                                // FIXME: This is a bad hack!!!
                                // It allows us to check contains for any type of pointers for the value, upper, and lower.
                                // Usefull for fixed arrays .end member wich is a void pointer
                                valueType = ASTPointerType.Of(ASTBaseType.Void);
                            }
                            else
                            {
                                Fail(containsExpression.Value.Trace, $"Can only do contains expressions on number/pointer types! Got '{valueType}'!");
                            }
                        }
                        
                        // FIXME! If we are comparing a word with 2 dwords we want to cast up!

                        if (TryGenerateImplicitCast(containsExpression.LowerBound, valueType, scope, typeMap, functionMap, constMap, globalMap, out var typedLower, out var lowerError) == false)
                            Fail(containsExpression.LowerBound.Trace, $"No implicit cast from {lowerType} to {valueType} for the lower bound '{containsExpression.LowerBound}'!");

                        if (TryGenerateImplicitCast(containsExpression.UpperBound, valueType, scope, typeMap, functionMap, constMap, globalMap, out var typedUpper, out var upperError) == false)
                            Fail(containsExpression.UpperBound.Trace, $"No implicit cast from {upperType} to {valueType} for the upper bound '{containsExpression.UpperBound}'!");
                        
                        // All types are the same!

                        // NOTE: We could do constant folding here!
                        var value = ConstantFold(containsExpression.Value, scope, typeMap, functionMap, constMap, globalMap);
                        var lower = ConstantFold(typedLower, scope, typeMap, functionMap, constMap, globalMap);
                        var upper = ConstantFold(typedUpper, scope, typeMap, functionMap, constMap, globalMap);

                        // NOTE: We could also try constant folding the size part!
                        // We could do this two ways, either we create an expression and try to constant fold it
                        // Or we try to recognize typlical scenarios like "someArray : someArray.end"
                        // and do fast optimizations for that. Because I don't think that the constant folding 
                        // would get that that is the same as "someArray.lenth" for this size part.
                        // We could also improve the constnat folding, but that might be very hard...?

                        // FIXME: If the cost of loading min two times is less than 5 instructions we just want to load it twice

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
                                EmitExpression(builder, lower, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                                EmitExpression(builder, value, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                                builder.AppendLine($"\tover sub swap");
                                EmitExpression(builder, upper, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                                builder.AppendLine($"\tswap sub");
                                builder.AppendLine($"\tinc sub");
                                if (produceResult) builder.AppendLine($"\tsetc\t; Set to one if the value is contained in the range");
                                else builder.AppendLine($"\tpop");
                                break;
                            case 2:
                                builder.AppendLine($"\t; Contains");
                                EmitExpression(builder, lower, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                                EmitExpression(builder, value, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                                builder.AppendLine($"\tlover lsub lswap");
                                EmitExpression(builder, upper, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
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
                        int funcCallIndex = context.LabelContext.FunctionCalls++;

                        string name;
                        List<(ASTType Type, string Name)> parameters;
                        ASTType returnType;

                        bool virtualCall = false;
                        string functionLabel;

                        bool intrinsicCall = false;
                        ASTIntrinsicFunction intrinsicFunc = null;

                        // FIXME: Function calls should really just be working on expressions! Or something like that
                        // Then we could do a function that returns a function pointer and then call that function directly
                        
                        List<ASTType> argumentTypes = functionCall.Arguments.Select(a => CalcReturnType(a, scope, typeMap, functionMap, constMap, globalMap)).ToList();

                        // Is there is something in our scope that is a better match for this function we use that and make this a virtual call
                        if (scope.TryGetValue(functionCall.FunctionName, out var variable) && variable.Type is ASTFunctionPointerType functionPointerType)
                        {
                            // This function call is referencing a local var
                            virtualCall = true;

                            name = $"{functionCall.FunctionName}({string.Join(", ", functionPointerType.ParamTypes)}) -> {functionPointerType.ReturnType}";
                            parameters = functionPointerType.ParamTypes.Select(p => (p, p.TypeName)).ToList();
                            returnType = functionPointerType.ReturnType;

                            functionLabel = "[SP]";

                            if (functionCall is ASTGenericFunctionCall)
                                Fail(functionCall.Trace, "Cannot use generic arguments on a function-pointer!");
                        }
                        else if (functionMap.TryGetValue(functionCall.FunctionName, out var functions))
                        {
                            if (TryFindBestFunctionMatch(functionCall.Trace, functions, argumentTypes, typeMap, out var function) == false)
                                Fail(functionCall.Trace, $"Did not find a overload for function '{functionCall.FunctionName}' with types '{string.Join(", ", argumentTypes)}'");
                            
                            name = function.Name;
                            parameters = function.Parameters;
                            returnType = function.ReturnType;

                            functionLabel = GetFunctionLabel(function, typeMap, functionMap);

                            if (functionCall is ASTGenericFunctionCall genericFunctionCall)
                            {
                                if (function is ASTIntrinsicFunction intrinsicFunction)
                                    Fail(functionCall.Trace, "Cannot call intrinsics with generics!");
                                else if (function is ASTGenericFunction genericFunction)
                                {
                                    if (Compiler.GenericSpecializations.TryGetValue(genericFunction, out var specializationList) == false)
                                    {
                                        specializationList = new SpecializationList();
                                        Compiler.GenericSpecializations[genericFunction] = specializationList;
                                    }

                                    ASTFunction specializedFunc = null;
                                    foreach (var func in specializationList)
                                    {
                                        if (func.GenericTypes.SequenceEqual(genericFunctionCall.GenericTypes))
                                        {
                                            specializedFunc = func.Specialization;
                                            break;
                                        }
                                    }

                                    if (specializedFunc == null)
                                    {
                                        // Here we need to specialize the generic function!
                                        specializedFunc = SpecializeFunction(functionCall.Trace, genericFunction, genericFunctionCall.GenericTypes, typeMap, functionMap);

                                        var (appendageBuilder, debugBuilder) = Compiler.AppendToFile(genericFunctionCall.Trace, genericFunction.Trace.File);

                                        // FIXME: This should be able to use the local typemap, functionmap etc, that the generic function was able to!
                                        // We should not be using the local maps for this!!!
                                        EmitFunction(appendageBuilder, specializedFunc, typeMap, functionMap, constMap, globalMap, debugBuilder);
                                        appendageBuilder.AppendLine();

                                        // Add the function to the list of overloads!
                                        // FIXME: Should we really do this as we are never actually using the fact that they are in the function map!!
                                        AddFunctionToMap(genericFunctionCall.Trace, functionMap, specializedFunc.Name, specializedFunc);

                                        // Add it to the specializations list
                                        specializationList.Add((specializedFunc, genericFunctionCall.GenericTypes));
                                    }

                                    name = specializedFunc.Name;
                                    parameters = specializedFunc.Parameters;
                                    returnType = specializedFunc.ReturnType;

                                    // FIXME: Fix this for generic functions!!
                                    functionLabel = GetGenericFunctionLabel(genericFunctionCall.Trace, genericFunction, genericFunctionCall.GenericTypes, typeMap, functionMap);
                                }
                            }
                            else
                            {
                                if (function is ASTIntrinsicFunction intrinsicFunction)
                                {
                                    intrinsicCall = true;
                                    intrinsicFunc = intrinsicFunction;
                                }
                                else if (function is ASTGenericFunction genericFunction)
                                    Fail(functionCall.Trace, $"Cannot call generic function '{name}' without generic parameters!");
                            }
                        }
                        else
                        {
                            Fail(functionCall.Trace, $"No function called '{functionCall.FunctionName}'");
                            return;
                        }

                        // This won't be needed as we check the types when we find the function above
                        if (functionCall.Arguments.Count != parameters.Count)
                            Fail(functionCall.Trace, $"Missmatching number of arguments for function {name}! Calling with {functionCall.Arguments.Count} expected {parameters.Count}");
                        
                        for (int i = 0; i < parameters.Count; i++)
                        {
                            ASTType targetType = ResolveType(parameters[i].Type, typeMap);
                            ASTType argumentType = CalcReturnType(functionCall.Arguments[i], scope, typeMap, functionMap, constMap, globalMap);

                            // Try and cast the arguemnt
                            if (TryGenerateImplicitCast(functionCall.Arguments[i], targetType, scope, typeMap, functionMap, constMap, globalMap, out ASTExpression typedArg, out string error) == false)
                                Fail(functionCall.Arguments[i].Trace, $"Missmatching types on parameter '{parameters[i].Name}' ({i}) when calling function '{functionCall.FunctionName}', expected '{parameters[i].Type}' got '{argumentType}'! (Cast error: '{error}')");

                            // We don't need to check the result as it will have the desired type.
                            // Switch the old argument for the new casted one
                            functionCall.Arguments[i] = typedArg;
                        }

                        if (functionCall.Arguments.Count > 0)
                        {
                            if (intrinsicCall)
                            {
                                builder.AppendLine($"\t; Args to intrinsic '{intrinsicFunc.Name}'");
                            }
                            else
                            {
                                builder.AppendLine($"\t; Args to function call ::{name}");
                            }
                        }

                        // This means adding a result type to expressions
                        foreach (var arg in functionCall.Arguments)
                        {
                            // Constant fold the arg
                            var foldedArg = ConstantFold(arg, scope, typeMap, functionMap, constMap, globalMap);
                            EmitExpression(builder, foldedArg, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                        }

                        if (intrinsicCall)
                        {
                            foreach (var line in intrinsicFunc.Body)
                            {
                                builder.AppendLine($"\t{line.Contents}");
                            }
                        }
                        else if (virtualCall)
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
                            LoadVariable(builder, functionCall.Trace, local, typeMap, constMap);
                            builder.AppendLine($"\t::[SP]\t; {name}");
                        }
                        else
                        {
                            // NOTE: Should we have a comment here?
                            // We could do something like:
                            //    proc_name(expr, expr)
                            // And do to string on all arguments

                            // Just call the function
                            builder.AppendLine($"\t::{functionLabel}\t; {functionCall.FunctionName}");
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
                case ASTVirtualFunctionCall virtualFunctionCall:
                    {
                        var targetType = ResolveType(CalcReturnType(virtualFunctionCall.FunctionPointer, scope, typeMap, functionMap, constMap, globalMap), typeMap);

                        if (targetType is ASTFunctionPointerType == false)
                            Fail(virtualFunctionCall.FunctionPointer.Trace, $"Cannot call non-function pointer type '{targetType}'!");

                        ASTFunctionPointerType functionPointerType = targetType as ASTFunctionPointerType;

                        // Check the parameter types
                        // Call the pointer

                        if (functionPointerType.ParamTypes.Count != virtualFunctionCall.Arguments.Count)
                            Fail(virtualFunctionCall.Trace, $"Missmatching number of arguments for type {functionPointerType}! Calling with {virtualFunctionCall.Arguments.Count} expected {functionPointerType.ParamTypes.Count}");

                        List<ASTType> parameters = functionPointerType.ParamTypes;

                        for (int i = 0; i < parameters.Count; i++)
                        {
                            ASTType paramType = parameters[i];
                            ASTType argumentType = CalcReturnType(virtualFunctionCall.Arguments[i], scope, typeMap, functionMap, constMap, globalMap);

                            // Try and cast the arguemnt
                            if (TryGenerateImplicitCast(virtualFunctionCall.Arguments[i], paramType, scope, typeMap, functionMap, constMap, globalMap, out ASTExpression typedArg, out string error) == false)
                                Fail(virtualFunctionCall.Arguments[i].Trace, $"Missmatching types on parameter '{i}', expected '{parameters[i]}' got '{argumentType}'! (Cast error: '{error}')");

                            // We don't need to check the result as it will have the desired type.

                            // NOTE: Should we really modify the AST like this?
                            // Switch the old argument for the new casted one
                            virtualFunctionCall.Arguments[i] = typedArg;
                        }

                        if (virtualFunctionCall.Arguments.Count > 0)
                            builder.AppendLine($"\t; Args to virtual function call to [{virtualFunctionCall.FunctionPointer}]");

                        // This means adding a result type to expressions
                        foreach (var arg in virtualFunctionCall.Arguments)
                        {
                            EmitExpression(builder, arg, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                        }

                        EmitExpression(builder, virtualFunctionCall.FunctionPointer, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                        builder.AppendLine($"\t::[SP]\t; Virtual call to [{virtualFunctionCall.FunctionPointer}]");

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

                            int baseTypeSize = SizeOfType(baseType, typeMap);

                            if (TryGenerateImplicitCast(pointerExpression.Offset, ASTBaseType.DoubleWord, scope, typeMap, functionMap, constMap, globalMap, out ASTExpression toFoldIndex, out string error) == false)
                                Fail(pointerExpression.Offset.Trace, $"Could not generate implicit cast for pointer offset of type {offsetType} to {ASTBaseType.DoubleWord}: {error}");

                            var foldedIndex = ConstantFold(toFoldIndex, scope, typeMap, functionMap, constMap, globalMap);

                            // Set up the dereferencing operation as arithmetic operations so we can do constant folding
                            // pointer + (typeSize * index)
                            // And we have to do a cast for things to be fine
                            // FIXME: We should refactor DerefPointer so we can include the pointer expression in the constant folding...
                            // TODO: We also want a way to emit a comment describing what the folded value is...
                            var offsetExpression = new ASTBinaryOp(pointerExpression.Offset.Trace, ASTBinaryOp.BinaryOperatorType.Multiplication,
                                                        foldedIndex, ASTDoubleWordLitteral.From(pointerExpression.Trace, baseTypeSize));

                            // Try to cast the offset to a dword
                            if (TryGenerateImplicitCast(offsetExpression, ASTBaseType.DoubleWord, scope, typeMap, functionMap, constMap, globalMap, out ASTExpression toFoldExpression, out error) == false)
                                Fail(pointerExpression.Offset.Trace, $"Could not generate implicit cast for pointer offset of type {offsetType} to {ASTBaseType.DoubleWord}: {error}");

                            var foldedExpr = ConstantFold(toFoldExpression, scope, typeMap, functionMap, constMap, globalMap);
                            
                            if (foldedExpr is ASTNumericLitteral numLit)
                            {
                                if (numLit.IntValue == 0)
                                {
                                    // Here we really don't need to add this offset
                                }
                                else
                                {
                                    // FIXME: Make sure it's a dword and not a word litteral!!!
                                    Debug.Assert(numLit is ASTDoubleWordLitteral);

                                    EmitLitteral(builder, numLit, $"Folded offset for index {numLit.IntValue / baseTypeSize}");
                                    builder.AppendLine("\tladd");
                                }
                            }
                            else
                            {
                                EmitExpression(builder, foldedIndex, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);

                                if (baseTypeSize == 1)
                                {
                                    // Here we just do an add
                                    builder.AppendLine($"\tladd");
                                }
                                else
                                {
                                    EmitLitteral(builder, ASTDoubleWordLitteral.From(pointerExpression.Trace, baseTypeSize), $"Size of pointer type '{baseType}'");
                                    builder.AppendLine($"\tlmuladd");
                                }
                            }
                            
                            VariableRef pointerRef = new VariableRef()
                            {
                                VariableType = VariableType.Pointer,
                                Type = baseType,
                                Comment = $"{pointerExpression.Pointer}[{pointerExpression.Offset}]",
                            };
                            
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

                                EmitExpression(builder, typedAssign, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);

                                StoreVariable(builder, pointerExpression.Trace, pointerRef, typeMap);
                            }
                            
                            // FIXME!!
                            // TODO: What if we should not produce a result and we are not assigning?
                            // It looks like we are leaving things on the stack!

                            if (produceResult)
                            {
                                LoadVariable(builder, pointerExpression.Trace, pointerRef, typeMap, constMap);
                            }
                        }

                        // Handle the case of an array type separate from the rest... (Not optimal...)
                        var pType = CalcReturnType(pointerExpression.Pointer, scope, typeMap, functionMap, constMap, globalMap);
                        if (pType is ASTArrayType)
                        {
                            // TODO: We should add a bounds check first!

                            // Basically load the data member and do the same ASTPointerExpression with that instead
                            var pExpr = pointerExpression;
                            var dataPointerExpression = new ASTPointerExpression(pExpr.Trace, new ASTMemberExpression(pExpr.Trace, pExpr.Pointer, "data", null, false), pExpr.Offset, pExpr.Assignment);
                            EmitExpression(builder, dataPointerExpression, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            return;
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
                                        // We can dereference constant pointers!
                                        if (variable.Type is ASTDereferenceableType == false)
                                            Fail(variableExpr.Trace, $"Cannot dereference constant of type '{variable.Type}'!");

                                        if (constMap.TryGetValue(variable.ConstantName, out var constant) == false)
                                            Fail(pointerExpression.Trace, $"No constant '{variable.ConstantName}'! This should not happen!");

                                        // If it's a constant array we need to load the generated proc
                                        if (constant.Value is ASTArrayLitteral)
                                            builder.AppendLine($"\tloadl :{variable.ConstantName}\t; {variable.ConstantName}[{pointerExpression.Offset}]");
                                        else
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

                            if (pointerType is ASTFixedArrayType fixedArray)
                            {
                                // If this is a fixed array we don't want to load the entire array.
                                if (pointerExpression.Pointer is ASTUnaryOp unaryOp && unaryOp.OperatorType == ASTUnaryOp.UnaryOperationType.Dereference)
                                {
                                    // This is a special case where we can load the pointer really easy. 
                                    // We just load the pointer to the fixedArray before dereferencing.
                                    EmitExpression(builder, unaryOp.Expr, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                                }
                                else
                                {
                                    Fail(pointerExpression.Pointer.Trace, $"We don't support indexing fixed arrays like this atm!");
                                }
                            }
                            else
                            {
                                EmitExpression(builder, pointerExpression.Pointer, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                            }
                            
                            // This handles assignment!
                            DerefPointer(pointerType);
                        }
                        break;
                    }
                case ASTPointerToVoidPointerCast cast:
                    {
                        // We really don't need to do anything as the cast is just for type-safety
                        EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                        break;
                    }
                case ASTFixedArrayToArrayCast cast:
                    {
                        var address = new ASTAddressOfExpression(cast.Trace, cast.From);
                        EmitExpression(builder, address, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                        if (produceResult) builder.AppendLineWithComment($"\tloadl #{cast.FromType.Size}", $"Length of fixed array {cast.FromType}");
                        break;
                    }
                case ASTStringToArrayCast cast:
                    {
                        if (cast.From is ASTStringLitteral litteral)
                        {
                            var ptr = new ASTExplicitCast(cast.Trace, litteral, ASTPointerType.Of(ASTBaseType.Char));
                            EmitExpression(builder, ptr, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            if (produceResult) builder.AppendLineWithComment($"\tloadl #{litteral.Contents.Length}", $"Length of string litteral '{litteral.Contents}'");
                        }
                        else
                        {
                            // Get the length and the string and the pointer to the string
                            // NOTE: There might be some better way of doing this?
                            var ptr = new ASTExplicitCast(cast.Trace, cast.From, ASTPointerType.Of(ASTBaseType.Char));
                            EmitExpression(builder, ptr, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);

                            var length = new ASTUnaryOp(cast.Trace, ASTUnaryOp.UnaryOperationType.Dereference, new ASTExplicitCast(cast.Trace, cast.From, ASTPointerType.Of(ASTBaseType.DoubleWord)));
                            EmitExpression(builder, length, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                        }
                        break;
                    }
                case ASTImplicitCast cast:
                    {
                        if (cast.ToType == ASTBaseType.Bool)
                        {
                            // Special cases for bool conversions

                            if (cast.FromType == ASTBaseType.Void)
                            {
                                Fail(cast.Trace, $"Cannot cast expression of type '{ASTBaseType.Void}' to '{ASTBaseType.Bool}'!");
                            }
                            else if (cast.FromType == ASTBaseType.Word || cast.FromType == ASTBaseType.Char)
                            {
                                EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                                if (produceResult) builder.AppendLine($"\tsetnz ; cast from {cast.FromType} to bool");
                            }
                            else if (cast.FromType == ASTBaseType.DoubleWord || cast.FromType == ASTBaseType.String)
                            {
                                // FIXME: STRING: This will not work for new strings!!!
                                EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                                if (produceResult) builder.AppendLine($"\tor setnz ; cast from {cast.FromType} to bool");
                            }
                            else
                            {
                                Fail(cast.Trace, $"Unknown implicit cast to bool from type '{cast.FromType}'! This is weird!");
                            }
                        }
                        else if (cast.FromType.Size == cast.ToType.Size)
                        {
                            // Do nothing fancy, they have the same size
                            EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                        }
                        else if (cast.FromType.Size + 1 == cast.ToType.Size)
                        {
                            if (produceResult) builder.AppendLine($"\tload #0\t; Cast from '{cast.FromType}' to '{cast.To}'");
                            EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
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
                            EmitExpression(builder, implicitCast, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                        }
                        else if (cast.From is ASTDoubleWordLitteral && toType == ASTBaseType.Word)
                        {
                            // This is an optimization for dword litterals casted to words. We can just compile time truncate the litteral to 12-bits.
                            ASTDoubleWordLitteral dwordLit = cast.From as ASTDoubleWordLitteral;
                            int truncatedValue = dwordLit.IntValue & 0xFFF;
                            ASTWordLitteral wordLit = ASTWordLitteral.From(dwordLit.Trace, truncatedValue);
                            EmitExpression(builder, wordLit, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                        }
                        else
                        {
                            // There was no implicit way to do it.
                            // How do we cast structs?

                            // FIXME: Make implicit casting nicer, this way we don't have to explicitly cast all the time
                            // FIXME: CLEAN THIS MESS UP!!!

                            fromType = ResolveType(fromType, typeMap);

                            // The simplest case first
                            if (fromType == toType)
                            {
                                // They are the same type behind the scenes, so we just don't do anything
                                EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                                return;
                            }

                            // If it's an aliased type, get the real type
                            if (fromType is ASTAliasedType aliasedType) fromType = aliasedType.RealType;

                            if (ResolveType(fromType, typeMap) == ResolveType(toType, typeMap))
                            {
                                // They are the same type behind the scenes, so we just don't do anything
                                EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType is ASTArrayType && (toType is ASTPointerType || toType == ASTBaseType.DoubleWord))
                            {
                                // We get the data member from the array
                                var dataMember = new ASTMemberExpression(cast.From.Trace, cast.From, "data", null, false);
                                EmitExpression(builder, dataMember, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType is ASTArrayType fromArrType && toType is ASTArrayType toArrType)
                            {
                                if (SizeOfType(fromArrType.BaseType, typeMap) != SizeOfType(toArrType.BaseType, typeMap))
                                    Fail(cast.Trace, $"Cannot cast from {fromArrType} to {toArrType} because the elements have different sizes! {SizeOfType(fromArrType.BaseType, typeMap)} != {SizeOfType(toArrType.BaseType, typeMap)}");

                                // Just emit the array
                                EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType is ASTFixedArrayType && (toType is ASTPointerType || toType == ASTBaseType.DoubleWord))
                            {
                                // We get the data member from the array
                                var dataMember = new ASTMemberExpression(cast.From.Trace, cast.From, "data", null, false);
                                EmitExpression(builder, dataMember, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType is ASTTypeRef typeRef && typeMap.TryGetValue(typeRef.Name, out var actType) && actType == ASTBaseType.DoubleWord && toType == ASTPointerType.Of(ASTBaseType.Void))
                            {
                                // FIXME: THIS IS REALLY SPECIFIC!!!
                                // We need to fix the rules for explicit casting!!
                                EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType == ASTBaseType.Word && toType == ASTBaseType.Char)
                            {
                                // They have the same size!
                                EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            }
                            // TODO: Should we hardcode these casts? The list is getting pretty long, I don't think we should hard code them like this...
                            else if (fromType == ASTBaseType.DoubleWord && toType == ASTBaseType.Word)
                            {
                                if (cast.From is ASTVariableExpression variableExpression)
                                {
                                    if (TryResolveVariable(variableExpression.Name, scope, globalMap, constMap, functionMap, typeMap, out VariableRef var) == false)
                                        Fail(cast.Trace, $"Could not find variable {variableExpression.Name}");

                                    switch (var.VariableType)
                                    {
                                        case VariableType.Local:
                                            if (produceResult) builder.AppendLineWithComment($"\tload {var.LocalAddress + 1}", $"[cast(word) {variableExpression.Name}]");
                                            break;
                                        case VariableType.Pointer:
                                        case VariableType.Global:
                                        case VariableType.Constant:
                                            // TODO: Investigate if this ever happens and implement an optimization in that case
                                        case VariableType.Function:
                                        default:
                                            // Do the default implementation for now
                                            EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                                            if (produceResult) builder.AppendLine($"\tswap pop\t; cast({toType})");
                                            break;
                                    }
                                }
                                else
                                {
                                    // This cast is easy
                                    EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                                    if (produceResult) builder.AppendLine($"\tswap pop\t; cast({toType})");
                                }
                            }
                            else if (fromType is ASTPointerType && toType is ASTPointerType)
                            {
                                // We don't have to do anything to swap base type for pointer types!
                                EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType is ASTPointerType && toType == ASTBaseType.DoubleWord)
                            {
                                // We don't have to do anything to convert a pointer to a dword!
                                EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType is ASTFunctionPointerType && toType == ASTBaseType.DoubleWord)
                            {
                                // These will have the same size just emit the expression
                                EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType is ASTFunctionPointerType && toType == ASTPointerType.Of(ASTBaseType.Void))
                            {
                                // NOTE: This might not be the best as we can confuse functions for structs!
                                // These will have the same size just emit the expression
                                EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType == ASTBaseType.Word && toType is ASTPointerType)
                            {
                                // FIXME: How should we do with sign!!!
                                if (produceResult) builder.AppendLine($"\tload #0\t; Cast from '{fromType}' to '{toType}'");
                                EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType == ASTBaseType.DoubleWord && toType is ASTPointerType)
                            {
                                // We don't have to do anything to convert a dword to a pointer!
                                EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType == ASTBaseType.DoubleWord && toType is ASTFunctionPointerType)
                            {
                                // We don't have to do anything to convert a dword to a function pointer!
                                EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType == ASTPointerType.Of(ASTBaseType.Void) && toType is ASTFunctionPointerType)
                            {
                                // We don't have to do anything to convert a *void to a function pointer!
                                EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType is ASTFunctionPointerType fromFuncType && toType is ASTFunctionPointerType toFuncType)
                            {
                                if (fromFuncType.ParamTypes.Count != toFuncType.ParamTypes.Count)
                                    Fail(cast.Trace, $"Cannot cast {fromFuncType} to {toFuncType} because they have different numner of arguemnts!");

                                for (int i = 0; i < fromFuncType.ParamTypes.Count; i++)
                                {
                                    int fromSize = SizeOfType(fromFuncType.ParamTypes[i], typeMap);
                                    int toSize = SizeOfType(toFuncType.ParamTypes[i], typeMap);

                                    if (fromSize != toSize)
                                        Fail(cast.Trace, $"Cannot cast {fromFuncType} to {toFuncType} because the sizes of arg {i} don't match! {fromFuncType.ParamTypes[i]}({fromSize}) != {toFuncType.ParamTypes[i]}({toSize})");
                                }

                                int fromRetSize = SizeOfType(fromFuncType.ReturnType, typeMap);
                                int toRetSize = SizeOfType(toFuncType.ReturnType, typeMap);

                                // If the return types have different size
                                if (fromRetSize != toRetSize)
                                    Fail(cast.Trace, $"Cannot cast {fromFuncType} to {toFuncType} because the sizes of the return type don't match! {fromFuncType.ReturnType}({fromRetSize}) != {toFuncType.ReturnType}({toRetSize})");

                                // Emit the compatible function pointer
                                EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType == ASTBaseType.String && (toType == ASTPointerType.Of(ASTBaseType.Word) || toType == ASTPointerType.Of(ASTBaseType.Char)))
                            {
                                // FIXME: Make proper strings! Now we are doing different things for different casts!!
                                // TODO: Proper strings!
                                // Take the string pointer and increment it by two
                                EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                                if (produceResult) builder.AppendLine($"\tlinc linc");
                            }
                            else if (fromType == ASTBaseType.String && toType == ASTPointerType.Of(ASTBaseType.DoubleWord))
                            {
                                // FIXME! FIXME! FIXME! FIXME! FIXME!
                                // For now we just cast to the raw pointer and not the data pointer
                                // This is because it is convenient in code while we don't have proper strings
                                EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType == ASTBaseType.String && toType == ASTBaseType.DoubleWord)
                            {
                                // FIXME: Make proper strings! Now we are doing different things for different casts!!
                                // Because a string is just a pointer ATM just emit the expresion
                                EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType is ASTPointerType && toType == ASTBaseType.String)
                            {
                                // FIXME: Make proper strings! Now we are doing different things for different casts!!
                                // Because a string is just a pointer ATM just emit the expresion
                                EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType == ASTPointerType.Of(ASTBaseType.Void) && toType is ASTArrayType arrayType)
                            {
                                // We want a *void to become a []something. That should be fine?
                                // Not really as it would need a length, but that is in the future!
                                // FIXME: Proper arrays with length and stuff!!!

                                // They have the same size so we just emit
                                EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType is ASTFixedArrayType fixedArrayType && (toType == ASTPointerType.Of(fixedArrayType.BaseType) || toType == ASTPointerType.Of(ASTBaseType.Void)))
                            {
                                // We take the "data" pointer of the fixed array and use that
                                var data_member = new ASTMemberExpression(cast.From.Trace, cast.From, "data", null, false);
                                EmitExpression(builder, data_member, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType == ASTBaseType.Bool && toType == ASTBaseType.Word)
                            {
                                // Here we do nothing but emit the bool
                                EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            }
                            else if (fromType is ASTStructType && toType is ASTStructType && SizeOfType(fromType, typeMap) == SizeOfType(toType, typeMap))
                            {
                                Warning(cast.Trace, "Using same size struct to struct cast!!");
                                EmitExpression(builder, cast.From, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
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

                        var structType = targetType;
                        
                        // If we are dereferencing, figure out what type we are dealing with.
                        if (memberExpression.Dereference)
                        {
                            // DerefType will handle error messages.
                            structType = DerefType(memberExpression.Trace, targetType);
                        }

                        // If the type is a fixed array we handle the cases here
                        if (structType is ASTFixedArrayType fixedArrayType)
                        {
                            if (memberExpression.Assignment != null)
                                Fail(memberExpression.Trace, $"We don't support assignments to fixed array type members! (yet?)");

                            if (memberExpression.Dereference == true)
                                Fail(memberExpression.Trace, $"Cannot dereference members of a fixed array! Use '.' instead of '->'.");

                            // All of these branches should end here!
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
                                        EmitExpression(builder, dataExpr, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                                        return;
                                    }
                                case "end":
                                    {
                                        if (memberExpression.TargetExpr is ASTVariableExpression varExpr && TryResolveVariable(varExpr.Name, scope, globalMap, constMap, functionMap, typeMap, out var variable))
                                        {
                                            switch (variable.VariableType)
                                            {
                                                case VariableType.Global:
                                                    builder.AppendLine($"\tloadl #(#{variable.GlobalName} {(fixedArrayType.Size.IntValue * SizeOfType(fixedArrayType.BaseType, typeMap)) - 1} +)\t; The end of the fixed array of type '{fixedArrayType}'");
                                                    break;
                                                case VariableType.Pointer:
                                                    Fail(memberExpression.Trace, $"TryResolveVariable should not return variable of type pointer! This is a compiler bug!");
                                                    break;
                                                case VariableType.Constant:
                                                    Fail(memberExpression.Trace, $"It does not make sense to get the end address of a constant!? {varExpr}");
                                                    break;
                                                case VariableType.Function:
                                                    // It kind of does make sense.... Hmmm
                                                    Fail(memberExpression.Trace, $"It does not make sense to get the end address of a function!? {varExpr}");
                                                    break;
                                                default:
                                                    Fail(memberExpression.Trace, $"Unknown variable type: '{variable.VariableType}'!");
                                                    // Here we do the default thing!!
                                                    break;
                                            }
                                        }
                                        else
                                        {
                                            // TODO: Constant fold this!
                                            var dataExpr = new ASTAddressOfExpression(memberExpression.TargetExpr.Trace, memberExpression.TargetExpr);
                                            EmitExpression(builder, dataExpr, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                                            if (produceResult) builder.AppendLine($"\tloadl #({(fixedArrayType.Size.IntValue * SizeOfType(fixedArrayType.BaseType, typeMap)) - 1})\t; Offset to end address of type '{fixedArrayType}'");
                                        }

                                        return;
                                    }
                                default:
                                    Fail(memberExpression.Trace, $"Fixed array type '{targetType}' does not have a memeber '{memberExpression.MemberName}'");
                                    break;
                            }
                        }
                        
                        // NOTE: We let array types pass here!! We will handle the offsets ourselves... (It's not optimal)
                        // If we get here and structType is not a struct we fail
                        if (structType is ASTStructType == false && structType is ASTArrayType == false)
                            Fail(memberExpression.TargetExpr.Trace, $"Type '{structType}' does not have members!");

                        // The special case for arrays here is not very nice...
                        // Arrays should be refactored as a struct type!!
                        // Then there would only be special cases when indexing.
                        StructMember member = default;
                        if (structType is ASTArrayType arrayType)
                        {
                            // We fix our own members....
                            switch (memberExpression.MemberName)
                            {
                                case "length":
                                    // Hmmm, what should we do here... (for now we leave it empty)
                                    member.In = null;

                                    member.Type = ASTBaseType.DoubleWord;
                                    member.Offset = 2;
                                    member.Index = 1;
                                    member.Size = 2;
                                    break;
                                case "data":
                                    // Hmmm, what should we do here... (for now we leave it empty)
                                    member.In = null;

                                    member.Type = ASTPointerType.Of(arrayType.BaseType);
                                    member.Offset = 0;
                                    member.Index = 0;
                                    member.Size = 2;
                                    break;
                                default:
                                    Fail(memberExpression.Trace, $"Array type '{arrayType}' does not have a member '{memberExpression.MemberName}'!");
                                    break;
                            }
                        }
                        else
                        {
                            // This is a normal struct type so we just try to get the member
                            if (TryGetStructMember(structType as ASTStructType, memberExpression.MemberName, typeMap, out member) == false)
                                Fail(memberExpression.Trace, $"No member '{memberExpression.MemberName}' in struct '{structType}'!");

                            // We have to do this because the specialize function doesn't fully eval nested generics!
                            // FIXME: We might need to do that to make it easier to code and reduce bugs
                            member.Type = ResolveGenericType(member.Type, typeMap);
                        }
                        
                        ASTExpression typedAssigmnent = null;
                        if (memberExpression.Assignment != null)
                        {
                            var retType = CalcReturnType(memberExpression.Assignment, scope, typeMap, functionMap, constMap, globalMap);

                            if (TryGenerateImplicitCast(memberExpression.Assignment, member.Type, scope, typeMap, functionMap, constMap, globalMap, out typedAssigmnent, out var error) == false)
                                Fail(memberExpression.Assignment.Trace, $"Can't generate implicit cast from type '{retType}' to type '{member.Type}'! (Cast error: {error})");
                        }

                        // Optimization for when accessing members of members without derefencing
                        // We look at the expression we should get the memeber from
                        // If that expression is another member expression that does not dereference or have an assignment
                        // then we can just calculate an offset directly instead of loading the the whole target expression
                        // TODO: There might be some way to optimize assignments that is not super complicated but I'm not sure
                        Stack<string> membersComment = new Stack<string>();

                        ASTMemberExpression target = memberExpression;
                        membersComment.Push($"{target.MemberName}");
                        while (target.TargetExpr is ASTMemberExpression next && next.Dereference == false && next.Assignment == null)
                        {
                            membersComment.Push($"{next.MemberName}");

                            var nextTargetType = ResolveType(CalcReturnType(next.TargetExpr, scope, typeMap, functionMap, constMap, globalMap), typeMap);

                            // If we get here and structType is not a struct we fail
                            if (structType is ASTStructType == false && structType is ASTArrayType == false)
                                Fail(memberExpression.TargetExpr.Trace, $"Type '{structType}' does not have members!");

                            if (TryGetStructMember(nextTargetType as ASTStructType, next.MemberName, typeMap, out var nextMember) == false)
                                Fail(memberExpression.Trace, $"No member '{memberExpression.MemberName}' in struct '{structType}'!");

                            member.Offset += nextMember.Offset;

                            target = target.TargetExpr as ASTMemberExpression;
                        }

                        string memberComment = $"{membersComment.Aggregate((s1, s2) => $"{s1}.{s2}")}";

                        if (target.TargetExpr is ASTMemberExpression membExpression && membExpression.Dereference == true && target.Dereference == false && membExpression.Assignment == null)
                        {
                            // NOTE: We don't do this optimization for assigmnent because they are hard to think about
                            var membPointerType = ResolveType(CalcReturnType(membExpression.TargetExpr, scope, typeMap, functionMap, constMap, globalMap), typeMap);

                            var membType = DerefType(membExpression.Trace, membPointerType);

                            if (TryGetStructMember(membType as ASTStructType, membExpression.MemberName, typeMap, out var memb) == false)
                                Fail(memberExpression.Trace, $"No member '{membExpression.MemberName}' in struct '{structType}'!");

                            // Load the base pointer
                            EmitExpression(builder, membExpression.TargetExpr, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);

                            member.Offset += memb.Offset;

                            if (member.Offset != 0)
                            {
                                builder.AppendLineWithComment($"\tloadl #{member.Offset}", $"Offset to [{membExpression.TargetExpr}->{membExpression.MemberName}.{memberComment}]");
                                builder.AppendLine($"\tladd");
                            }

                            if (typedAssigmnent != null)
                            {
                                // Duplicate the pointer if we are going to produce a result
                                if (produceResult) builder.AppendLine("\tldup");
                                // Load the value to store
                                EmitExpression(builder, typedAssigmnent, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                                // Store the loaded value at the pointer
                                StoreSP(builder, typedAssigmnent.Trace, member.Size, $"[{membExpression.TargetExpr}->{membExpression.MemberName}.{memberComment}] = {target.Assignment}");
                            }

                            if (produceResult)
                            {
                                // Load the result from the pointer
                                LoadSP(builder, member.Size, $"[{membExpression.TargetExpr}->{membExpression.MemberName}.{memberComment}]");
                            }
                        }
                        else if (target.TargetExpr is ASTVariableExpression varExpr)
                        {
                            // This is an optimization for when we know where the variable is comming from

                            if (TryResolveVariable(varExpr.Name, scope, globalMap, constMap, functionMap, typeMap, out VariableRef variable) == false)
                                Fail(varExpr.Trace, $"There is no variable called '{varExpr.Name}'!");

                            // FIXME: Type checking when dereferencing?

                            switch (variable.VariableType)
                            {
                                case VariableType.Local:
                                    {
                                        // The local variable pointer
                                        if (target.Dereference)
                                        {
                                            // Load the target pointer
                                            LoadVariable(builder, target.Trace, variable, typeMap, constMap);
                                            // Add the member offset
                                            if (member.Offset != 0) builder.AppendLine($"\tloadl #{member.Offset} ladd\t; {memberComment} offset");
                                            
                                            if (typedAssigmnent != null)
                                            {
                                                // Duplicate the pointer if we are going to produce a result
                                                if (produceResult) builder.AppendLine("\tldup");
                                                // Load the value to store
                                                EmitExpression(builder, typedAssigmnent, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                                                // Store the loaded value at the pointer
                                                StoreSP(builder, typedAssigmnent.Trace, member.Size, $"[{target.TargetExpr}->{memberComment}] = {target.Assignment}");
                                            }
                                            
                                            if (produceResult)
                                            {
                                                // Load the result from the pointer
                                                LoadSP(builder, member.Size, $"[{target.TargetExpr}->{memberComment}]");
                                            }
                                        }
                                        else
                                        {
                                            VariableRef memberRef = new VariableRef()
                                            {
                                                VariableType = VariableType.Local,
                                                LocalAddress = variable.LocalAddress + member.Offset,
                                                Type = member.Type,
                                                Comment = $"[{target.TargetExpr}.{memberComment}]",
                                            };

                                            if (typedAssigmnent != null)
                                            {
                                                // Load the assignment value
                                                EmitExpression(builder, typedAssigmnent, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);

                                                // Store that value into local
                                                StoreVariable(builder, varExpr.Trace, memberRef, typeMap);
                                            }

                                            if (produceResult)
                                            {
                                                LoadVariable(builder, varExpr.Trace, memberRef, typeMap, constMap);

                                                // What is this? This case is already handled above?
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
                                            if (member.Offset != 0) builder.AppendLine($"\tloadl #{member.Offset} ladd\t; {memberComment} offset");

                                            if (typedAssigmnent != null)
                                            {
                                                // Duplicate the pointer if we are going to produce a result
                                                if (produceResult) builder.AppendLine("ldup");
                                                // Load the value to store
                                                EmitExpression(builder, typedAssigmnent, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                                                // Store the loaded value at the pointer
                                                StoreSP(builder, typedAssigmnent.Trace, member.Size, $"{target.TargetExpr}->{memberComment} = {target.Assignment}");
                                            }

                                            if (produceResult)
                                            {
                                                // Load the result from the pointer
                                                LoadSP(builder, member.Size, $"[{target.TargetExpr}->{memberComment}]");
                                            }
                                        }
                                        else
                                        {
                                            VariableRef memberRef = new VariableRef
                                            {
                                                // NOTE: This might not be the right thing to do...
                                                VariableType = VariableType.Pointer,
                                                Type = member.Type,
                                                Comment = $"{variable.GlobalName}.{memberComment}",
                                            };

                                            if (typedAssigmnent != null)
                                            {
                                                // Can we do this?
                                                builder.AppendLine($"\tloadl #(#{variable.GlobalName} {member.Offset} +)");
                                                EmitExpression(builder, typedAssigmnent, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                                                StoreVariable(builder, varExpr.Trace, memberRef, typeMap);
                                            }

                                            if (produceResult)
                                            {
                                                builder.AppendLine($"\tloadl #(#{variable.GlobalName} {member.Offset} +)");
                                                LoadVariable(builder, varExpr.Trace, memberRef, typeMap, constMap);
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
                            // NOTE: Why are we doing this optimization? Is it worth it?
                            
                            // Here we are derefing something and just taking one thing from the result.
                            // Then we can just get the pointer that points to the member
                            // We don't do this if we are dereferencing once again becase then we can't just
                            // add to the pointer

                            // If we are assigning to the pointer this becomes harder, so we just don't do this atm
                            
                            if (pointerExpression.Assignment != null) Fail(pointerExpression.Assignment.Trace, $"Assigning to the pointer expression should not happen here!");

                            var pointerType = CalcReturnType(pointerExpression.Pointer, scope, typeMap, functionMap, constMap, globalMap);
                            
                            // Load the pointer expression address
                            var addressOfPointerExpr = new ASTAddressOfExpression(pointerExpression.Trace, pointerExpression);
                            EmitExpression(builder, addressOfPointerExpr, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);

                            // If the member has a offset, add that offset
                            if (member.Offset != 0) builder.AppendLine($"\tloadl #{member.Offset} ladd\t; Offset to member {target.MemberName}");

                            VariableRef variable = new VariableRef
                            {
                                VariableType = VariableType.Pointer,
                                Type = member.Type,
                                Comment = $"[{target.MemberName}]",
                            };

                            if (typedAssigmnent != null)
                            {
                                // Duplicate the pointer so we can assign to it.
                                if (produceResult) builder.AppendLine("\tldup");

                                // Load the value
                                EmitExpression(builder, typedAssigmnent, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);

                                StoreVariable(builder, typedAssigmnent.Trace, variable, typeMap);
                            }

                            if (produceResult) LoadVariable(builder, target.Trace, variable, typeMap, constMap);
                        }
                        // TODO: Optimization where the explicit cast is a struct to struct of same size cast
                        /*else if (target.TargetExpr is ASTExplicitCast explicitCast && )*/
                        else
                        {
                            if (target.Dereference)
                            {
                                EmitExpression(builder, target.TargetExpr, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);

                                // Add the member offset to the pointer we just got!
                                if (member.Offset != 0) builder.AppendLine($"\tloadl #{member.Offset} ladd\t; {target.MemberName} offset");

                                // We are derefing a pointer to the member!
                                VariableRef pointerRef = new VariableRef
                                {
                                    VariableType = VariableType.Pointer,
                                    Type = member.Type,
                                    Comment = $"{target.TargetExpr}->{memberComment}",
                                };
                                
                                if (typedAssigmnent != null)
                                {
                                    // Duplicate the pointer
                                    if (produceResult) builder.AppendLine("\tldup");

                                    EmitExpression(builder, typedAssigmnent, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);

                                    StoreVariable(builder, memberExpression.Trace, pointerRef, typeMap);
                                }

                                if (produceResult)
                                {
                                    LoadVariable(builder, target.Trace, pointerRef, typeMap, constMap);
                                }
                            }
                            else
                            {
                                // We get the address to our current struct!
                                // Then we add our memberoffset
                                // Then we load the member

                                var addressOf = new ASTAddressOfExpression(target.TargetExpr.Trace, target.TargetExpr);
                                var memberAddr = new ASTBinaryOp(target.Trace, ASTBinaryOp.BinaryOperatorType.Addition, 
                                    new ASTExplicitCast(addressOf.Trace, addressOf, ASTPointerType.Of(ASTBaseType.Word)),
                                    ASTNumericLitteral.From(target.Trace, member.Offset));
                                var foldedAddr = ConstantFold(memberAddr, scope, typeMap, functionMap, constMap, globalMap);

                                EmitExpression(builder, foldedAddr, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);

                                if (typedAssigmnent != null)
                                {
                                    // Duplicate the pointer
                                    if (produceResult) builder.AppendLine("\tldup");

                                    EmitExpression(builder, typedAssigmnent, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);

                                    StoreSP(builder, typedAssigmnent.Trace, member.Size, $"[{target.TargetExpr}.{memberComment}] = {typedAssigmnent}");
                                }

                                if (produceResult)
                                {
                                    LoadSP(builder, member.Size, $"[{target.TargetExpr}.{memberComment}]");
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
                            EmitLitteral(builder, ASTNumericLitteral.From(sizeofTypeExpression.Trace, size), $"Size of type '{sizeOfType}'");
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
                                        builder.AppendLine($"\t[FP] loadl #4 ladd loadl [SP]\t; Load the number of locals");
                                        if (variable.LocalAddress != 0)
                                        {
                                            builder.AppendLine($"\tloadl #{variable.LocalAddress}");
                                            builder.AppendLine($"\tlsub\t; Subtract the local index");
                                        }
                                        builder.AppendLine($"\tlsub\t; &{addressOfExpression.Expr}");
                                        break;
                                    }
                                case VariableType.Global:
                                    {
                                        builder.AppendLine($"\tloadl #{variable.GlobalName}\t; &[{variable.GlobalName}]");
                                        break;
                                    }
                                case VariableType.Pointer:
                                    Fail(addressOfExpression.Trace, $"TryResolveVariable should not return variable of type pointer! This is a compiler bug!");
                                    break;
                                case VariableType.Constant:
                                    if (constMap.TryGetValue(variable.ConstantName, out var constant) && constant.Type is ASTFixedArrayType fixedArrayType)
                                        builder.AppendLine($"\tloadl :{variable.ConstantName}");
                                    else
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
                            if (pointerType is ASTFixedArrayType || pointerType is ASTArrayType)
                            {
                                var dataMember = new ASTMemberExpression(addressOfExpression.Trace, pointerExpression.Pointer, "data", null, false);
                                EmitExpression(builder, dataMember, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                            }
                            else
                            {
                                EmitExpression(builder, pointerExpression.Pointer, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);
                            }

                            var offsetType = CalcReturnType(pointerExpression.Offset, scope, typeMap, functionMap, constMap, globalMap);
                            // Try to cast the offset to a dword
                            if (TryGenerateImplicitCast(pointerExpression.Offset, ASTBaseType.DoubleWord, scope, typeMap, functionMap, constMap, globalMap, out ASTExpression dwordOffset, out string error) == false)
                                Fail(pointerExpression.Offset.Trace, $"Could not generate implicit cast for pointer offset of type {offsetType} to {ASTBaseType.DoubleWord}: {error}");

                            int baseTypeSize = SizeOfType(baseType, typeMap);

                            // Multiply by pointer base type size!
                            var typedOffset = new ASTBinaryOp(pointerExpression.Trace, ASTBinaryOp.BinaryOperatorType.Multiplication, dwordOffset, ASTNumericLitteral.From(addressOfExpression.Trace, baseTypeSize));

                            var foldedOffset = ConstantFold(typedOffset, scope, typeMap, functionMap, constMap, globalMap);
                            
                            if (foldedOffset is ASTNumericLitteral numLit && numLit.IntValue == 0)
                            {
                                // Here we don't have to add anything
                            }
                            else
                            {
                                // Emit the casted offset
                                EmitExpression(builder, foldedOffset, scope, varList, typeMap, context, functionMap, constMap, globalMap, true);

                                // Add the offset to the pointer
                                builder.AppendLine($"\tladd");
                            }

                            // This will be the address where the element is stored
                        }
                        else if (addressOfExpression.Expr is ASTMemberExpression memberExpression)
                        {
                            var targetType = ResolveType(CalcReturnType(memberExpression.TargetExpr, scope, typeMap, functionMap, constMap, globalMap), typeMap);

                            // This is the type we should search for members in
                            var structType = memberExpression.Dereference ? DerefType(memberExpression.Trace, targetType) : targetType;

                            if (structType is ASTStructType == false)
                                Fail(memberExpression.Trace, $"Type '{structType}' does not have any members!");

                            if (TryGetStructMember(structType as ASTStructType, memberExpression.MemberName, typeMap, out StructMember member) == false)
                                Fail(memberExpression.Trace, $"No member '{memberExpression.MemberName}' in struct '{structType}'!");

                            if (produceResult) builder.AppendLine($"\t; Address of '{memberExpression}'");

                            if (memberExpression.Dereference)
                            {
                                // Here we load the pointer and add the member offset.
                                EmitExpression(builder, memberExpression.TargetExpr, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            }
                            else
                            {
                                // Here we take the address of the expression and add the member offset
                                var structAddr = new ASTAddressOfExpression(memberExpression.TargetExpr.Trace, memberExpression.TargetExpr);
                                EmitExpression(builder, structAddr, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            }

                            // We have the base pointer, so we just add the member offset
                            if (produceResult)
                            {
                                if (member.Offset != 0)
                                {
                                    builder.AppendLine($"\tloadl #{member.Offset} ; {memberExpression}");
                                    builder.AppendLine($"\tladd");
                                }
                            }
                        }
                        else if (addressOfExpression.Expr is ASTUnaryOp unaryOp)
                        {
                            if (unaryOp.OperatorType == ASTUnaryOp.UnaryOperationType.Dereference)
                            {
                                // Here we want the address of the thing that the expression is pointing to.
                                // Which is just the value of the expression.
                                EmitExpression(builder, unaryOp.Expr, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                            }
                            else
                            {
                                Fail(addressOfExpression.Trace, $"Cannot take address of a unary operator that is not a dereference! Op type: '{unaryOp.OperatorType}'");
                            }
                        }
                        else
                        {
                            Fail(addressOfExpression.Trace, $"Unsupported or invalid type for address of: '{addressOfExpression.Expr}'");
                        }
                        break;
                    }
                case ASTTypeOfExpression typeOfExpression:
                    {
                        var typeOfType = ResolveType(typeOfExpression.Type, typeMap);

                        int typeID = Compiler.AddReferencedType(typeOfType);

                        if (produceResult)
                        {
                            EmitLitteral(builder, ASTDoubleWordLitteral.From(typeOfExpression.Trace, typeID, ASTNumericLitteral.NumberFormat.Hexadecimal), $"TypeID of '{typeOfType}'");
                        }
                        break;
                    }
                case ASTDefaultExpression defaultExpression:
                    {
                        int typeSize = SizeOfType(defaultExpression.Type, typeMap);

                        if (produceResult)
                        {
                            builder.AppendLine($"\t; Default value for type '{defaultExpression.Type}'({typeSize})");
                            LoadZeroes(builder, typeSize);
                        }
                        break;
                    }
                case ASTInlineAssemblyExpression assemblyStatement:
                    {
                        foreach (var line in assemblyStatement.Assembly)
                        {
                            builder.AppendLine($"\t{line.Contents}");
                        }

                        if (produceResult == false)
                        {
                            var resultType = ResolveType(assemblyStatement.ResultType, typeMap);
                            int resultSize = SizeOfType(resultType, typeMap);

                            if (resultSize > 0)
                            {
                                Warning(assemblyStatement.Trace, $"We are not using the result of this inline assembly expression! It's recomended to manually clean up in an assenbly statement! Result type: '{resultType}'");

                                builder.AppendLine("\t; Pop inline assembly result");
                                for (int i = 0; i < resultSize; i++)
                                {
                                    builder.AppendLine("\tpop");
                                }
                            }
                        }
                        break;
                    }
                case ASTInternalCompoundExpression compoundExpression:
                    {
                        if (compoundExpression.Comment != null)
                            builder.AppendLine($"\t; {compoundExpression.Comment}");

                        foreach (var expr in compoundExpression.Expressions)
                        {
                            EmitExpression(builder, expr, scope, varList, typeMap, context, functionMap, constMap, globalMap, produceResult);
                        }
                        break;
                    }
                default:
                    Fail(expression.Trace, $"Unknown expression type {expression}, this is a compiler bug!");
                    break;
            }
        }

        private static void EmitLitteral(StringBuilder builder, ASTLitteral litteral, string comment = null)
        {
            switch (litteral)
            {
                case ASTWordLitteral wordLitteral:
                    builder.AppendLineWithComment($"\tload #{litteral}", comment);
                    break;
                case ASTDoubleWordLitteral dwordLitteral:
                    builder.AppendLineWithComment($"\tloadl #{litteral}", comment);
                    break;
                case ASTCharLitteral charLitteral:
                    if (charLitteral.Value.StartsWith("'"))
                        builder.AppendLineWithComment($"\tload {litteral}", comment);
                    else
                        builder.AppendLineWithComment($"\tload #{litteral}", comment);
                    break;
                case ASTStringLitteral stringLitteral:
                    builder.AppendLineWithComment($"\tload {litteral}", comment);
                    break;
                case ASTBoolLitteral boolLitteral:
                    // NOTE: Should we load the constants instead?
                    // We have moved to litterals just needing ToString to be valid for emitting,
                    // Bools don't follow this for now but should probably be changed
                    builder.AppendLineWithComment($"\tload #{(boolLitteral.BoolValue ? 1 : 0)}", comment ?? (boolLitteral.BoolValue ? "true" : "false"));
                    break;
                case ASTNullLitteral nullLitteral:
                    // The same goes for null as for bool, its not directly emittable for now!
                    builder.AppendLineWithComment($"\tloadl #0", comment ?? "null");
                    break;
                default:
                    Fail(litteral.Trace, $"Unknown litteral type {litteral.GetType()}, this is a compiler bug!");
                    break;
            }
        }
    }
}
 