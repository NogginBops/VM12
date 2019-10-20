using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace T12
{
    using ConstMap = Dictionary<string, ASTConstDirective>;
    using FunctionMap = Dictionary<string, List<ASTFunction>>;
    using GlobalMap = Dictionary<string, ASTGlobalDirective>;
    using ImportMap = Dictionary<string, ASTFile>;
    using TypeMap = Dictionary<string, ASTType>;
    using VarList = List<(string Name, int Offset, ASTType Type)>;
    using VarMap = Dictionary<string, (int Offset, ASTType Type)>;
    using GenericMap = Dictionary<string, ASTType>;

    static partial class Emitter
    {
        private static void AppendLineWithComment(this StringBuilder builder, string data, string comment = "")
        {
            if (comment != null && comment.Length > 0)
            {
                builder.AppendLine($"{data}\t; {comment}");
            }
            else
            {
                builder.AppendLine(data);
            }
        }

        private static void AppendTypeToFunctionLabel(StringBuilder label, ASTType type)
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
                case ASTFunctionPointerType fpType:
                    // FIXME
                    label.Append($"FP..");
                    foreach (var param in fpType.ParamTypes)
                    {
                        label.Append("_");
                        AppendTypeToFunctionLabel(label, param);
                    }
                    label.Append("..");
                    AppendTypeToFunctionLabel(label, fpType.ReturnType);
                    break;
                default:
                    label.Append(type.TypeName);
                    break;
            }
        }

        private static void IncrementLocal(StringBuilder builder, TraceData trace, int localAddress, ASTType type, int typeSize, string comment = "")
        {
            if ((type == ASTBaseType.Word || type == ASTBaseType.DoubleWord) == false)
                Fail(trace, $"We don't support incrementing variables of type '{type}'!");

            switch (typeSize)
            {
                case 1:
                    builder.AppendLineWithComment($"\tinc {localAddress}", comment);
                    break;
                case 2:
                    builder.AppendLineWithComment($"\tlinc {localAddress}", comment);
                    break;
                default:
                    Fail(trace, $"This should not happen!!!");
                    break;
            }
        }

        private static void IncrementSP(StringBuilder builder, TraceData trace, ASTType type, int typeSize, string comment = "")
        {
            // NOTE: Atm we are duplicating this check...
            if ((type == ASTBaseType.Word || type == ASTBaseType.DoubleWord) == false)
                Fail(trace, $"We don't support incrementing variables of type '{type}'!");

            switch (typeSize)
            {
                case 1:
                    builder.AppendLineWithComment($"\tinc", comment);
                    break;
                case 2:
                    builder.AppendLineWithComment($"\tlinc", comment);
                    break;
                default:
                    Fail(trace, $"Cannot increment types with size larger than 2! Type '{type}' with size '{typeSize}'");
                    break;
            }
        }

        private static void DecrementLocal(StringBuilder builder, TraceData trace, int localAddress, ASTType type, int typeSize, string comment = "")
        {
            if ((type == ASTBaseType.Word || type == ASTBaseType.DoubleWord) == false)
                Fail(trace, $"We don't support decrementing variables of type '{type}'!");

            switch (typeSize)
            {
                case 1:
                    builder.AppendLineWithComment($"\tdec {localAddress}", comment);
                    break;
                case 2:
                    builder.AppendLineWithComment($"\tldec {localAddress}", comment);
                    break;
                default:
                    Fail(trace, $"This should not happen!!!");
                    break;
            }
        }

        private static void DecrementSP(StringBuilder builder, TraceData trace, ASTType type, int typeSize, string comment = "")
        {
            // NOTE: Atm we are duplicating this check...
            if ((type == ASTBaseType.Word || type == ASTBaseType.DoubleWord) == false)
                Fail(trace, $"We don't support decrementing variables of type '{type}'!");

            switch (typeSize)
            {
                case 1:
                    builder.AppendLineWithComment($"\tdec", comment);
                    break;
                case 2:
                    builder.AppendLineWithComment($"\tldec", comment);
                    break;
                default:
                    Fail(trace, $"Cannot decrement types with size larger than 2! Type '{type}' with size '{typeSize}'");
                    break;
            }
        }
        
        private static void DuplicateSP(StringBuilder builder, TraceData trace, int typeSize, string comment = "")
        {
            switch (typeSize)
            {
                case 1:
                    builder.AppendLineWithComment($"\tdup", comment);
                    break;
                case 2:
                    builder.AppendLineWithComment($"\tldup", comment);
                    break;
                default:
                    Fail(trace, $"We can't duplicate arbitrary structs on the stack yet! This is a compiler bug!");
                    break;
            }
        }
        
        private static void SwapSP(StringBuilder builder, TraceData trace, int typeASize, int typeBSize, string comment = "")
        {
            if (typeASize <= 0 || typeASize > 2 || typeBSize <= 0 || typeBSize > 2)
                Fail(trace, $"Cannot swap values of size '{typeASize}' and '{typeBSize}'!");

            switch (typeASize)
            {
                case 1:
                    switch (typeBSize)
                    {
                        case 1:
                            builder.AppendLineWithComment($"\tswap", comment);
                            break;
                        case 2:
                            builder.AppendLineWithComment($"\tslswap", comment);
                            break;
                    }
                    break;
                case 2:
                    switch (typeBSize)
                    {
                        case 1:
                            builder.AppendLineWithComment($"\tslswap slswap", comment);
                            break;
                        case 2:
                            builder.AppendLineWithComment($"\tlswap", comment);
                            break;
                    }
                    break;
            }
        }
        
        private static void OverSP(StringBuilder builder, TraceData trace, int targetSize, int overSize, string comment = "")
        {
            if (targetSize <= 0 || targetSize > 2 || overSize <= 0 || overSize > 2)
                Fail(trace, $"Cannot over a value of size '{targetSize}' over a value of '{overSize}'!");

            switch (targetSize)
            {
                case 1:
                    switch (overSize)
                    {
                        case 1:
                            builder.AppendLineWithComment($"\tover", comment);
                            break;
                        case 2:
                            builder.AppendLineWithComment($"\tsoverl", comment);
                            break;
                    }
                    break;
                case 2:
                    switch (overSize)
                    {
                        case 1:
                            builder.AppendLineWithComment($"\tlovers", comment);
                            break;
                        case 2:
                            builder.AppendLineWithComment($"\tlover", comment);
                            break;
                    }
                    break;
                default:
                    throw new NotImplementedException($"OverSP with targetSize {targetSize} and overSize {overSize}");
            }
        }

        private static void LoadSP(StringBuilder builder, int typeSize, string comment = "")
        {
            switch (typeSize)
            {
                case 1:
                    builder.AppendLineWithComment($"\tload [SP]", comment);
                    break;
                case 2:
                    builder.AppendLineWithComment($"\tloadl [SP]", comment);
                    break;
                default:
                    builder.AppendLine($"\t; {comment} ({typeSize})");
                    builder.AppendLine($"\t[SP] ldec");
                    builder.AppendLine($"\tloadl #{typeSize}");
                    builder.AppendLineWithComment($"\tmemc", "Copy the data from the pointer to the stack");
                    builder.AppendLineWithComment($"\tladd [SP] #{typeSize}", "Set the stack pointer to after the copied data");
                    break;
            }
        }

        private static void LoadVariable(StringBuilder builder, TraceData trace, VariableRef var, TypeMap typeMap, ConstMap constMap)
        {
            int typeSize = SizeOfType(var.Type, typeMap);
            switch (var.VariableType)
            {
                case VariableType.Local:
                    switch (typeSize)
                    {
                        case 1:
                            builder.AppendLineWithComment($"\tload {var.LocalAddress}", var.Comment);
                            break;
                        case 2:
                            builder.AppendLineWithComment($"\tloadl {var.LocalAddress}", var.Comment);
                            break;
                        default:
                            builder.AppendLine($"\t; {var.Comment} ({typeSize})");
                            for (int i = 0; i < typeSize / 2; i++)
                            {
                                builder.AppendLine($"\tloadl {var.LocalAddress + (i * 2)}\t; {var.Comment}:{i * 2} {(i * 2) + 1}");
                            }

                            if (typeSize % 2 == 1)
                            {
                                builder.AppendLine($"\tload {var.LocalAddress + typeSize - 1}\t; {var.Comment}:{typeSize - 1}");
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
                    if (constMap.TryGetValue(var.ConstantName, out var constant) == false)
                        Fail(trace, $"No constant '{var.ConstantName}'! This is a compiler bug, we should not get!");
                    
                    if (constant.Value is ASTArrayLitteral)
                    {
                        builder.AppendLineWithComment($"\tload :{var.ConstantName}", var.Comment);
                    }
                    else
                    {
                        switch (typeSize)
                        {
                            case 1:
                                builder.AppendLineWithComment($"\tload #{var.ConstantName}", var.Comment);
                                break;
                            case 2:
                                builder.AppendLineWithComment($"\tloadl #{var.ConstantName}", var.Comment);
                                break;
                            default:
                                throw new NotImplementedException();
                        }
                    }
                    break;
                default:
                    Fail(trace, $"Unknown variable type '{var.VariableType}'!");
                    break;
            }
        }

        private static void StoreSP(StringBuilder builder, TraceData trace, int typeSize, string comment = "")
        {
            // It will be hard to implement storing of things larger than 2 words!
            switch (typeSize)
            {
                case 1:
                    builder.AppendLineWithComment($"\tstore [SP]", comment);
                    break;
                case 2:
                    builder.AppendLineWithComment($"\tstorel [SP]", comment);
                    break;
                default:
                    builder.AppendLine($"\t; {comment} ({typeSize})");
                    // src
                    builder.AppendLine($"\t[SP]");
                    builder.AppendLine($"\tloadl #{typeSize - 1}");
                    builder.AppendLineWithComment($"\tlsub", "Here we load the start of the struct");
                    // dest 
                    builder.AppendLine($"\tldup");
                    builder.AppendLine($"\tldec ldec");
                    builder.AppendLineWithComment($"\tloadl [SP]", "Here we load the destination pointer");
                    // len
                    builder.AppendLineWithComment($"\tloadl #{typeSize}", "Here we load the size of the struct");
                    // copy
                    builder.AppendLineWithComment($"\tmemc", "Do the copying");
                    // cleanup
                    builder.AppendLine($"\t[SP]");
                    builder.AppendLine($"\tloadl #{typeSize + 2}");
                    builder.AppendLine($"\tlsub");
                    builder.AppendLineWithComment($"\tset [SP]", "Set sp to before the loaded data and pointer");
                    break;
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
                            builder.AppendLineWithComment($"\tstore {var.LocalAddress}", var.Comment);
                            break;
                        case 2:
                            builder.AppendLineWithComment($"\tstorel {var.LocalAddress}", var.Comment);
                            break;
                        default:
                            builder.AppendLine($"\t; {var.Comment} ({typeSize})");

                            // We assume the value is on the stack
                            // Because the stack is fifo the first value on the stack will be the last value of the type.
                            if (typeSize % 2 == 1)
                            {
                                builder.AppendLine($"\tstore {var.LocalAddress + (typeSize - 1)}\t; {var.Comment}:{typeSize - 1}");
                            }

                            for (int i = (typeSize / 2) - 1; i >= 0; i--)
                            {
                                builder.AppendLine($"\tstorel {var.LocalAddress + (i * 2)}\t; {var.Comment}:{(i * 2) + 1} {i * 2}");
                            }
                            break;
                    }
                    break;
                case VariableType.Pointer:
                    // NOTE: Here we assume the pointer is already on the stack
                    StoreSP(builder, trace, typeSize, var.Comment);
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

        private static void LoadZeroes(StringBuilder builder, int zerosToLoad)
        {
            while (zerosToLoad >= 2)
            {
                builder.AppendLine($"\tloadl #0");
                zerosToLoad -= 2;
            }

            if (zerosToLoad == 1)
            {
                builder.AppendLine($"\tload #0");
            }
        }

        private static ASTFunction SpecializeFunction(TraceData trace, ASTGenericFunction GenericFunction, List<ASTType> GenericTypes, TypeMap typeMap, FunctionMap functionMap)
        {
            GenericMap GenericMap = GenerateGenericMap(trace, GenericFunction.GenericNames, GenericTypes);

            List<(ASTType type, string name)> parameters = new List<(ASTType type, string name)>(GenericFunction.Parameters.Count);
            foreach (var param in GenericFunction.Parameters)
            {
                ASTType type = SpecializeType(trace, param.Type, GenericMap);
                parameters.Add((type, param.Name));
            }

            List<ASTBlockItem> body = new List<ASTBlockItem>(GenericFunction.Body.Count);
            foreach (var blockItem in GenericFunction.Body)
            {
                body.Add(SpecializeBlockItem(trace, blockItem, GenericMap));
            }

            ASTType returnType = SpecializeType(trace, GenericFunction.ReturnType, GenericMap);

            string label = GetGenericFunctionLabel(trace, GenericFunction, GenericTypes, typeMap, functionMap);

            return new ASTFunction(GenericFunction.Trace, label, returnType, parameters, body);
        }

        private static ASTBlockItem SpecializeBlockItem(TraceData trace, ASTBlockItem blockItem, GenericMap genericMap)
        {
            switch (blockItem)
            {
                case ASTDeclaration declaration:
                    return SpecializeDeclaration(trace, declaration, genericMap);
                case ASTStatement statement:
                    return SpecializeStatement(trace, statement, genericMap);
                default:
                    // FIXME: A special fail where we can trace back to the function that specialized this func
                    Fail(blockItem.Trace, $"Unknown block item {blockItem}, this is a compiler bug!");
                    return default;
            }
        }

        private static ASTDeclaration SpecializeDeclaration(TraceData trace, ASTDeclaration declaration, GenericMap genericMap)
        {
            switch (declaration)
            {
                case ASTVariableDeclaration variableDeclaration:
                    {
                        ASTType type = SpecializeType(trace, variableDeclaration.Type, genericMap);

                        ASTExpression specializedInitializer = variableDeclaration.Initializer;
                        if (specializedInitializer != null)
                            specializedInitializer = SpecializeExpression(trace, specializedInitializer, genericMap); ;

                        return new ASTVariableDeclaration(variableDeclaration.Trace, type, variableDeclaration.VariableName, specializedInitializer);
                    }
                default:
                    Fail(declaration.Trace, $"Unknown declaration {declaration}, this is a compiler bug!");
                    return default;
            }
        }

        private static ASTStatement SpecializeOptionalStatement(TraceData trace, ASTStatement statement, GenericMap genericMap)
        {
            if (statement == null) return null;
            else return SpecializeStatement(trace, statement, genericMap);
        }

        private static ASTStatement SpecializeStatement(TraceData trace, ASTStatement statement, GenericMap genericMap)
        {
            switch (statement)
            {
                case ASTEmptyStatement emptyStatement:
                    {
                        return emptyStatement;
                    } 
                case ASTReturnStatement returnStatement:
                    {
                        var retExpr = SpecializeOptionalExpression(trace, returnStatement.ReturnValueExpression, genericMap);
                        return new ASTReturnStatement(returnStatement.Trace, retExpr);
                    }
                case ASTIfStatement ifStatement:
                    {
                        var cond = SpecializeExpression(trace, ifStatement.Condition, genericMap);
                        var ifTrue = SpecializeStatement(trace, ifStatement.IfTrue, genericMap);
                        var ifFalse = SpecializeOptionalStatement(trace, ifStatement.IfFalse, genericMap);

                        return new ASTIfStatement(ifStatement.Trace, cond, ifTrue, ifFalse);
                    }
                case ASTCompoundStatement compoundStatement:
                    {
                        List<ASTBlockItem> Block = new List<ASTBlockItem>(compoundStatement.Block.Count);
                        foreach (var blockItem in compoundStatement.Block)
                        {
                            Block.Add(SpecializeBlockItem(trace, blockItem, genericMap));
                        }

                        return new ASTCompoundStatement(compoundStatement.Trace, Block);
                    }
                case ASTExpressionStatement expressionStatement:
                    {
                        var expr = SpecializeExpression(trace, expressionStatement.Expr, genericMap);
                        return new ASTExpressionStatement(expressionStatement.Trace, expr);
                    }
                case ASTForWithDeclStatement forWithDecl:
                    {
                        var declType = SpecializeType(trace, forWithDecl.Declaration.Type, genericMap);
                        var declInit = forWithDecl.Declaration.Initializer;
                        if (declInit != null) declInit = SpecializeExpression(trace, declInit, genericMap);
                        var declaration = new ASTVariableDeclaration(forWithDecl.Declaration.Trace, declType, forWithDecl.Declaration.VariableName, declInit);

                        var condition = SpecializeExpression(trace, forWithDecl.Condition, genericMap);
                        var postExpr = SpecializeExpression(trace, forWithDecl.PostExpression, genericMap);

                        var body = SpecializeStatement(trace, forWithDecl.Body, genericMap);

                        return new ASTForWithDeclStatement(forWithDecl.Trace, declaration, condition, postExpr, body);
                    }
                case ASTWhileStatement whileStatement:
                    {
                        var cond = SpecializeExpression(trace, whileStatement.Condition, genericMap);
                        var body = SpecializeStatement(trace, whileStatement.Body, genericMap);

                        return new ASTWhileStatement(whileStatement.Trace, cond, body);
                    }
                case ASTContinueStatement continueStatement:
                    {
                        return continueStatement;
                    }
                case ASTBreakStatement breakStatement:
                    {
                        return breakStatement;
                    }
                default:
                    Fail(statement.Trace, $"Unknown statement {statement}, this is a compiler bug!");
                    return default;
            }
        }

        private static ASTExpression SpecializeOptionalExpression(TraceData trace, ASTExpression expression, GenericMap genericMap)
        {
            if (expression != null)
                return SpecializeExpression(trace, expression, genericMap);
            else
                return null;
        }

        private static ASTExpression SpecializeExpression(TraceData trace, ASTExpression expression, GenericMap genericMap)
        {
            switch (expression)
            {
                case ASTLitteral litteral:
                    // NOTE: For now we don't do copies of litterals
                    // But that means that we can't start changing the contents of
                    // ASTLitteral ast nodes
                    return litteral;
                case ASTVariableExpression variableExpr:
                    {
                        var assignmentExpr = SpecializeOptionalExpression(trace, variableExpr.AssignmentExpression, genericMap);
                        return new ASTVariableExpression(variableExpr.Trace, variableExpr.Name, assignmentExpr);
                    }
                case ASTUnaryOp unaryOp:
                    {
                        var expr = SpecializeExpression(trace, unaryOp.Expr, genericMap);

                        return new ASTUnaryOp(unaryOp.Trace, unaryOp.OperatorType, expr);
                    }
                case ASTBinaryOp binaryOp:
                    {
                        var left = SpecializeExpression(trace, binaryOp.Left, genericMap);
                        var right = SpecializeExpression(trace, binaryOp.Right, genericMap);

                        return new ASTBinaryOp(binaryOp.Trace, binaryOp.OperatorType, left, right);
                    }
                case ASTFunctionCall functionCall:
                    {
                        List<ASTExpression> arguments = new List<ASTExpression>(functionCall.Arguments.Count);
                        foreach (var arg in functionCall.Arguments)
                        {
                            arguments.Add(SpecializeExpression(trace, arg, genericMap));
                        }

                        // Specialize the generic arguments and return a generic call
                        if (functionCall is ASTGenericFunctionCall genericFunctionCall)
                        {
                            List<ASTType> genericTypes = new List<ASTType>(genericFunctionCall.GenericTypes.Count);
                            foreach (var type in genericFunctionCall.GenericTypes)
                            {
                                genericTypes.Add(SpecializeType(trace, type, genericMap));
                            }

                            return new ASTGenericFunctionCall(functionCall.Trace, functionCall.FunctionName, genericTypes, arguments);
                        }

                        return new ASTFunctionCall(functionCall.Trace, functionCall.FunctionName, arguments);
                    }
                case ASTPointerExpression pointerExpression:
                    {
                        var pointer = SpecializeExpression(trace, pointerExpression.Pointer, genericMap);
                        var offset = SpecializeExpression(trace, pointerExpression.Offset, genericMap);
                        var assignment = SpecializeOptionalExpression(trace, pointerExpression.Assignment, genericMap);
                        return new ASTPointerExpression(pointerExpression.Trace, pointer, offset, assignment);
                    }
                case ASTExplicitCast cast:
                    {
                        var from = SpecializeExpression(trace, cast.From, genericMap);
                        var to = SpecializeType(trace, cast.To, genericMap);
                        return new ASTExplicitCast(cast.Trace, from, to);
                    }
                case ASTMemberExpression memberExpression:
                    {
                        var targetExpr = SpecializeExpression(trace, memberExpression.TargetExpr, genericMap);
                        var assignment = SpecializeOptionalExpression(trace, memberExpression.Assignment, genericMap);
                        return new ASTMemberExpression(memberExpression.Trace, targetExpr, memberExpression.MemberName, assignment, memberExpression.Dereference);
                    }
                case ASTSizeofTypeExpression sizeofTypeExpression:
                    {
                        var type = SpecializeType(trace, sizeofTypeExpression.Type, genericMap);
                        return new ASTSizeofTypeExpression(sizeofTypeExpression.Trace, type);
                    }
                case ASTAddressOfExpression addressOfExpression:
                    {
                        var expr = SpecializeExpression(trace, addressOfExpression.Expr, genericMap);
                        return new ASTAddressOfExpression(addressOfExpression.Trace, expr);
                    }
                case ASTTypeOfExpression typeOfExpression:
                    {
                        var type = SpecializeType(trace, typeOfExpression.Type, genericMap);
                        return new ASTTypeOfExpression(typeOfExpression.Trace, type);
                    }
                case ASTDefaultExpression defaultExpression:
                    {
                        var type = SpecializeType(trace, defaultExpression.Type, genericMap);
                        return new ASTDefaultExpression(defaultExpression.Trace, type);
                    }
                default:
                    Fail(expression.Trace, $"Unknown expression type {expression}, this is a compiler bug!");
                    return default;
            }
        }

        private static ASTType SpecializeType(TraceData trace, ASTType type, GenericMap genericMap)
        {
            switch (type)
            {
                case ASTBaseType baseType:
                    return baseType;
                case ASTStructType structType:
                    // FIXME: When we implement generic striucts we want to fix this!!
                    return structType;
                case ASTGenericType genericType:
                    {
                        List<string> genericNamesLeft = new List<string>();
                        foreach (var name in genericType.GenericNames)
                        {
                            if (genericMap.ContainsKey(name) == false)
                                genericNamesLeft.Add(name);
                        }

                        switch (genericType.Type)
                        {
                            case ASTStructType structType:
                                {
                                    List<(ASTType Type, string Name) > members = new List<(ASTType, string)>(structType.Members.Count);
                                    foreach (var member in structType.Members)
                                    {
                                        members.Add((SpecializeType(trace, member.Type, genericMap), member.Name));
                                    }

                                    // FIXME: Naming is going to be weird and should be proper (or we should have proper type comparisons)
                                    if (genericNamesLeft.Count > 0)
                                        return new ASTGenericType(trace, new ASTStructType(structType.Trace, structType.TypeName, members), genericNamesLeft);
                                    else
                                        return new ASTStructType(structType.Trace, $"{structType.TypeName}<...>", members);
                                }
                            default:
                                Fail(genericType.Trace, $"We don't handle generic types where the underlying type is '{genericType.Type.GetType()}'");
                                return null;
                        }
                    }
                case ASTFunctionPointerType functionPointerType:
                    {
                        List<ASTType> paramTypes = new List<ASTType>(functionPointerType.ParamTypes.Count);
                        foreach (var param in functionPointerType.ParamTypes)
                        {
                            paramTypes.Add(SpecializeType(trace, param, genericMap));
                        }

                        var returnType = SpecializeType(trace, functionPointerType.ReturnType, genericMap);

                        return new ASTFunctionPointerType(functionPointerType.Trace, paramTypes, returnType);
                    }
                case ASTFixedArrayType fixedArrayType:
                    return new ASTFixedArrayType(fixedArrayType.Trace, SpecializeType(trace, fixedArrayType.BaseType, genericMap), fixedArrayType.Size);
                case ASTArrayType arrayType:
                    return new ASTArrayType(arrayType.Trace, SpecializeType(trace, arrayType.BaseType, genericMap));
                case ASTPointerType pointerType:
                    return new ASTPointerType(pointerType.Trace, SpecializeType(trace, pointerType.BaseType, genericMap));
                case ASTGenericTypeRef genericTypeRef:
                    {
                        bool anyTypeStillGeneric = false;
                        List<ASTType> genericTypes = new List<ASTType>(genericTypeRef.GenericTypes.Count);
                        foreach (var genType in genericTypeRef.GenericTypes)
                        {
                            var spezType = SpecializeType(trace, genType, genericMap);
                            genericTypes.Add(spezType);
                            if (spezType is ASTGenericType || spezType is ASTGenericTypeRef) anyTypeStillGeneric = true;
                        }

                        return new ASTGenericTypeRef(genericTypeRef.Trace, genericTypeRef.Name, genericTypes);

                        // FIXME: Idk what to do here?
                        // Wouldn't we want to output the actual specialized type here?
                        throw new NotImplementedException();

                        /*
                        List<ASTType> genericTypes = new List<ASTType>(genericTypeRef.GenericTypes.Count);
                        foreach (var genType in genericTypeRef.GenericTypes)
                        {
                            genericTypes.Add(SpecializeType(trace, genType, genericMap));
                        }

                        if (genericMap.TryGetValue(genericTypeRef.Name, out ASTType genericType))
                            return new 
                        }
                        */

                        //if (genericMap.TryGetValue(typeRef.Name, out ASTType genType))
                        //    return genType;
                        //else
                        //    return typeRef;
                    }
                case ASTTypeRef typeRef:
                    {
                        if (genericMap.TryGetValue(typeRef.Name, out ASTType genType))
                            return genType;
                        else
                            return typeRef;
                    }
                default:
                    Fail(trace, $"Unknown type '{type}', this is a compiler bug!");
                    return default;
            }
        }

        private static GenericMap GenerateGenericMap(TraceData trace, List<string> names, List<ASTType> types)
        {
            if (names.Count != types.Count)
                Fail(trace, $"Missmatching number of generic arguments! Got '{types.Count}' Expected '{types.Count}'");

            return names.Zip(types, (name, type) => (name, type)).ToDictionary(kvp => kvp.name, kvp => kvp.type);
        }
    }
}
