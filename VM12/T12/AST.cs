using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
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

    public struct TraceData
    {
        public string File;
        public int StartLine;
        public int EndLine;

        public static readonly TraceData Internal = new TraceData
        {
            File = "internal",
            StartLine = -1,
            EndLine = -1,
        };

        public static TraceData From(Token tok)
        {
            return new TraceData
            {
                File = tok.File,
                StartLine = tok.Line,
                EndLine = tok.Line,
            };
        }
    }

    public abstract class ASTNode
    {
        public readonly TraceData Trace;

        public ASTNode(TraceData trace)
        {
            Trace = trace;
        }

        protected static void Fail(Token tok, string error)
        {
            // TODO: Do something better!
            //throw new FormatException(error);
            throw new FormatException($"Error in file {Path.GetFileName(tok.File)} at line {tok.Line}: '{error}'");
        }
    }
    
    public class ASTProgram : ASTNode
    {
        public readonly List<ASTDirective> Directives;
        public readonly List<ASTFunction> Functions;

        public ASTProgram(TraceData trace, List<ASTDirective> Directives, List<ASTFunction> Functions) : base(trace)
        {
            this.Directives = Directives;
            this.Functions = Functions;
        }

        public static ASTProgram Parse(Queue<Token> Tokens)
        {
            List<ASTDirective> directives = new List<ASTDirective>();
            List<ASTFunction> functions = new List<ASTFunction>();
            
            var trace = new TraceData
            {
                File = Tokens.Peek().File,
                StartLine = Tokens.Peek().Line,
                EndLine = Tokens.Last().Line,
            };

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
                    Fail(peek, $"Unknown token {peek} in program!");
                }
            }
            
            while (Tokens.Count > 0 && Tokens.Peek().IsType)
            {
                functions.Add(ASTFunction.Parse(Tokens));
            }

            if (Tokens.Count > 0) Fail(Tokens.Peek(), $"There was '{Tokens.Count}' tokens left that couldn't be parsed. Next token: '{Tokens.Peek()}'");
            
            return new ASTProgram(trace, directives, functions);
        }
    }

    #region Directives

    public abstract class ASTDirective : ASTNode
    {
        public ASTDirective(TraceData trace) : base(trace) { }

        public static ASTDirective Parse(Queue<Token> Tokens)
        {
            var peek = Tokens.Peek();
            switch (peek.Type)
            {
                case TokenType.Keyword_Public:
                    {
                        var publicTok = Tokens.Dequeue();
                        var colonTok = Tokens.Dequeue();
                        if (colonTok.Type != TokenType.Colon) Fail(peek, "Expected ':' after visibility directive!");

                        var trace = new TraceData
                        {
                            File = publicTok.File,
                            StartLine = publicTok.Line,
                            EndLine = colonTok.Line,
                        };

                        return new ASTVisibilityDirective(trace, true);
                    }
                case TokenType.Keyword_Private:
                    {
                        var privateTok = Tokens.Dequeue();
                        var colonTok = Tokens.Dequeue();
                        if (colonTok.Type != TokenType.Colon) Fail(peek, "Expected ':' after visibility directive!");

                        var trace = new TraceData
                        {
                            File = privateTok.File,
                            StartLine = privateTok.Line,
                            EndLine = colonTok.Line,
                        };

                        return new ASTVisibilityDirective(trace, false);
                    }
                case TokenType.Keyword_Use:
                    return ASTUseDirective.Parse(Tokens);
                case TokenType.Keyword_Extern:
                    if (Tokens.ElementAt(1).Type == TokenType.Keyword_Const)
                    {
                        return ASTExternConstantDirective.Parse(Tokens);
                    }
                    else
                    {
                        return ASTExternFunctionDirective.Parse(Tokens);
                    }
                case TokenType.Keyword_Const:
                    return ASTConstDirective.Parse(Tokens);
                case TokenType.Keyword_Global:
                    return ASTGlobalDirective.Parse(Tokens);
                case TokenType.Keyword_Struct:
                    return ASTStructDeclarationDirective.Parse(Tokens);
                default:
                    Fail(peek, $"Unexpected token '{peek}' expected a valid directive!");
                    return default;
            }
        }
    }

    public class ASTVisibilityDirective : ASTDirective
    {
        public readonly bool IsPublic;

        public ASTVisibilityDirective(TraceData trace, bool isPublic) : base(trace)
        {
            IsPublic = isPublic;
        }
    }

    public class ASTUseDirective : ASTDirective
    {
        public readonly string FileName;

        public ASTUseDirective(TraceData trace, string filename) : base(trace)
        {
            FileName = filename;
        }

        public static new ASTUseDirective Parse(Queue<Token> Tokens)
        {
            var useTok = Tokens.Dequeue();
            if (useTok.Type != TokenType.Keyword_Use) Fail(useTok, "Exptected 'use'!");

            string name = "";
            var peek = Tokens.Peek();
            while (peek.Type != TokenType.Semicolon)
            {
                var tok = Tokens.Dequeue();
                name += tok.Value;

                peek = Tokens.Peek();
            }

            // Dequeue semicolon
            var semicolonTok = Tokens.Dequeue();

            var trace = new TraceData
            {
                File = useTok.File,
                StartLine = useTok.Line,
                EndLine = semicolonTok.Line,
            };
            
            return new ASTUseDirective(trace, name);
        }
    }

    public class ASTExternFunctionDirective : ASTDirective
    {
        public readonly string FunctionName;
        public readonly List<(ASTType Type, string Name)> Parameters;
        public readonly ASTType ReturnType;

        public ASTExternFunctionDirective(TraceData trace, string functionName, List<(ASTType, string)> parameters, ASTType returnType) : base(trace)
        {
            FunctionName = functionName;
            Parameters = parameters;
            ReturnType = returnType;
        }

        public static new ASTExternFunctionDirective Parse(Queue<Token> Tokens)
        {
            var externTok = Tokens.Dequeue();
            if (externTok.Type != TokenType.Keyword_Extern) Fail(externTok, "Expected 'extern'!");

            var retType = ASTType.Parse(Tokens);

            var nameTok = Tokens.Dequeue();
            if (nameTok.IsIdentifier == false) Fail(nameTok, $"Expected external function name (identifier)! Got {nameTok}");
            string funcName = nameTok.Value;

            List<(ASTType, string)> parameters = new List<(ASTType, string)>();

            // Confirm that we have a opening parenthesis
            var openParenTok = Tokens.Dequeue();
            if (openParenTok.Type != TokenType.Open_parenthesis) Fail(openParenTok, "Expected '('");

            // We peeked so we can handle this loop more uniform by always dequeueing at the start
            var peek = Tokens.Peek();
            while (peek.Type != TokenType.Close_parenthesis)
            {
                ASTType type = ASTType.Parse(Tokens);

                string name = "";
                if (Tokens.Peek().IsIdentifier)
                    name = Tokens.Dequeue().Value;

                parameters.Add((type, name));

                var contToken = Tokens.Peek();
                if (contToken.Type != TokenType.Comma && contToken.Type == TokenType.Close_parenthesis) break;
                else if (contToken.Type != TokenType.Comma) Fail(contToken, "Expected ',' or a ')'");
                // Dequeue the comma
                Tokens.Dequeue();

                peek = Tokens.Peek();
            }

            var closeParenTok = Tokens.Dequeue();
            if (closeParenTok.Type != TokenType.Close_parenthesis) Fail(closeParenTok, "Expected ')'");

            var semicolonTok = Tokens.Dequeue();
            if (semicolonTok.Type != TokenType.Semicolon) Fail(semicolonTok, "Expected semicolon!");

            var trace = new TraceData
            {
                File = externTok.File,
                StartLine = externTok.Line,
                EndLine = semicolonTok.Line,
            };

            return new ASTExternFunctionDirective(trace, funcName, parameters, retType);
        }
    }

    public class ASTExternConstantDirective : ASTDirective
    {
        public readonly ASTType Type;
        public readonly string Name;

        public ASTExternConstantDirective(TraceData trace, ASTType type, string name) : base(trace)
        {
            Type = type;
            Name = name;
        }

        public static new ASTExternConstantDirective Parse(Queue<Token> Tokens)
        {
            var externTok = Tokens.Dequeue();
            if (externTok.Type != TokenType.Keyword_Extern) Fail(externTok, "Expected 'extern'!");

            var constTok = Tokens.Dequeue();
            if (constTok.Type != TokenType.Keyword_Const) Fail(constTok, "Expected 'const'!");

            var type = ASTType.Parse(Tokens);
            
            var nameTok = Tokens.Dequeue();
            if (nameTok.IsIdentifier == false) Fail(nameTok, "Expected identifier!");
            string name = nameTok.Value;

            var semicolonTok = Tokens.Dequeue();
            if (semicolonTok.Type != TokenType.Semicolon) Fail(semicolonTok, "Expected semicolon!");

            var trace = new TraceData
            {
                File = externTok.File,
                StartLine = externTok.Line,
                EndLine = semicolonTok.Line,
            };

            return new ASTExternConstantDirective(trace, type, name);
        }
    }

    public class ASTConstDirective : ASTDirective
    {
        // There is only constants of base types
        public readonly ASTType Type;
        public readonly string Name;
        public readonly ASTExpression Value;

        public ASTConstDirective(TraceData trace, ASTType type, string name, ASTExpression value) : base(trace)
        {
            Type = type;
            Name = name;
            Value = value;
        }

        public static new ASTConstDirective Parse(Queue<Token> Tokens)
        {
            var constTok = Tokens.Dequeue();
            if (constTok.Type != TokenType.Keyword_Const) Fail(constTok, "Expected 'const'!");

            var type = ASTType.Parse(Tokens);

            var nameTok = Tokens.Dequeue();
            if (nameTok.Type != TokenType.Identifier) Fail(nameTok, $"Expected constant name!");
            string name = nameTok.Value;

            var equalsTok = Tokens.Dequeue();
            if (equalsTok.Type != TokenType.Equal) Fail(equalsTok, $"Expected equals!");

            var value = ASTExpression.Parse(Tokens);

            var semicolonTok = Tokens.Dequeue();
            if (semicolonTok.Type != TokenType.Semicolon) Fail(semicolonTok, "Expected semicolon!");

            var trace = new TraceData
            {
                File = constTok.File,
                StartLine = constTok.Line,
                EndLine = semicolonTok.Line,
            };

            return new ASTConstDirective(trace, type, name, value);
        }
    }

    public class ASTGlobalDirective : ASTDirective
    {
        public readonly ASTType Type;
        public readonly string Name;

        public ASTGlobalDirective(TraceData trace, ASTType type, string name) : base(trace)
        {
            Type = type;
            Name = name;
        }

        public static new ASTGlobalDirective Parse(Queue<Token> Tokens)
        {
            var globalTok = Tokens.Dequeue();
            if (globalTok.Type != TokenType.Keyword_Global) Fail(globalTok, "Expected 'global'!");

            var type = ASTType.Parse(Tokens);

            var nameTok = Tokens.Dequeue();
            if (nameTok.IsIdentifier == false) Fail(nameTok, "Expected global name!");
            string name = nameTok.Value;
            
            var semicolonTok = Tokens.Dequeue();
            if (semicolonTok.Type != TokenType.Semicolon) Fail(semicolonTok, "Expected semicolon!");

            var trace = new TraceData
            {
                File = globalTok.File,
                StartLine = globalTok.Line,
                EndLine = semicolonTok.Line,
            };

            return new ASTGlobalDirective(trace, type, name);
        }
    }

    // NOTE: This should really be a ASTDeclaration and not really a ASTDirective
    // But that is somewhat hard to implement
    public class ASTStructDeclarationDirective : ASTDirective
    {
        // NOTE: Is this really needed. There is probably a way to make this a lot cleaner!

        public readonly string Name;
        public readonly ASTType DeclaredType;
        
        public ASTStructDeclarationDirective(TraceData trace, string name, ASTType type) : base(trace)
        {
            Name = name;
            DeclaredType = type;
        }
        
        public static new ASTStructDeclarationDirective Parse(Queue<Token> Tokens)
        {
            var structTok = Tokens.Dequeue();
            if (structTok.Type != TokenType.Keyword_Struct) Fail(structTok, "Expected struct!");

            var nameTok = Tokens.Dequeue();
            if (nameTok.IsIdentifier == false) Fail(nameTok, $"Expected struct name! Got '{nameTok}'!");
            string name = nameTok.Value;

            var defTok = Tokens.Dequeue();
            switch (defTok.Type)
            {
                case TokenType.Equal:
                    {
                        ASTType type = ASTType.Parse(Tokens);

                        var semicolonTok = Tokens.Dequeue();
                        if (semicolonTok.Type != TokenType.Semicolon) Fail(semicolonTok, "Expected semicolon!");

                        var trace = new TraceData
                        {
                            File = structTok.File,
                            StartLine = structTok.Line,
                            EndLine = semicolonTok.Line,
                        };

                        return new ASTStructDeclarationDirective(trace, name, type);
                    }
                case TokenType.Open_brace:
                    {
                        List<(ASTType Type, string Name)> members = new List<(ASTType Type, string Name)>();

                        var peek = Tokens.Peek();
                        while (peek.Type != TokenType.Close_brace)
                        {
                            var type = ASTType.Parse(Tokens);
                            var memberNameTok = Tokens.Dequeue();
                            if (memberNameTok.IsIdentifier == false) Fail(memberNameTok, $"Expected member name! Got {memberNameTok}!");
                            string memberName = memberNameTok.Value;

                            var semicolonTok = Tokens.Dequeue();
                            if (semicolonTok.Type != TokenType.Semicolon) Fail(semicolonTok, "Expected semicolon!");

                            if (members.Select(m => m.Name).Contains(memberName))
                                Fail(memberNameTok, $"Duplicate member name '{memberName}' in struct '{name}'!");

                            members.Add((type, memberName));

                            peek = Tokens.Peek();
                        }

                        var closeBrace = Tokens.Dequeue();
                        if (closeBrace.Type != TokenType.Close_brace) Fail(closeBrace, "Expected '}'!");

                        var trace = new TraceData
                        {
                            File = structTok.File,
                            StartLine = structTok.Line,
                            EndLine = closeBrace.Line,
                        };

                        return new ASTStructDeclarationDirective(trace, name, new ASTStructType(trace, name, members));
                    }
                default:
                    Fail(defTok, $"Unknown struct definition token '{defTok}'!");
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

        public ASTFunction(TraceData trace, string Name, ASTType ReturnType, List<(ASTType, string)> Parameters, List<ASTBlockItem> Body) : base(trace)
        {
            this.Name = Name;
            this.ReturnType = ReturnType;
            this.Parameters = Parameters;
            this.Body = Body;
        }

        public static ASTFunction Parse(Queue<Token> Tokens)
        {
            var typeTok = Tokens.Peek();
            if (typeTok.IsType == false) Fail(typeTok, "Expected a type!");
            var returnType = ASTType.Parse(Tokens);
            
            var identTok = Tokens.Dequeue();
            if (identTok.Type != TokenType.Identifier) Fail(identTok, "Expected an identifier!");
            var name = identTok.Value;

            List<(ASTType Type, string Name)> parameters = new List<(ASTType Type, string Name)>();

            // Confirm that we have a opening parenthesis
            var openParenTok = Tokens.Dequeue();
            if (openParenTok.Type != TokenType.Open_parenthesis) Fail(openParenTok, "Expected '('");
            
            // We peeked so we can handle this loop more uniform by always dequeueing at the start
            var peek = Tokens.Peek();
            while (peek.Type != TokenType.Close_parenthesis)
            {
                ASTType type = ASTType.Parse(Tokens);

                var paramIdentTok = Tokens.Dequeue();
                if (paramIdentTok.IsIdentifier == false) Fail(paramIdentTok, "Expected identifier!");
                string param_name = paramIdentTok.Value;

                parameters.Add((type, param_name));

                var contToken = Tokens.Peek();
                if (contToken.Type != TokenType.Comma && contToken.Type == TokenType.Close_parenthesis) break;
                else if (contToken.Type != TokenType.Comma) Fail(contToken, "Expected ',' or a ')'");
                // Dequeue the comma
                Tokens.Dequeue();
                
                peek = Tokens.Peek();
            }

            var closeParenTok = Tokens.Dequeue();
            if (closeParenTok.Type != TokenType.Close_parenthesis) Fail(closeParenTok, "Expected ')'");

            var openBraceTok = Tokens.Dequeue();
            if (openBraceTok.Type != TokenType.Open_brace) Fail(openBraceTok, "Expected '{'");

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
            var closeBraceTok = Tokens.Dequeue();
            if (closeBraceTok.Type != TokenType.Close_brace) Fail(closeBraceTok, "Expected closing brace");

            var trace = new TraceData
            {
                File = typeTok.File,
                StartLine = typeTok.Line,
                EndLine = closeBraceTok.Line,
            };

            return new ASTFunction(trace, name, returnType, parameters, body);
        }
    }

    public class ASTInterrupt : ASTFunction
    {
        public readonly InterruptType Type;
        
        public ASTInterrupt(TraceData trace, InterruptType type, List<(ASTType Type, string Name)> parameters, List<ASTBlockItem> body) : base(trace, InterruptTypeToName(type), ASTBaseType.Void, parameters, body)
        {
            if (type == InterruptType.stop)
                throw new ArgumentException("Cannot define a interupt procedure for the interrupt stop");

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
            var interruptTok = Tokens.Dequeue();
            if (interruptTok.Type != TokenType.Keyword_Interrupt) Fail(interruptTok, "Expected interrupt!");

            var interruptTypeTok = Tokens.Dequeue();
            if (interruptTypeTok.IsIdentifier == false) Fail(interruptTypeTok, "Expected interrupt type!");
            if (Enum.TryParse(interruptTypeTok.Value, out InterruptType interruptType) == false) Fail(interruptTypeTok, $"'{interruptTypeTok.Value}' is not a valid interrupt type!");

            if (interruptType == InterruptType.stop)
                Fail(interruptTypeTok, "Cannot define a interupt procedure for the interrupt stop");

            // FIXME!! Validate params and parse body!!!!
            var openParenthesis = Tokens.Dequeue();
            if (openParenthesis.Type != TokenType.Open_parenthesis) Fail(openParenthesis, "Expected '('!");

            List<(ASTType Type, string Name)> parameters = new List<(ASTType Type, string Name)>();

            // This is to provide accurate debug info
            List<Token> paramTokens = new List<Token>();

            // We peeked so we can handle this loop more uniform by always dequeueing at the start
            var peek = Tokens.Peek();
            while (peek.Type != TokenType.Close_parenthesis)
            {
                ASTType type = ASTType.Parse(Tokens);

                var paramIdentTok = Tokens.Dequeue();
                if (paramIdentTok.IsIdentifier == false) Fail(paramIdentTok, "Expected identifier!");
                string param_name = paramIdentTok.Value;

                parameters.Add((type, param_name));
                paramTokens.Add(paramIdentTok);

                var contToken = Tokens.Peek();
                if (contToken.Type != TokenType.Comma && contToken.Type == TokenType.Close_parenthesis) break;
                else if (contToken.Type != TokenType.Comma) Fail(contToken, "Expected ',' or a ')'");
                // Dequeue the comma
                Tokens.Dequeue();

                peek = Tokens.Peek();
            }

            var closeParenTok = Tokens.Dequeue();
            if (closeParenTok.Type != TokenType.Close_parenthesis) Fail(closeParenTok, "Expected ')'");

            var interruptParams = InterruptToParameterList(interruptType);

            if (interruptParams.Count != parameters.Count)
                Fail(closeParenTok, $"Missmatching parameter count for interrupt {interruptType}, expected {interruptParams.Count} params, got {parameters.Count}");

            for (int i = 0; i < interruptParams.Count; i++)
            {
                if (interruptParams[i].Type != parameters[i].Type)
                    Fail(paramTokens[i], $"Non matching type for interrupt {interruptType}! Parameter {parameters[i].Name} ({i}) expected '{interruptParams[i].Type}' got '{parameters[i].Type}'");
            }

            var openBraceTok = Tokens.Dequeue();
            if (openBraceTok.Type != TokenType.Open_brace) Fail(openBraceTok, "Expected '{'");

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
            var closeBraceTok = Tokens.Dequeue();
            if (closeBraceTok.Type != TokenType.Close_brace) Fail(closeBraceTok, "Expected closing brace");

            var trace = new TraceData
            {
                File = interruptTok.File,
                StartLine = interruptTok.Line,
                EndLine = closeBraceTok.Line,
            };

            return new ASTInterrupt(trace, interruptType, parameters, body);
        }
    }

    public abstract class ASTBlockItem : ASTNode
    {
        public ASTBlockItem(TraceData trace) : base(trace) { }

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
        public ASTDeclaration(TraceData trace) : base(trace) { }

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

        public ASTVariableDeclaration(TraceData trace, ASTType Type, string VariableName, ASTExpression Initializer) : base(trace)
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

            var identTok = Tokens.Dequeue();
            if (identTok.Type != TokenType.Identifier) Fail(identTok, $"Invalid identifier in variable declareation. '{identTok}'");
            string name = identTok.Value;

            ASTExpression init = null;

            var peek_init = Tokens.Peek();
            if (peek_init.Type == TokenType.Equal)
            {
                // Dequeue the '='
                Tokens.Dequeue();

                init = ASTExpression.Parse(Tokens);
            }

            var semicolonTok = Tokens.Dequeue();
            if (semicolonTok.Type != TokenType.Semicolon) Fail(semicolonTok, "A statement must end in a semicolon!");

            var trace = new TraceData
            {
                File = type.Trace.File,
                StartLine = type.Trace.StartLine,
                EndLine = semicolonTok.Line,
            };

            return new ASTVariableDeclaration(trace, type, name, init);
        }
    }
    
    #endregion

    #region Statements

    public abstract class ASTStatement : ASTBlockItem
    {
        public ASTStatement(TraceData trace) : base(trace) { }

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
        public ASTEmptyStatement(TraceData trace) : base(trace) { }

        public static new ASTEmptyStatement Parse(Queue<Token> Tokens)
        {
            var semicolonTok = Tokens.Dequeue();
            if (semicolonTok.Type != TokenType.Semicolon) Fail(semicolonTok, "Expected a semicolon!");

            var trace = new TraceData
            {
                File = semicolonTok.File,
                StartLine = semicolonTok.Line,
                EndLine = semicolonTok.Line,
            };

            return new ASTEmptyStatement(trace);
        }
    }

    public class ASTExpressionStatement : ASTStatement
    {
        public readonly ASTExpression Expr;

        public ASTExpressionStatement(TraceData trace, ASTExpression Expr) : base(trace)
        {
            this.Expr = Expr;
        }

        public static new ASTExpressionStatement Parse(Queue<Token> Tokens)
        {
            var expr = ASTExpression.Parse(Tokens);

            var semicolonTok = Tokens.Dequeue();
            if (semicolonTok.Type != TokenType.Semicolon) Fail(semicolonTok, $"Expected semicolon! Got '{semicolonTok}'!");

            var trace = new TraceData
            {
                File = expr.Trace.File,
                StartLine = expr.Trace.StartLine,
                EndLine = semicolonTok.Line,
            };

            return new ASTExpressionStatement(trace, expr);
        }
    }

    public class ASTAssignmentStatement : ASTStatement
    {
        public readonly ReadOnlyCollection<string> VariableNames;
        public readonly ASTExpression AssignmentExpression;

        public ASTAssignmentStatement(TraceData trace, List<string> VariableNames, ASTExpression AssignmentExpression) : base(trace)
        {
            this.VariableNames = new ReadOnlyCollection<string>(VariableNames);
            this.AssignmentExpression = AssignmentExpression;
        }

        public static new ASTAssignmentStatement Parse(Queue<Token> Tokens)
        {
            List<string> ids = new List<string>();

            var identTok = Tokens.Dequeue();
            if (identTok.IsIdentifier == false) Fail(identTok, "Expected identifier!");
            ids.Add(identTok.Value);

            ASTExpression expr = null;

            var peek = Tokens.Peek();
            while (peek.Type == TokenType.Equal)
            {
                // Dequeue equals
                Tokens.Dequeue();

                var contIdentTok = Tokens.Peek();
                if (contIdentTok.IsIdentifier)
                {
                    // Here we add another value to assign to.
                    ids.Add(contIdentTok.Value);
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

            if (expr == null) Fail(peek, "Assignment must end in an expression!");

            var semicolonTok = Tokens.Dequeue();
            if (semicolonTok.Type != TokenType.Semicolon) Fail(semicolonTok, "A statement must end in a semicolon!");

            var trace = new TraceData
            {
                File = identTok.File,
                StartLine = identTok.Line,
                EndLine = semicolonTok.Line,
            };

            return new ASTAssignmentStatement(trace, ids, expr);
        }

        
    }
    
    public class ASTReturnStatement : ASTStatement
    {
        public readonly ASTExpression ReturnValueExpression;

        public ASTReturnStatement(TraceData trace, ASTExpression ReturnValueExpression) : base(trace)
        {
            this.ReturnValueExpression = ReturnValueExpression;
        }

        public static new ASTReturnStatement Parse(Queue<Token> Tokens)
        {
            var retTok = Tokens.Dequeue();
            if (retTok.Type != TokenType.Keyword_Return) Fail(retTok, "Expected return keyword!");

            ASTExpression returnValExpr = null;

            var peek = Tokens.Peek();
            if (peek.Type != TokenType.Semicolon)
                returnValExpr = ASTExpression.Parse(Tokens);

            var semicolonTok = Tokens.Dequeue();
            if (semicolonTok.Type != TokenType.Semicolon) Fail(semicolonTok, "A statement must end in a semicolon!");

            var trace = new TraceData
            {
                File = retTok.File,
                StartLine = retTok.Line,
                EndLine = semicolonTok.Line,
            };

            return new ASTReturnStatement(trace, returnValExpr);
        }
    }

    public class ASTIfStatement : ASTStatement
    {
        public readonly ASTExpression Condition;
        public readonly ASTStatement IfTrue;
        public readonly ASTStatement IfFalse;

        public ASTIfStatement(TraceData trace, ASTExpression Condition, ASTStatement IfTrue, ASTStatement IfFalse) : base(trace)
        {
            this.Condition = Condition;
            this.IfTrue = IfTrue;
            this.IfFalse = IfFalse;
        }

        public static new ASTIfStatement Parse(Queue<Token> Tokens)
        {
            var ifTok = Tokens.Dequeue();
            if (ifTok.Type != TokenType.Keyword_If) Fail(ifTok, "Expected if keyword!");

            var openParenTok = Tokens.Dequeue();
            if (openParenTok.Type != TokenType.Open_parenthesis) Fail(openParenTok, "Expected a opening parenthesis!");

            var expr = ASTExpression.Parse(Tokens);

            var closeParenTok = Tokens.Dequeue();
            if (closeParenTok.Type != TokenType.Close_parenthesis) Fail(closeParenTok, "Expected a closing parenthesis!");

            var ifTrue = ASTStatement.Parse(Tokens);

            var ifFalse = (ASTStatement) null;

            var else_peek = Tokens.Peek();
            if (else_peek.Type == TokenType.Keyword_Else)
            {
                // Dequeue the else keyword
                Tokens.Dequeue();

                ifFalse = ASTStatement.Parse(Tokens);
            }

            var trace = new TraceData
            {
                File = ifTok.File,
                StartLine = ifTok.Line,
                EndLine = ifFalse?.Trace.EndLine ?? ifTrue.Trace.EndLine,
            };

            return new ASTIfStatement(trace, expr, ifTrue, ifFalse);
        }
    }

    public class ASTCompoundStatement : ASTStatement
    {
        public readonly ReadOnlyCollection<ASTBlockItem> Block;

        public ASTCompoundStatement(TraceData trace, List<ASTBlockItem> Block) : base(trace)
        {
            this.Block = new ReadOnlyCollection<ASTBlockItem>(Block);
        }

        public static new ASTCompoundStatement Parse(Queue<Token> Tokens)
        {
            var openBraceTok = Tokens.Dequeue();
            if (openBraceTok.Type != TokenType.Open_brace) Fail(openBraceTok, "Expected opening brace!");

            List<ASTBlockItem> blockItems = new List<ASTBlockItem>();

            var peek = Tokens.Peek();
            while (peek.Type != TokenType.Close_brace)
            {
                blockItems.Add(ASTBlockItem.Parse(Tokens));

                peek = Tokens.Peek();
            }

            var closeBraceTok = Tokens.Dequeue();
            if (closeBraceTok.Type != TokenType.Close_brace) Fail(closeBraceTok, "Expected closing brace!");

            var trace = new TraceData
            {
                File = openBraceTok.File,
                StartLine = openBraceTok.Line,
                EndLine = closeBraceTok.Line,
            };

            return new ASTCompoundStatement(trace, blockItems);
        }
    }

    public class ASTForWithDeclStatement : ASTStatement
    {
        public readonly ASTVariableDeclaration Declaration;
        public readonly ASTExpression Condition;
        public readonly ASTExpression PostExpression;
        public readonly ASTStatement Body;

        public ASTForWithDeclStatement(TraceData trace, ASTVariableDeclaration Declaration, ASTExpression Condition, ASTExpression PostExpression, ASTStatement Body) : base(trace)
        {
            this.Declaration = Declaration;
            this.Condition = Condition;
            this.PostExpression = PostExpression;
            this.Body = Body;
        }

        public static new ASTForWithDeclStatement Parse(Queue<Token> Tokens)
        {
            var forTok = Tokens.Dequeue();
            if (forTok.Type != TokenType.Keyword_For) Fail(forTok, "Expected token for!");

            var openParenTok = Tokens.Dequeue();
            if (openParenTok.Type != TokenType.Open_parenthesis) Fail(openParenTok, "Expected opening parenthesis!");
            
            // Ends with a semicolon
            var declaration = ASTVariableDeclaration.Parse(Tokens);

            var condition = ASTExpression.Parse(Tokens);

            var semicolonTok = Tokens.Dequeue();
            if (semicolonTok.Type != TokenType.Semicolon) Fail(semicolonTok, "Expected a semicolon!");

            var postExpr = ASTExpression.Parse(Tokens);
            
            var closeParenTok = Tokens.Dequeue();
            if (closeParenTok.Type != TokenType.Close_parenthesis) Fail(closeParenTok, "Expected closing parenthesis!");

            var body = ASTStatement.Parse(Tokens);

            var trace = new TraceData
            {
                File = forTok.File,
                StartLine = forTok.Line,
                EndLine = body.Trace.EndLine,
            };

            return new ASTForWithDeclStatement(trace, declaration, condition, postExpr, body);
        }
    }

    public class ASTWhileStatement : ASTStatement
    {
        public readonly ASTExpression Condition;
        public readonly ASTStatement Body;

        public ASTWhileStatement(TraceData trace, ASTExpression Condition, ASTStatement Body) : base(trace)
        {
            this.Condition = Condition;
            this.Body = Body;
        }

        public static new ASTWhileStatement Parse(Queue<Token> Tokens)
        {
            var whileTok = Tokens.Dequeue();
            if (whileTok.Type != TokenType.Keyword_While) Fail(whileTok, "Expected while!");

            var openParenTok = Tokens.Dequeue();
            if (openParenTok.Type != TokenType.Open_parenthesis) Fail(openParenTok, "Expected opening parenthesis!");

            var condition = ASTExpression.Parse(Tokens);

            var closeParenTok = Tokens.Dequeue();
            if (closeParenTok.Type != TokenType.Close_parenthesis) Fail(closeParenTok, "Expected closing parenthesis!");
            
            var body = ASTStatement.Parse(Tokens);

            var trace = new TraceData
            {
                File = whileTok.File,
                StartLine = whileTok.Line,
                EndLine = body.Trace.EndLine,
            };

            return new ASTWhileStatement(trace, condition, body);
        }
    }

    public class ASTDoWhileStatement : ASTStatement
    {
        public readonly ASTStatement Body;
        public readonly ASTExpression Condition;

        public ASTDoWhileStatement(TraceData trace, ASTStatement Body, ASTExpression Condition) : base(trace)
        {
            this.Body = Body;
            this.Condition = Condition;
        }

        public static new ASTDoWhileStatement Parse(Queue<Token> Tokens)
        {
            var doTok = Tokens.Dequeue();
            if (doTok.Type != TokenType.Keyword_Do) Fail(doTok, "Expected do!");

            var body = ASTStatement.Parse(Tokens);

            var whileTok = Tokens.Dequeue();
            if (whileTok.Type != TokenType.Keyword_While) Fail(whileTok, "Expected while!");

            var openParenTok = Tokens.Dequeue();
            if (openParenTok.Type != TokenType.Open_parenthesis) Fail(openParenTok, "Expected opening parenthesis!");

            var condition = ASTExpression.Parse(Tokens);

            var closeParenTok = Tokens.Dequeue();
            if (closeParenTok.Type != TokenType.Close_parenthesis) Fail(closeParenTok, "Expected closing parenthesis!");

            var semicolonTok = Tokens.Dequeue();
            if (semicolonTok.Type != TokenType.Semicolon) Fail(semicolonTok, "Expected a semicolon!");

            var trace = new TraceData
            {
                File = doTok.File,
                StartLine = doTok.Line,
                EndLine = semicolonTok.Line,
            };

            return new ASTDoWhileStatement(trace, body, condition);
        }
    }

    public class ASTBreakStatement : ASTStatement
    {
        public ASTBreakStatement(TraceData trace) : base(trace) { }

        public static new ASTBreakStatement Parse(Queue<Token> Tokens)
        {
            var breakTok = Tokens.Dequeue();
            if (breakTok.Type != TokenType.Keyword_Break) Fail(breakTok, "Expected keyword break!");

            var trace = TraceData.From(breakTok);

            return new ASTBreakStatement(trace);
        }
    }

    public class ASTContinueStatement : ASTStatement
    {
        public ASTContinueStatement(TraceData trace) : base(trace) { }

        public static new ASTContinueStatement Parse(Queue<Token> Tokens)
        {
            var continueTok = Tokens.Dequeue();
            if (continueTok.Type != TokenType.Keyword_Continue) Fail(continueTok, "Expected keyword continue!");

            var trace = TraceData.From(continueTok);

            return new ASTContinueStatement(trace);
        }
    }

    public class ASTInlineAssemblyStatement : ASTStatement
    {
        public readonly List<ASTStringLitteral> Assembly;

        public ASTInlineAssemblyStatement(TraceData trace, List<ASTStringLitteral> assembly) : base(trace)
        {
            Assembly = assembly;
        }

        public static new ASTInlineAssemblyStatement Parse(Queue<Token> Tokens)
        {
            var assemTok = Tokens.Dequeue();
            if (assemTok.Type != TokenType.Keyword_Assembly) Fail(assemTok, "Expected assembly!");

            var openParenthesis = Tokens.Dequeue();
            if (openParenthesis.Type != TokenType.Open_parenthesis) Fail(openParenthesis, "Expected '('");

            List<ASTStringLitteral> assembly = new List<ASTStringLitteral>();
            while (Tokens.Peek().Type != TokenType.Close_parenthesis)
            {
                if (Tokens.Peek().Type != TokenType.String_Litteral)
                    Fail(Tokens.Peek(), $"Assembly statements can only contain assembly strings! Got {Tokens.Peek()}");

                var string_lit = ASTStringLitteral.Parse(Tokens);
                assembly.Add(string_lit);
            }

            var closeParenthesis = Tokens.Dequeue();
            if (closeParenthesis.Type != TokenType.Close_parenthesis) Fail(closeParenthesis, "Expected ')'");

            var semicolonTok = Tokens.Dequeue();
            if (semicolonTok.Type != TokenType.Semicolon) Fail(semicolonTok, "Expected semicolon!");

            var trace = new TraceData
            {
                File = assemTok.File,
                StartLine = assemTok.Line,
                EndLine = semicolonTok.Line,
            };

            return new ASTInlineAssemblyStatement(trace, assembly);
        }
    }

    #endregion

    #region Expressions

    public abstract class ASTExpression : ASTNode
    {
        public ASTExpression(TraceData trace) : base(trace) { }

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

                var colonTok = Tokens.Dequeue();
                if (colonTok.Type != TokenType.Colon) Fail(colonTok, "Expected a colon!");

                var ifFalse = ASTExpression.Parse(Tokens);

                var trace = new TraceData
                {
                    File = expr.Trace.File,
                    StartLine = expr.Trace.StartLine,
                    EndLine = ifFalse.Trace.EndLine,
                };

                expr = new ASTConditionalExpression(trace, expr, ifTrue, ifFalse);
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
                var opType = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var nextTerm = ParseLogicalAnd(Tokens);

                var trace = new TraceData
                {
                    File = expr.Trace.File,
                    StartLine = expr.Trace.StartLine,
                    EndLine = nextTerm.Trace.EndLine,
                };

                expr = new ASTBinaryOp(trace, opType, expr, nextTerm);
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
                var opType = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var nextTerm = ParseBitwiseOr(Tokens);

                var trace = new TraceData
                {
                    File = expr.Trace.File,
                    StartLine = expr.Trace.StartLine,
                    EndLine = nextTerm.Trace.EndLine,
                };

                expr = new ASTBinaryOp(trace, opType, expr, nextTerm);
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
                var opType = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var nextTerm = ParseBitwiseXor(Tokens);

                var trace = new TraceData
                {
                    File = expr.Trace.File,
                    StartLine = expr.Trace.StartLine,
                    EndLine = nextTerm.Trace.EndLine,
                };

                expr = new ASTBinaryOp(trace, opType, expr, nextTerm);
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
                var opType = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var nextTerm = ParseBitwiseAnd(Tokens);

                var trace = new TraceData
                {
                    File = expr.Trace.File,
                    StartLine = expr.Trace.StartLine,
                    EndLine = nextTerm.Trace.EndLine,
                };

                expr = new ASTBinaryOp(trace, opType, expr, nextTerm);
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
                var opType = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var nextTerm = ParseEqual(Tokens);

                var trace = new TraceData
                {
                    File = expr.Trace.File,
                    StartLine = expr.Trace.StartLine,
                    EndLine = nextTerm.Trace.EndLine,
                };

                expr = new ASTBinaryOp(trace, opType, expr, nextTerm);
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
                var opType = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var nextTerm = ParseRelational(Tokens);

                var trace = new TraceData
                {
                    File = expr.Trace.File,
                    StartLine = expr.Trace.StartLine,
                    EndLine = nextTerm.Trace.EndLine,
                };

                expr = new ASTBinaryOp(trace, opType, expr, nextTerm);
                peek = Tokens.Peek();
            }

            return expr;
        }
        
        // The third level of precedence (< | > | <= | >=)
        public static ASTExpression ParseRelational(Queue<Token> Tokens)
        {
            var expr = ParseAddative(Tokens);

            var peek = Tokens.Peek();
            while (peek.Type == TokenType.Open_angle_bracket ||
                peek.Type == TokenType.Close_angle_bracket ||
                peek.Type == TokenType.Less_than_or_equal ||
                peek.Type == TokenType.Greater_than_or_equal)
            {
                var opType = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var nextTerm = ParseAddative(Tokens);

                var trace = new TraceData
                {
                    File = expr.Trace.File,
                    StartLine = expr.Trace.StartLine,
                    EndLine = nextTerm.Trace.EndLine,
                };

                expr = new ASTBinaryOp(trace, opType, expr, nextTerm);
                peek = Tokens.Peek();
            }

            return expr;
        }

        // The second level of precedence (+ | -)
        public static ASTExpression ParseAddative(Queue<Token> Tokens)
        {
            var expr = ParseTerm(Tokens);

            var peek = Tokens.Peek();
            while (peek.Type == TokenType.Plus || peek.Type == TokenType.Minus)
            {
                var opType = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var nextTerm = ParseTerm(Tokens);

                var trace = new TraceData
                {
                    File = expr.Trace.File,
                    StartLine = expr.Trace.StartLine,
                    EndLine = nextTerm.Trace.EndLine,
                };

                expr = new ASTBinaryOp(trace, opType, expr, nextTerm);
                peek = Tokens.Peek();
            }

            return expr;
        }

        // The highest level of precedence (* and /)
        public static ASTExpression ParseTerm(Queue<Token> Tokens)
        {
            // We start by parsing a factor, and handle it as an expression
            var expr = ParseFactor(Tokens);

            var peek = Tokens.Peek();
            while (peek.Type == TokenType.Asterisk || peek.Type == TokenType.Slash || peek.Type == TokenType.Percent)
            {
                var opType = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var nextTerm = ParseFactor(Tokens);

                var trace = new TraceData
                {
                    File = expr.Trace.File,
                    StartLine = expr.Trace.StartLine,
                    EndLine = nextTerm.Trace.EndLine,
                };

                expr = new ASTBinaryOp(trace, opType, expr, nextTerm);
                peek = Tokens.Peek();
            }

            return expr;
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

                var closingTok = Tokens.Dequeue();
                if (closingTok.Type != TokenType.Close_parenthesis) Fail(closingTok, "Expected closing parenthesis");

                return ParsePostfix(Tokens, expr);
            }
            else if (peek.IsUnaryOp)
            {
                var opTok = Tokens.Dequeue();
                var opType = ASTUnaryOp.TokenToOperatorType(opTok);
                var factor = ASTExpression.ParseFactor(Tokens);

                var trace = new TraceData
                {
                    File = opTok.File,
                    StartLine = opTok.Line,
                    EndLine = factor.Trace.EndLine,
                };

                return new ASTUnaryOp(trace, opType, factor);
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
                Fail(peek, $"Could not parse factor. Didn't know what to do with token '{peek}'");
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
                        Fail(peek, "We have not implemented function pointers yet so this will not mean anything");
                        return default;
                    case TokenType.Open_square_bracket:
                        targetExpr = ASTPointerExpression.Parse(Tokens, targetExpr);
                        break;
                    case TokenType.Period:
                    case TokenType.Arrow:
                        targetExpr = ASTMemberExpression.Parse(Tokens, targetExpr);
                        break;
                    default:
                        Fail(peek, $"Unknown postfix operator '{peek}'!");
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

        public ASTLitteral(TraceData trace, ASTBaseType type, string value) : base(trace)
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
                    var trueTok = Tokens.Dequeue();
                    return new ASTBoolLitteral(TraceData.From(trueTok), true);
                case TokenType.Keyword_False:
                    var falseTok = Tokens.Dequeue();
                    return new ASTBoolLitteral(TraceData.From(falseTok), false);
                case TokenType.Char_Litteral:
                    return ASTCharLitteral.Parse(Tokens);
                case TokenType.String_Litteral:
                    return ASTStringLitteral.Parse(Tokens);
                default:
                    Fail(peek, $"Expected litteral, got '{peek}'");
                    return default;
            }
        }
    }

    public abstract class ASTNumericLitteral : ASTLitteral
    {
        public readonly int IntValue;

        public ASTNumericLitteral(TraceData trace, ASTBaseType type, string value, int intValue) : base(trace, type, value)
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
                    Fail(peek, $"Expected numeric litteral! Got {peek}");
                    return default;
            }
        }
    }

    public class ASTWordLitteral : ASTNumericLitteral
    {
        public const int WORD_MAX_VALUE = 0xFFF;

        public ASTWordLitteral(TraceData trace, string value, int intValue) : base(trace, ASTBaseType.Word, value, intValue) { }
        
        public static new ASTWordLitteral Parse(Queue<Token> Tokens)
        {
            var tok = Tokens.Dequeue();
            if (tok.Type != TokenType.Word_Litteral) Fail(tok, "Expected word litteral!");

            if (int.TryParse(tok.Value, out int value) == false) Fail(tok, $"Could not parse int '{tok.Value}'");
            
            if (value > WORD_MAX_VALUE) Fail(tok, $"Litteral '{value}' is to big for a word litteral!");

            var trace = TraceData.From(tok);

            return new ASTWordLitteral(trace, tok.Value, value);
        }
    }

    public class ASTDoubleWordLitteral : ASTNumericLitteral
    {
        public const int DOUBLE_WORD_MAX_VALUE = 0xFFF_FFF;

        public ASTDoubleWordLitteral(TraceData trace, string value, int intValue) : base(trace, ASTBaseType.DoubleWord, value, intValue) { }

        public static new ASTDoubleWordLitteral Parse(Queue<Token> Tokens)
        {
            var tok = Tokens.Dequeue();
            if (tok.Type != TokenType.Double_Word_Litteral) Fail(tok, "Expected dword litteral!");

            // Parse without the end letter!
            if (int.TryParse(tok.Value.Substring(0, tok.Value.Length - 1), out int value) == false) Fail(tok, $"Could not parse int '{tok.Value}'");

            if (value > DOUBLE_WORD_MAX_VALUE) Fail(tok, $"Litteral '{value}' is too big for a double word!");

            var trace = TraceData.From(tok);

            return new ASTDoubleWordLitteral(trace, tok.Value, value);
        }
    }

    public class ASTBoolLitteral : ASTLitteral
    {
        //public static readonly ASTBoolLitteral True = new ASTBoolLitteral("true");
        //public static readonly ASTBoolLitteral False = new ASTBoolLitteral("false");

        public readonly bool BoolValue;

        public ASTBoolLitteral(TraceData trace, bool value) : base(trace, ASTBaseType.Bool, value.ToString())
        {
            BoolValue = value;
        }
    }

    public class ASTCharLitteral : ASTLitteral
    {
        public readonly char CharValue;

        public ASTCharLitteral(TraceData trace, string value, char charValue) : base(trace, ASTBaseType.Char, value)
        {
            CharValue = charValue;
        }

        public static new ASTCharLitteral Parse(Queue<Token> Tokens)
        {
            var tok = Tokens.Dequeue();
            if (tok.Type != TokenType.Char_Litteral) Fail(tok, "Expected char litteral!");

            // FIXME: This does not handle escapes!
            // Get the second character of the string
            char value = tok.Value[1];

            var trace = TraceData.From(tok);

            return new ASTCharLitteral(trace, tok.Value, value);
        }
    }

    public class ASTStringLitteral : ASTLitteral
    {
        public readonly string Contents;

        public ASTStringLitteral(TraceData trace, string value) : base(trace, ASTBaseType.String, value)
        {
            Contents = value.Substring(1, value.Length - 2);
        }

        public static new ASTStringLitteral Parse(Queue<Token> Tokens)
        {
            var stringTok = Tokens.Dequeue();
            if (stringTok.Type != TokenType.String_Litteral) Fail(stringTok, "Expected string litteral!");

            var trace = TraceData.From(stringTok);

            return new ASTStringLitteral(trace, stringTok.Value);
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

        public ASTVariableExpression(TraceData trace, string variableName, ASTExpression assignmentExpression) : base(trace)
        {
            this.Name = variableName;
            this.AssignmentExpression = assignmentExpression;
        }

        public override string ToString()
        {
            return Name;
        }

        public static new ASTVariableExpression Parse(Queue<Token> Tokens)
        {
            var identTok = Tokens.Dequeue();
            if (identTok.IsIdentifier == false) Fail(identTok, "Expected an identifier!");
            string name = identTok.Value;

            ASTExpression expr = null;

            var peek = Tokens.Peek();
            if (peek.IsAssignmentOp)
            {
                var opTok = Tokens.Dequeue();

                expr = ASTExpression.Parse(Tokens);

                if (opTok.Type != TokenType.Equal)
                {
                    // Replace expr with the appropriate bin op
                    var opType = TokenToOperatorType(opTok);

                    var assignmentTrace = new TraceData
                    {
                        File = identTok.File,
                        StartLine = identTok.Line,
                        EndLine = expr.Trace.EndLine,
                    };

                    expr = new ASTBinaryOp(assignmentTrace, opType, new ASTVariableExpression(TraceData.From(identTok), name, null), expr);
                }
            }

            var trace = new TraceData
            {
                File = identTok.File,
                StartLine = identTok.Line,
                EndLine = expr?.Trace.EndLine ?? identTok.Line,
            };

            return new ASTVariableExpression(trace, name, expr);
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
                    Fail(token, $"Token '{token}' is not a assignment operator!");
                    return ASTBinaryOp.BinaryOperatorType.Unknown;
            }
        }
    }

    public class ASTPointerExpression : ASTExpression
    {
        public readonly ASTExpression Pointer;
        public readonly ASTExpression Offset;
        public readonly ASTExpression Assignment;

        public ASTPointerExpression(TraceData trace, ASTExpression pointer, ASTExpression offset, ASTExpression assignment) : base(trace)
        {
            Pointer = pointer;
            Offset = offset;
            Assignment = assignment;
        }

        public static ASTPointerExpression Parse(Queue<Token> Tokens, ASTExpression target)
        {
            var openSquareTok = Tokens.Dequeue();
            if (openSquareTok.Type != TokenType.Open_square_bracket) Fail(openSquareTok, "Expected '['!");

            var offset = ASTExpression.Parse(Tokens);

            var closedSquareTok = Tokens.Dequeue();
            if (closedSquareTok.Type != TokenType.Close_squre_bracket) Fail(closedSquareTok, "Expected ']'!");

            ASTExpression assignment = null;

            var peek = Tokens.Peek();
            if (peek.IsAssignmentOp)
            {
                var opTok = Tokens.Dequeue();

                assignment = ASTExpression.Parse(Tokens);

                if (opTok.Type != TokenType.Equal)
                {
                    // Replace expr with the appropriate bin op
                    var opType = ASTVariableExpression.TokenToOperatorType(opTok);

                    var assignmentTrace = new TraceData
                    {
                        File = target.Trace.File,
                        StartLine = target.Trace.StartLine,
                        EndLine = opTok.Line,
                    };

                    assignment = new ASTBinaryOp(assignmentTrace, opType, target, assignment);
                }
            }

            var trace = new TraceData
            {
                File = openSquareTok.File,
                StartLine = openSquareTok.Line,
                EndLine = assignment?.Trace.EndLine ?? closedSquareTok.Line,
            };

            return new ASTPointerExpression(trace, target, offset, assignment);
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

        public ASTUnaryOp(TraceData trace, UnaryOperationType OperatorType, ASTExpression Expr) : base(trace)
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
                    Fail(token, $"Expected a unary operator token, not '{token}'");
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

        public ASTBinaryOp(TraceData trace, BinaryOperatorType OperatorType, ASTExpression Left, ASTExpression Right) : base(trace)
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
                    Fail(token, $"Expected a binary operator token, not '{token}'");
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

        public ASTConditionalExpression(TraceData trace, ASTExpression Condition, ASTExpression IfTrue, ASTExpression IfFalse) : base(trace)
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

        public ASTFunctionCall(TraceData trace, string functionName, List<ASTExpression> arguments) : base(trace)
        {
            FunctionName = functionName;
            Arguments = arguments;
        }

        public static new ASTFunctionCall Parse(Queue<Token> Tokens)
        {
            var nameTok = Tokens.Dequeue();
            if (nameTok.IsIdentifier == false) Fail(nameTok, "Expected identifier!");
            string funcName = nameTok.Value;

            var openParenTok = Tokens.Dequeue();
            if (openParenTok.Type != TokenType.Open_parenthesis) Fail(openParenTok, "Expected '('");

            List<ASTExpression> arguments = new List<ASTExpression>();

            // We peeked so we can handle this loop more uniform by always dequeueing at the start
            var peek = Tokens.Peek();
            while (peek.Type != TokenType.Close_parenthesis)
            {
                var expr = ASTExpression.Parse(Tokens);
                arguments.Add(expr);

                var contToken = Tokens.Peek();
                if (contToken.Type != TokenType.Comma && contToken.Type == TokenType.Close_parenthesis) break;
                else if (contToken.Type != TokenType.Comma) Fail(contToken, "Expected ',' or a ')'");
                // Dequeue the comma
                Tokens.Dequeue();

                peek = Tokens.Peek();
            }

            var closeParenTok = Tokens.Dequeue();
            if (closeParenTok.Type != TokenType.Close_parenthesis) Fail(closeParenTok, "Expected ')'");

            var trace = new TraceData
            {
                File = nameTok.File,
                StartLine = nameTok.Line,
                EndLine = closeParenTok.Line,
            };

            return new ASTFunctionCall(trace, funcName, arguments);
        }
    }

    public class ASTMemberExpression : ASTExpression
    {
        public readonly ASTExpression TargetExpr;
        public readonly string MemberName;
        public readonly ASTExpression Assignment;
        public readonly bool Dereference;

        public ASTMemberExpression(TraceData trace, ASTExpression targetExpr, string memberName, ASTExpression assignment, bool dereference) : base(trace)
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
            else if (periodTok.Type != TokenType.Period) Fail(periodTok, "Expected period!");

            var memberTok = Tokens.Dequeue();
            if (memberTok.IsIdentifier == false) Fail(memberTok, "Expected member name!");
            string memberName = memberTok.Value;
            
            ASTExpression assignment = null;
            
            if (Tokens.Peek().IsAssignmentOp)
            {
                var opTok = Tokens.Dequeue();

                assignment = ASTExpression.Parse(Tokens);

                if (opTok.Type != TokenType.Equal)
                {
                    // Replace expr with the appropriate bin op
                    var opType = ASTVariableExpression.TokenToOperatorType(opTok);

                    var memberTrace = new TraceData
                    {
                        File = targetExpr.Trace.File,
                        StartLine = targetExpr.Trace.StartLine,
                        EndLine = memberTok.Line,
                    };

                    var assignmentTrace = new TraceData
                    {
                        File = targetExpr.Trace.File,
                        StartLine = targetExpr.Trace.StartLine,
                        EndLine = opTok.Line,
                    };

                    assignment = new ASTBinaryOp(assignmentTrace, opType, new ASTMemberExpression(memberTrace, targetExpr, memberName, null, dereference), assignment);
                }
            }
            
            var trace = new TraceData
            {
                File = periodTok.File,
                StartLine = periodTok.Line,
                EndLine = assignment?.Trace.EndLine ?? memberTok.Line,
            };

            return new ASTMemberExpression(trace, targetExpr, memberName, assignment, dereference);
        }
    }
    
    public class ASTSizeofTypeExpression : ASTExpression
    {
        public readonly ASTType Type;

        public ASTSizeofTypeExpression(TraceData trace, ASTType type) : base(trace)
        {
            Type = type;
        }

        public static new ASTSizeofTypeExpression Parse(Queue<Token> Tokens)
        {
            var sizeofTok = Tokens.Dequeue();
            if (sizeofTok.Type != TokenType.Keyword_Sizeof) Fail(sizeofTok, "Expected sizeof!");

            var openParen = Tokens.Dequeue();
            if (openParen.Type != TokenType.Open_parenthesis) Fail(openParen, "Expected '('!");

            ASTType type = ASTType.Parse(Tokens);

            var closeParen = Tokens.Dequeue();
            if (closeParen.Type != TokenType.Close_parenthesis) Fail(closeParen, "Expected ')'!");

            var trace = new TraceData
            {
                File = sizeofTok.File,
                StartLine = sizeofTok.Line,
                EndLine = closeParen.Line,
            };

            return new ASTSizeofTypeExpression(trace, type);
        }
    }

    #region Casts

    public abstract class ASTCastExpression : ASTExpression
    {
        public readonly ASTExpression From;
        public readonly ASTType To;

        public ASTCastExpression(TraceData trace, ASTExpression from, ASTType to) : base(trace)
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
        public ASTImplicitCast(TraceData trace, ASTExpression from, ASTBaseType fromType, ASTBaseType to) : base(trace, from, to)
        {
            FromType = fromType;
        }
    }

    public class ASTExplicitCast : ASTCastExpression
    {
        public ASTExplicitCast(TraceData trace, ASTExpression from, ASTType to) : base(trace, from, to) { }

        public static new ASTExplicitCast Parse(Queue<Token> Tokens)
        {
            var castTok = Tokens.Dequeue();
            if (castTok.Type != TokenType.Keyword_Cast) Fail(castTok, "Expected cast!");

            var openParenthesis = Tokens.Dequeue();
            if (openParenthesis.Type != TokenType.Open_parenthesis) Fail(openParenthesis, "Expected '('!");

            var castType = ASTType.Parse(Tokens);

            var closeParenthesis = Tokens.Dequeue();
            if (closeParenthesis.Type != TokenType.Close_parenthesis) Fail(closeParenthesis, "Expected ')'!");

            // FIXME: Make casting left-associative

            var expression = ASTExpression.Parse(Tokens);

            var trace = new TraceData
            {
                File = castTok.File,
                StartLine = castTok.Line,
                EndLine = expression.Trace.EndLine,
            };

            return new ASTExplicitCast(trace, expression, castType);
        }
    }

    public class ASTPointerToVoidPointerCast : ASTCastExpression
    {
        public readonly ASTPointerType FromType;

        public ASTPointerToVoidPointerCast(TraceData trace, ASTExpression from, ASTPointerType fromType) : base(trace, from, ASTPointerType.Of(ASTBaseType.Void))
        {
            FromType = fromType;
        }
    }

    public class ASTFixedArrayToArrayCast : ASTCastExpression
    {
        public readonly ASTFixedArrayType FromType;
        public new readonly ASTArrayType To;

        public ASTFixedArrayToArrayCast(TraceData trace, ASTExpression from, ASTFixedArrayType fromType, ASTArrayType to) : base(trace, from, to)
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
        
        public ASTType(TraceData trace, string Type) : base(trace)
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

                        var trace = new TraceData
                        {
                            File = tok.File,
                            StartLine = tok.Line,
                            EndLine = type.Trace.EndLine,
                        };

                        return new ASTPointerType(trace, type);
                    }
                case TokenType.Open_square_bracket:
                    {
                        // Parse and make the current type the base for an array
                        var peek = Tokens.Peek();
                        if (peek.Type == TokenType.Word_Litteral || peek.Type == TokenType.Double_Word_Litteral)
                        {
                            var numLit = ASTNumericLitteral.Parse(Tokens);

                            var closeSquareTok = Tokens.Dequeue();
                            if (closeSquareTok.Type != TokenType.Close_squre_bracket) Fail(closeSquareTok, "Expected ']'!");

                            ASTType type = ASTType.Parse(Tokens);

                            var trace = new TraceData
                            {
                                File = tok.File,
                                StartLine = tok.Line,
                                EndLine = type.Trace.EndLine,
                            };

                            return new ASTFixedArrayType(trace, type, numLit);
                        }
                        else if (peek.Type == TokenType.Close_squre_bracket)
                        {
                            // This is an array with a runtime size

                            // Dequeue the closing square bracket
                            Tokens.Dequeue();

                            ASTType type = ASTType.Parse(Tokens);

                            var trace = new TraceData
                            {
                                File = tok.File,
                                StartLine = tok.Line,
                                EndLine = type.Trace.EndLine,
                            };

                            return new ASTArrayType(trace, type);
                        }
                        else
                        {
                            Fail(peek, $"Undexpected token while parsing array type '{peek}'!");
                            return default;
                        }
                    }
                default:
                    {
                        // Do normal parsing
                        if (tok.IsType == false) Fail(tok, "Exptected type identifier!");

                        // TODO: Fix traces for base types?
                        ASTType type;
                        if (ASTBaseType.BaseTypeMap.TryGetValue(tok.Value, out ASTBaseType baseType))
                        {
                            type = baseType;
                        }
                        else
                        {
                            var trace = TraceData.From(tok);
                            type = new ASTTypeRef(trace, tok.Value);
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
        
        // FIXME: Trace data for internal types?
        private ASTBaseType(string name, int size) : base(TraceData.Internal, name)
        {
            Size = size;
        }
    }
    
    public class ASTPointerType : ASTType
    {
        public readonly ASTType BaseType;

        public const int Size = 2;

        public static ASTPointerType Of(ASTType type) => new ASTPointerType(type.Trace, type);

        public ASTPointerType(TraceData trace, ASTType baseType) : base(trace, $"*{baseType.TypeName}")
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

        public ASTArrayType(TraceData trace, ASTType baseType) : this(trace, baseType, $"[]{baseType.TypeName}") { }

        protected ASTArrayType(TraceData trace, ASTType baseType, string name) : base(trace, name)
        {
            BaseType = baseType;
        }
    }
    
    public class ASTFixedArrayType : ASTArrayType
    {
        public new readonly ASTNumericLitteral Size;

        public ASTFixedArrayType(TraceData trace, ASTType baseType, ASTNumericLitteral size) : base(trace, baseType, $"[{size}]{baseType.TypeName}")
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

        public ASTTypeRef(TraceData trace, string name) : base(trace, name)
        {
            Name = name;
        }
    }

    public class ASTStructType : ASTType
    {
        public readonly List<(ASTType Type, string Name)> Members;

        public ASTStructType(TraceData trace, string name, List<(ASTType, string)> members) : base(trace, name)
        {
            Members = members;
        }
    }
    
    #endregion
}
