using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace T12
{
    using VarMap = Dictionary<string, (int Offset, ASTType Type)>;

    using TypeMap = Dictionary<string, int>;

    public static class Emitter
    {
        private static void Fail(string error)
        {
            throw new InvalidOperationException(error);
        }
        
        public static string EmitAsem(AST ast)
        {
            StringBuilder builder = new StringBuilder();

            TypeMap typeMap = new TypeMap()
            {
                // { "void", 0 }
                { "word", 1 },
                { "bool", 1 },
                { "dword", 2 },
            };

            foreach (var func in ast.Program.Functions)
            {
                EmitFunction(builder, func, typeMap);
                builder.AppendLine();
            }

            return builder.ToString();
        }

        private static int SizeOfType(ASTType type, TypeMap map)
        {
            if (map.TryGetValue(type.TypeName, out int size))
            {
                return size;
            }
            else
            {
                Fail($"Could not find type named '{type.TypeName}'");
                return default;
            }
        }

        private static void EmitFunction(StringBuilder builder, ASTFunction func, TypeMap typeMap)
        {
            VarMap VariableMap = new VarMap();
            int local_index = 0;

            builder.AppendLine($":{func.Name}");

            int param_index = builder.Length;

            // FIXME!
            //builder.AppendLine("\t0 0");

            int @params = func.Parameters.Sum(kvp => typeMap[kvp.Type.TypeName]);

            foreach (var param in func.Parameters)
            {
                VariableMap.Add(param.Name, (local_index, param.Type));
                local_index += SizeOfType(param.Type, typeMap);
            }

            VarMap Scope = new VarMap(VariableMap);

            foreach (var blockItem in func.Body)
            {
                EmitBlockItem(builder, blockItem, Scope, VariableMap, ref local_index, typeMap);
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

        private static void EmitBlockItem(StringBuilder builder, ASTBlockItem blockItem, VarMap scope, VarMap varMap, ref int local_index, TypeMap typeMap)
        {
            switch (blockItem)
            {
                case ASTDeclaration declaration:
                    EmitDeclaration(builder, declaration, scope, varMap, ref local_index, typeMap);
                    break;
                case ASTStatement statement:
                    // @TODO: Make this cleaner, like using an imutable map or other datastructure for handling scopes
                    // Make a copy of the scope so that the statement does not modify the current scope
                    var new_scope = new VarMap(scope);
                    EmitStatment(builder, statement, new_scope, varMap, ref local_index, typeMap);
                    break;
                default:
                    Fail($"Unknown block item {blockItem}, this is a compiler bug!");
                    break;
            }
        }

        private static void EmitDeclaration(StringBuilder builder, ASTDeclaration declaration, VarMap scope, VarMap varMap, ref int local_index, TypeMap typeMap)
        {
            switch (declaration)
            {
                case ASTVariableDeclaration variableDeclaration:
                    {
                        string varName = variableDeclaration.VariableName;
                        if (scope.ContainsKey(varName)) Fail($"Cannot declare the variable '{varName}' more than once!");

                        if (variableDeclaration.Initializer != null)
                        {
                            EmitExpression(builder, variableDeclaration.Initializer, scope, varMap);

                            scope.Add(varName, (local_index, variableDeclaration.Type));
                            varMap.Add(varName, (local_index, variableDeclaration.Type));
                            local_index += SizeOfType(variableDeclaration.Type, typeMap);

                            builder.AppendLine($"\tstore {varMap[varName].Offset}\t; [{varName}]");
                        }
                        break;
                    }
                default:
                    Fail($"Unknown declaration {declaration}, this is a compiler bug!");
                    break;
            }
        }

        private static void EmitStatment(StringBuilder builder, ASTStatement statement, VarMap scope, VarMap varMap, ref int local_index, TypeMap typeMap)
        {
            switch (statement)
            {
                case ASTEmptyStatement _:
                    builder.AppendLine("\tnop");
                    break;
                case ASTReturnStatement returnStatement:
                    {
                        EmitExpression(builder, returnStatement.ReturnValueExpression, scope, varMap);
                        // FIXME: Handle the size of the return type!
                        builder.AppendLine("\tret1");
                        break;
                    }
                case ASTAssignmentStatement assignment:
                    {
                        string varName = assignment.VariableNames[0];
                        EmitExpression(builder, assignment.AssignmentExpression, scope, varMap);
                        builder.AppendLine($"\tstore {scope[varName].Offset}\t; [{varName}]");
                        break;
                    }
                case ASTIfStatement ifStatement:
                    {
                        EmitExpression(builder, ifStatement.Condition, scope, varMap);
                        // FIXME: There could be hash collisions!
                        
                        int hash = ifStatement.GetHashCode();
                        if (ifStatement.IfFalse == null)
                        {
                            // If-statement without else
                            // builder.AppendLine($"\t; If {ifStatement.Condition.GetType()} ({hash})");
                            builder.AppendLine($"\tjz :post_{hash}");
                            //builder.AppendLine($"\t:if_{hash}");
                            EmitStatment(builder, ifStatement.IfTrue, scope, varMap, ref local_index, typeMap);
                            builder.AppendLine($"\t:post_{hash}");
                        }
                        else
                        {
                            // If-statement with else
                            // builder.AppendLine($"\t; If Else {ifStatement.Condition.GetType()} ({hash})");
                            builder.AppendLine($"\tjz :else_{hash}");
                            //builder.AppendLine($"\t:if_{hash}");
                            EmitStatment(builder, ifStatement.IfTrue, scope, varMap, ref local_index, typeMap);
                            builder.AppendLine($"\tjmp :post_{hash}");
                            builder.AppendLine($"\t:else_{hash}");
                            EmitStatment(builder, ifStatement.IfFalse, scope, varMap, ref local_index, typeMap);
                            builder.AppendLine($"\t:post_{hash}");
                        }

                    }
                    break;
                case ASTCompoundStatement compoundStatement:
                    foreach (var blockItem in compoundStatement.Block)
                    {
                        EmitBlockItem(builder, blockItem, scope, varMap, ref local_index, typeMap);
                    }
                    break;
                case ASTExpressionStatement expression:
                    EmitExpression(builder, expression.Expr, scope, varMap);
                    break;
                case ASTForWithDeclStatement forWithDecl:
                    {
                        int hash = forWithDecl.GetHashCode();

                        builder.AppendLine($"\t; For loop {forWithDecl.Condition.GetType()} {hash}");

                        VarMap new_scope = new VarMap(scope);
                        EmitDeclaration(builder, forWithDecl.Declaration, new_scope, varMap, ref local_index, typeMap);

                        builder.AppendLine($"\t:for_cond_{hash}");
                        EmitExpression(builder, forWithDecl.Condition, new_scope, varMap);
                        builder.AppendLine($"\tjz :for_end_{hash}");

                        EmitStatment(builder, forWithDecl.Body, new_scope, varMap, ref local_index, typeMap);

                        EmitExpression(builder, forWithDecl.PostExpression, new_scope, varMap);

                        builder.AppendLine($"\tjmp :for_cond_{hash}");
                        builder.AppendLine($"\t:for_end_{hash}");

                        break;
                    }
                default:
                    Fail($"Could not emit code for statement {statement}, this is a compiler bug!");
                    break;
            }
        }

        private static void EmitExpression(StringBuilder builder, ASTExpression expression, VarMap scope, VarMap varMap)
        {
            switch (expression)
            {
                case ASTLitteral litteral:
                    EmitLitteral(builder, litteral);
                    break;
                case ASTVariableExpression variable:
                    if (scope.TryGetValue(variable.VariableName, out var var) == false)
                        Fail($"Cannot use variable '{variable.VariableName}' before it is declared!");

                    if (variable.AssignmentExpression != null)
                    {
                        EmitExpression(builder, variable.AssignmentExpression, scope, varMap);
                        builder.AppendLine($"\tstore {var.Offset}\t; [{variable.VariableName}]");

                        // FIXME: Don't do this if not nessesary
                        builder.AppendLine($"\tload {var.Offset}\t; [{variable.VariableName}]");
                    }
                    else
                    {
                        builder.AppendLine($"\tload {var.Offset}\t; [{variable.VariableName}]");
                    }
                    break;
                case ASTUnaryOp unaryOp:
                    EmitExpression(builder, unaryOp.Expr, scope, varMap);
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
                    EmitExpression(builder, binaryOp.Left, scope, varMap);
                    EmitExpression(builder, binaryOp.Right, scope, varMap);
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
                    int hash = conditional.GetHashCode();
                    // builder.AppendLine($"\t; Ternary {conditional.Condition.GetType()} ({hash})");
                    EmitExpression(builder, conditional.Condition, scope, varMap);
                    builder.AppendLine($"\tjz :else_cond_{hash}");
                    builder.AppendLine($"\t:if_cond_{hash}");
                    EmitExpression(builder, conditional.IfTrue, scope, varMap);
                    builder.AppendLine($"\tjmp :post_cond_{hash}");
                    builder.AppendLine($"\t:else_cond_{hash}");
                    EmitExpression(builder, conditional.IfFalse, scope, varMap);
                    builder.AppendLine($"\t:post_cond_{hash}");
                    break;
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
                default:
                    Fail($"Unknown litteral type {litteral}, this is a compiler bug!");
                    break;
            }
        }
    }
}
