using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VM12_Opcode;

namespace T12
{
    public class AST
    {
        public readonly ASTProgram Program;

        public AST(ASTProgram Program)
        {
            this.Program = Program;
        }

        public static AST Parse(Queue<Token> Tokens)
        {
            // We can probably do this better!
            // Because we will want to emit comments to the assembly
            Tokens = new Queue<Token>(Tokens.Where(tok => tok.Type != TokenType.Comment));

            var program = ASTProgram.Parse(Tokens);

            return new AST(program);
        }
    }

    public abstract class ASTNode
    {
        protected static void Fail(string error)
        {
            // TODO: Do something better!
            //throw new FormatException(error);
            throw new FormatException(error);
        }
    }
    
    public class ASTProgram : ASTNode
    {
        public readonly List<ASTDirective> Directives;
        public readonly List<ASTFunction> Functions;

        public ASTProgram(List<ASTDirective> Directives, List<ASTFunction> Functions)
        {
            this.Directives = Directives;
            this.Functions = Functions;
        }

        public static ASTProgram Parse(Queue<Token> Tokens)
        {
            List<ASTDirective> directives = new List<ASTDirective>();
            List<ASTFunction> functions = new List<ASTFunction>();

            while (Tokens.Count > 0)
            {
                var peek = Tokens.Peek();
                if (peek.IsDirectiveKeyword)
                {
                    var directive = ASTDirective.Parse(Tokens);
                    directives.Add(directive);
                }
                else if (peek.IsType)
                {
                    var function = ASTFunction.Parse(Tokens);
                    functions.Add(function);
                }
                else if (peek.Type == TokenType.Keyword_Interrupt)
                {
                    var interrupt = ASTInterrupt.Parse(Tokens);
                    functions.Add(interrupt);
                }
                else
                {
                    Fail($"Unknown token {peek} in program!");
                }
            }
            
            while (Tokens.Count > 0 && Tokens.Peek().IsType)
            {
                functions.Add(ASTFunction.Parse(Tokens));
            }

            if (Tokens.Count > 0) Fail($"There was '{Tokens.Count}' tokens left that couldn't be parsed. Next token: '{Tokens.Peek()}'");
            
            return new ASTProgram(directives, functions);
        }
    }

    #region Directives

    public abstract class ASTDirective : ASTNode
    {
        public static ASTDirective Parse(Queue<Token> Tokens)
        {
            var peek = Tokens.Peek();
            switch (peek.Type)
            {
                case TokenType.Keyword_Use:
                    return ASTUseDirective.Parse(Tokens);
                case TokenType.Keyword_Extern:
                    return ASTExternFunctionDirective.Parse(Tokens);
                case TokenType.Keyword_Const:
                    return ASTConstDirective.Parse(Tokens);
                case TokenType.Keyword_Global:
                    return ASTGlobalDirective.Parse(Tokens);
                case TokenType.Keyword_Struct:
                    return ASTStructDeclarationDirective.Parse(Tokens);
                default:
                    Fail($"Unexpected token '{peek}' expected a valid directive!");
                    return default;
            }
        }
    }

    public class ASTUseDirective : ASTDirective
    {
        public readonly string FileName;

        public ASTUseDirective(string filename)
        {
            FileName = filename;
        }

        public static new ASTUseDirective Parse(Queue<Token> Tokens)
        {
            var use_tok = Tokens.Dequeue();
            if (use_tok.Type != TokenType.Keyword_Use) Fail("Exptected 'use'!");

            string name = "";
            var peek = Tokens.Peek();
            while (peek.Type != TokenType.Semicolon)
            {
                var tok = Tokens.Dequeue();
                name += tok.Value;

                peek = Tokens.Peek();
            }

            // Dequeue semicolon
            Tokens.Dequeue();
            
            return new ASTUseDirective(name);
        }
    }

    public class ASTExternFunctionDirective : ASTDirective
    {
        public readonly string FunctionName;
        public readonly List<(ASTType Type, string Name)> Parameters;
        public readonly ASTType ReturnType;

        public ASTExternFunctionDirective(string functionName, List<(ASTType, string)> parameters, ASTType returnType)
        {
            FunctionName = functionName;
            Parameters = parameters;
            ReturnType = returnType;
        }

        public static new ASTExternFunctionDirective Parse(Queue<Token> Tokens)
        {
            var extern_tok = Tokens.Dequeue();
            if (extern_tok.Type != TokenType.Keyword_Extern) Fail("Expected 'extern'!");

            var retType = ASTType.Parse(Tokens);

            var name_tok = Tokens.Dequeue();
            if (name_tok.IsIdentifier == false) Fail($"Expected external function name (identifier)! Got {name_tok}");
            string funcName = name_tok.Value;

            List<(ASTType, string)> parameters = new List<(ASTType, string)>();

            // Confirm that we have a opening parenthesis
            var open_paren_tok = Tokens.Dequeue();
            if (open_paren_tok.Type != TokenType.Open_parenthesis) Fail("Expected '('");

            // We peeked so we can handle this loop more uniform by always dequeueing at the start
            var peek = Tokens.Peek();
            while (peek.Type != TokenType.Close_parenthesis)
            {
                ASTType type = ASTType.Parse(Tokens);

                string name = "";
                if (Tokens.Peek().IsIdentifier)
                    name = Tokens.Dequeue().Value;

                parameters.Add((type, name));

                var cont_token = Tokens.Peek();
                if (cont_token.Type != TokenType.Comma && cont_token.Type == TokenType.Close_parenthesis) break;
                else if (cont_token.Type != TokenType.Comma) Fail("Expected ',' or a ')'");
                // Dequeue the comma
                Tokens.Dequeue();

                peek = Tokens.Peek();
            }

            var close_paren_tok = Tokens.Dequeue();
            if (close_paren_tok.Type != TokenType.Close_parenthesis) Fail("Expected ')'");

            var semicolon_tok = Tokens.Dequeue();
            if (semicolon_tok.Type != TokenType.Semicolon) Fail("Expected semicolon!");

            return new ASTExternFunctionDirective(funcName, parameters, retType);
        }
    }

    public class ASTConstDirective : ASTDirective
    {
        // There is only constants of base types
        public readonly ASTBaseType Type;
        public readonly string Name;
        public readonly ASTExpression Value;

        public ASTConstDirective(ASTBaseType type, string name, ASTExpression value)
        {
            Type = type;
            Name = name;
            Value = value;
        }

        public static new ASTConstDirective Parse(Queue<Token> Tokens)
        {
            var const_tok = Tokens.Dequeue();
            if (const_tok.Type != TokenType.Keyword_Const) Fail("Expected 'const'!");

            var type = ASTType.Parse(Tokens);
            if ((type is ASTBaseType) == false) Fail($"Cannot use a non-base type for a constant variable! Got '{type}'");

            var name_tok = Tokens.Dequeue();
            if (name_tok.Type != TokenType.Identifier) Fail($"Expected constant name!");
            string name = name_tok.Value;

            var equals_tok = Tokens.Dequeue();
            if (equals_tok.Type != TokenType.Equal) Fail($"Expected equals!");

            var value = ASTExpression.Parse(Tokens);

            var semicolon_tok = Tokens.Dequeue();
            if (semicolon_tok.Type != TokenType.Semicolon) Fail("Expected semicolon!");

            return new ASTConstDirective(type as ASTBaseType, name, value);
        }
    }

    public class ASTGlobalDirective : ASTDirective
    {
        public readonly ASTType Type;
        public readonly string Name;

        public ASTGlobalDirective(ASTType type, string name)
        {
            Type = type;
            Name = name;
        }

        public static new ASTGlobalDirective Parse(Queue<Token> Tokens)
        {
            var global_tok = Tokens.Dequeue();
            if (global_tok.Type != TokenType.Keyword_Global) Fail("Expected 'global'!");

            var type = ASTType.Parse(Tokens);

            var name_tok = Tokens.Dequeue();
            if (name_tok.IsIdentifier == false) Fail("Expected global name!");
            string name = name_tok.Value;
            
            var semicolon_tok = Tokens.Dequeue();
            if (semicolon_tok.Type != TokenType.Semicolon) Fail("Expected semicolon!");

            return new ASTGlobalDirective(type, name);
        }
    }

    // NOTE: This should really be a ASTDeclaration and not really a ASTDirective
    // But that is somewhat hard to implement
    public class ASTStructDeclarationDirective : ASTDirective
    {
        // NOTE: Is this really needed. There is probably a way to make this a lot cleaner!

        public readonly string Name;
        public readonly ASTType DeclaredType;
        
        public ASTStructDeclarationDirective(string name, ASTType type)
        {
            Name = name;
            DeclaredType = type;
        }
        
        public static new ASTStructDeclarationDirective Parse(Queue<Token> Tokens)
        {
            var structTok = Tokens.Dequeue();
            if (structTok.Type != TokenType.Keyword_Struct) Fail("Expected struct!");

            var nameTok = Tokens.Dequeue();
            if (nameTok.IsIdentifier == false) Fail($"Expected struct name! Got '{nameTok}'!");
            string name = nameTok.Value;

            var defTok = Tokens.Dequeue();
            switch (defTok.Type)
            {
                case TokenType.Equal:
                    {
                        ASTType type = ASTType.Parse(Tokens);

                        var semicolonTok = Tokens.Dequeue();
                        if (semicolonTok.Type != TokenType.Semicolon) Fail("Expected semicolon!");
                        
                        return new ASTStructDeclarationDirective(name, type);
                    }
                case TokenType.Open_brace:
                    {
                        List<(ASTType Type, string Name)> members = new List<(ASTType Type, string Name)>();

                        var peek = Tokens.Peek();
                        while (peek.Type != TokenType.Close_brace)
                        {
                            var type = ASTType.Parse(Tokens);
                            var memberNameTok = Tokens.Dequeue();
                            if (memberNameTok.IsIdentifier == false) Fail($"Expected member name! Got {memberNameTok}!");
                            string memberName = memberNameTok.Value;

                            var semicolonTok = Tokens.Dequeue();
                            if (semicolonTok.Type != TokenType.Semicolon) Fail("Expected semicolon!");

                            if (members.Select(m => m.Name).Contains(memberName))
                                Fail($"Duplicate member name '{memberName}' in struct '{name}'!");

                            members.Add((type, memberName));

                            peek = Tokens.Peek();
                        }

                        var closeBrace = Tokens.Dequeue();
                        if (closeBrace.Type != TokenType.Close_brace) Fail("Expected '}'!");

                        return new ASTStructDeclarationDirective(name, new ASTStructType(name, members));
                    }
                default:
                    Fail($"Unknown struct definition token '{defTok}'!");
                    return default;
            }
        }
    }

    #endregion

    public class ASTFunction : ASTNode
    {
        public readonly string Name;
        public readonly ASTType ReturnType;
        public readonly List <(ASTType Type, string Name)> Parameters;
        
        public readonly List<ASTBlockItem> Body;

        public ASTFunction(string Name, ASTType ReturnType, List<(ASTType, string)> Parameters, List<ASTBlockItem> Body)
        {
            this.Name = Name;
            this.ReturnType = ReturnType;
            this.Parameters = Parameters;
            this.Body = Body;
        }

        public static ASTFunction Parse(Queue<Token> Tokens)
        {
            var type_tok = Tokens.Peek();
            if (type_tok.IsType == false) Fail("Expected a type!");
            var returnType = ASTType.Parse(Tokens);
            
            var ident_tok = Tokens.Dequeue();
            if (ident_tok.Type != TokenType.Identifier) Fail("Expected an identifier!");
            var name = ident_tok.Value;

            List<(ASTType Type, string Name)> parameters = new List<(ASTType Type, string Name)>();

            // Confirm that we have a opening parenthesis
            var open_paren_tok = Tokens.Dequeue();
            if (open_paren_tok.Type != TokenType.Open_parenthesis) Fail("Expected '('");
            
            // We peeked so we can handle this loop more uniform by always dequeueing at the start
            var peek = Tokens.Peek();
            while (peek.Type != TokenType.Close_parenthesis)
            {
                ASTType type = ASTType.Parse(Tokens);

                var param_ident_tok = Tokens.Dequeue();
                if (param_ident_tok.IsIdentifier == false) Fail("Expected identifier!");
                string param_name = param_ident_tok.Value;

                parameters.Add((type, param_name));

                var cont_token = Tokens.Peek();
                if (cont_token.Type != TokenType.Comma && cont_token.Type == TokenType.Close_parenthesis) break;
                else if (cont_token.Type != TokenType.Comma) Fail("Expected ',' or a ')'");
                // Dequeue the comma
                Tokens.Dequeue();
                
                peek = Tokens.Peek();
            }

            var close_paren_tok = Tokens.Dequeue();
            if (close_paren_tok.Type != TokenType.Close_parenthesis) Fail("Expected ')'");

            var open_brace_tok = Tokens.Dequeue();
            if (open_brace_tok.Type != TokenType.Open_brace) Fail("Expected '{'");

            List<ASTBlockItem> body = new List<ASTBlockItem>();

            peek = Tokens.Peek();
            while (peek.Type != TokenType.Close_brace)
            {
                body.Add(ASTBlockItem.Parse(Tokens));
                peek = Tokens.Peek();
                // This is a .net core 2+ only feature
                //if (!Tokens.TryPeek(out peek)) Fail("Expected a closing brace!");
            }

            // Dequeue the closing brace
            var close_brace_tok = Tokens.Dequeue();
            if (close_brace_tok.Type != TokenType.Close_brace) Fail("Expected closing brace");

            return new ASTFunction(name, returnType, parameters, body);
        }
    }

    public class ASTInterrupt : ASTFunction
    {
        public readonly InterruptType Type;
        
        public ASTInterrupt(InterruptType type, List<(ASTType Type, string Name)> parameters, List<ASTBlockItem> body) : base(InterruptTypeToName(type), ASTBaseType.Void, parameters, body)
        {
            if (type == InterruptType.stop)
                Fail("Cannot define a interupt procedure for the interrupt stop");

            Type = type;
        }

        public static string InterruptTypeToName(InterruptType type)
        {
            switch (type)
            {
                case InterruptType.stop:
                    return "stop";
                case InterruptType.h_Timer:
                    return "h_timer";
                case InterruptType.v_Blank:
                    return "v_blank";
                case InterruptType.keyboard:
                    return "keyboard";
                case InterruptType.mouse:
                    return "mouse";
                default:
                    throw new ArgumentException($"Unkonwn InterruptType {type}");
            }
        }

        public static List<(ASTType, string)> H_TimerParamList = new List<(ASTType, string)>() { (ASTBaseType.Word, "delta") };
        public static List<(ASTType, string)> V_BlankParamList = new List<(ASTType, string)>() { };
        public static List<(ASTType, string)> KeyboardParamList = new List<(ASTType, string)>() { (ASTBaseType.Word, "pressed"), (ASTBaseType.Word, "scancode") };
        public static List<(ASTType, string)> MouseParamList = new List<(ASTType, string)>() { (ASTBaseType.Word, "x"), (ASTBaseType.Word, "y"), (ASTBaseType.Word, "status") };

        public static List<(ASTType Type, string Name)> InterruptToParameterList(InterruptType type)
        {
            switch (type)
            {
                case InterruptType.h_Timer:
                    return H_TimerParamList;
                case InterruptType.v_Blank:
                    return V_BlankParamList;
                case InterruptType.keyboard:
                    return KeyboardParamList;
                case InterruptType.mouse:
                    return MouseParamList;
                case InterruptType.stop:
                default:
                    throw new ArgumentException($"Invalid interrupt type {type}");
            }
        }
        
        public static new ASTInterrupt Parse(Queue<Token> Tokens)
        {
            var interrupt_tok = Tokens.Dequeue();
            if (interrupt_tok.Type != TokenType.Keyword_Interrupt) Fail("Expected interrupt!");

            var interrupt_type_tok = Tokens.Dequeue();
            if (interrupt_type_tok.IsIdentifier == false) Fail("Expected interrupt type!");
            if (Enum.TryParse(interrupt_type_tok.Value, out InterruptType interruptType) == false) Fail($"'{interrupt_type_tok.Value}' is not a valid interrupt type!");

            // FIXME!! Validate params and parse body!!!!
            var open_parenthesis = Tokens.Dequeue();
            if (open_parenthesis.Type != TokenType.Open_parenthesis) Fail("Expected '('!");

            List<(ASTType Type, string Name)> parameters = new List<(ASTType Type, string Name)>();

            // We peeked so we can handle this loop more uniform by always dequeueing at the start
            var peek = Tokens.Peek();
            while (peek.Type != TokenType.Close_parenthesis)
            {
                ASTType type = ASTType.Parse(Tokens);

                var param_ident_tok = Tokens.Dequeue();
                if (param_ident_tok.IsIdentifier == false) Fail("Expected identifier!");
                string param_name = param_ident_tok.Value;

                parameters.Add((type, param_name));

                var cont_token = Tokens.Peek();
                if (cont_token.Type != TokenType.Comma && cont_token.Type == TokenType.Close_parenthesis) break;
                else if (cont_token.Type != TokenType.Comma) Fail("Expected ',' or a ')'");
                // Dequeue the comma
                Tokens.Dequeue();

                peek = Tokens.Peek();
            }

            var close_paren_tok = Tokens.Dequeue();
            if (close_paren_tok.Type != TokenType.Close_parenthesis) Fail("Expected ')'");

            var interruptParams = InterruptToParameterList(interruptType);

            if (interruptParams.Count != parameters.Count)
                Fail($"Missmatching parameter count for interrupt {interruptType}, expected {interruptParams.Count} params, got {parameters.Count}");

            for (int i = 0; i < interruptParams.Count; i++)
            {
                if (interruptParams[i].Type != parameters[i].Type)
                    Fail($"Non matching type for interrupt {interruptType}! Parameter {parameters[i].Name} ({i}) expected '{interruptParams[i].Type}' got '{parameters[i].Type}'");
            }

            var open_brace_tok = Tokens.Dequeue();
            if (open_brace_tok.Type != TokenType.Open_brace) Fail("Expected '{'");

            List<ASTBlockItem> body = new List<ASTBlockItem>();

            peek = Tokens.Peek();
            while (peek.Type != TokenType.Close_brace)
            {
                body.Add(ASTBlockItem.Parse(Tokens));
                peek = Tokens.Peek();
                // This is a .net core 2+ only feature
                //if (!Tokens.TryPeek(out peek)) Fail("Expected a closing brace!");
            }

            // Dequeue the closing brace
            var close_brace_tok = Tokens.Dequeue();
            if (close_brace_tok.Type != TokenType.Close_brace) Fail("Expected closing brace");

            return new ASTInterrupt(interruptType, parameters, body);
        }
    }

    public abstract class ASTBlockItem : ASTNode
    {
        public static ASTBlockItem Parse(Queue<Token> Tokens)
        {
            var peek = Tokens.Peek();

            if (peek.IsBaseType || peek.Type == TokenType.Asterisk || peek.Type == TokenType.Open_square_bracket)
            {
                return ASTDeclaration.Parse(Tokens);
            }
            else if (peek.IsIdentifier)
            {
                var namePeek = Tokens.ElementAt(1);
                var semicolonEqualsPeek = Tokens.ElementAt(2);
                if (namePeek.IsIdentifier && 
                    (semicolonEqualsPeek.Type == TokenType.Semicolon || semicolonEqualsPeek.Type == TokenType.Equal))
                {
                    //  This is a variable declaration of a complex type!
                    return ASTDeclaration.Parse(Tokens);
                }
            }

            return ASTStatement.Parse(Tokens);
        }
    }
    
    #region Declarations

    public abstract class ASTDeclaration : ASTBlockItem
    {
        public static new ASTBlockItem Parse(Queue<Token> Tokens)
        {
            return ASTVariableDeclaration.Parse(Tokens);
        }
    }

    public class ASTVariableDeclaration : ASTDeclaration
    {
        public readonly ASTType Type;
        public readonly string VariableName;
        public readonly ASTExpression Initializer;

        public ASTVariableDeclaration(ASTType Type, string VariableName, ASTExpression Initializer)
        {
            this.Type = Type;
            this.VariableName = VariableName;
            this.Initializer = Initializer;
        }

        public static new ASTVariableDeclaration Parse(Queue<Token> Tokens)
        {
            var type = ASTType.Parse(Tokens);
            //var word_tok = Tokens.Dequeue();
            //if (word_tok.Type != TokenType.Keyword_Word) Fail("We only support word variables atm!");

            var ident_tok = Tokens.Dequeue();
            if (ident_tok.Type != TokenType.Identifier) Fail($"Invalid identifier in variable declareation. '{ident_tok}'");
            string name = ident_tok.Value;

            ASTExpression init = null;

            var peek_init = Tokens.Peek();
            if (peek_init.Type == TokenType.Equal)
            {
                // Dequeue the '='
                Tokens.Dequeue();

                init = ASTExpression.Parse(Tokens);
            }

            var semicolon_tok = Tokens.Dequeue();
            if (semicolon_tok.Type != TokenType.Semicolon) Fail("A statement must end in a semicolon!");

            return new ASTVariableDeclaration(type, name, init);
        }
    }
    
    #endregion

    #region Statements

    public abstract class ASTStatement : ASTBlockItem
    {
        public static new ASTStatement Parse(Queue<Token> Tokens)
        {
            // Here we need to figure out what statement we need to parse
            var peek = Tokens.Peek();
            
            // We switch on the token trying to find what kind of statement this is.
            // TODO: Add more kinds of statements
            switch (peek.Type)
            {
                case TokenType.Semicolon:
                    return ASTEmptyStatement.Parse(Tokens);
                //case TokenType.Identifier:
                //    return ASTAssignmentStatement.Parse(Tokens);
                case TokenType.Keyword_Return:
                    return ASTReturnStatement.Parse(Tokens);
                case TokenType.Keyword_If:
                    return ASTIfStatement.Parse(Tokens);
                case TokenType.Keyword_For:
                    return ASTForWithDeclStatement.Parse(Tokens);
                case TokenType.Keyword_While:
                    return ASTWhileStatement.Parse(Tokens);
                case TokenType.Keyword_Do:
                    return ASTDoWhileStatement.Parse(Tokens);
                case TokenType.Keyword_Break:
                    return ASTBreakStatement.Parse(Tokens);
                case TokenType.Keyword_Continue:
                    return ASTContinueStatement.Parse(Tokens);
                case TokenType.Open_brace:
                    return ASTCompoundStatement.Parse(Tokens);
                case TokenType.Keyword_Assembly:
                    return ASTInlineAssemblyStatement.Parse(Tokens);
                default:
                    return ASTExpressionStatement.Parse(Tokens);
            }
        }
    }
    
    public class ASTEmptyStatement : ASTStatement
    {
        public ASTEmptyStatement() { }

        public static new ASTEmptyStatement Parse(Queue<Token> Tokens)
        {
            var semicolon_tok = Tokens.Dequeue();
            if (semicolon_tok.Type != TokenType.Semicolon) Fail("Expected a semicolon!");

            return new ASTEmptyStatement();
        }
    }

    public class ASTExpressionStatement : ASTStatement
    {
        public readonly ASTExpression Expr;

        public ASTExpressionStatement(ASTExpression Expr)
        {
            this.Expr = Expr;
        }

        public static new ASTExpressionStatement Parse(Queue<Token> Tokens)
        {
            var expr = ASTExpression.Parse(Tokens);

            var semicolon_tok = Tokens.Dequeue();
            if (semicolon_tok.Type != TokenType.Semicolon) Fail($"Expected semicolon! Got '{semicolon_tok}'!");

            return new ASTExpressionStatement(expr);
        }
    }

    public class ASTAssignmentStatement : ASTStatement
    {
        public readonly ReadOnlyCollection<string> VariableNames;
        public readonly ASTExpression AssignmentExpression;

        public ASTAssignmentStatement(List<string> VariableNames, ASTExpression AssignmentExpression)
        {
            this.VariableNames = new ReadOnlyCollection<string>(VariableNames);
            this.AssignmentExpression = AssignmentExpression;
        }

        public static new ASTAssignmentStatement Parse(Queue<Token> Tokens)
        {
            List<string> ids = new List<string>();

            var ident_tok = Tokens.Dequeue();
            if (ident_tok.IsIdentifier == false) Fail("Expected identifier!");
            ids.Add(ident_tok.Value);

            ASTExpression expr = null;

            var peek = Tokens.Peek();
            while (peek.Type == TokenType.Equal)
            {
                // Dequeue equals
                Tokens.Dequeue();

                var cont_ident_tok = Tokens.Peek();
                if (cont_ident_tok.IsIdentifier)
                {
                    // Here we add another value to assign to.
                    ids.Add(cont_ident_tok.Value);
                    Tokens.Dequeue();
                }
                else
                {
                    // Here we parse the experssion
                    expr = ASTExpression.Parse(Tokens);
                    break;
                }

                peek = Tokens.Peek();
            }

            if (expr == null) Fail("Assignment must end in an expression!");

            var semicolon_tok = Tokens.Dequeue();
            if (semicolon_tok.Type != TokenType.Semicolon) Fail("A statement must end in a semicolon!");
            
            return new ASTAssignmentStatement(ids, expr);
        }

        
    }
    
    public class ASTReturnStatement : ASTStatement
    {
        public readonly ASTExpression ReturnValueExpression;

        public ASTReturnStatement(ASTExpression ReturnValueExpression)
        {
            this.ReturnValueExpression = ReturnValueExpression;
        }

        public static new ASTReturnStatement Parse(Queue<Token> Tokens)
        {
            var ret_tok = Tokens.Dequeue();
            if (ret_tok.Type != TokenType.Keyword_Return) Fail("Expected return keyword!");

            ASTExpression returnValExpr = null;

            var peek = Tokens.Peek();
            if (peek.Type != TokenType.Semicolon)
                returnValExpr = ASTExpression.Parse(Tokens);

            var semicolon_tok = Tokens.Dequeue();
            if (semicolon_tok.Type != TokenType.Semicolon) Fail("A statement must end in a semicolon!");

            return new ASTReturnStatement(returnValExpr);
        }
    }

    public class ASTIfStatement : ASTStatement
    {
        public readonly ASTExpression Condition;
        public readonly ASTStatement IfTrue;
        public readonly ASTStatement IfFalse;

        public ASTIfStatement(ASTExpression Condition, ASTStatement IfTrue, ASTStatement IfFalse)
        {
            this.Condition = Condition;
            this.IfTrue = IfTrue;
            this.IfFalse = IfFalse;
        }

        public static new ASTIfStatement Parse(Queue<Token> Tokens)
        {
            var if_tok = Tokens.Dequeue();
            if (if_tok.Type != TokenType.Keyword_If) Fail("Expected if keyword!");

            var open_paren_tok = Tokens.Dequeue();
            if (open_paren_tok.Type != TokenType.Open_parenthesis) Fail("Expected a opening parenthesis!");

            var expr = ASTExpression.Parse(Tokens);

            var close_paren_tok = Tokens.Dequeue();
            if (close_paren_tok.Type != TokenType.Close_parenthesis) Fail("Expected a closing parenthesis!");

            var ifTrue = ASTStatement.Parse(Tokens);

            var ifFalse = (ASTStatement) null;

            var else_peek = Tokens.Peek();
            if (else_peek.Type == TokenType.Keyword_Else)
            {
                // Dequeue the else keyword
                Tokens.Dequeue();

                ifFalse = ASTStatement.Parse(Tokens);
            }

            return new ASTIfStatement(expr, ifTrue, ifFalse);
        }
    }

    public class ASTCompoundStatement : ASTStatement
    {
        public readonly ReadOnlyCollection<ASTBlockItem> Block;

        public ASTCompoundStatement(List<ASTBlockItem> Block)
        {
            this.Block = new ReadOnlyCollection<ASTBlockItem>(Block);
        }

        public static new ASTCompoundStatement Parse(Queue<Token> Tokens)
        {
            var open_brace_tok = Tokens.Dequeue();
            if (open_brace_tok.Type != TokenType.Open_brace) Fail("Expected opening brace!");

            List<ASTBlockItem> blockItems = new List<ASTBlockItem>();

            var peek = Tokens.Peek();
            while (peek.Type != TokenType.Close_brace)
            {
                blockItems.Add(ASTBlockItem.Parse(Tokens));

                peek = Tokens.Peek();
            }

            var close_brace_tok = Tokens.Dequeue();
            if (close_brace_tok.Type != TokenType.Close_brace) Fail("Expected closing brace!");

            return new ASTCompoundStatement(blockItems);
        }
    }

    public class ASTForWithDeclStatement : ASTStatement
    {
        public readonly ASTVariableDeclaration Declaration;
        public readonly ASTExpression Condition;
        public readonly ASTExpression PostExpression;
        public readonly ASTStatement Body;

        public ASTForWithDeclStatement(ASTVariableDeclaration Declaration, ASTExpression Condition, ASTExpression PostExpression, ASTStatement Body)        {
            this.Declaration = Declaration;
            this.Condition = Condition;
            this.PostExpression = PostExpression;
            this.Body = Body;
        }

        public static new ASTForWithDeclStatement Parse(Queue<Token> Tokens)
        {
            var for_tok = Tokens.Dequeue();
            if (for_tok.Type != TokenType.Keyword_For) Fail("Expected token for!");

            var open_paren_tok = Tokens.Dequeue();
            if (open_paren_tok.Type != TokenType.Open_parenthesis) Fail("Expected opening parenthesis!");
            
            // Ends with a semicolon
            var declaration = ASTVariableDeclaration.Parse(Tokens);

            var condition = ASTExpression.Parse(Tokens);

            var semicolon_tok = Tokens.Dequeue();
            if (semicolon_tok.Type != TokenType.Semicolon) Fail("Expected a semicolon!");

            var postExpr = ASTExpression.Parse(Tokens);
            
            var close_paren_tok = Tokens.Dequeue();
            if (close_paren_tok.Type != TokenType.Close_parenthesis) Fail("Expected closing parenthesis!");

            var body = ASTStatement.Parse(Tokens);

            return new ASTForWithDeclStatement(declaration, condition, postExpr, body);
        }
    }

    public class ASTWhileStatement : ASTStatement
    {
        public readonly ASTExpression Condition;
        public readonly ASTStatement Body;

        public ASTWhileStatement(ASTExpression Condition, ASTStatement Body)
        {
            this.Condition = Condition;
            this.Body = Body;
        }

        public static new ASTWhileStatement Parse(Queue<Token> Tokens)
        {
            var while_tok = Tokens.Dequeue();
            if (while_tok.Type != TokenType.Keyword_While) Fail("Expected while!");

            var open_paren_tok = Tokens.Dequeue();
            if (open_paren_tok.Type != TokenType.Open_parenthesis) Fail("Expected opening parenthesis!");

            var condition = ASTExpression.Parse(Tokens);

            var close_paren_tok = Tokens.Dequeue();
            if (close_paren_tok.Type != TokenType.Close_parenthesis) Fail("Expected closing parenthesis!");
            
            var body = ASTStatement.Parse(Tokens);

            return new ASTWhileStatement(condition, body);
        }
    }

    public class ASTDoWhileStatement : ASTStatement
    {
        public readonly ASTStatement Body;
        public readonly ASTExpression Condition;

        public ASTDoWhileStatement(ASTStatement Body, ASTExpression Condition)
        {
            this.Body = Body;
            this.Condition = Condition;
        }

        public static new ASTDoWhileStatement Parse(Queue<Token> Tokens)
        {
            var do_tok = Tokens.Dequeue();
            if (do_tok.Type != TokenType.Keyword_Do) Fail("Expected do!");

            var body = ASTStatement.Parse(Tokens);

            var while_tok = Tokens.Dequeue();
            if (while_tok.Type != TokenType.Keyword_While) Fail("Expected while!");

            var open_paren_tok = Tokens.Dequeue();
            if (open_paren_tok.Type != TokenType.Open_parenthesis) Fail("Expected opening parenthesis!");

            var condition = ASTExpression.Parse(Tokens);

            var close_paren_tok = Tokens.Dequeue();
            if (close_paren_tok.Type != TokenType.Close_parenthesis) Fail("Expected closing parenthesis!");

            var semicolon_tok = Tokens.Dequeue();
            if (semicolon_tok.Type != TokenType.Semicolon) Fail("Expected a semicolon!");

            return new ASTDoWhileStatement(body, condition);
        }
    }

    public class ASTBreakStatement : ASTStatement
    {
        public static new ASTBreakStatement Parse(Queue<Token> Tokens)
        {
            var break_tok = Tokens.Dequeue();
            if (break_tok.Type != TokenType.Keyword_Break) Fail("Expected keyword break!");

            return new ASTBreakStatement();
        }
    }

    public class ASTContinueStatement : ASTStatement
    {
        public static new ASTContinueStatement Parse(Queue<Token> Tokens)
        {
            var break_tok = Tokens.Dequeue();
            if (break_tok.Type != TokenType.Keyword_Continue) Fail("Expected keyword continue!");

            return new ASTContinueStatement();
        }
    }

    public class ASTInlineAssemblyStatement : ASTStatement
    {
        public readonly List<ASTStringLitteral> Assembly;

        public ASTInlineAssemblyStatement(List<ASTStringLitteral> assembly)
        {
            Assembly = assembly;
        }

        public static new ASTInlineAssemblyStatement Parse(Queue<Token> Tokens)
        {
            var assem_tok = Tokens.Dequeue();
            if (assem_tok.Type != TokenType.Keyword_Assembly) Fail("Expected assembly!");

            var open_parenthesis = Tokens.Dequeue();
            if (open_parenthesis.Type != TokenType.Open_parenthesis) Fail("Expected '('");

            List<ASTStringLitteral> assembly = new List<ASTStringLitteral>();
            while (Tokens.Peek().Type != TokenType.Close_parenthesis)
            {
                if (Tokens.Peek().Type != TokenType.String_Litteral)
                    Fail($"Assembly statements can only contain assembly strings! Got {Tokens.Peek()}");

                var string_lit = ASTStringLitteral.Parse(Tokens);
                assembly.Add(string_lit);
            }

            var close_parenthesis = Tokens.Dequeue();
            if (close_parenthesis.Type != TokenType.Close_parenthesis) Fail("Expected ')'");

            var semicolon_tok = Tokens.Dequeue();
            if (semicolon_tok.Type != TokenType.Semicolon) Fail("Expected semicolon!");

            return new ASTInlineAssemblyStatement(assembly);
        }
    }

    #endregion

    #region Expressions

    public abstract class ASTExpression : ASTNode
    {
        public static ASTExpression Parse(Queue<Token> Tokens)
        {
            // We start by parsing logical or which has the lowest precedence
            // It will then go through all levels of precedence
            return ParseConditional(Tokens);
        }

        // The tenth level of precedence (?:)
        public static ASTExpression ParseConditional(Queue<Token> Tokens)
        {
            var expr = ParseLogicalOr(Tokens);

            var peek = Tokens.Peek();
            if (peek.Type == TokenType.Questionmark)
            {
                // Dequeue the questionmark
                Tokens.Dequeue();

                var ifTrue = ASTExpression.Parse(Tokens);

                var colon_tok = Tokens.Dequeue();
                if (colon_tok.Type != TokenType.Colon) Fail("Expected a colon!");

                var ifFalse = ASTExpression.Parse(Tokens);

                expr = new ASTConditionalExpression(expr, ifTrue, ifFalse);
            }

            return expr;
        }

        // The nineth level of precedence (||)
        public static ASTExpression ParseLogicalOr(Queue<Token> Tokens)
        {
            var expr = ParseLogicalAnd(Tokens);

            var peek = Tokens.Peek();
            while (peek.Type == TokenType.DoublePipe)
            {
                var op_type = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var next_term = ParseLogicalAnd(Tokens);
                expr = new ASTBinaryOp(op_type, expr, next_term);
                peek = Tokens.Peek();
            }

            return expr;
        }

        // The eigth level of precedence (&&)
        public static ASTExpression ParseLogicalAnd(Queue<Token> Tokens)
        {
            var expr = ParseBitwiseOr(Tokens);

            var peek = Tokens.Peek();
            while (peek.Type == TokenType.DoubleAnd)
            {
                var op_type = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var next_term = ParseBitwiseOr(Tokens);
                expr = new ASTBinaryOp(op_type, expr, next_term);
                peek = Tokens.Peek();
            }

            return expr;
        }

        // The seventh level of precedence (|)
        public static ASTExpression ParseBitwiseOr(Queue<Token> Tokens)
        {
            var expr = ParseBitwiseXor(Tokens);

            var peek = Tokens.Peek();
            while (peek.Type == TokenType.Pipe)
            {
                var op_type = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var next_term = ParseBitwiseXor(Tokens);
                expr = new ASTBinaryOp(op_type, expr, next_term);
                peek = Tokens.Peek();
            }

            return expr;
        }

        // The sixth level of precedence (^)
        public static ASTExpression ParseBitwiseXor(Queue<Token> Tokens)
        {
            var expr = ParseBitwiseAnd(Tokens);

            var peek = Tokens.Peek();
            while (peek.Type == TokenType.Caret)
            {
                var op_type = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var next_term = ParseBitwiseAnd(Tokens);
                expr = new ASTBinaryOp(op_type, expr, next_term);
                peek = Tokens.Peek();
            }

            return expr;
        }

        // The fifth level of precedence (&)
        public static ASTExpression ParseBitwiseAnd(Queue<Token> Tokens)
        {
            var expr = ParseEqual(Tokens);

            var peek = Tokens.Peek();
            while (peek.Type == TokenType.And)
            {
                var op_type = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var next_term = ParseEqual(Tokens);
                expr = new ASTBinaryOp(op_type, expr, next_term);
                peek = Tokens.Peek();
            }

            return expr;
        }

        // The fourth level of precedence (!= | ==)
        public static ASTExpression ParseEqual(Queue<Token> Tokens)
        {
            var expr = ParseRelational(Tokens);

            var peek = Tokens.Peek();
            while (peek.Type == TokenType.NotEqual || peek.Type == TokenType.DoubleEqual)
            {
                var op_type = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var next_term = ParseRelational(Tokens);
                expr = new ASTBinaryOp(op_type, expr, next_term);
                peek = Tokens.Peek();
            }

            return expr;
        }
        
        // The third level of precedence (< | > | <= | >=)
        public static ASTExpression ParseRelational(Queue<Token> Tokens)
        {
            var term = ParseAddative(Tokens);

            var peek = Tokens.Peek();
            while (peek.Type == TokenType.Open_angle_bracket ||
                peek.Type == TokenType.Close_angle_bracket ||
                peek.Type == TokenType.Less_than_or_equal ||
                peek.Type == TokenType.Greater_than_or_equal)
            {
                var op_type = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var next_term = ParseAddative(Tokens);
                term = new ASTBinaryOp(op_type, term, next_term);
                peek = Tokens.Peek();
            }

            return term;
        }

        // The second level of precedence (+ | -)
        public static ASTExpression ParseAddative(Queue<Token> Tokens)
        {
            var term = ParseTerm(Tokens);

            var peek = Tokens.Peek();
            while (peek.Type == TokenType.Plus || peek.Type == TokenType.Minus)
            {
                var op_type = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var next_term = ParseTerm(Tokens);
                term = new ASTBinaryOp(op_type, term, next_term);
                peek = Tokens.Peek();
            }

            return term;
        }

        // The highest level of precedence (* and /)
        public static ASTExpression ParseTerm(Queue<Token> Tokens)
        {
            // We start by parsing a factor, and handle it as an expression
            var term = ParseFactor(Tokens);

            var peek = Tokens.Peek();
            while (peek.Type == TokenType.Asterisk || peek.Type == TokenType.Slash || peek.Type == TokenType.Percent)
            {
                var op_type = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var next_term = ParseFactor(Tokens);
                term = new ASTBinaryOp(op_type, term, next_term);
                peek = Tokens.Peek();
            }

            return term;
        }

        // Other kinds of expressions like function calls and variable assignment
        public static ASTExpression ParseFactor(Queue<Token> Tokens)
        {
            var peek = Tokens.Peek();
            if (peek.Type == TokenType.Open_parenthesis)
            {
                // This is an expression surrounded by parentheses
                // Discard the opening parenthesis
                Tokens.Dequeue();

                var expr = ASTExpression.Parse(Tokens);

                var closing_tok = Tokens.Dequeue();
                if (closing_tok.Type != TokenType.Close_parenthesis) Fail("Expected closing parenthesis");

                return ParsePostfix(Tokens, expr);
            }
            else if (peek.IsUnaryOp)
            {
                var opType = ASTUnaryOp.TokenToOperatorType(Tokens.Dequeue());
                var factor = ASTExpression.ParseFactor(Tokens);
                return new ASTUnaryOp(opType, factor);
            }
            else if (peek.IsLitteral)
            {
                return ASTLitteral.Parse(Tokens);
            }
            else if (peek.IsIdentifier || peek.IsPostfixOperator)
            {
                // First we parse a variable or function call expression
                // that we use as the target for all postfix expressions

                ASTExpression expr;
                var peekActionTok = Tokens.ElementAt(1);
                if (peekActionTok.Type == TokenType.Open_parenthesis)
                {
                    expr = ASTFunctionCall.Parse(Tokens);
                }
                else
                {
                    // We know its a variable but we don't know what we will do with it
                    expr = ASTVariableExpression.Parse(Tokens);
                }

                return ParsePostfix(Tokens, expr);
            }
            else if (peek.Type == TokenType.Keyword_Cast)
            {
                return ASTExplicitCast.Parse(Tokens);
            }
            else if (peek.Type == TokenType.Keyword_Sizeof)
            {
                return ASTSizeofTypeExpression.Parse(Tokens);
            }
            else
            {
                Fail($"Could not parse factor. Didn't know what to do with token '{peek}'");
                return default;
            }
        }

        public static ASTExpression ParsePostfix(Queue<Token> Tokens, ASTExpression targetExpr)
        {
            // As long as there is a postfix token we parse a postfix expression

            var peek = Tokens.Peek();
            while (peek.IsPostfixOperator)
            {
                switch (peek.Type)
                {
                    case TokenType.Open_parenthesis:
                        Fail("We have not implemented function pointers yet so this will not mean anything");
                        return default;
                    case TokenType.Open_square_bracket:
                        targetExpr = ASTPointerExpression.Parse(Tokens, targetExpr);
                        break;
                    case TokenType.Period:
                    case TokenType.Arrow:
                        targetExpr = ASTMemberExpression.Parse(Tokens, targetExpr);
                        break;
                    default:
                        Fail($"Unknown postfix operator '{peek}'!");
                        break;
                }

                peek = Tokens.Peek();
            }
            
            return targetExpr;
        }
    }

    #region Litterals

    public abstract class ASTLitteral : ASTExpression
    {
        public readonly ASTBaseType Type;

        public readonly string Value;

        public ASTLitteral(ASTBaseType type, string value)
        {
            Type = type;
            Value = value;
        }

        public override string ToString()
        {
            return Value;
        }

        public static new ASTLitteral Parse(Queue<Token> Tokens)
        {
            var peek = Tokens.Peek();
            switch (peek.Type)
            {
                case TokenType.Word_Litteral:
                    return ASTWordLitteral.Parse(Tokens);
                case TokenType.Double_Word_Litteral:
                    return ASTDoubleWordLitteral.Parse(Tokens);
                case TokenType.Keyword_True:
                    Tokens.Dequeue();
                    return ASTBoolLitteral.True;
                case TokenType.Keyword_False:
                    Tokens.Dequeue();
                    return ASTBoolLitteral.False;
                case TokenType.Char_Litteral:
                    return ASTCharLitteral.Parse(Tokens);
                case TokenType.String_Litteral:
                    return ASTStringLitteral.Parse(Tokens);
                default:
                    Fail($"Expected litteral, got '{peek}'");
                    return default;
            }
        }
    }

    public abstract class ASTNumericLitteral : ASTLitteral
    {
        public readonly int IntValue;

        public ASTNumericLitteral(ASTBaseType type, string value, int intValue) : base(type, value)
        {
            IntValue = intValue;
        }

        public static new ASTNumericLitteral Parse(Queue<Token> Tokens)
        {
            var peek = Tokens.Peek();
            switch (peek.Type)
            {
                case TokenType.Word_Litteral:
                    return ASTWordLitteral.Parse(Tokens);
                case TokenType.Double_Word_Litteral:
                    return ASTDoubleWordLitteral.Parse(Tokens);
                default:
                    Fail($"Expected numeric litteral! Got {peek}");
                    return default;
            }
        }
    }

    public class ASTWordLitteral : ASTNumericLitteral
    {
        public const int WORD_MAX_VALUE = 0xFFF;

        public ASTWordLitteral(string value, int intValue) : base(ASTBaseType.Word, value, intValue) { }
        
        public static new ASTWordLitteral Parse(Queue<Token> Tokens)
        {
            var tok = Tokens.Dequeue();
            if (tok.Type != TokenType.Word_Litteral) Fail("Expected word litteral!");

            if (int.TryParse(tok.Value, out int value) == false) Fail($"Could not parse int '{tok.Value}'");
            
            if (value > WORD_MAX_VALUE) Fail($"Litteral '{value}' is to big for a word litteral!");

            return new ASTWordLitteral(tok.Value, value);
        }
    }

    public class ASTDoubleWordLitteral : ASTNumericLitteral
    {
        public ASTDoubleWordLitteral(string value, int intValue) : base(ASTBaseType.DoubleWord, value, intValue) { }

        public static new ASTDoubleWordLitteral Parse(Queue<Token> Tokens)
        {
            var tok = Tokens.Dequeue();
            if (tok.Type != TokenType.Double_Word_Litteral) Fail("Expected dword litteral!");

            // Parse without the end letter!
            if (int.TryParse(tok.Value.Substring(0, tok.Value.Length - 1), out int value) == false) Fail($"Could not parse int '{tok.Value}'");

            return new ASTDoubleWordLitteral(tok.Value, value);
        }
    }

    public class ASTBoolLitteral : ASTLitteral
    {
        public static readonly ASTBoolLitteral True = new ASTBoolLitteral("true");
        public static readonly ASTBoolLitteral False = new ASTBoolLitteral("false");
        
        private ASTBoolLitteral(string value) : base(ASTBaseType.Bool, value) { }
    }

    public class ASTCharLitteral : ASTLitteral
    {
        public readonly char CharValue;

        public ASTCharLitteral(string value, char charValue) : base(ASTBaseType.Char, value)
        {
            CharValue = charValue;
        }

        public static new ASTCharLitteral Parse(Queue<Token> Tokens)
        {
            var tok = Tokens.Dequeue();
            if (tok.Type != TokenType.Char_Litteral) Fail("Expected char litteral!");

            // FIXME: This does not handle escapes!
            // Get the second character of the string
            char value = tok.Value[1];

            return new ASTCharLitteral(tok.Value, value);
        }
    }

    public class ASTStringLitteral : ASTLitteral
    {
        public readonly string Contents;

        public ASTStringLitteral(string value) : base(ASTBaseType.String, value)
        {
            Contents = value.Substring(1, value.Length - 2);
        }

        public static new ASTStringLitteral Parse(Queue<Token> Tokens)
        {
            var string_tok = Tokens.Dequeue();
            if (string_tok.Type != TokenType.String_Litteral) Fail("Expected string litteral!");

            return new ASTStringLitteral(string_tok.Value);
        }
    }

    #endregion

    public class ASTVariableExpression : ASTExpression
    {
        public enum AssignmentOperationType
        {
            Unknown,
            Set,
            Add,
            Subtract,
            Multiply,
            Divide,
            Modulo,
            Bitwise_And,
            Bitwise_Or,
            Botwise_Xor,
        }

        public readonly string Name;
        public readonly ASTExpression AssignmentExpression;

        public ASTVariableExpression(string VariableName, ASTExpression AssignmentExpression)
        {
            this.Name = VariableName;
            this.AssignmentExpression = AssignmentExpression;
        }

        public override string ToString()
        {
            return Name;
        }

        public static new ASTVariableExpression Parse(Queue<Token> Tokens)
        {
            var ident_tok = Tokens.Dequeue();
            if (ident_tok.IsIdentifier == false) Fail("Expected an identifier!");
            string name = ident_tok.Value;

            ASTExpression expr = null;

            var peek = Tokens.Peek();
            if (peek.IsAssignmentOp)
            {
                var op_tok = Tokens.Dequeue();

                expr = ASTExpression.Parse(Tokens);

                if (op_tok.Type != TokenType.Equal)
                {
                    // Replace expr with the appropriate bin op
                    var op_type = TokenToOperatorType(op_tok);

                    expr = new ASTBinaryOp(op_type, new ASTVariableExpression(name, null), expr);
                }
            }

            return new ASTVariableExpression(name, expr);
        }

        public static ASTBinaryOp.BinaryOperatorType TokenToOperatorType(Token token)
        {
            switch (token.Type)
            {
                case TokenType.PlusEqual:
                    return ASTBinaryOp.BinaryOperatorType.Addition;
                case TokenType.MinusEqual:
                    return ASTBinaryOp.BinaryOperatorType.Subtraction;
                case TokenType.AsteriskEqual:
                    return ASTBinaryOp.BinaryOperatorType.Multiplication;
                case TokenType.SlashEqual:
                    return ASTBinaryOp.BinaryOperatorType.Division;
                case TokenType.PercentEqual:
                    return ASTBinaryOp.BinaryOperatorType.Modulo;
                case TokenType.AndEqual:
                    return ASTBinaryOp.BinaryOperatorType.Bitwise_And;
                case TokenType.PipeEqual:
                    return ASTBinaryOp.BinaryOperatorType.Bitwise_Or;
                case TokenType.CaretEqual:
                    return ASTBinaryOp.BinaryOperatorType.Bitwise_Xor;
                default:
                    Fail($"Token '{token}' is not a assignment operator!");
                    return ASTBinaryOp.BinaryOperatorType.Unknown;
            }
        }
    }

    public class ASTPointerExpression : ASTExpression
    {
        public readonly ASTExpression Pointer;
        public readonly ASTExpression Offset;
        public readonly ASTExpression Assignment;

        public ASTPointerExpression(ASTExpression pointer, ASTExpression offset, ASTExpression assignment)
        {
            Pointer = pointer;
            Offset = offset;
            Assignment = assignment;
        }

        public static ASTPointerExpression Parse(Queue<Token> Tokens, ASTExpression target)
        {
            var open_square_tok = Tokens.Dequeue();
            if (open_square_tok.Type != TokenType.Open_square_bracket) Fail("Expected '['!");

            var offset = ASTExpression.Parse(Tokens);

            var closed_square_tok = Tokens.Dequeue();
            if (closed_square_tok.Type != TokenType.Close_squre_bracket) Fail("Expected ']'!");

            ASTExpression assignment = null;

            var peek = Tokens.Peek();
            if (peek.IsAssignmentOp)
            {
                var op_tok = Tokens.Dequeue();

                assignment = ASTExpression.Parse(Tokens);

                if (op_tok.Type != TokenType.Equal)
                {
                    // Replace expr with the appropriate bin op
                    var op_type = ASTVariableExpression.TokenToOperatorType(op_tok);

                    assignment = new ASTBinaryOp(op_type, target, assignment);
                }
            }

            return new ASTPointerExpression(target, offset, assignment);
        }
    }

    public class ASTUnaryOp : ASTExpression
    {
        public enum UnaryOperationType
        {
            Unknown,
            Identity,
            Negation,
            Compliment,
            Logical_negation,
            Dereference,
            // TODO: More
        }

        public readonly UnaryOperationType OperatorType;
        public readonly ASTExpression Expr;

        public ASTUnaryOp(UnaryOperationType OperatorType, ASTExpression Expr)
        {
            this.OperatorType = OperatorType;
            this.Expr = Expr;
        }
        
        public static UnaryOperationType TokenToOperatorType(Token token)
        {
            switch (token.Type)
            {
                case TokenType.Plus:
                    return UnaryOperationType.Identity;
                case TokenType.Minus:
                    return UnaryOperationType.Negation;
                case TokenType.Tilde:
                    return UnaryOperationType.Compliment;
                case TokenType.Exclamationmark:
                    return UnaryOperationType.Logical_negation;
                case TokenType.ShiftLeft:
                    return UnaryOperationType.Dereference;
                default:
                    Fail($"Expected a unary operator token, not '{token}'");
                    return UnaryOperationType.Unknown;
            }
        }
    }

    public class ASTBinaryOp : ASTExpression
    {
        public enum BinaryOperatorType
        {
            Unknown,
            Addition,
            Subtraction,
            Multiplication,
            Division,
            Modulo,
            Bitwise_And,
            Bitwise_Or,
            Bitwise_Xor,
            Bitwise_shift_left,
            Bitwise_shift_right,
            Logical_And,
            Logical_Or,
            Equal,
            Not_equal,
            Less_than,
            Less_than_or_equal,
            Greater_than,
            Greater_than_or_equal,
            // TODO: Add more!
        }

        public readonly BinaryOperatorType OperatorType;
        public readonly ASTExpression Left;
        public readonly ASTExpression Right;

        public ASTBinaryOp(BinaryOperatorType OperatorType, ASTExpression Left, ASTExpression Right)
        {
            this.OperatorType = OperatorType;
            this.Left = Left;
            this.Right = Right;
        }

        public override string ToString()
        {
            return $"{base.ToString()}({OperatorTypeToString(OperatorType)})";
        }

        public static BinaryOperatorType TokenToOperatorType(Token token)
        {
            switch (token.Type)
            {
                case TokenType.Plus:
                    return BinaryOperatorType.Addition;
                case TokenType.Minus:
                    return BinaryOperatorType.Subtraction;
                case TokenType.Asterisk:
                    return BinaryOperatorType.Multiplication;
                case TokenType.Slash:
                    return BinaryOperatorType.Division;
                case TokenType.Percent:
                    return BinaryOperatorType.Modulo;
                case TokenType.DoubleAnd:
                    return BinaryOperatorType.Logical_And;
                case TokenType.DoublePipe:
                    return BinaryOperatorType.Logical_Or;
                case TokenType.DoubleEqual:
                    return BinaryOperatorType.Equal;
                case TokenType.NotEqual:
                    return BinaryOperatorType.Not_equal;
                case TokenType.Open_angle_bracket:
                    return BinaryOperatorType.Less_than;
                case TokenType.Close_angle_bracket:
                    return BinaryOperatorType.Greater_than;
                case TokenType.Less_than_or_equal:
                    return BinaryOperatorType.Less_than_or_equal;
                case TokenType.Greater_than_or_equal:
                    return BinaryOperatorType.Greater_than_or_equal;
                case TokenType.And:
                    return BinaryOperatorType.Bitwise_And;
                case TokenType.Pipe:
                    return BinaryOperatorType.Bitwise_Or;
                case TokenType.Caret:
                    return BinaryOperatorType.Bitwise_Xor;
                default:
                    Fail($"Expected a binary operator token, not '{token}'");
                    return BinaryOperatorType.Unknown;
            }
        }

        public static string OperatorTypeToString(BinaryOperatorType type)
        {
            switch (type)
            {
                case BinaryOperatorType.Addition:
                    return "+";
                case BinaryOperatorType.Subtraction:
                    return "-";
                case BinaryOperatorType.Multiplication:
                    return "*";
                case BinaryOperatorType.Division:
                    return "/";
                case BinaryOperatorType.Modulo:
                    return "%";
                case BinaryOperatorType.Bitwise_And:
                    return "&";
                case BinaryOperatorType.Bitwise_Or:
                    return "|";
                case BinaryOperatorType.Bitwise_Xor:
                    return "^";
                case BinaryOperatorType.Bitwise_shift_left:
                    return "<<";
                case BinaryOperatorType.Bitwise_shift_right:
                    return ">>";
                case BinaryOperatorType.Logical_And:
                    return "&&";
                case BinaryOperatorType.Logical_Or:
                    return "||";
                case BinaryOperatorType.Equal:
                    return "==";
                case BinaryOperatorType.Not_equal:
                    return "!=";
                case BinaryOperatorType.Less_than:
                    return "<";
                case BinaryOperatorType.Less_than_or_equal:
                    return "<=";
                case BinaryOperatorType.Greater_than:
                    return ">";
                case BinaryOperatorType.Greater_than_or_equal:
                    return ">=";
                case BinaryOperatorType.Unknown:
                default:
                    return "UNKNOWN";
            }
        }
    }

    public class ASTConditionalExpression : ASTExpression
    {
        public readonly ASTExpression Condition;
        public readonly ASTExpression IfTrue;
        public readonly ASTExpression IfFalse;

        public ASTConditionalExpression(ASTExpression Condition, ASTExpression IfTrue, ASTExpression IfFalse)
        {
            this.Condition = Condition;
            this.IfTrue = IfTrue;
            this.IfFalse = IfFalse;
        }
    }
    
    public class ASTFunctionCall : ASTExpression
    {
        public readonly string FunctionName;
        public readonly List<ASTExpression> Arguments;

        public ASTFunctionCall(string functionName, List<ASTExpression> arguments)
        {
            FunctionName = functionName;
            Arguments = arguments;
        }

        public static new ASTFunctionCall Parse(Queue<Token> Tokens)
        {
            var name_tok = Tokens.Dequeue();
            if (name_tok.IsIdentifier == false) Fail("Expected identifier!");
            string funcName = name_tok.Value;

            var open_paren_tok = Tokens.Dequeue();
            if (open_paren_tok.Type != TokenType.Open_parenthesis) Fail("Expected '('");

            List<ASTExpression> arguments = new List<ASTExpression>();

            // We peeked so we can handle this loop more uniform by always dequeueing at the start
            var peek = Tokens.Peek();
            while (peek.Type != TokenType.Close_parenthesis)
            {
                var expr = ASTExpression.Parse(Tokens);
                arguments.Add(expr);

                var cont_token = Tokens.Peek();
                if (cont_token.Type != TokenType.Comma && cont_token.Type == TokenType.Close_parenthesis) break;
                else if (cont_token.Type != TokenType.Comma) Fail("Expected ',' or a ')'");
                // Dequeue the comma
                Tokens.Dequeue();

                peek = Tokens.Peek();
            }

            var close_paren_tok = Tokens.Dequeue();
            if (close_paren_tok.Type != TokenType.Close_parenthesis) Fail("Expected ')'");

            return new ASTFunctionCall(funcName, arguments);
        }
    }

    public class ASTMemberExpression : ASTExpression
    {
        public readonly ASTExpression TargetExpr;
        public readonly string MemberName;
        public readonly ASTExpression Assignment;
        public readonly bool Dereference;

        public ASTMemberExpression(ASTExpression targetExpr, string memberName, ASTExpression assignment, bool dereference)
        {
            TargetExpr = targetExpr;
            MemberName = memberName;
            Assignment = assignment;
            Dereference = dereference;
        }

        public static ASTMemberExpression Parse(Queue<Token> Tokens, ASTExpression targetExpr)
        {
            bool dereference = false;

            // If we are using an arrow, set the dereference flag
            var periodTok = Tokens.Dequeue();
            if (periodTok.Type == TokenType.Arrow) dereference = true;
            else if (periodTok.Type != TokenType.Period) Fail("Expected period!");

            var memberTok = Tokens.Dequeue();
            if (memberTok.IsIdentifier == false) Fail("Expected member name!");
            string memberName = memberTok.Value;
            
            ASTExpression assignment = null;
            
            if (Tokens.Peek().IsAssignmentOp)
            {
                var op_tok = Tokens.Dequeue();

                assignment = ASTExpression.Parse(Tokens);

                if (op_tok.Type != TokenType.Equal)
                {
                    // Replace expr with the appropriate bin op
                    var op_type = ASTVariableExpression.TokenToOperatorType(op_tok);

                    assignment = new ASTBinaryOp(op_type, new ASTMemberExpression(targetExpr, memberName, null, dereference), assignment);
                }
            }
            
            return new ASTMemberExpression(targetExpr, memberName, assignment, dereference);
        }
    }
    
    public class ASTSizeofTypeExpression : ASTExpression
    {
        public readonly ASTType Type;

        public ASTSizeofTypeExpression(ASTType type)
        {
            Type = type;
        }

        public static new ASTSizeofTypeExpression Parse(Queue<Token> Tokens)
        {
            var sizeofTok = Tokens.Dequeue();
            if (sizeofTok.Type != TokenType.Keyword_Sizeof) Fail("Expected sizeof!");

            var openParen = Tokens.Dequeue();
            if (openParen.Type != TokenType.Open_parenthesis) Fail("Expected '('!");

            ASTType type = ASTType.Parse(Tokens);

            var closeParen = Tokens.Dequeue();
            if (closeParen.Type != TokenType.Close_parenthesis) Fail("Expected ')'!");

            return new ASTSizeofTypeExpression(type);
        }
    }

    #region Casts

    public abstract class ASTCastExpression : ASTExpression
    {
        public readonly ASTExpression From;
        public readonly ASTType To;

        public ASTCastExpression(ASTExpression from, ASTType to)
        {
            From = from;
            To = to;
        }
    }

    public class ASTImplicitCast : ASTCastExpression
    {
        public readonly ASTBaseType FromType;
        public ASTBaseType ToType => To as ASTBaseType;

        // There will be no way for the parser to generate implicit casts
        // They will only be created when generating assembly
        public ASTImplicitCast(ASTExpression from, ASTBaseType fromType, ASTBaseType to) : base(from, to)
        {
            FromType = fromType;
        }
    }

    public class ASTExplicitCast : ASTCastExpression
    {
        public ASTExplicitCast(ASTExpression from, ASTType to) : base(from, to) { }

        public static new ASTExplicitCast Parse(Queue<Token> Tokens)
        {
            var cast_tok = Tokens.Dequeue();
            if (cast_tok.Type != TokenType.Keyword_Cast) Fail("Expected cast!");

            var open_parenthesis = Tokens.Dequeue();
            if (open_parenthesis.Type != TokenType.Open_parenthesis) Fail("Expected '('!");

            var castType = ASTType.Parse(Tokens);

            var close_parenthesis = Tokens.Dequeue();
            if (close_parenthesis.Type != TokenType.Close_parenthesis) Fail("Expected ')'!");

            var expression = ASTExpression.Parse(Tokens);

            return new ASTExplicitCast(expression, castType);
        }
    }

    public class ASTPointerToVoidPointerCast : ASTCastExpression
    {
        public readonly ASTPointerType FromType;

        public ASTPointerToVoidPointerCast(ASTExpression from, ASTPointerType fromType) : base(from, ASTPointerType.Of(ASTBaseType.Void))
        {
            FromType = fromType;
        }
    }

    public class ASTFixedArrayToArrayCast : ASTCastExpression
    {
        public readonly ASTFixedArrayType FromType;
        public new readonly ASTArrayType To;

        public ASTFixedArrayToArrayCast(ASTExpression from, ASTFixedArrayType fromType, ASTArrayType to) : base(from, to)
        {
            FromType = fromType;
        }
    }

    #endregion

    #endregion

    #region Types

    public abstract class ASTType : ASTNode
    {
        public readonly string TypeName;
        
        public ASTType(string Type)
        {
            this.TypeName = Type;
        }

        public override string ToString()
        {
            return TypeName;
        }
        
        public static bool operator ==(ASTType left, ASTType right) => left?.TypeName == right?.TypeName;
        public static bool operator !=(ASTType left, ASTType right) => left?.TypeName != right?.TypeName;

        public override bool Equals(object obj)
        {
            var type = obj as ASTType;
            return type != null &&
                   TypeName == type.TypeName;
        }

        public override int GetHashCode()
        {
            return -448171650 + EqualityComparer<string>.Default.GetHashCode(TypeName);
            // This is a .net core 2+ only feature
            //return HashCode.Combine(TypeName);
        }
        
        public static ASTType Parse(Queue<Token> Tokens)
        {
            var tok = Tokens.Dequeue();
            switch (tok.Type)
            {
                case TokenType.Asterisk:
                    {
                        ASTType type = ASTType.Parse(Tokens);
                        return new ASTPointerType(type);
                    }
                case TokenType.Open_square_bracket:
                    {
                        // Parse and make the current type the base for an array
                        var peek = Tokens.Peek();
                        if (peek.Type == TokenType.Word_Litteral || peek.Type == TokenType.Double_Word_Litteral)
                        {
                            var numLit = ASTNumericLitteral.Parse(Tokens);

                            if (Tokens.Dequeue().Type != TokenType.Close_squre_bracket)
                                Fail("Expected ']'!");

                            ASTType type = ASTType.Parse(Tokens);

                            return new ASTFixedArrayType(type, numLit);
                        }
                        else if (peek.Type == TokenType.Close_squre_bracket)
                        {
                            // This is an array with a runtime size

                            // Dequeue the closing square bracket
                            Tokens.Dequeue();

                            ASTType type = ASTType.Parse(Tokens);

                            return new ASTArrayType(type);
                        }
                        else
                        {
                            Fail($"Undexpected token while parsing array type '{peek}'!");
                            return default;
                        }
                    }
                default:
                    {
                        // Do normal parsing
                        if (tok.IsType == false) Fail("Exptected type identifier!");

                        ASTType type;
                        if (ASTBaseType.BaseTypeMap.TryGetValue(tok.Value, out ASTBaseType baseType))
                        {
                            type = baseType;
                        }
                        else
                        {
                            type = new ASTTypeRef(tok.Value);
                        }

                        return type;
                    }
            }
        }
    }

    public class ASTBaseType : ASTType
    {
        public static readonly Dictionary<string, ASTBaseType> BaseTypeMap = new Dictionary<string, ASTBaseType>()
        {
            { "void", new ASTBaseType("void", 0) },
            { "word", new ASTBaseType("word", 1) },
            { "dword", new ASTBaseType("dword", 2) },
            { "bool", new ASTBaseType("bool", 1) },
            { "char", new ASTBaseType("char", 1) },
            // TODO? Move over to the 4 word strings with length and data pointer?
            { "string", new ASTBaseType("string", 2) },
        };

        public static ASTBaseType Void => BaseTypeMap["void"];
        public static ASTBaseType Word => BaseTypeMap["word"];
        public static ASTBaseType DoubleWord => BaseTypeMap["dword"];
        public static ASTBaseType Bool => BaseTypeMap["bool"];
        public static ASTBaseType Char => BaseTypeMap["char"];
        public static ASTBaseType String => BaseTypeMap["string"];

        public readonly int Size;
        
        private ASTBaseType(string name, int size) : base(name)
        {
            Size = size;
        }
    }
    
    public class ASTPointerType : ASTType
    {
        public readonly ASTType BaseType;

        public const int Size = 2;

        public static ASTPointerType Of(ASTType type) => new ASTPointerType(type);

        public ASTPointerType(ASTType baseType) : base($"*{baseType.TypeName}")
        {
            BaseType = baseType;
        }
        
        public override bool Equals(object obj)
        {
            if (obj is ASTPointerType) return BaseType == (obj as ASTPointerType);

            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            var hashCode = 1774614950;
            hashCode = hashCode * -1521134295 + base.GetHashCode();
            hashCode = hashCode * -1521134295 + EqualityComparer<ASTType>.Default.GetHashCode(BaseType);
            return hashCode;
            // This is a .net core 2+ only feature
            // return HashCode.Combine(BaseType);
        }
    }

    public class ASTArrayType : ASTType
    {
        public const int Size = 4;

        public readonly ASTType BaseType;

        public ASTArrayType(ASTType baseType) : this(baseType, $"[]{baseType.TypeName}") { }

        protected ASTArrayType(ASTType baseType, string name) : base(name)
        {
            BaseType = baseType;
        }
    }
    
    public class ASTFixedArrayType : ASTArrayType
    {
        public new readonly ASTNumericLitteral Size;

        public ASTFixedArrayType(ASTType baseType, ASTNumericLitteral size) : base(baseType, $"[{size}]{baseType.TypeName}")
        {
            Size = size;
        }
    }

    /// <summary>
    /// A named reference to a complex type with the given name.
    /// </summary>
    public class ASTTypeRef : ASTType
    {
        public readonly string Name;

        public ASTTypeRef(string name) : base(name)
        {
            Name = name;
        }
    }

    public class ASTStructType : ASTType
    {
        public readonly List<(ASTType Type, string Name)> Members;

        public ASTStructType(string name, List<(ASTType, string)> members) : base(name)
        {
            Members = members;
        }
    }
    
    #endregion
}
