using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;

namespace T12
{
    using VarMap = Dictionary<string, (int Offset, ASTType Type)>;

    using TypeMap = Dictionary<string, Type>;

    using FunctionMap = Dictionary<string, ASTFunction>;

    public enum BaseTypes
    {
        Word,
        Double_Word,
        Boolean,
    }
    
    public abstract class Type
    {
        public abstract string Name { get; }

        public abstract int Size { get; }
    }

    // NOTE: Is this the right way to model void?
    public class VoidType : Type
    {
        public static readonly VoidType Void = new VoidType();

        public override string Name => "void";
        public override int Size => 0;
    }
    
    public class BaseType : Type
    {
        public static List<Type> BaseTypes = new List<Type>();

        static BaseType()
        {
            // Generate all base types
            foreach (BaseTypes type in Enum.GetValues(typeof(BaseTypes)))
            {
                BaseTypes.Add(new BaseType(type));
            }
        }

        public static string GetBaseTypeName(BaseTypes type)
        {
            switch (type)
            {
                case T12.BaseTypes.Word:
                    return "word";
                case T12.BaseTypes.Double_Word:
                    return "dword";
                case T12.BaseTypes.Boolean:
                    return "bool";
                default:
                    throw new ArgumentException($"Unknown base type '{type}'!");
            }
        }

        public static int GetBaseTypeSize(BaseTypes type)
        {
            switch (type)
            {
                case T12.BaseTypes.Word:
                    return 1;
                case T12.BaseTypes.Double_Word:
                    return 2;
                case T12.BaseTypes.Boolean:
                    return 1;
                default:
                    throw new ArgumentException($"Unknown base type '{type}'!");
            }
        }

        public readonly BaseTypes Type;

        public override string Name => GetBaseTypeName(Type);

        public override int Size => GetBaseTypeSize(Type);

        public BaseType(BaseTypes type)
        {
            Type = type;
        }
}
    
    public class StructType : Type
    {
        public struct Member
        {
            public readonly Type Type;
            public readonly string Name;

            public Member(Type type, string name)
            {
                Type = type;
                Name = name;
            }
        }

        public readonly string name;
        public readonly List<Member> Members;
        private readonly int size;

        public override string Name => name;
        public override int Size => size;

        public StructType(string name, List<Member> members)
        {
            this.name = name;
            this.Members = members;

            size  = this.Members.Select(m => m.Type.Size).Sum();
        }
    }

    public class PointerType : Type
    {
        public readonly Type BaseType;

        public override string Name => $"{BaseType.Name}*";
        // A pointer is always 2 words
        public override int Size => 2;

        public PointerType(Type baseType)
        {
            BaseType = baseType;
        }
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
            if (map.TryGetValue(type.TypeName, out Type outType))
            {
                return outType.Size;
            }
            else
            {
                Fail($"Could not find type named '{type.TypeName}'");
                return default;
            }
        }

        private static ASTType CalcReturnType(ASTExpression expression, VarMap scope, FunctionMap functionMap)
        {
            switch (expression)
            {
                case ASTLitteral litteral:
                    return litteral.Type;
                case ASTVariableExpression variableExpression:
                    {
                        if (scope.TryGetValue(variableExpression.VariableName, out var varType) == false)
                            Fail($"Could not find variable called '{variableExpression.VariableName}'!");

                        return varType.Type;
                    }
                case ASTUnaryOp unaryOp:
                    return CalcReturnType(unaryOp.Expr, scope, functionMap);
                case ASTBinaryOp binaryOp:
                    {
                        ASTType left = CalcReturnType(binaryOp.Left, scope, functionMap);
                        ASTType right = CalcReturnType(binaryOp.Right, scope, functionMap);

                        // TODO!! Merge types!
                        return left;
                    }
                case ASTConditionalExpression conditional:
                    {
                        ASTType left = CalcReturnType(conditional.IfTrue, scope, functionMap);
                        ASTType right = CalcReturnType(conditional.IfFalse, scope, functionMap);

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

            TypeMap typeMap = BaseType.BaseTypes.ToDictionary(type => type.Name, type => type);

            FunctionMap functionMap = ast.Program.Functions.ToDictionary(func => func.Name, func => func);

            foreach (var directive in ast.Program.Directives)
            {
                EmitDirective(builder, directive, functionMap);
            }

            builder.AppendLine();

            foreach (var func in ast.Program.Functions)
            {
                EmitFunction(builder, func, typeMap, functionMap);
                builder.AppendLine();
            }

            return builder.ToString();
        }
        
        private static void EmitDirective(StringBuilder builder, ASTDirective directive, FunctionMap functionMap)
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
                default:
                    Fail($"Unknown directive {directive}, this is a compiler bug!");
                    break;
            }
        }

        private static void EmitFunction(StringBuilder builder, ASTFunction func, TypeMap typeMap, FunctionMap functionMap)
        {
            VarMap VariableMap = new VarMap();
            FunctionConext functionConext = new FunctionConext(func.Name, func.ReturnType);
            int local_index = 0;

            builder.AppendLine($":{func.Name}");

            int param_index = builder.Length;

            // FIXME!
            //builder.AppendLine("\t0 0");

            int @params = func.Parameters.Sum(kvp => typeMap[kvp.Type.TypeName].Size);

            foreach (var param in func.Parameters)
            {
                VariableMap.Add(param.Name, (local_index, param.Type));
                local_index += SizeOfType(param.Type, typeMap);
            }

            VarMap Scope = new VarMap(VariableMap);

            foreach (var blockItem in func.Body)
            {
                EmitBlockItem(builder, blockItem, Scope, VariableMap, ref local_index, typeMap, functionConext, LoopContext.Empty, functionMap);
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

        private static void EmitBlockItem(StringBuilder builder, ASTBlockItem blockItem, VarMap scope, VarMap varMap, ref int local_index, TypeMap typeMap, FunctionConext functionConext, LoopContext loopContext, FunctionMap functionMap)
        {
            switch (blockItem)
            {
                case ASTDeclaration declaration:
                    EmitDeclaration(builder, declaration, scope, varMap, ref local_index, typeMap, functionMap);
                    break;
                case ASTStatement statement:
                    // @TODO: Make this cleaner, like using an imutable map or other datastructure for handling scopes
                    // Make a copy of the scope so that the statement does not modify the current scope
                    var new_scope = new VarMap(scope);
                    EmitStatement(builder, statement, new_scope, varMap, ref local_index, typeMap, functionConext, loopContext, functionMap);
                    break;
                default:
                    Fail($"Unknown block item {blockItem}, this is a compiler bug!");
                    break;
            }
        }

        private static void EmitDeclaration(StringBuilder builder, ASTDeclaration declaration, VarMap scope, VarMap varMap, ref int local_index, TypeMap typeMap, FunctionMap functionMap)
        {
            switch (declaration)
            {
                case ASTVariableDeclaration variableDeclaration:
                    {
                        string varName = variableDeclaration.VariableName;
                        if (scope.ContainsKey(varName)) Fail($"Cannot declare the variable '{varName}' more than once!");

                        if (variableDeclaration.Initializer != null)
                        {
                            var initExpr = variableDeclaration.Initializer;
                            var initType = CalcReturnType(initExpr, scope, functionMap);
                            if (initType != variableDeclaration.Type) Fail($"Cannot assign expression of type '{initType}' to variable ('{variableDeclaration.VariableName}') of type '{variableDeclaration.Type}'");

                            EmitExpression(builder, initExpr, scope, varMap, functionMap);

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

        private static void EmitStatement(StringBuilder builder, ASTStatement statement, VarMap scope, VarMap varMap, ref int local_index, TypeMap typeMap, FunctionConext functionConext, LoopContext loopContext, FunctionMap functionMap)
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
                            var ret_type = CalcReturnType(returnStatement.ReturnValueExpression, scope, functionMap);

                            if (ret_type != functionConext.ReturnType) Fail($"Cannot return expression of type '{ret_type}' in a function that returns type '{functionConext.ReturnType}'");

                            EmitExpression(builder, returnStatement.ReturnValueExpression, scope, varMap, functionMap);
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
                        EmitExpression(builder, assignment.AssignmentExpression, scope, varMap, functionMap);
                        builder.AppendLine($"\tstore {scope[varName].Offset}\t; [{varName}]");
                        break;
                    }
                case ASTIfStatement ifStatement:
                    {
                        EmitExpression(builder, ifStatement.Condition, scope, varMap, functionMap);
                        // FIXME: There could be hash collisions!
                        
                        int hash = ifStatement.GetHashCode();
                        if (ifStatement.IfFalse == null)
                        {
                            // If-statement without else
                            builder.AppendLine($"\tjz :post_{hash}");
                            EmitStatement(builder, ifStatement.IfTrue, scope, varMap, ref local_index, typeMap, functionConext, loopContext, functionMap);
                            builder.AppendLine($"\t:post_{hash}");
                        }
                        else
                        {
                            // If-statement with else
                            builder.AppendLine($"\tjz :else_{hash}");
                            EmitStatement(builder, ifStatement.IfTrue, scope, varMap, ref local_index, typeMap, functionConext, loopContext, functionMap);
                            builder.AppendLine($"\tjmp :post_{hash}");
                            builder.AppendLine($"\t:else_{hash}");
                            EmitStatement(builder, ifStatement.IfFalse, scope, varMap, ref local_index, typeMap, functionConext, loopContext, functionMap);
                            builder.AppendLine($"\t:post_{hash}");
                        }

                        break;
                    }
                case ASTCompoundStatement compoundStatement:
                    foreach (var blockItem in compoundStatement.Block)
                    {
                        EmitBlockItem(builder, blockItem, scope, varMap, ref local_index, typeMap, functionConext, loopContext, functionMap);
                    }
                    break;
                case ASTExpressionStatement expression:
                    EmitExpression(builder, expression.Expr, scope, varMap, functionMap);
                    break;
                case ASTForWithDeclStatement forWithDecl:
                    {
                        int hash = forWithDecl.GetHashCode();
                        LoopContext newLoopContext = new LoopContext($":post_statement_{hash}", $":for_end_{hash}");

                        builder.AppendLine($"\t; For loop {forWithDecl.Condition} {hash}");
                        
                        VarMap new_scope = new VarMap(scope);
                        EmitDeclaration(builder, forWithDecl.Declaration, new_scope, varMap, ref local_index, typeMap, functionMap);

                        builder.AppendLine($"\t:for_cond_{hash}");
                        EmitExpression(builder, forWithDecl.Condition, new_scope, varMap, functionMap);
                        builder.AppendLine($"\tjz {newLoopContext.EndLabel}");

                        EmitStatement(builder, forWithDecl.Body, new_scope, varMap, ref local_index, typeMap, functionConext, newLoopContext, functionMap);

                        builder.AppendLine($"\t{newLoopContext.ContinueLabel}");
                        EmitExpression(builder, forWithDecl.PostExpression, new_scope, varMap, functionMap);

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

                        EmitExpression(builder, whileStatement.Condition, scope, varMap, functionMap);
                        builder.AppendLine($"\tjz {newLoopContext.EndLabel}");

                        EmitStatement(builder, whileStatement.Body, scope, varMap, ref local_index, typeMap, functionConext, loopContext, functionMap);

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

                        EmitStatement(builder, doWhile.Body, scope, varMap, ref local_index, typeMap, functionConext, newLoopContext, functionMap);

                        builder.AppendLine($"\t{newLoopContext.ContinueLabel}");
                        EmitExpression(builder, doWhile.Condition, scope, varMap, functionMap);
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

        private static void EmitExpression(StringBuilder builder, ASTExpression expression, VarMap scope, VarMap varMap, FunctionMap functionMap)
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
                        var var_type = TypeOfVariable(variable.VariableName, scope);
                        var expr_type = CalcReturnType(variable.AssignmentExpression, scope, functionMap);
                        if (var_type != expr_type) Fail($"Cannot assign expression of type '{expr_type}' to variable ('{variable.VariableName}') of type '{var_type}'");

                        EmitExpression(builder, variable.AssignmentExpression, scope, varMap, functionMap);
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
                    EmitExpression(builder, unaryOp.Expr, scope, varMap, functionMap);
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
                    EmitExpression(builder, binaryOp.Left, scope, varMap, functionMap);
                    EmitExpression(builder, binaryOp.Right, scope, varMap, functionMap);
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
                        EmitExpression(builder, conditional.Condition, scope, varMap, functionMap);
                        builder.AppendLine($"\tjz :else_cond_{hash}");
                        builder.AppendLine($"\t:if_cond_{hash}");
                        EmitExpression(builder, conditional.IfTrue, scope, varMap, functionMap);
                        builder.AppendLine($"\tjmp :post_cond_{hash}");
                        builder.AppendLine($"\t:else_cond_{hash}");
                        EmitExpression(builder, conditional.IfFalse, scope, varMap, functionMap);
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
                            var argType = CalcReturnType(functionCall.Arguments[i], scope, functionMap);
                            if (argType != function.Parameters[i].Type)
                                Fail($"Missmatching types on parameter {function.Parameters[i].Name} ({i}), expected '{function.Parameters[i].Type}' got '{argType}'!");
                        }

                        if (functionCall.Arguments.Count > 0)
                            builder.AppendLine($"\t; Args to function call ::{functionCall.FunctionName} {hash}");
                        
                        // This means adding a result type to expressions
                        foreach (var arg in functionCall.Arguments)
                        {
                            EmitExpression(builder, arg, scope, varMap, functionMap);
                        }

                        builder.AppendLine($"\t::{function.Name}");
                        
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
