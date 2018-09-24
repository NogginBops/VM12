using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;

namespace T12
{
    using ConstMap = Dictionary<string, ASTConstDirective>;

    using VarMap = Dictionary<string, (int Offset, ASTType Type)>;

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

        private static ASTType CalcReturnType(ASTExpression expression, VarMap scope, FunctionMap functionMap, ConstMap constMap)
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
                        
                        Fail($"Could not find variable called '{variableExpression.Name}'!");
                        break;
                    }
                case ASTUnaryOp unaryOp:
                    return CalcReturnType(unaryOp.Expr, scope, functionMap, constMap);
                case ASTBinaryOp binaryOp:
                    {
                        ASTType left = CalcReturnType(binaryOp.Left, scope, functionMap, constMap);
                        ASTType right = CalcReturnType(binaryOp.Right, scope, functionMap, constMap);

                        // TODO!! Merge types!
                        return left;
                    }
                case ASTConditionalExpression conditional:
                    {
                        ASTType left = CalcReturnType(conditional.IfTrue, scope, functionMap, constMap);
                        ASTType right = CalcReturnType(conditional.IfFalse, scope, functionMap, constMap);

                        if (left != right) Fail("Differing return types!");

                        return left;
                    }
                case ASTFunctionCall functionCall:
                    {
                        if (functionMap.TryGetValue(functionCall.FunctionName, out ASTFunction function) == false)
                            Fail($"No function called '{functionCall.FunctionName}'!");

                        return function.ReturnType;
                    }
                default:
                    Fail($"Unknown expression type {expression}, this is a compiler bug!");
                    break;
            }

            return default;
        }

        public static string EmitAsem(AST ast)
        {
            StringBuilder builder = new StringBuilder();
            
            TypeMap typeMap = ASTBaseType.BaseTypeMap.ToDictionary(kvp => kvp.Key, kvp => (ASTType) kvp.Value);

            ConstMap constMap = new ConstMap();

            FunctionMap functionMap = ast.Program.Functions.ToDictionary(func => func.Name, func => func);
            
            foreach (var directive in ast.Program.Directives)
            {
                EmitDirective(builder, directive, functionMap, constMap);
            }

            builder.AppendLine();

            foreach (var func in ast.Program.Functions)
            {
                EmitFunction(builder, func, typeMap, functionMap, constMap);
                builder.AppendLine();
            }

            return builder.ToString();
        }
        
        private static void EmitDirective(StringBuilder builder, ASTDirective directive, FunctionMap functionMap, ConstMap constMap)
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
                        // TODO: Resolve constant value
                        // TODO: Resolve conflicts
                        constMap[constDirective.Name] = constDirective;
                        // FIXME: Proper constant folding!!!!!!!
                        builder.AppendLine($"<{constDirective.Name} = {(constDirective.Value as ASTLitteral).Value}>");
                        break;
                    }
                default:
                    Fail($"Unknown directive {directive}, this is a compiler bug!");
                    break;
            }
        }

        private static void EmitFunction(StringBuilder builder, ASTFunction func, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap)
        {
            VarMap VariableMap = new VarMap();
            FunctionConext functionConext = new FunctionConext(func.Name, func.ReturnType);
            int local_index = 0;

            builder.AppendLine($":{func.Name}");

            int param_index = builder.Length;

            // FIXME!
            //builder.AppendLine("\t0 0");

            int @params = func.Parameters.Sum(param => SizeOfType(param.Type, typeMap));

            foreach (var param in func.Parameters)
            {
                VariableMap.Add(param.Name, (local_index, param.Type));
                local_index += SizeOfType(param.Type, typeMap);
            }

            VarMap Scope = new VarMap(VariableMap);

            foreach (var blockItem in func.Body)
            {
                EmitBlockItem(builder, blockItem, Scope, VariableMap, ref local_index, typeMap, functionConext, LoopContext.Empty, functionMap, constMap);
            }

            int locals = local_index;

            string params_string = string.Join(", ", func.Parameters.Select(param => $"/{param.Name} {param.Type.TypeName}"));

            string locals_string = string.Join(", ", VariableMap.Select(kvp => ((ASTType Type, string Name))(kvp.Value.Type, kvp.Key)).Except(func.Parameters).Select(local => $"/{local.Name} {local.Type.TypeName}"));
            
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

        private static void EmitBlockItem(StringBuilder builder, ASTBlockItem blockItem, VarMap scope, VarMap varMap, ref int local_index, TypeMap typeMap, FunctionConext functionConext, LoopContext loopContext, FunctionMap functionMap, ConstMap constMap)
        {
            switch (blockItem)
            {
                case ASTDeclaration declaration:
                    EmitDeclaration(builder, declaration, scope, varMap, ref local_index, typeMap, functionMap, constMap);
                    break;
                case ASTStatement statement:
                    // @TODO: Make this cleaner, like using an imutable map or other datastructure for handling scopes
                    // Make a copy of the scope so that the statement does not modify the current scope
                    var new_scope = new VarMap(scope);
                    EmitStatement(builder, statement, new_scope, varMap, ref local_index, typeMap, functionConext, loopContext, functionMap, constMap);
                    break;
                default:
                    Fail($"Unknown block item {blockItem}, this is a compiler bug!");
                    break;
            }
        }

        private static void EmitDeclaration(StringBuilder builder, ASTDeclaration declaration, VarMap scope, VarMap varMap, ref int local_index, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap)
        {
            switch (declaration)
            {
                case ASTVariableDeclaration variableDeclaration:
                    {
                        string varName = variableDeclaration.VariableName;
                        if (scope.ContainsKey(varName)) Fail($"Cannot declare the variable '{varName}' more than once!");

                        scope.Add(varName, (local_index, variableDeclaration.Type));
                        varMap.Add(varName, (local_index, variableDeclaration.Type));
                        local_index += SizeOfType(variableDeclaration.Type, typeMap);

                        if (variableDeclaration.Initializer != null)
                        {
                            var initExpr = variableDeclaration.Initializer;
                            var initType = CalcReturnType(initExpr, scope, functionMap, constMap);
                            if (initType != variableDeclaration.Type) Fail($"Cannot assign expression of type '{initType}' to variable ('{variableDeclaration.VariableName}') of type '{variableDeclaration.Type}'");

                            EmitExpression(builder, initExpr, scope, varMap, typeMap, functionMap, constMap);
                            
                            builder.AppendLine($"\tstore {varMap[varName].Offset}\t; [{varName}]");
                        }
                        break;
                    }
                default:
                    Fail($"Unknown declaration {declaration}, this is a compiler bug!");
                    break;
            }
        }

        private static void EmitStatement(StringBuilder builder, ASTStatement statement, VarMap scope, VarMap varMap, ref int local_index, TypeMap typeMap, FunctionConext functionConext, LoopContext loopContext, FunctionMap functionMap, ConstMap constMap)
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
                            var ret_type = CalcReturnType(returnStatement.ReturnValueExpression, scope, functionMap, constMap);

                            if (ret_type != functionConext.ReturnType) Fail($"Cannot return expression of type '{ret_type}' in a function that returns type '{functionConext.ReturnType}'");

                            EmitExpression(builder, returnStatement.ReturnValueExpression, scope, varMap, typeMap, functionMap, constMap);
                            // FIXME: Handle the size of the return type!
                            builder.AppendLine("\tret1");
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
                        string varName = assignment.VariableNames[0];
                        EmitExpression(builder, assignment.AssignmentExpression, scope, varMap, typeMap, functionMap, constMap);
                        builder.AppendLine($"\tstore {scope[varName].Offset}\t; [{varName}]");
                        break;
                    }
                case ASTIfStatement ifStatement:
                    {
                        EmitExpression(builder, ifStatement.Condition, scope, varMap, typeMap, functionMap, constMap);
                        // FIXME: There could be hash collisions!
                        
                        int hash = ifStatement.GetHashCode();
                        if (ifStatement.IfFalse == null)
                        {
                            // If-statement without else
                            builder.AppendLine($"\tjz :post_{hash}");
                            EmitStatement(builder, ifStatement.IfTrue, scope, varMap, ref local_index, typeMap, functionConext, loopContext, functionMap, constMap);
                            builder.AppendLine($"\t:post_{hash}");
                        }
                        else
                        {
                            // If-statement with else
                            builder.AppendLine($"\tjz :else_{hash}");
                            EmitStatement(builder, ifStatement.IfTrue, scope, varMap, ref local_index, typeMap, functionConext, loopContext, functionMap, constMap);
                            builder.AppendLine($"\tjmp :post_{hash}");
                            builder.AppendLine($"\t:else_{hash}");
                            EmitStatement(builder, ifStatement.IfFalse, scope, varMap, ref local_index, typeMap, functionConext, loopContext, functionMap, constMap);
                            builder.AppendLine($"\t:post_{hash}");
                        }

                        break;
                    }
                case ASTCompoundStatement compoundStatement:
                    foreach (var blockItem in compoundStatement.Block)
                    {
                        EmitBlockItem(builder, blockItem, scope, varMap, ref local_index, typeMap, functionConext, loopContext, functionMap, constMap);
                    }
                    break;
                case ASTExpressionStatement expression:
                    EmitExpression(builder, expression.Expr, scope, varMap, typeMap, functionMap, constMap);
                    break;
                case ASTForWithDeclStatement forWithDecl:
                    {
                        int hash = forWithDecl.GetHashCode();
                        LoopContext newLoopContext = new LoopContext($":post_statement_{hash}", $":for_end_{hash}");

                        builder.AppendLine($"\t; For loop {forWithDecl.Condition} {hash}");
                        
                        VarMap new_scope = new VarMap(scope);
                        EmitDeclaration(builder, forWithDecl.Declaration, new_scope, varMap, ref local_index, typeMap, functionMap, constMap);

                        builder.AppendLine($"\t:for_cond_{hash}");
                        EmitExpression(builder, forWithDecl.Condition, new_scope, varMap, typeMap, functionMap, constMap);
                        builder.AppendLine($"\tjz {newLoopContext.EndLabel}");

                        EmitStatement(builder, forWithDecl.Body, new_scope, varMap, ref local_index, typeMap, functionConext, newLoopContext, functionMap, constMap);

                        builder.AppendLine($"\t{newLoopContext.ContinueLabel}");
                        EmitExpression(builder, forWithDecl.PostExpression, new_scope, varMap, typeMap, functionMap, constMap);

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

                        EmitExpression(builder, whileStatement.Condition, scope, varMap, typeMap, functionMap, constMap);
                        builder.AppendLine($"\tjz {newLoopContext.EndLabel}");

                        EmitStatement(builder, whileStatement.Body, scope, varMap, ref local_index, typeMap, functionConext, loopContext, functionMap, constMap);

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

                        EmitStatement(builder, doWhile.Body, scope, varMap, ref local_index, typeMap, functionConext, newLoopContext, functionMap, constMap);

                        builder.AppendLine($"\t{newLoopContext.ContinueLabel}");
                        EmitExpression(builder, doWhile.Condition, scope, varMap, typeMap, functionMap, constMap);
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
                default:
                    Fail($"Could not emit code for statement {statement}, this is a compiler bug!");
                    break;
            }
        }

        private static void EmitExpression(StringBuilder builder, ASTExpression expression, VarMap scope, VarMap varMap, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap)
        {
            switch (expression)
            {
                case ASTLitteral litteral:
                    EmitLitteral(builder, litteral);
                    break;
                case ASTVariableExpression variable:
                    if (scope.TryGetValue(variable.Name, out var var))
                    {
                        if (variable.AssignmentExpression != null)
                        {
                            var var_type = TypeOfVariable(variable.Name, scope);
                            var expr_type = CalcReturnType(variable.AssignmentExpression, scope, functionMap, constMap);
                            if (var_type != expr_type) Fail($"Cannot assign expression of type '{expr_type}' to variable ('{variable.Name}') of type '{var_type}'");

                            EmitExpression(builder, variable.AssignmentExpression, scope, varMap, typeMap, functionMap, constMap);
                            builder.AppendLine($"\tstore {var.Offset}\t; [{variable.Name}]");

                            // FIXME: Don't do this if not nessesary
                            builder.AppendLine($"\tload {var.Offset}\t; [{variable.Name}]");
                        }
                        else
                        {
                            builder.AppendLine($"\tload {var.Offset}\t; [{variable.Name}]");
                        }
                    }
                    else if (constMap.TryGetValue(variable.Name, out var constDirective))
                    {
                        if (variable.AssignmentExpression != null)
                            Fail($"Cannot assign to const '{variable.Name}'!");

                        builder.AppendLine($"\tload #{variable.Name}");
                    }
                    else
                    {
                        Fail($"Cannot use variable '{variable.Name}' before it is declared!");
                    }
                    break;
                case ASTUnaryOp unaryOp:
                    EmitExpression(builder, unaryOp.Expr, scope, varMap, typeMap, functionMap, constMap);
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
                    break;
                case ASTBinaryOp binaryOp:
                    EmitExpression(builder, binaryOp.Left, scope, varMap, typeMap, functionMap, constMap);
                    EmitExpression(builder, binaryOp.Right, scope, varMap, typeMap, functionMap, constMap);
                    // FIXME: Consider the size of the result of the expression
                    switch (binaryOp.OperatorType)
                    {
                        case ASTBinaryOp.BinaryOperatorType.Addition:
                            builder.AppendLine("\tadd");
                            break;
                        case ASTBinaryOp.BinaryOperatorType.Subtraction:
                            builder.AppendLine("\tsub");
                            break;
                        case ASTBinaryOp.BinaryOperatorType.Multiplication:
                            builder.AppendLine("\tmul");
                            break;
                        case ASTBinaryOp.BinaryOperatorType.Division:
                            builder.AppendLine("\tdiv");
                            break;
                        case ASTBinaryOp.BinaryOperatorType.Modulo:
                            builder.AppendLine("\tmod");
                            break;
                        case ASTBinaryOp.BinaryOperatorType.Bitwise_And:
                            builder.AppendLine("\tand");
                            break;
                        case ASTBinaryOp.BinaryOperatorType.Bitwise_Or:
                            builder.AppendLine("\tor");
                            break;
                        case ASTBinaryOp.BinaryOperatorType.Bitwise_Xor:
                            builder.AppendLine("\txor");
                            break;
                        case ASTBinaryOp.BinaryOperatorType.Equal:
                            // TODO: Better handling?
                            builder.AppendLine("\txor ; Equals");
                            break;
                        case ASTBinaryOp.BinaryOperatorType.Less_than:
                            // FIXME: This is really inefficient
                            builder.AppendLine("\tswap ; Less than");
                            builder.AppendLine("\tsub");
                            builder.AppendLine("\tload #0");
                            builder.AppendLine("\tswap");
                            builder.AppendLine("\tload #1");
                            builder.AppendLine("\tswap");
                            builder.AppendLine("\tselgz ; If b - a > 0 signed");
                            break;
                        case ASTBinaryOp.BinaryOperatorType.Greater_than:
                            // FIXME: This is really inefficient
                            builder.AppendLine("\tsub ; Greater than");
                            builder.AppendLine("\tload #0");
                            builder.AppendLine("\tswap");
                            builder.AppendLine("\tload #1");
                            builder.AppendLine("\tswap");
                            builder.AppendLine("\tselgz ; If b - a > 0 signed");
                            break;
                        default:
                            Fail($"Unknown binary operator type {binaryOp.OperatorType}, this is a compiler bug!");
                            break;
                    }
                    break;
                case ASTConditionalExpression conditional:
                    {
                        int hash = conditional.GetHashCode();
                        // builder.AppendLine($"\t; Ternary {conditional.Condition.GetType()} ({hash})");
                        EmitExpression(builder, conditional.Condition, scope, varMap, typeMap, functionMap, constMap);
                        builder.AppendLine($"\tjz :else_cond_{hash}");
                        builder.AppendLine($"\t:if_cond_{hash}");
                        EmitExpression(builder, conditional.IfTrue, scope, varMap, typeMap, functionMap, constMap);
                        builder.AppendLine($"\tjmp :post_cond_{hash}");
                        builder.AppendLine($"\t:else_cond_{hash}");
                        EmitExpression(builder, conditional.IfFalse, scope, varMap, typeMap, functionMap, constMap);
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
                            var argType = CalcReturnType(functionCall.Arguments[i], scope, functionMap, constMap);
                            if (argType != function.Parameters[i].Type)
                                Fail($"Missmatching types on parameter {function.Parameters[i].Name} ({i}), expected '{function.Parameters[i].Type}' got '{argType}'!");
                        }

                        if (functionCall.Arguments.Count > 0)
                            builder.AppendLine($"\t; Args to function call ::{functionCall.FunctionName} {hash}");
                        
                        // This means adding a result type to expressions
                        foreach (var arg in functionCall.Arguments)
                        {
                            EmitExpression(builder, arg, scope, varMap, typeMap, functionMap, constMap);
                        }

                        builder.AppendLine($"\t::{function.Name}");
                        
                        break;
                    }
                case ASTPointerExpression pointerExpression:
                    {
                        if (scope.TryGetValue(pointerExpression.Name, out var variable) == false)
                            Fail($"No variable called '{pointerExpression.Name}'");

                        if ((variable.Type is ASTPointerType) == false)
                            Fail("Cannot dereference a non-pointer type!");
                        
                        builder.AppendLine($"\tloadl {variable.Offset}\t; [{pointerExpression.Name}]");

                        int baseTypeSize = SizeOfType((variable.Type as ASTPointerType).BaseType, typeMap);
                        
                        // FIXME!!! Try to cast to dword
                        var offset_type = CalcReturnType(pointerExpression.Offset, scope, functionMap, constMap);
                        if (offset_type != ASTBaseType.Word)
                            Fail($"Can only index pointer with type {ASTBaseType.DoubleWord}");

                        EmitExpression(builder, pointerExpression.Offset, scope, varMap, typeMap, functionMap, constMap);
                        
                        if (baseTypeSize > 1)
                        {
                            builder.AppendLine($"\tloadl #{baseTypeSize}\t; {variable.Type} pointer size ({baseTypeSize})");

                            builder.AppendLine($"\tlmul");
                        }

                        builder.AppendLine($"\tladd");

                        if (pointerExpression.Assignment != null)
                        {
                            var assign_type = CalcReturnType(pointerExpression.Assignment, scope, functionMap, constMap);

                            if (assign_type != (variable.Type as ASTPointerType).BaseType)
                                Fail($"Cannot assign expression of type '{assign_type}' to pointer of type '{variable.Type}'!");

                            // Copy the pointer address
                            builder.AppendLine($"\tldup");

                            EmitExpression(builder, pointerExpression.Assignment, scope, varMap, typeMap, functionMap, constMap);

                            builder.AppendLine($"\tstore [SP]\t; {pointerExpression.Name}[{pointerExpression.Offset}]");
                        }

                        // TODO: We only support word pointer atm..
                        builder.AppendLine($"\tload [SP]\t; {pointerExpression.Name}[{pointerExpression.Offset}]");

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
                case ASTBoolLitteral boolLitteral:
                    // NOTE: Should we load the constants instead?
                    builder.AppendLine($"\tload #{(boolLitteral == ASTBoolLitteral.True ? 1 : 0)}\t; {(boolLitteral == ASTBoolLitteral.True ? "true" : "false")}");
                    break;
                default:
                    Fail($"Unknown litteral type {litteral}, this is a compiler bug!");
                    break;
            }
        }
    }
}
