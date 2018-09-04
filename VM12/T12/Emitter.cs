using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace T12
{
    using VarMap = Dictionary<string, (int Offset, ASTType Type)>;

    using TypeMap = Dictionary<string, int>;

    public class Emitter
    {
        private static void Fail(string error)
        {
            throw new InvalidOperationException(error);
        }

        private static int StackIndex(VarMap vmap, TypeMap tmap)
        {
            // FIXME: Different variables will have different sizes
            return vmap.Sum(kvp => tmap[kvp.Value.Type.TypeName]);
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

        private static void EmitFunction(StringBuilder builder, ASTFunction func, TypeMap typeMap)
        {
            VarMap VariableMap = new VarMap();

            builder.AppendLine($":{func.Name}");

            int param_index = builder.Length;

            // FIXME!
            //builder.AppendLine("\t0 0");

            int @params = func.Parameters.Sum(kvp => typeMap[kvp.Type.TypeName]);

            foreach (var param in func.Parameters)
            {
                VariableMap.Add(param.Name, (StackIndex(VariableMap, typeMap), param.Type));
            }

            foreach (var statement in func.Body)
            {
                EmitStatment(builder, statement, VariableMap, typeMap);
            }

            int locals = StackIndex(VariableMap, typeMap);

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

        private static void EmitStatment(StringBuilder builder, ASTStatement statement, VarMap varMap, TypeMap typeMap)
        {
            switch (statement)
            {
                case ASTReturnStatement returnStatement:
                    {
                        EmitExpression(builder, returnStatement.ReturnValueExpression, varMap);
                        // FIXME: Handle the size of the return type!
                        builder.AppendLine("\tret1");
                        break;
                    }
                case ASTVariableDeclaration variableDeclaration:
                    {
                        string varName = variableDeclaration.VariableName;
                        if (varMap.ContainsKey(varName)) Fail($"Cannot declare the variable '{varName}' more than once!");

                        if (variableDeclaration.Initializer != null)
                        {
                            EmitExpression(builder, variableDeclaration.Initializer, varMap);
                            varMap.Add(varName, (StackIndex(varMap, typeMap), variableDeclaration.Type));
                            builder.AppendLine($"\tstore {varMap[varName].Offset}\t; [{varName}]");
                        }
                        break;
                    }
                case ASTVariableAssignment variableAssignment:
                    {
                        string varName = variableAssignment.VariableName;
                        EmitExpression(builder, variableAssignment.AssignmentExpression, varMap);
                        builder.AppendLine($"\tstore {varMap[varName].Offset}\t; [{varName}]");
                        break;
                    }
                default:
                    Fail($"Could not emit code for statement {statement}, this is a compiler bug!");
                    break;
            }
        }

        private static void EmitExpression(StringBuilder builder, ASTExpression expression, VarMap varMap)
        {
            switch (expression)
            {
                case ASTLitteral litteral:
                    EmitLitteral(builder, litteral);
                    break;
                case ASTVariableExpression variable:
                    if (varMap.TryGetValue(variable.VariableName, out var var) == false)
                        Fail($"Cannot use variable '{variable.VariableName}' before it is declared!");
                    builder.AppendLine($"\tload {var.Offset}\t; [{variable.VariableName}]");
                    break;
                case ASTUnaryOp unaryOp:
                    EmitExpression(builder, unaryOp.Expr, varMap);
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
                    EmitExpression(builder, binaryOp.Left, varMap);
                    EmitExpression(builder, binaryOp.Right, varMap);
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
                        default:
                            Fail($"Unknown binary operator type {binaryOp.OperatorType}, this is a compiler bug!");
                            break;
                    }
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
