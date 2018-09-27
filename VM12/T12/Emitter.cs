using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;

namespace T12
{
    using ConstMap = Dictionary<string, ASTConstDirective>;

    using GlobalMap = Dictionary<string, ASTGlobalDirective>;

    using VarMap = Dictionary<string, (int Offset, ASTType Type)>;
    using VarList = List<(string Name, int Offset, ASTType Type)>;

    using TypeMap = Dictionary<string, ASTType>;

    using FunctionMap = Dictionary<string, ASTFunction>;
    
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

        private static ASTType TypeOfVariable(string variableName, VarMap varMap)
        {
            if (varMap.TryGetValue(variableName, out var varType) == false)
                Fail($"No variable called '{variableName}'!");

            return varType.Type;
        }

        private static int SizeOfType(ASTType type, TypeMap map)
        {
            if (type is ASTPointerType)
            {
                return ASTPointerType.Size;
            }
            else if (map.TryGetValue(type.TypeName, out ASTType outType))
            {
                if (outType is ASTBaseType)
                {
                    return (outType as ASTBaseType).Size;
                }
                else
                {
                    Fail("We don't support complex types atm!");
                    return default;
                }
            }
            else
            {
                Fail($"Could not find type named '{type.TypeName}'");
                return default;
            }
        }

        private static ASTType CalcReturnType(ASTExpression expression, VarMap scope, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap)
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
                    return CalcReturnType(unaryOp.Expr, scope, functionMap, constMap, globalMap);
                case ASTBinaryOp binaryOp:
                    {
                        ASTType left = CalcReturnType(binaryOp.Left, scope, functionMap, constMap, globalMap);
                        ASTType right = CalcReturnType(binaryOp.Right, scope, functionMap, constMap, globalMap);

                        // TODO!! Merge types!
                        return left;
                    }
                case ASTConditionalExpression conditional:
                    {
                        ASTType left = CalcReturnType(conditional.IfTrue, scope, functionMap, constMap, globalMap);
                        ASTType right = CalcReturnType(conditional.IfFalse, scope, functionMap, constMap, globalMap);

                        if (left != right) Fail("Differing return types!");

                        return left;
                    }
                case ASTFunctionCall functionCall:
                    {
                        if (functionMap.TryGetValue(functionCall.FunctionName, out ASTFunction function) == false)
                            Fail($"No function called '{functionCall.FunctionName}'!");

                        return function.ReturnType;
                    }
                case ASTCastExpression cast:
                    // We assume all casts will work. Because if they are in the AST they shuold work!
                    return cast.To;
                default:
                    Fail($"Unknown expression type {expression}, this is a compiler bug!");
                    break;
            }

            return default;
        }

        private static bool TryGenerateImplicitCast(ASTExpression expression, ASTType targetType, VarMap scope, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap, out ASTExpression result, out string error)
        {
            ASTType exprType = CalcReturnType(expression, scope, functionMap, constMap, globalMap);

            if (exprType == targetType)
            {
                result = expression;
                error = default;
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
            var leftType = CalcReturnType(condition.Left, scope, functionMap, constMap, globalMap);
            var rightType = CalcReturnType(condition.Right, scope, functionMap, constMap, globalMap);
            // Try and cast the right type to the left type so we can apply the binary operation.
            if (TryGenerateImplicitCast(condition.Right, leftType, scope, functionMap, constMap, globalMap, out ASTExpression typedRight, out string error) == false)
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

        private static void AppendTypedLoad(StringBuilder builder, string load_content, ASTType type, TypeMap typeMap)
        {
            int size = SizeOfType(type, typeMap);

            if (size == 1)
            {
                builder.AppendLine($"\tload {load_content}");
            }
            else if (size == 2)
            {
                builder.AppendLine($"\tloadl {load_content}");
            }
            else
            {
                Fail($"We don't support structs larger than 2 words! Got '{type}' with size {size}");
            }
        }

        private static void AppendTypedStore(StringBuilder builder, string load_content, ASTType type, TypeMap typeMap)
        {
            int size = SizeOfType(type, typeMap);

            if (size == 1)
            {
                builder.AppendLine($"\tstore {load_content}");
            }
            else if (size == 2)
            {
                builder.AppendLine($"\tstorel {load_content}");
            }
            else
            {
                Fail($"We don't support structs larger than 2 words! Got '{type}' with size {size}");
            }
        }

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
                default:
                    Fail($"Unknown directive {directive}, this is a compiler bug!");
                    break;
            }
        }

        private static void EmitFunction(StringBuilder builder, ASTFunction func, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap)
        {
            // FIXME: Do control flow analysis to check that the function returns!

            VarList VariableList = new VarList();
            FunctionConext functionConext = new FunctionConext(func.Name, func.ReturnType);
            int local_index = 0;

            builder.AppendLine($":{func.Name}");

            int param_index = builder.Length;

            // FIXME!
            //builder.AppendLine("\t0 0");

            int @params = func.Parameters.Sum(param => SizeOfType(param.Type, typeMap));

            foreach (var param in func.Parameters)
            {
                VariableList.Add((param.Name, local_index, param.Type));
                local_index += SizeOfType(param.Type, typeMap);
            }

            VarMap Scope = VariableList.ToDictionary(var => var.Name, var => (var.Offset, var.Type));

            foreach (var blockItem in func.Body)
            {
                EmitBlockItem(builder, blockItem, Scope, VariableList, ref local_index, typeMap, functionConext, LoopContext.Empty, functionMap, constMap, globalMap);
            }

            int locals = local_index;

            string params_string = string.Join(", ", func.Parameters.Select(param => $"/{param.Name} {param.Type.TypeName}"));

            string locals_string = string.Join(", ", VariableList.Skip(func.Parameters.Count).Select(var => (var.Type, var.Name)).Select(local => $"/{local.Name} {local.Type.TypeName}"));
            
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

                        int var_offset = local_index;
                        scope.Add(varName, (var_offset, variableDeclaration.Type));
                        varList.Add((varName, var_offset, variableDeclaration.Type));
                        local_index += SizeOfType(variableDeclaration.Type, typeMap);

                        if (variableDeclaration.Initializer != null)
                        {
                            var initExpr = variableDeclaration.Initializer;
                            var initType = CalcReturnType(initExpr, scope, functionMap, constMap, globalMap);

                            if (TryGenerateImplicitCast(initExpr, variableDeclaration.Type, scope, functionMap, constMap, globalMap, out ASTExpression typedExpression, out string error) == false)
                                Fail($"Cannot assign expression of type '{initType}' to variable ('{variableDeclaration.VariableName}') of type '{variableDeclaration.Type}'");
                            
                            EmitExpression(builder, typedExpression, scope, varList, typeMap, functionMap, constMap, globalMap, true);

                            AppendTypedStore(builder, $"{var_offset}\t; [{varName}]", variableDeclaration.Type, typeMap);
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
                            var retType = CalcReturnType(returnStatement.ReturnValueExpression, scope, functionMap, constMap, globalMap);

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
                                builder.AppendLine($"\tretv {retSize}");
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
                case ASTVariableExpression variable:
                    if (scope.TryGetValue(variable.Name, out var var))
                    {
                        var var_type = TypeOfVariable(variable.Name, scope);
                        int type_size = SizeOfType(var_type, typeMap);

                        if (variable.AssignmentExpression != null)
                        {
                            var expr_type = CalcReturnType(variable.AssignmentExpression, scope, functionMap, constMap, globalMap);
                            if (var_type != expr_type) Fail($"Cannot assign expression of type '{expr_type}' to variable ('{variable.Name}') of type '{var_type}'");

                            EmitExpression(builder, variable.AssignmentExpression, scope, varList, typeMap, functionMap, constMap, globalMap, true);

                            AppendTypedStore(builder, $"{var.Offset}\t; [{variable.Name}]", var_type, typeMap);
                        }

                        if (produceResult)
                        {
                            AppendTypedLoad(builder, $"{var.Offset}\t; [{variable.Name}]", var_type, typeMap);
                        }
                    }
                    else if (constMap.TryGetValue(variable.Name, out var constDirective))
                    {
                        if (variable.AssignmentExpression != null)
                            Fail($"Cannot assign to const '{variable.Name}'!");

                        if (produceResult)
                        {
                            // FIXME: This does not feel good
                            // We are comparing the type of the constant against strign
                            // So we can load a label instead of a litteral
                            if (constDirective.Value is ASTStringLitteral)
                            {
                                builder.AppendLine($"\tload {(constDirective.Value as ASTStringLitteral).Value}");
                            }
                            else
                            {
                                builder.AppendLine($"\tload #{variable.Name}");
                            }
                        }
                    }
                    else if (globalMap.TryGetValue(variable.Name, out var globalDirective))
                    {
                        if (variable.AssignmentExpression != null)
                        {
                            var var_type = globalDirective.Type;
                            var expr_type = CalcReturnType(variable.AssignmentExpression, scope, functionMap, constMap, globalMap);
                            if (var_type != expr_type) Fail($"Cannot assign expression of type '{expr_type}' to variable ('{variable.Name}') of type '{var_type}'");

                            builder.AppendLine($"\tloadl #{globalDirective.Name}");

                            EmitExpression(builder, variable.AssignmentExpression, scope, varList, typeMap, functionMap, constMap, globalMap, true);

                            builder.AppendLine($"\tstore [SP]");
                        }

                        if (produceResult)
                        {
                            builder.AppendLine($"\tloadl #{globalDirective.Name}");
                            builder.AppendLine($"\tload [SP]\t; [{variable.Name}]");
                        }
                    }
                    else
                    {
                        Fail($"Cannot use variable '{variable.Name}' before it is declared!");
                    }
                    break;
                case ASTUnaryOp unaryOp:
                    {
                        var type = CalcReturnType(unaryOp.Expr, scope, functionMap, constMap, globalMap);
                        int type_size = SizeOfType(type, typeMap);

                        // TODO: Check to see if this expression has side-effects. This way we can avoid poping at the end
                        EmitExpression(builder, unaryOp.Expr, scope, varList, typeMap, functionMap, constMap, globalMap, true);

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
                            default:
                                Fail($"Unknown unary operator type {unaryOp.OperatorType}, this is a compiler bug!");
                                break;
                        }

                        if (produceResult == false)
                        {
                            builder.AppendLine("\tpop");
                        }
                        break;
                    }
                case ASTBinaryOp binaryOp:
                    {
                        var leftType = CalcReturnType(binaryOp.Left, scope, functionMap, constMap, globalMap);
                        var rightType = CalcReturnType(binaryOp.Right, scope, functionMap, constMap, globalMap);
                        // Try and cast the right type to the left type so we can apply the binary operation.
                        if (TryGenerateImplicitCast(binaryOp.Right, leftType, scope, functionMap, constMap, globalMap, out ASTExpression typedRight, out string error) == false)
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

                        var ifTrueType = CalcReturnType(conditional.IfTrue, scope, functionMap, constMap, globalMap);
                        var ifFalseType = CalcReturnType(conditional.IfFalse, scope, functionMap, constMap, globalMap);

                        if (TryGenerateImplicitCast(conditional.IfFalse, ifTrueType, scope, functionMap, constMap, globalMap, out ASTExpression typedIfFalse, out string error) == false)
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
                            ASTType argumentType = CalcReturnType(functionCall.Arguments[i], scope, functionMap, constMap, globalMap);

                            // Try and cast the arguemnt
                            if (TryGenerateImplicitCast(functionCall.Arguments[i], targetType, scope, functionMap, constMap, globalMap, out ASTExpression typedArg, out string error) == false)
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
                        if (scope.TryGetValue(pointerExpression.Name, out var variable) == false)
                            Fail($"No variable called '{pointerExpression.Name}'");

                        if ((variable.Type is ASTPointerType) == false)
                            Fail("Cannot dereference a non-pointer type!");

                        ASTPointerType pointerType = variable.Type as ASTPointerType;

                        if (pointerType.BaseType == ASTBaseType.Void)
                            Fail("Cannot deference void pointer! Cast to a valid pointer type!");
                        
                        // Load the local variable. Here we are loading a pointer, so we know we should loadl
                        builder.AppendLine($"\tloadl {variable.Offset}\t; [{pointerExpression.Name}]");

                        var offsetType = CalcReturnType(pointerExpression.Offset, scope, functionMap, constMap, globalMap);
                        // Try to cast the offset to a dword
                        if (TryGenerateImplicitCast(pointerExpression.Offset, ASTBaseType.DoubleWord, scope, functionMap, constMap, globalMap, out ASTExpression dwordOffset, out string error) == false)
                            Fail($"Could not generate implicit cast for pointer offset of type {offsetType} to {ASTBaseType.DoubleWord}: {error}");

                        // Emit the casted offset
                        EmitExpression(builder, dwordOffset, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                        
                        int baseTypeSize = SizeOfType(pointerType.BaseType, typeMap);
                        // Multiply by pointer base type size!
                        if (baseTypeSize > 1)
                        {
                            builder.AppendLine($"\tloadl #{baseTypeSize}\t; {variable.Type} pointer size ({baseTypeSize})");

                            builder.AppendLine($"\tlmul");
                        }

                        // Add the offset to the pointer
                        builder.AppendLine($"\tladd");

                        if (pointerExpression.Assignment != null)
                        {
                            var assign_type = CalcReturnType(pointerExpression.Assignment, scope, functionMap, constMap, globalMap);

                            if (TryGenerateImplicitCast(pointerExpression.Assignment, pointerType.BaseType, scope, functionMap, constMap, globalMap, out ASTExpression typedAssign, out error) == false)
                                Fail($"Cannot assign expression of type '{assign_type}' to pointer to type '{pointerType.BaseType}'! (Implicit cast error: '{error}')");
                            
                            if (produceResult)
                            {
                                // Copy the pointer address
                                builder.AppendLine($"\tldup");
                            }

                            EmitExpression(builder, typedAssign, scope, varList, typeMap, functionMap, constMap, globalMap, true);

                            AppendTypedStore(builder, $"[SP]\t; {pointerExpression.Name}[{pointerExpression.Offset}]", pointerType, typeMap);
                        }

                        if (produceResult)
                        {
                            AppendTypedLoad(builder, $"[SP]\t; {pointerExpression.Name}[{pointerExpression.Offset}]", pointerType, typeMap);
                        }
                        break;
                    }
                case ASTPointerToVoidPointerCast cast:
                    // We really don't need to do anything as the cast is just for type-safety
                    EmitExpression(builder, cast.From, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                    break;
                case ASTImplicitCast cast:
                    {
                        if (cast.FromType.Size + 1 == cast.ToType.Size)
                        {
                            builder.AppendLine("\tload #0");
                            EmitExpression(builder, cast.From, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                        }
                        else
                        {
                            Fail($"We don't know how to cast {cast.FromType} to {cast.ToType} right now!");
                        }
                        break;
                    }
                case ASTExplicitCast cast:
                    {
                        ASTType fromType = CalcReturnType(cast.From, scope, functionMap, constMap, globalMap);
                        ASTType toType = cast.To;

                        if (TryGenerateImplicitCast(cast.From, cast.To, scope, functionMap, constMap, globalMap, out ASTExpression implicitCast, out string _))
                        {
                            // There existed an implicit cast! Use that!
                            EmitExpression(builder, implicitCast, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                        }
                        else if (cast.From is ASTDoubleWordLitteral && toType == ASTBaseType.Word)
                        {
                            // This is an optimization for dword litterals casted to words. We can just compile time truncate the litteral to 12-bits.
                            ASTDoubleWordLitteral dwordLit = cast.From as ASTDoubleWordLitteral;
                            int truncatedValue = dwordLit.IntValue & 0xFFF;
                            ASTWordLitteral wordLit = new ASTWordLitteral(truncatedValue.ToString(), truncatedValue);
                            EmitExpression(builder, wordLit, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                        }
                        else
                        {
                            // There was no implicit way to do it.
                            // How do we cast structs?

                            // TODO: Should we hardcode these casts?
                            // Atm we have them hardcoded
                            if (fromType == ASTBaseType.DoubleWord && toType == ASTBaseType.Word)
                            {
                                // This cast is easy
                                EmitExpression(builder, cast.From, scope, varList, typeMap, functionMap, constMap, globalMap, true);
                                builder.AppendLine($"\tswap pop\t; cast({toType})");
                            }
                            else
                            {
                                Fail($"There is no explicit cast from {fromType} to {toType}!");
                            }
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
