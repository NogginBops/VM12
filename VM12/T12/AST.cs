﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using VM12Opcode;
using VM12Util;
using System.Text;

namespace T12
{
    public class AST
    {
        public readonly Dictionary<StringRef, (ASTFile File, FileInfo FileInfo)> Files;
        
        public AST(Dictionary<StringRef, (ASTFile File, FileInfo FileInfo)> files)
        {
            this.Files = files;
        }

        public static AST Parse(FileInfo inFile, Dictionary<StringRef, FileInfo> dirFiles)
        {
            Compiler.Watch.Restart();
            string fileData = File.ReadAllText(inFile.FullName);
            Compiler.Watch.Stop();
            Compiler.MiscTime += Compiler.Watch.ElapsedTicks;
            
            // FIXME: Just add the current file as an import and just do all the logic in the while loop

            Compiler.Watch.Restart();
            // We can probably do this better!
            // Because we will want to emit comments to the assembly
            Tokenizer tokenizer = new Tokenizer(inFile.FullName, fileData);
            List<Token> TokenList = tokenizer.Tokenize();
            Compiler.Watch.Stop();
            Compiler.TokenizerTime += Compiler.Watch.ElapsedTicks;
            
            Dictionary<StringRef, (ASTFile File, FileInfo FileInfo)> files = new Dictionary<StringRef, (ASTFile File, FileInfo FileInfo)>(Compiler.CurrentAST.Files);
            
            Compiler.Watch.Restart();
            Queue<Token> Tokens = new Queue<Token>(TokenList.Where(tok => tok.Type != TokenType.Comment));
            var file = ASTFile.Parse(Tokens);
            files.Add((StringRef)inFile.Name, (file, inFile));
            Compiler.Watch.Stop();
            Compiler.ParserTime += Compiler.Watch.ElapsedTicks;
            
            List<ASTImportDirective> imports = file.Directives.Where(d => d is ASTImportDirective).Cast<ASTImportDirective>().ToList();

            while (imports.Count > 0)
            {
                var import = imports.First();
                if (files.ContainsKey(import.File) == false)
                {
                    // We have not parsed this file yet
                    if (dirFiles.TryGetValue(import.File, out var importFile) == false)
                        // TODO: Better error thing here!
                        Emitter.Fail(import.Trace, $"Could not import '{import.File}', did not find file '{import.File}'!");
                    
                    // Parse the file!
                    Compiler.Watch.Restart();
                    string importedFileData = File.ReadAllText(importFile.FullName);
                    Compiler.Watch.Stop();
                    Compiler.MiscTime += Compiler.Watch.ElapsedTicks;

                    Compiler.Watch.Restart();
                    tokenizer = new Tokenizer(importFile.FullName, importedFileData);
                    TokenList = tokenizer.Tokenize();
                    Compiler.Watch.Stop();
                    Compiler.TokenizerTime += Compiler.Watch.ElapsedTicks;
                    
                    Compiler.Watch.Restart();
                    Queue<Token> tokens = new Queue<Token>(TokenList.Where(tok => tok.Type != TokenType.Comment));
                    var importAST = ASTFile.Parse(tokens);
                    Compiler.Watch.Stop();
                    Compiler.ParserTime += Compiler.Watch.ElapsedTicks;

                    StringRef importNameRef = (StringRef)importFile.Name;
                    files.Add(importNameRef, (importAST, importFile));
                    imports.AddRange(files[importNameRef].File.Directives.Where(d => d is ASTImportDirective).Cast<ASTImportDirective>());

                    Compiler.CompiledFiles += 1;
                    Compiler.CompiledLines += importedFileData.CountLines();
                }
                imports.Remove(import);
            }
            
            return new AST(files);
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
                File = tok.FilePath,
                StartLine = tok.Line,
                EndLine = tok.Line,
            };
        }

        public static TraceData From(Token from, Token to)
        {
            return new TraceData
            {
                File = from.FilePath,
                StartLine = from.Line,
                EndLine = to.Line,
            };
        }

        public static TraceData From(TraceData trace, Token to)
        {
            return new TraceData
            {
                File = trace.File,
                StartLine = trace.StartLine,
                EndLine = to.Line,
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

        public static void Fail(Token tok, string error)
        {
            Compiler.CurrentErrorHandler?.Invoke(Compiler.MessageData.FromError(tok, error));

            throw new FormatException($"Error in file {Path.GetFileName(tok.FilePath)} at line {tok.Line}: '{error}'");
        }

        internal static void Warning(Token tok, string warning)
        {
            Compiler.CurrentErrorHandler?.Invoke(Compiler.MessageData.FromWarning(tok, warning));

            Console.WriteLine($"WARNING ({Path.GetFileName(tok.FilePath)}:{tok.Line}): '{warning}'");
        }
    }
    
    public class ASTFile : ASTNode
    {
        public readonly List<ASTDirective> Directives;
        public readonly List<ASTFunction> Functions;

        public ASTFile(TraceData trace, List<ASTDirective> Directives, List<ASTFunction> Functions) : base(trace)
        {
            this.Directives = Directives;
            this.Functions = Functions;
        }

        public static ASTFile Parse(Queue<Token> Tokens)
        {
            List<ASTDirective> directives = new List<ASTDirective>();
            List<ASTFunction> functions = new List<ASTFunction>();
            
            var trace = new TraceData
            {
                File = Tokens.Peek().FilePath,
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
                    // NOTE: Generic functions get parsed here too
                    var function = ASTFunction.Parse(Tokens);
                    functions.Add(function);
                }
                else if (peek.Type == TokenType.Keyword_Interrupt)
                {
                    var interrupt = ASTInterrupt.Parse(Tokens);
                    functions.Add(interrupt);
                }
                else if (peek.Type == TokenType.Keyword_Intrinsic)
                {
                    var intrinsic = ASTIntrinsicFunction.Parse(Tokens);
                    functions.Add(intrinsic);
                }
                else
                {
                    Fail(peek, $"Unknown token {peek} in program!");
                }
            }
            
            if (Tokens.Count > 0) Fail(Tokens.Peek(), $"There was '{Tokens.Count}' tokens left that couldn't be parsed. Next token: '{Tokens.Peek()}'");
            
            return new ASTFile(trace, directives, functions);
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
                            File = publicTok.FilePath,
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
                            File = privateTok.FilePath,
                            StartLine = privateTok.Line,
                            EndLine = colonTok.Line,
                        };

                        return new ASTVisibilityDirective(trace, false);
                    }
                case TokenType.Keyword_Use:
                    return ASTUseDirective.Parse(Tokens);
                case TokenType.Keyword_Import:
                    return ASTImportDirective.Parse(Tokens);
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
        public readonly StringRef FileName;

        public ASTUseDirective(TraceData trace, StringRef filename) : base(trace)
        {
            FileName = filename;
        }

        public static new ASTUseDirective Parse(Queue<Token> Tokens)
        {
            var useTok = Tokens.Dequeue();
            if (useTok.Type != TokenType.Keyword_Use) Fail(useTok, "Exptected 'use'!");

            var peek = Tokens.Peek();
            StringRef name = peek.Value;
            name.Length = 0;
            while (peek.Type != TokenType.Semicolon)
            {
                var tok = Tokens.Dequeue();
                name.Length += tok.Value.Length;

                peek = Tokens.Peek();
            }

            // Dequeue semicolon
            var semicolonTok = Tokens.Dequeue();
            if (semicolonTok.Type != TokenType.Semicolon) Fail(semicolonTok, "Expected ';'!");

            var trace = new TraceData
            {
                File = useTok.FilePath,
                StartLine = useTok.Line,
                EndLine = semicolonTok.Line,
            };
            
            return new ASTUseDirective(trace, name);
        }
    }

    public class ASTImportDirective : ASTDirective, IEquatable<ASTImportDirective>
    {
        public readonly StringRef File;

        public ASTImportDirective(TraceData trace, StringRef file) : base(trace)
        {
            File = file;
        }

        public static new ASTImportDirective Parse(Queue<Token> Tokens)
        {
            var importTok = Tokens.Dequeue();
            if (importTok.Type != TokenType.Keyword_Import) Fail(importTok, "Expected 'import'!");

            // NOTE: This is not great tbh, because it will
            // eat the entire file before it knows it's wrong.
            // We could really like a more robust way to parse filenames.
            var peek = Tokens.Peek();
            StringRef file = peek.Value;
            file.Length = 0;
            while (peek.Type != TokenType.Semicolon)
            {
                var tok = Tokens.Dequeue();
                file.Length += tok.Value.Length;

                peek = Tokens.Peek();
            }
            
            var endTok = Tokens.Dequeue();
            if (endTok.Type != TokenType.Semicolon) Fail(endTok, "Expected ';'!");
            
            var trace = new TraceData
            {
                File = importTok.FilePath,
                StartLine = importTok.Line,
                EndLine = endTok.Line,
            };

            return new ASTImportDirective(trace, file);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ASTImportDirective);
        }

        public bool Equals(ASTImportDirective other)
        {
            return other != null &&
                   File == other.File;
        }

        public override int GetHashCode()
        {
            var hashCode = 780891818;
            hashCode = hashCode * -1521134295 + EqualityComparer<StringRef>.Default.GetHashCode(File);
            return hashCode;
        }

        public static bool operator ==(ASTImportDirective directive1, ASTImportDirective directive2)
        {
            return EqualityComparer<ASTImportDirective>.Default.Equals(directive1, directive2);
        }

        public static bool operator !=(ASTImportDirective directive1, ASTImportDirective directive2)
        {
            return !(directive1 == directive2);
        }
    }

    public class ASTExternFunctionDirective : ASTDirective
    {
        public readonly StringRef FunctionName;
        public readonly List<(ASTType Type, StringRef Name)> Parameters;
        public readonly ASTType ReturnType;

        public ASTExternFunctionDirective(TraceData trace, StringRef functionName, List<(ASTType, StringRef)> parameters, ASTType returnType) : base(trace)
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
            StringRef funcName = nameTok.Value;

            List<(ASTType, StringRef)> parameters = new List<(ASTType, StringRef)>();

            // Confirm that we have a opening parenthesis
            var openParenTok = Tokens.Dequeue();
            if (openParenTok.Type != TokenType.Open_parenthesis) Fail(openParenTok, "Expected '('");

            // We peeked so we can handle this loop more uniform by always dequeueing at the start
            var peek = Tokens.Peek();
            while (peek.Type != TokenType.Close_parenthesis)
            {
                ASTType type = ASTType.Parse(Tokens);

                // FIXME: if there is no name this will result in weird behaviour...
                StringRef name = default;
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
                File = externTok.FilePath,
                StartLine = externTok.Line,
                EndLine = semicolonTok.Line,
            };

            return new ASTExternFunctionDirective(trace, funcName, parameters, retType);
        }
    }

    public class ASTExternConstantDirective : ASTDirective
    {
        public readonly ASTType Type;
        public readonly StringRef Name;

        public ASTExternConstantDirective(TraceData trace, ASTType type, StringRef name) : base(trace)
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
            StringRef name = nameTok.Value;

            var semicolonTok = Tokens.Dequeue();
            if (semicolonTok.Type != TokenType.Semicolon) Fail(semicolonTok, "Expected semicolon!");

            var trace = new TraceData
            {
                File = externTok.FilePath,
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
        public readonly StringRef Name;
        public readonly ASTExpression Value;

        public ASTConstDirective(TraceData trace, ASTType type, StringRef name, ASTExpression value) : base(trace)
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
            StringRef name = nameTok.Value;

            var equalsTok = Tokens.Dequeue();
            if (equalsTok.Type != TokenType.Equal) Fail(equalsTok, $"Expected equals!");

            var value = ASTExpression.Parse(Tokens);

            // If it is a constant array, we set the size of the type
            if (type is ASTArrayType arrayType && value is ASTArrayLitteral arrayLit)
                type = new ASTFixedArrayType(arrayType.Trace, arrayType.BaseType, 
                            ASTNumericLitteral.From(arrayLit.Trace, arrayLit.Values.Count));

            var semicolonTok = Tokens.Dequeue();
            if (semicolonTok.Type != TokenType.Semicolon) Fail(semicolonTok, "Expected semicolon!");

            var trace = new TraceData
            {
                File = constTok.FilePath,
                StartLine = constTok.Line,
                EndLine = semicolonTok.Line,
            };

            return new ASTConstDirective(trace, type, name, value);
        }
    }

    public class ASTGlobalDirective : ASTDirective
    {
        public readonly ASTType Type;
        public readonly StringRef Name;

        public ASTGlobalDirective(TraceData trace, ASTType type, StringRef name) : base(trace)
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
            StringRef name = nameTok.Value;
            
            var semicolonTok = Tokens.Dequeue();
            if (semicolonTok.Type != TokenType.Semicolon) Fail(semicolonTok, "Expected semicolon!");

            var trace = new TraceData
            {
                File = globalTok.FilePath,
                StartLine = globalTok.Line,
                EndLine = semicolonTok.Line,
            };

            return new ASTGlobalDirective(trace, type, name);
        }
    }

    public class ASTExternGlobalDirective : ASTGlobalDirective
    {
        public readonly ASTGlobalDirective GlobalDirective;

        public ASTExternGlobalDirective(TraceData trace, ASTType type, StringRef name, ASTGlobalDirective globalDirective) : base(trace, type, name)
        {
            GlobalDirective = globalDirective;
        }
    }

    // NOTE: This should really be a ASTDeclaration and not really a ASTDirective
    // But that is somewhat hard to implement
    public class ASTStructDeclarationDirective : ASTDirective
    {
        public readonly StringRef Name;
        public readonly ASTType DeclaredType;
        
        public ASTStructDeclarationDirective(TraceData trace, StringRef name, ASTType type) : base(trace)
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
            StringRef name = nameTok.Value;

            List<StringRef> genericNames = null;
            if (Tokens.Peek().Type == TokenType.LessThan)
            {
                Tokens.Dequeue();

                genericNames = new List<StringRef>();

                // Parse the generic names list
                while (Tokens.Peek().Type != TokenType.GreaterThan)
                {
                    var genNameTok = Tokens.Dequeue();
                    if (genNameTok.Type != TokenType.Identifier) Fail(genNameTok, $"Expected generic name! Got '{genNameTok}'");
                    if (genericNames.Contains(genNameTok.Value)) Fail(genNameTok, $"There is already a generic parameter called '{genNameTok.Value}'");
                    genericNames.Add(genNameTok.Value);

                    var contToken = Tokens.Peek();
                    if (contToken.Type != TokenType.Comma && contToken.Type == TokenType.GreaterThan) break;
                    else if (contToken.Type != TokenType.Comma) Fail(contToken, "Expected ',' or a '>'");
                    // Dequeue the comma
                    Tokens.Dequeue();
                }

                if (genericNames.Count == 0) Fail(Tokens.Peek(), "Generic structs must define atleast one generic name!");

                var closeAngleTok = Tokens.Dequeue();
                if (closeAngleTok.Type != TokenType.GreaterThan) Fail(closeAngleTok, "Expected '>'");

                // Here we create the new name for the type
                //name = $"{name}<{string.Join(",", genericNames)}>";
            }

            var defTok = Tokens.Dequeue();
            switch (defTok.Type)
            {
                case TokenType.Equal:
                    {
                        // This is a type alias

                        ASTType type = ASTType.Parse(Tokens);

                        var semicolonTok = Tokens.Dequeue();
                        if (semicolonTok.Type != TokenType.Semicolon) Fail(semicolonTok, "Expected semicolon!");

                        var trace = new TraceData
                        {
                            File = structTok.FilePath,
                            StartLine = structTok.Line,
                            EndLine = semicolonTok.Line,
                        };

                        if (genericNames != null)
                            return new ASTStructDeclarationDirective(trace, name, new ASTGenericType(trace, ASTAliasedType.Of(name, type), genericNames));
                        else
                            return new ASTStructDeclarationDirective(trace, name, ASTAliasedType.Of(name, type));
                    }
                case TokenType.Open_brace:
                    {
                        List<(ASTType Type, StringRef Name)> members = new List<(ASTType Type, StringRef Name)>();

                        var peek = Tokens.Peek();
                        while (peek.Type != TokenType.Close_brace)
                        {
                            var type = ASTType.Parse(Tokens);
                            var memberNameTok = Tokens.Dequeue();
                            if (memberNameTok.IsIdentifier == false) Fail(memberNameTok, $"Expected member name! Got {memberNameTok}!");
                            StringRef memberName = memberNameTok.Value;

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
                            File = structTok.FilePath,
                            StartLine = structTok.Line,
                            EndLine = closeBrace.Line,
                        };

                        if (genericNames != null)
                            return new ASTStructDeclarationDirective(trace, name, new ASTGenericType(trace, new ASTStructType(trace, name, members), genericNames));
                        else
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
        public readonly StringRef Name;
        public readonly ASTType ReturnType;
        public readonly List <(ASTType Type, StringRef Name)> Parameters;
        
        public readonly List<ASTBlockItem> Body;

        public ASTFunction(TraceData trace, StringRef Name, ASTType ReturnType, List<(ASTType, StringRef)> Parameters, List<ASTBlockItem> Body) : base(trace)
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
            StringRef name = identTok.Value;

            List<StringRef> genericNames = null;
            if (Tokens.Peek().Type == TokenType.LessThan)
            {
                Tokens.Dequeue();

                genericNames = new List<StringRef>();

                // Parse the generic names list
                while (Tokens.Peek().Type != TokenType.GreaterThan)
                {
                    var nameTok = Tokens.Dequeue();
                    if (nameTok.Type != TokenType.Identifier) Fail(nameTok, $"Expected generic name! Got '{nameTok}'");
                    if (genericNames.Contains(nameTok.Value)) Fail(nameTok, $"There is already a generic parameter called '{nameTok.Value}'");
                    genericNames.Add(nameTok.Value);

                    var contToken = Tokens.Peek();
                    if (contToken.Type != TokenType.Comma && contToken.Type == TokenType.GreaterThan) break;
                    else if (contToken.Type != TokenType.Comma) Fail(contToken, "Expected ',' or a '>'");
                    // Dequeue the comma
                    Tokens.Dequeue();
                }

                if (genericNames.Count == 0) Fail(Tokens.Peek(), "Generic function must define atleast one generic name!");

                var closeAngleTok = Tokens.Dequeue();
                if (closeAngleTok.Type != TokenType.GreaterThan) Fail(closeAngleTok, "Expected '>'");
            }

            List<(ASTType Type, StringRef Name)> parameters = new List<(ASTType Type, StringRef Name)>();

            // Confirm that we have a opening parenthesis
            var openParenTok = Tokens.Dequeue();
            if (openParenTok.Type != TokenType.Open_parenthesis) Fail(openParenTok, "Expected '('");
            
            // We peeked so we can handle this loop more uniform by always dequeueing at the start
            while (Tokens.Peek().Type != TokenType.Close_parenthesis)
            {
                ASTType type = ASTType.Parse(Tokens);
                
                var paramIdentTok = Tokens.Dequeue();
                if (paramIdentTok.IsIdentifier == false) Fail(paramIdentTok, "Expected identifier!");
                StringRef param_name = paramIdentTok.Value;

                parameters.Add((type, param_name));

                var contToken = Tokens.Peek();
                if (contToken.Type != TokenType.Comma && contToken.Type == TokenType.Close_parenthesis) break;
                else if (contToken.Type != TokenType.Comma) Fail(contToken, "Expected ',' or a ')'");
                // Dequeue the comma
                Tokens.Dequeue();
            }

            var closeParenTok = Tokens.Dequeue();
            if (closeParenTok.Type != TokenType.Close_parenthesis) Fail(closeParenTok, "Expected ')'");

            var openBraceTok = Tokens.Dequeue();
            if (openBraceTok.Type != TokenType.Open_brace) Fail(openBraceTok, "Expected '{'");

            List<ASTBlockItem> body = new List<ASTBlockItem>();

            while (Tokens.Peek().Type != TokenType.Close_brace)
            {
                body.Add(ASTBlockItem.Parse(Tokens));
                // This is a .net core 2+ only feature
                //if (!Tokens.TryPeek(out peek)) Fail("Expected a closing brace!");
            }

            // Dequeue the closing brace
            var closeBraceTok = Tokens.Dequeue();
            if (closeBraceTok.Type != TokenType.Close_brace) Fail(closeBraceTok, "Expected closing brace");

            var trace = new TraceData
            {
                File = typeTok.FilePath,
                StartLine = typeTok.Line,
                EndLine = closeBraceTok.Line,
            };

            if (genericNames != null)
                return new ASTGenericFunction(trace, name, returnType, genericNames, parameters, body);
            else
                return new ASTFunction(trace, name, returnType, parameters, body);
        }
    }
    
    public class ASTInterrupt : ASTFunction
    {
        public readonly InterruptType Type;
        
        public ASTInterrupt(TraceData trace, InterruptType type, List<(ASTType Type, StringRef Name)> parameters, List<ASTBlockItem> body) : base(trace, (StringRef)InterruptTypeToName(type), ASTBaseType.Void, parameters, body)
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
                case InterruptType.h_timer:
                    return "h_timer";
                case InterruptType.v_blank:
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
                case InterruptType.h_timer:
                    return H_TimerParamList;
                case InterruptType.v_blank:
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
            // FIXME: Here we might want to roll our own parsing to avoid the ToString() call, but it's fine for now. - 2019-10-28
            if (Enum.TryParse(interruptTypeTok.Value.ToString(), out InterruptType interruptType) == false) Fail(interruptTypeTok, $"'{interruptTypeTok.Value}' is not a valid interrupt type!");

            if (interruptType == InterruptType.stop)
                Fail(interruptTypeTok, "Cannot define a interupt procedure for the interrupt stop");

            // FIXME!! Validate params and parse body!!!!
            var openParenthesis = Tokens.Dequeue();
            if (openParenthesis.Type != TokenType.Open_parenthesis) Fail(openParenthesis, "Expected '('!");

            List<(ASTType Type, StringRef Name)> parameters = new List<(ASTType Type, StringRef Name)>();

            // This is to provide accurate debug info
            List<Token> paramTokens = new List<Token>();

            // We peeked so we can handle this loop more uniform by always dequeueing at the start
            var peek = Tokens.Peek();
            while (peek.Type != TokenType.Close_parenthesis)
            {
                ASTType type = ASTType.Parse(Tokens);

                var paramIdentTok = Tokens.Dequeue();
                if (paramIdentTok.IsIdentifier == false) Fail(paramIdentTok, "Expected identifier!");
                StringRef param_name = paramIdentTok.Value;

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
                File = interruptTok.FilePath,
                StartLine = interruptTok.Line,
                EndLine = closeBraceTok.Line,
            };

            return new ASTInterrupt(trace, interruptType, parameters, body);
        }
    }

    public class ASTIntrinsicFunction : ASTFunction
    {
        public new readonly List<ASTStringLitteral> Body;

        public ASTIntrinsicFunction(TraceData data, StringRef name, ASTType returnType, List<(ASTType, StringRef)> parameters, List<ASTStringLitteral> body) : base(data, name, returnType, parameters, null)
        {
            Body = body;
        }

        public static new ASTIntrinsicFunction Parse(Queue<Token> Tokens)
        {
            var intrinsicTok = Tokens.Dequeue();
            if (intrinsicTok.Type != TokenType.Keyword_Intrinsic) Fail(intrinsicTok, "Expected intrinsic!");

            var typeTok = Tokens.Peek();
            if (typeTok.IsType == false) Fail(typeTok, "Expected a type!");
            var returnType = ASTType.Parse(Tokens);

            var identTok = Tokens.Dequeue();
            if (identTok.Type != TokenType.Identifier) Fail(identTok, "Expected an identifier!");
            StringRef name = identTok.Value;

            List<(ASTType Type, StringRef Name)> parameters = new List<(ASTType Type, StringRef Name)>();

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
                StringRef param_name = paramIdentTok.Value;

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

            List<ASTStringLitteral> body = new List<ASTStringLitteral>();
            while (Tokens.Peek().Type != TokenType.Close_brace)
            {
                if (Tokens.Peek().Type != TokenType.String_Litteral)
                    Fail(Tokens.Peek(), $"Assembly statements can only contain assembly strings! Got {Tokens.Peek()}");

                var string_lit = ASTStringLitteral.Parse(Tokens);
                body.Add(string_lit);
            }

            var closeBraceTok = Tokens.Dequeue();
            if (closeBraceTok.Type != TokenType.Close_brace) Fail(closeBraceTok, "Expected closing brace");

            var trace = new TraceData
            {
                File = intrinsicTok.FilePath,
                StartLine = closeBraceTok.Line,
                EndLine = closeBraceTok.Line,
            };

            return new ASTIntrinsicFunction(trace, name, returnType, parameters, body);
        }
    }

    public class ASTGenericFunction : ASTFunction
    {
        // NOTE: We might want to be able to match: T somefunc<*T>()
        // Where we could then do: somefunc<*Thing>() and we would get back a Thing
        public readonly List<StringRef> GenericNames;

        public ASTGenericFunction(TraceData trace, StringRef name, ASTType returnType, List<StringRef> genericNames, List<(ASTType, StringRef)> parameters, List<ASTBlockItem> body) : base(trace, name, returnType, parameters, body)
        {
            GenericNames = genericNames;
        }
    }

    public abstract class ASTBlockItem : ASTNode
    {
        public ASTBlockItem(TraceData trace) : base(trace) { }

        public static ASTBlockItem Parse(Queue<Token> Tokens)
        {
            var peek = Tokens.Peek();

            if (peek.IsBaseType || peek.IsTypePrefix)
            {
                // Here we know its going to ba a variable declaration
                return ASTDeclaration.Parse(Tokens);
            }
            else if (peek.IsIdentifier)
            {
                var namePeek = Tokens.ElementAt(1);

                Token semicolonEqualsPeek;
                if (namePeek.IsIdentifier)
                {
                    semicolonEqualsPeek = Tokens.ElementAt(2);
                    if (semicolonEqualsPeek.Type == TokenType.Semicolon || semicolonEqualsPeek.Type == TokenType.Equal)
                    {
                        //  This is a variable declaration of a complex type!
                        return ASTDeclaration.Parse(Tokens);
                    }
                }
                else if (namePeek.Type == TokenType.DoubleColon)
                {
                    // Is this a extern type variable declaration?
                    semicolonEqualsPeek = Tokens.ElementAt(4);
                    if (semicolonEqualsPeek.Type == TokenType.Semicolon || semicolonEqualsPeek.Type == TokenType.Equal)
                    {
                        //  This is a variable declaration of a complex type!
                        return ASTDeclaration.Parse(Tokens);
                    }
                }
                else if (namePeek.Type == TokenType.LessThan)
                {
                    // Here we need to figure out if this is a generic variable declaration or if this an expression using generics or just comparing two variables

                    int openAngles = 1;
                    int index = 2;
                    while (true)
                    {
                        if (Tokens.Count - 1 < index) Fail(namePeek, $"We read the whole file and could not find a matching closing angle bracket...");
                        var peekGeneric = Tokens.ElementAt(index++);

                        if (peekGeneric.IsType || peekGeneric.Type == TokenType.Comma || peekGeneric.Type == TokenType.LessThan ||
                            // FIXME: Do proper parsing for fixed arrays!
                            peekGeneric.Type == TokenType.Close_squre_bracket || peekGeneric.Type == TokenType.Numeric_Litteral)
                        {
                            if (peekGeneric.Type == TokenType.LessThan) openAngles++;
                            continue;
                        }
                        else if (peekGeneric.Type == TokenType.GreaterThan || peekGeneric.Type == TokenType.ShiftRight)
                        {
                            openAngles -= peekGeneric.Type == TokenType.GreaterThan ? 1 : 2;

                            // FIXME: Better error message?
                            if (openAngles < 0) Fail(peekGeneric, "Angle bracket missamatch!");

                            // Here we've found all matching angles, so now we can look at what comes after the bracket
                            if (openAngles == 0)
                            {
                                if (Tokens.ElementAt(index).IsIdentifier)
                                {
                                    return ASTVariableDeclaration.Parse(Tokens);
                                }
                                else
                                {
                                    return ASTStatement.Parse(Tokens);
                                }
                            }
                        }
                        else
                        {
                            // This cannot be a generic thing, so we just break here and default to parsing the rest as a statement.
                            break;
                        }
                    }
                }
            }

            return ASTStatement.Parse(Tokens);
        }
    }
    
    #region Declarations

    public abstract class ASTDeclaration : ASTBlockItem
    {
        public ASTDeclaration(TraceData trace) : base(trace) { }

        public static new ASTDeclaration Parse(Queue<Token> Tokens)
        {
            return ASTVariableDeclaration.Parse(Tokens);
        }
    }

    public class ASTVariableDeclaration : ASTDeclaration
    {
        public readonly ASTType Type;
        public readonly StringRef VariableName;
        public readonly ASTExpression Initializer;

        public ASTVariableDeclaration(TraceData trace, ASTType Type, StringRef VariableName, ASTExpression Initializer) : base(trace)
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
            StringRef name = identTok.Value;

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
            switch (peek.Type)
            {
                case TokenType.Semicolon:
                    return ASTEmptyStatement.Parse(Tokens);
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
                File = semicolonTok.FilePath,
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
        public readonly List<StringRef> VariableNames;
        public readonly ASTExpression AssignmentExpression;

        public ASTAssignmentStatement(TraceData trace, List<StringRef> VariableNames, ASTExpression AssignmentExpression) : base(trace)
        {
            this.VariableNames = VariableNames;
            this.AssignmentExpression = AssignmentExpression;
        }

        public static new ASTAssignmentStatement Parse(Queue<Token> Tokens)
        {
            List<StringRef> ids = new List<StringRef>();

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
                File = identTok.FilePath,
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
                File = retTok.FilePath,
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
                File = ifTok.FilePath,
                StartLine = ifTok.Line,
                EndLine = ifFalse?.Trace.EndLine ?? ifTrue.Trace.EndLine,
            };

            return new ASTIfStatement(trace, expr, ifTrue, ifFalse);
        }
    }

    public class ASTCompoundStatement : ASTStatement
    {
        public readonly List<ASTBlockItem> Block;

        public ASTCompoundStatement(TraceData trace, List<ASTBlockItem> Block) : base(trace)
        {
            this.Block = Block;
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
                File = openBraceTok.FilePath,
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
                File = forTok.FilePath,
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
                File = whileTok.FilePath,
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
                File = doTok.FilePath,
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

            var semicolonTok = Tokens.Dequeue();
            if (semicolonTok.Type != TokenType.Semicolon) Fail(semicolonTok, "Expected keyword semicolon!");

            var trace = TraceData.From(breakTok, semicolonTok);

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

            var semicolonTok = Tokens.Dequeue();
            if (semicolonTok.Type != TokenType.Semicolon) Fail(semicolonTok, "Expected keyword semicolon!");

            var trace = TraceData.From(continueTok, semicolonTok);

            return new ASTContinueStatement(trace);
        }
    }

    #endregion

    #region Expressions

    public abstract class ASTExpression : ASTNode
    {
        public ASTExpression(TraceData trace) : base(trace) { }

        // NOTE: Is there a way to avoid doing this crazy call chain to parse all of this?
        // It might actually increase performance (tokenizer should be fixed first though)

        public static ASTExpression Parse(Queue<Token> Tokens)
        {
            // We start by parsing logical or which has the lowest precedence
            // It will then go through all levels of precedence
            return ParseConditional(Tokens);
        }

        // The tenth level of precedence (?:) (=><=:)
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
            else if (peek.Type == TokenType.Contains)
            {
                // Dequeue the contains token
                Tokens.Dequeue();

                var lowerBound = ASTExpression.Parse(Tokens);

                var colonTok = Tokens.Dequeue();
                if (colonTok.Type != TokenType.Colon) Fail(colonTok, "Expected a colon!");

                var upperBound = ASTExpression.Parse(Tokens);

                var trace = new TraceData
                {
                    File = expr.Trace.File,
                    StartLine = expr.Trace.StartLine,
                    EndLine = upperBound.Trace.EndLine,
                };

                expr = new ASTContainsExpression(trace, expr, lowerBound, upperBound);
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
            while (peek.Type == TokenType.LessThan ||
                peek.Type == TokenType.GreaterThan ||
                peek.Type == TokenType.LessThanOrEqual ||
                peek.Type == TokenType.GreaterThanOrEqual)
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
                    File = opTok.FilePath,
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
                if(peekActionTok.Type == TokenType.Open_parenthesis)
                {
                    expr = ASTFunctionCall.Parse(Tokens);
                }
                else if (peekActionTok.Type == TokenType.LessThan)
                {
                    // Here we need to check if this is a generic function call or a comparison

                    // Set it to null so it is not undefined later
                    expr = null;

                    int openAngles = 1;

                    // We do a linear seach forward to check if this is a comparison or not
                    IEnumerable<Token> check = Tokens;
                    IEnumerator<Token> en = Tokens.Skip(2).GetEnumerator();
                    while (en.MoveNext())
                    {
                        var peekGeneric = en.Current;

                        if (peekGeneric.IsType || peekGeneric.Type == TokenType.Comma || peekGeneric.Type == TokenType.LessThan ||
                            // FIXME: Do proper parsing for fixed arrays!
                            peekGeneric.Type == TokenType.Close_squre_bracket || peekGeneric.Type == TokenType.Numeric_Litteral)
                        {
                            if (peekGeneric.Type == TokenType.LessThan) openAngles++;
                            
                            continue;
                        }
                        else if (peekGeneric.Type == TokenType.GreaterThan || peekGeneric.Type == TokenType.ShiftRight)
                        {
                            openAngles -= peekGeneric.Type == TokenType.GreaterThan ? 1 : 2;

                            // FIXME: For now we do this, but it's probably not an error
                            if (openAngles < 0) Fail(peekGeneric, "Angle bracket missamatch!");

                            if (openAngles == 0)
                            {
                                if (en.MoveNext())
                                {
                                    if (en.Current.Type == TokenType.Open_parenthesis)
                                    {
                                        // This is actually a generic function call!!
                                        expr = ASTGenericFunctionCall.Parse(Tokens);
                                        break;
                                    }
                                    else if (en.Current.Type == TokenType.Open_brace)
                                    {
                                        // This is actually a generic struft litteral
                                        expr = ASTStructLitteral.Parse(Tokens);
                                        break;
                                    }
                                }
                            }
                        }
                        else
                        {
                            // This can't be a generic function call, so it must be a comparison
                            expr = ASTVariableExpression.Parse(Tokens);
                            break;
                        }
                    }
                    if (expr == null) Fail(peekActionTok, "THIS SHOULD NOT HAPPEN!!!");
                }
                else if (peekActionTok.Type == TokenType.Open_brace)
                {
                    // This is a struct litteral!
                    return ASTStructLitteral.Parse(Tokens);
                }
                // FIXME: Make a robust way to detect that a variable expression has ended!
                else if (peekActionTok.IsAssignmentOp || peekActionTok.IsPostfixOperator || peekActionTok.IsBinaryOp ||
                    peekActionTok.Type == TokenType.Comma || peekActionTok.Type == TokenType.Close_parenthesis || 
                    peekActionTok.Type == TokenType.Semicolon || peekActionTok.Type == TokenType.Close_squre_bracket ||
                    peekActionTok.Type == TokenType.Contains || peekActionTok.Type == TokenType.Colon || 
                    peekActionTok.Type == TokenType.Questionmark || peekActionTok.Type == TokenType.Close_brace)
                {
                    // This is just a variable expression.
                    expr = ASTVariableExpression.Parse(Tokens);
                }
                else
                {
                    Fail(peekActionTok, $"Unknown variable operation '{peekActionTok}'!");
                    expr = default;
                }

                return ParsePostfix(Tokens, expr);
            }
            else if (peek.Type == TokenType.Keyword_Assembly)
            {
                // Here we want to parse the assembly expression
                return ASTInlineAssemblyExpression.Parse(Tokens);
            }
            else if (peek.Type == TokenType.Keyword_Cast)
            {
                return ASTExplicitCast.Parse(Tokens);
            }
            else if (peek.Type == TokenType.Keyword_Sizeof)
            {
                return ASTSizeofTypeExpression.Parse(Tokens);
            }
            else if (peek.Type == TokenType.Keyword_Typeof)
            {
                return ASTTypeOfExpression.Parse(Tokens);
            }
            else if (peek.Type == TokenType.Keyword_Default)
            {
                return ASTDefaultExpression.Parse(Tokens);
            }
            else if (peek.Type == TokenType.And)
            {
                return ASTAddressOfExpression.Parse(Tokens);
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
                        targetExpr = ASTVirtualFunctionCall.Parse(Tokens, targetExpr);
                        break;
                    case TokenType.Open_square_bracket:
                        targetExpr = ASTPointerExpression.Parse(Tokens, targetExpr);
                        break;
                    case TokenType.Period:
                    case TokenType.Arrow:
                        targetExpr = ASTMemberExpression.Parse(Tokens, targetExpr);
                        break;
                    case TokenType.PlusPlus:
                        targetExpr = new ASTUnaryOp(TraceData.From(targetExpr.Trace, peek), ASTUnaryOp.UnaryOperationType.Increment_post, targetExpr);
                        Tokens.Dequeue();
                        break;
                    case TokenType.MinusMinus:
                        targetExpr = new ASTUnaryOp(TraceData.From(targetExpr.Trace, peek), ASTUnaryOp.UnaryOperationType.Decrement_post, targetExpr);
                        Tokens.Dequeue();
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
        public ASTType Type;
        public StringRef Value;
        
        public ASTLitteral(TraceData trace, ASTType type, StringRef value) : base(trace)
        {
            Type = type;
            Value = value;
        }

        public static new ASTLitteral Parse(Queue<Token> Tokens)
        {
            var peek = Tokens.Peek();
            switch (peek.Type)
            {
                // For now we don't allow leading plus
                //case TokenType.Plus:
                case TokenType.Minus:
                case TokenType.Numeric_Litteral:
                    return ASTNumericLitteral.Parse(Tokens);
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
                case TokenType.Keyword_Null:
                    var nullTok = Tokens.Dequeue();
                    return new ASTNullLitteral(TraceData.From(nullTok));
                case TokenType.Open_brace:
                    return ASTArrayLitteral.Parse(Tokens);
                default:
                    Fail(peek, $"Expected litteral, got '{peek}'");
                    return default;
            }
        }
        
        public override string ToString()
        {
            return Value.ToString();
        }
    }

    // FIXME: An idea here would be to refactor this so that a litteral is just a value with an associated numeric type.
    // This would mean that we don't need four different types to represent numeric litterals.
    // It would also make it easier to have "fuzzy" litteral types that can match whatever it needs to be.
    public abstract class ASTNumericLitteral : ASTLitteral
    {
        public enum NumberFormat
        {
            Decimal,
            Hexadecimal,
            Octal,
            Binary,
        }

        public readonly int IntValue;
        public readonly bool Signed;
        public readonly NumberFormat NumberFromat;

        public ASTNumericLitteral(TraceData trace, ASTBaseType type, StringRef value, int intValue, bool signed, NumberFormat numFormat) : base(trace, type, value)
        {
            IntValue = intValue;
            Signed = signed;
            NumberFromat = numFormat;
        }

        public static new ASTNumericLitteral Parse(Queue<Token> Tokens)
        {
            bool negate = false;
            if (Tokens.Peek().Type == TokenType.Minus)
            {
                negate = true;
                Tokens.Dequeue();
            }

            var peek = Tokens.Peek();
            if (peek.Type != TokenType.Numeric_Litteral)
                Fail(peek, $"Expected numeric litteral! Got {peek}");

            // FIXME: Make parsing that does not need the underscores removed!!
            // Remove any underscores
            string number = peek.Value.ToString().Replace("_", "");

            bool unsigned = false;
            bool forceDouble = false;
            int value;
            try
            {
                if (number.StartsWith("0x"))
                {
                    if (number.Length - 2 > 3) forceDouble = true;
                    value = Convert.ToInt32(number.Substring(2), 16);
                }
                else if (number.StartsWith("8x"))
                {
                    if (number.Length - 2 > 4) forceDouble = true;
                    value = Convert.ToInt32(number.Substring(2), 8);
                }
                else if (number.StartsWith("0b"))
                {
                    if (number.Length - 2 > 12) forceDouble = true;
                    value = Convert.ToInt32(number.Substring(2), 2);
                }
                else
                {
                    forceDouble = number.EndsWith("D", true, System.Globalization.CultureInfo.InvariantCulture);
                    unsigned = number.EndsWith("U", true, System.Globalization.CultureInfo.InvariantCulture);

                    number = number.TrimEnd('d', 'D', 'w', 'W', 'u', 'U');
                    // Try normal parsing!
                    value = Convert.ToInt32(number, 10);
                }
            }
            catch (Exception e)
            {
                Fail(peek, $"Could not parse number '{peek.Value}'. Got error '{e.Message}'.");
                return default;
            }

            // Dequeue the token we have just parsed
            Tokens.Dequeue();

            TraceData trace = TraceData.From(peek);

            NumberFormat format = GetFormat(peek.Value);

            // Negate if needed.
            if (negate) value = -value;

            // First figure out what type of thing we are creating.
            bool dword_type = forceDouble;

            // If we aren't forcing this to be a double we check to see what type it fits into
            if (forceDouble == false)
            {
                if (value < ASTWordLitteral.WORD_MIN_SIGNED_VALUE)
                    dword_type = true;
                else if (value > ASTWordLitteral.WORD_MAX_SIGNED_VALUE)
                    if (format != NumberFormat.Decimal && value <= ASTUWordLitteral.U_WORD_MAX_VALUE) ;
                    else dword_type = true;
            }
            
            // Check for overflow
            if (value < ASTDoubleWordLitteral.DOUBLE_WORD_MIN_SIGNED_VALUE)
                Fail(peek, $"Numeric litteral is less than '{ASTDoubleWordLitteral.DOUBLE_WORD_MIN_SIGNED_VALUE}' and does not fit in a double word!");
            else if (value > ASTDoubleWordLitteral.DOUBLE_WORD_MAX_SIGNED_VALUE)
                // Here we automatically promote this type
                if (format != NumberFormat.Decimal && value <= ASTUDoubleWordLitteral.U_DOUBLE_WORD_MAX_VALUE) ;
                else Fail(peek, $"Numeric litteral is larger than '{ASTDoubleWordLitteral.DOUBLE_WORD_MAX_SIGNED_VALUE}' and does not fit in a double word!");

            if (unsigned)
            {
                if (dword_type) return new ASTUDoubleWordLitteral(trace, peek.Value, value, format);
                else return new ASTUWordLitteral(trace, peek.Value, value, format);
            }
            else
            {
                if (dword_type) return new ASTDoubleWordLitteral(trace, peek.Value, value, format);
                else return new ASTWordLitteral(trace, peek.Value, value, format);
            }
        }

        public static ASTNumericLitteral From(TraceData trace, int value, NumberFormat format = NumberFormat.Decimal)
        {
            // FIXME!!! What if it's larger than max dword
            if (value > ASTWordLitteral.WORD_MAX_SIGNED_VALUE)
            {
                return new ASTDoubleWordLitteral(trace, (StringRef)FormatNumber(value, format), value, format);
            }
            else
            {
                return new ASTWordLitteral(trace, (StringRef)FormatNumber(value, format), value, format);
            }
        }

        public static NumberFormat GetFormat(StringRef number)
        {
            if (number.StartsWith("0x"))
                return NumberFormat.Hexadecimal;
            else if (number.StartsWith("8x"))
                return NumberFormat.Octal;
            else if (number.StartsWith("0b"))
                return NumberFormat.Binary;
            else
                return NumberFormat.Decimal;
        }

        /// <summary>
        /// Combines formats so that some intent can be keept when doing certain operations
        /// </summary>
        /// <param name="format1"></param>
        /// <param name="format2"></param>
        /// <returns></returns>
        public static NumberFormat CombineFormats(NumberFormat format1, NumberFormat format2)
        {
            // Hexadecimal has precedence
            if (format1 == NumberFormat.Hexadecimal || format2 == NumberFormat.Hexadecimal)
            {
                return NumberFormat.Hexadecimal;
            }
            else if (format1 == NumberFormat.Octal || format2 == NumberFormat.Octal)
            {
                return NumberFormat.Octal;
            }
            else if (format1 == NumberFormat.Binary || format2 == NumberFormat.Binary)
            {
                return NumberFormat.Binary;
            }
            else
            {
                return NumberFormat.Decimal;
            }
        }

        // FIXME: Negative numbers converted to anything other than decimal will be wrong!!! (Larger than 12/24 bit)
        public static string FormatNumber(int value, NumberFormat format)
        {
            switch (format)
            {
                case NumberFormat.Decimal:
                    return Convert.ToString(value, 10);
                case NumberFormat.Hexadecimal:
                    return "0x" + Convert.ToString(value, 16);
                case NumberFormat.Octal:
                    return "8x" + Convert.ToString(value, 8);
                case NumberFormat.Binary:
                    return "0b" + Convert.ToString(value, 2);
                default:
                    throw new ArgumentException($"Unknown number format {format}!");
            }
        }
    }

    public class ASTWordLitteral : ASTNumericLitteral
    {
        public const int WORD_MAX_SIGNED_VALUE = 0x7FF;
        public const int WORD_MIN_SIGNED_VALUE = -2048; // -2^11

        public ASTWordLitteral(TraceData trace, StringRef value, int intValue, NumberFormat numFormat) : base(trace, ASTBaseType.Word, value, intValue, true, numFormat) { }
        
        public new static ASTWordLitteral From(TraceData trace, int value, NumberFormat format = NumberFormat.Decimal)
        {
            // FIXME!!! Check negative values too!!
            // FIXME!!!! Make a proper check here!!!
            //Console.WriteLine("FIXME!!!! Make a proper ASTWordLitteral.From overflow check here!!!");
            /*if (value > WORD_MAX_SIGNED_VALUE)
            {
                Emitter.Fail(trace, $"The value '{value}' does not fit in a word!");
                return default;
            }
            else
            {*/
                return new ASTWordLitteral(trace, (StringRef)FormatNumber(value, format), value, format);
            //}
        }
    }

    public class ASTDoubleWordLitteral : ASTNumericLitteral
    {
        public const int DOUBLE_WORD_MAX_SIGNED_VALUE = 0x7FF_FFF;
        public const int DOUBLE_WORD_MIN_SIGNED_VALUE = -8_388_608; // -2^23

        public ASTDoubleWordLitteral(TraceData trace, StringRef value, int intValue, NumberFormat numFormat) : base(trace, ASTBaseType.DoubleWord, value, intValue, true, numFormat) { }

        // FIXME:!!!!! We really need to fix this unsigned stuff!
        public new static ASTDoubleWordLitteral From(TraceData trace, int value, NumberFormat format = NumberFormat.Decimal)
        {
            // Check for overflow
            if (value < DOUBLE_WORD_MIN_SIGNED_VALUE)
            {
                Emitter.Fail(trace, $"Numeric litteral value '{FormatNumber(value, format)}' is less than '{DOUBLE_WORD_MIN_SIGNED_VALUE}' and does not fit in a double word!");
                return default;
            }
            else if (value > DOUBLE_WORD_MAX_SIGNED_VALUE)
            {
                // Here we automatically promote this type
                if (format != NumberFormat.Decimal && value <= ASTUDoubleWordLitteral.U_DOUBLE_WORD_MAX_VALUE) ;
                else
                {
                    Emitter.Fail(trace, $"Numeric litteral value '{FormatNumber(value, format)}' is larger than '{DOUBLE_WORD_MAX_SIGNED_VALUE}' and does not fit in a double word!");
                    return default;
                }
            }

            return new ASTDoubleWordLitteral(trace, (StringRef)FormatNumber(value, format), value, format);
        }

        // FIXME: General tostring for numlit which does consideres format
        public override string ToString()
        {
            if (Value.EndsWith("d"))
            {
                return Value.Substring(0, Value.Length - 1).ToString();
            }
            else
            {
                return base.ToString();
            }
        }
    }

    public class ASTUWordLitteral : ASTNumericLitteral
    {
        public const int U_WORD_MAX_VALUE = 0xFFF;

        public ASTUWordLitteral(TraceData trace, StringRef value, int intValue, NumberFormat numFormat) : base(trace, ASTBaseType.UWord, value, intValue, false, numFormat) { }

        public new static ASTUWordLitteral From(TraceData trace, int value, NumberFormat format = NumberFormat.Decimal)
        {
            if (value < 0) {
                Emitter.Fail(trace, $"The unsigned value '{value}' cannot be negative!");
                return default;
            }
            else if (value > U_WORD_MAX_VALUE)
            {
                Emitter.Fail(trace, $"The value '{value}' does not fit in a word!");
                return default;
            }
            else
            {
                return new ASTUWordLitteral(trace, (StringRef)FormatNumber(value, format), value, format);
            }
        }

        // FIXME: Proper ToString() that trims the end so we output this correctly!!!
        public override string ToString()
        {
            if (Value.EndsWith("u") || Value.EndsWith("U"))
                return Value.Substring(0, Value.Length - 1).ToString();
            else
                return base.ToString();
        }
    }

    public class ASTUDoubleWordLitteral : ASTNumericLitteral
    {
        public const int U_DOUBLE_WORD_MAX_VALUE = 0xFFF_FFF;

        public ASTUDoubleWordLitteral(TraceData trace, StringRef value, int intValue, NumberFormat numFormat) : base(trace, ASTBaseType.UWord, value, intValue, false, numFormat) { }

        public new static ASTUDoubleWordLitteral From(TraceData trace, int value, NumberFormat format = NumberFormat.Decimal)
        {
            if (value < 0)
            {
                Emitter.Fail(trace, $"The unsigned value '{value}' cannot be negative!");
                return default;
            }
            else if (value > U_DOUBLE_WORD_MAX_VALUE)
            {
                Emitter.Fail(trace, $"The value '{value}' does not fit in a word!");
                return default;
            }
            else
            {
                return new ASTUDoubleWordLitteral(trace, (StringRef)FormatNumber(value, format), value, format);
            }
        }

        public override string ToString()
        {
            // FIXME: Better use of StringRef!!
            if (Value.EndsWithAny('d', 'D', 'u', 'U'))
            {
                return Value.ToString().TrimEnd('d', 'D', 'u', 'U');
            }
            else
            {
                return base.ToString();
            }
        }
    }

    public class ASTBoolLitteral : ASTLitteral
    {
        // This is so we don't need to allocate these every time
        public static readonly StringRef TrueStrRef = (StringRef)"True";
        public static readonly StringRef FalseStrRef = (StringRef)"False";

        public readonly bool BoolValue;

        public ASTBoolLitteral(TraceData trace, bool value) : base(trace, ASTBaseType.Bool, value ? TrueStrRef : FalseStrRef)
        {
            BoolValue = value;
        }
    }

    public class ASTCharLitteral : ASTLitteral
    {
        public readonly char CharValue;

        public ASTCharLitteral(TraceData trace, StringRef value, char charValue) : base(trace, ASTBaseType.Char, value)
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
        public readonly StringRef Contents;

        public ASTStringLitteral(TraceData trace, StringRef value) : base(trace, ASTBaseType.String, value)
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

    public class ASTNullLitteral : ASTLitteral
    {
        public static readonly StringRef NullStrRef = (StringRef)"null";

        public ASTNullLitteral(TraceData trace) : base(trace, ASTPointerType.Of(ASTBaseType.Void), NullStrRef) { }
    }

    // FIXME: Atm we don't support array litterals with non-litteral elements...
    // This is so we can know the type of the litteral at parse time
    // But constant arrays of function pointers might be a really usefull thing
    // So we will probably want to change this to support that
    public class ASTArrayLitteral : ASTLitteral
    {
        public static readonly StringRef ArrayLitteralStrRef = (StringRef)"ARRAY LITTERAL";

        public List<ASTExpression> Values;

        public ASTArrayLitteral(TraceData trace, ASTType baseType, List<ASTExpression> values) : base(trace, new ASTFixedArrayType(trace, baseType, ASTNumericLitteral.From(trace, values.Count)), ArrayLitteralStrRef)
        {
            Values = values;
        }

        public static new ASTArrayLitteral Parse(Queue<Token> Tokens)
        {
            var open_brace_tok = Tokens.Dequeue();
            if (open_brace_tok.Type != TokenType.Open_brace) Fail(open_brace_tok, "Expected opening '{'");

            List<ASTExpression> elements = new List<ASTExpression>();
            // ASTType litteralType = null;

            // We peeked so we can handle this loop more uniform by always dequeueing at the start
            var peek = Tokens.Peek();
            while (peek.Type != TokenType.Close_brace)
            {
                var expr = ASTExpression.Parse(Tokens);
                // NOTE: We don't do typechecking at this stage...
                //if (litteralType == null) litteralType = expr.Type;
                //if (expr.Type != litteralType) Fail(peek, $"Cannot add a element of type '{expr.Type}' to an array of type '{litteralType}'");
                elements.Add(expr);

                var contToken = Tokens.Peek();
                if (contToken.Type != TokenType.Comma && contToken.Type == TokenType.Close_brace) break;
                else if (contToken.Type != TokenType.Comma) Fail(contToken, "Expected ',' or a '}'");
                // Dequeue the comma
                Tokens.Dequeue();

                peek = Tokens.Peek();
            }

            var closeParenTok = Tokens.Dequeue();
            if (closeParenTok.Type != TokenType.Close_brace) Fail(closeParenTok, "Expected '}'");

            var trace = new TraceData
            {
                File = open_brace_tok.FilePath,
                StartLine = open_brace_tok.Line,
                EndLine = closeParenTok.Line,
            };

            // We will figure out the type later
            return new ASTArrayLitteral(trace, ASTBaseType.Void, elements);
        }
    }

    public class ASTPointerLitteral : ASTDoubleWordLitteral
    {
        public readonly ASTPointerType PType;

        public ASTPointerLitteral(TraceData trace, StringRef value, int intValue, ASTPointerType pType) : base(trace, value, intValue, NumberFormat.Hexadecimal)
        {
            PType = pType;
            this.Type = pType;
        }
    }

    // NOTE: This does not extend ASTLitteral but extends ASTExpression instead
    public class ASTStructLitteral : ASTExpression
    {
        public ASTType StructType;
        public Dictionary<StringRef, ASTExpression> MemberInitializers;

        public ASTStructLitteral(TraceData trace, ASTType structType, Dictionary<StringRef, ASTExpression> initializers) : base(trace)
        {
            StructType = structType;
            MemberInitializers = initializers;
        }

        public override string ToString()
        {
            return $"{StructType}{{ {(string.Join(", ", MemberInitializers.Select(kvp => $"{kvp.Key}={kvp.Value}")))} }}";
        }

        public static new ASTStructLitteral Parse(Queue<Token> Tokens)
        {
            ASTType type = ASTType.Parse(Tokens);

            var braceTok = Tokens.Dequeue();
            if (braceTok.Type != TokenType.Open_brace) Fail(braceTok, "Expected '{'!");

            Dictionary<StringRef, ASTExpression> inits = new Dictionary<StringRef, ASTExpression>();

            var peek = Tokens.Peek();
            while (peek.Type != TokenType.Close_brace)
            {
                if (peek.IsIdentifier == false) Fail(peek, "Expected identifier!");
                StringRef name = Tokens.Dequeue().Value;

                var equalsTok = Tokens.Dequeue();
                if (equalsTok.Type != TokenType.Equal) Fail(equalsTok, "Expected equals!");

                ASTExpression init = ASTExpression.Parse(Tokens);

                if (inits.ContainsKey(name)) Fail(peek, $"Cannot have two initializers for the member '{name}'!");
                inits.Add(name, init);

                var contToken = Tokens.Peek();
                if (contToken.Type != TokenType.Comma && contToken.Type == TokenType.Close_brace) break;
                else if (contToken.Type != TokenType.Comma) Fail(contToken, "Expected ',' or a '}'");
                Tokens.Dequeue();

                peek = Tokens.Peek();
            }

            var closeBraceTok = Tokens.Dequeue();
            if (closeBraceTok.Type != TokenType.Close_brace) Fail(closeBraceTok, "Expected '}'!");

            TraceData trace = new TraceData
            {
                File = type.Trace.File,
                StartLine = type.Trace.StartLine,
                EndLine = closeBraceTok.Line,
            };

            return new ASTStructLitteral(trace, type, inits);
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

        public readonly StringRef Name;
        public readonly ASTExpression AssignmentExpression;

        public ASTVariableExpression(TraceData trace, StringRef variableName, ASTExpression assignmentExpression) : base(trace)
        {
            this.Name = variableName;
            this.AssignmentExpression = assignmentExpression;
        }

        public override string ToString()
        {
            return Name.ToString();
        }

        public static new ASTVariableExpression Parse(Queue<Token> Tokens)
        {
            var identTok = Tokens.Dequeue();
            if (identTok.IsIdentifier == false) Fail(identTok, "Expected an identifier!");
            StringRef name = identTok.Value;

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
                        File = identTok.FilePath,
                        StartLine = identTok.Line,
                        EndLine = expr.Trace.EndLine,
                    };

                    expr = new ASTBinaryOp(assignmentTrace, opType, new ASTVariableExpression(TraceData.From(identTok), name, null), expr);
                }
            }

            var trace = new TraceData
            {
                File = identTok.FilePath,
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
                File = openSquareTok.FilePath,
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
            Increment,
            Decrement,
            Increment_post,
            Decrement_post,
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
                // NOTE: Here we really need to know if it's before or after...
                // Or we always assume before here..? Sounds weird
                case TokenType.PlusPlus:
                    return UnaryOperationType.Increment;
                case TokenType.MinusMinus:
                    return UnaryOperationType.Decrement;
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
            return $"{Left} {OperatorTypeToString(OperatorType)} {Right}";
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
                case TokenType.And:
                    return BinaryOperatorType.Bitwise_And;
                case TokenType.Pipe:
                    return BinaryOperatorType.Bitwise_Or;
                case TokenType.Caret:
                    return BinaryOperatorType.Bitwise_Xor;
                case TokenType.DoubleAnd:
                    return BinaryOperatorType.Logical_And;
                case TokenType.DoublePipe:
                    return BinaryOperatorType.Logical_Or;
                case TokenType.DoubleEqual:
                    return BinaryOperatorType.Equal;
                case TokenType.NotEqual:
                    return BinaryOperatorType.Not_equal;
                case TokenType.LessThanOrEqual:
                    return BinaryOperatorType.Less_than_or_equal;
                case TokenType.GreaterThanOrEqual:
                    return BinaryOperatorType.Greater_than_or_equal;
                case TokenType.LessThan:
                    return BinaryOperatorType.Less_than;
                case TokenType.GreaterThan:
                    return BinaryOperatorType.Greater_than;
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

        public static bool IsBooleanOpType(BinaryOperatorType type)
        {
            switch (type)
            {
                case BinaryOperatorType.Logical_And:
                case BinaryOperatorType.Logical_Or:
                case BinaryOperatorType.Equal:
                case BinaryOperatorType.Not_equal:
                case BinaryOperatorType.Less_than:
                case BinaryOperatorType.Less_than_or_equal:
                case BinaryOperatorType.Greater_than:
                case BinaryOperatorType.Greater_than_or_equal:
                    return true;
                case BinaryOperatorType.Addition:
                case BinaryOperatorType.Subtraction:
                case BinaryOperatorType.Multiplication:
                case BinaryOperatorType.Division:
                case BinaryOperatorType.Modulo:
                case BinaryOperatorType.Bitwise_And:
                case BinaryOperatorType.Bitwise_Or:
                case BinaryOperatorType.Bitwise_Xor:
                case BinaryOperatorType.Bitwise_shift_left:
                case BinaryOperatorType.Bitwise_shift_right:
                    return false;
                case BinaryOperatorType.Unknown:
                default:
                    Fail(default, $"Unknown binary operation '{type}'");
                    return false;
            }
        }

        public static bool IsPointerCompatibleOpType(BinaryOperatorType type)
        {
            switch (type)
            {
                case BinaryOperatorType.Addition:
                case BinaryOperatorType.Subtraction:
                    // NOTE: Should these be allowed?
                case BinaryOperatorType.Multiplication:
                case BinaryOperatorType.Division:
                case BinaryOperatorType.Modulo:
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsComparisonOp(BinaryOperatorType opType)
        {
            switch (opType)
            {
                case BinaryOperatorType.Equal:
                case BinaryOperatorType.Not_equal:
                case BinaryOperatorType.Less_than:
                case BinaryOperatorType.Less_than_or_equal:
                case BinaryOperatorType.Greater_than:
                case BinaryOperatorType.Greater_than_or_equal:
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsEqualsOp(BinaryOperatorType opType)
        {
            switch (opType)
            {
                case BinaryOperatorType.Equal:
                case BinaryOperatorType.Not_equal:
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsArithmetic(BinaryOperatorType opType)
        {
            switch (opType)
            {
                case BinaryOperatorType.Addition:
                case BinaryOperatorType.Subtraction:
                case BinaryOperatorType.Multiplication:
                case BinaryOperatorType.Division:
                case BinaryOperatorType.Modulo:
                case BinaryOperatorType.Bitwise_And:
                case BinaryOperatorType.Bitwise_Or:
                case BinaryOperatorType.Bitwise_Xor:
                case BinaryOperatorType.Bitwise_shift_left:
                case BinaryOperatorType.Bitwise_shift_right:
                    return true;
                default:
                    return false;
            }
        }

        internal static BinaryOperatorType InvertBooleanOp(BinaryOperatorType opType)
        {
            switch (opType)
            {
                case BinaryOperatorType.Equal:
                    return BinaryOperatorType.Not_equal;
                case BinaryOperatorType.Not_equal:
                    return BinaryOperatorType.Equal;
                case BinaryOperatorType.Less_than:
                    return BinaryOperatorType.Greater_than_or_equal;
                case BinaryOperatorType.Less_than_or_equal:
                    return BinaryOperatorType.Greater_than;
                case BinaryOperatorType.Greater_than:
                    return BinaryOperatorType.Less_than_or_equal;
                case BinaryOperatorType.Greater_than_or_equal:
                    return BinaryOperatorType.Less_than;
                
                // We can't really invert these so they will be here for now
                case BinaryOperatorType.Logical_And:
                case BinaryOperatorType.Logical_Or:
                default:
                    Warning(default, $"Cannot invert op type '{opType}'!");
                    return opType;
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
    
    public class ASTContainsExpression : ASTExpression
    {
        public readonly ASTExpression Value;
        public readonly ASTExpression LowerBound;
        public readonly ASTExpression UpperBound;

        public ASTContainsExpression(TraceData trace, ASTExpression value, ASTExpression lowerBound, ASTExpression upperBound) : base(trace)
        {
            Value = value;
            LowerBound = lowerBound;
            UpperBound = upperBound;
        }
    }

    public class ASTFunctionCall : ASTExpression
    {
        public readonly StringRef FunctionName;
        public readonly List<ASTExpression> Arguments;

        public ASTFunctionCall(TraceData trace, StringRef functionName, List<ASTExpression> arguments) : base(trace)
        {
            FunctionName = functionName;
            Arguments = arguments;
        }

        public static new ASTFunctionCall Parse(Queue<Token> Tokens)
        {
            var nameTok = Tokens.Dequeue();
            if (nameTok.IsIdentifier == false) Fail(nameTok, "Expected identifier!");
            StringRef funcName = nameTok.Value;
            
            var openParenTok = Tokens.Dequeue();
            if (openParenTok.Type != TokenType.Open_parenthesis) Fail(openParenTok, "Expected '('");

            List<ASTExpression> arguments = new List<ASTExpression>();

            while (Tokens.Peek().Type != TokenType.Close_parenthesis)
            {
                var expr = ASTExpression.Parse(Tokens);
                arguments.Add(expr);

                // FIXME: This needs some cleanup
                var contToken = Tokens.Peek();
                if (contToken.Type != TokenType.Comma && contToken.Type == TokenType.Close_parenthesis) break;
                else if (contToken.Type != TokenType.Comma) Fail(contToken, "Expected ',' or a ')'");

                // Dequeue the comma
                Tokens.Dequeue();
            }

            var closeParenTok = Tokens.Dequeue();
            if (closeParenTok.Type != TokenType.Close_parenthesis) Fail(closeParenTok, "Expected ')'");

            var trace = new TraceData
            {
                File = nameTok.FilePath,
                StartLine = nameTok.Line,
                EndLine = closeParenTok.Line,
            };

            return new ASTFunctionCall(trace, funcName, arguments);
        }

        public override string ToString()
        {
            return $"{FunctionName}(...)";
        }
    }

    public class ASTVirtualFunctionCall : ASTExpression
    {
        public readonly ASTExpression FunctionPointer;
        public readonly List<ASTExpression> Arguments;

        public ASTVirtualFunctionCall(TraceData trace, ASTExpression functionPointer, List<ASTExpression> arguments) : base(trace)
        {
            FunctionPointer = functionPointer;
            Arguments = arguments;
        }

        public static ASTVirtualFunctionCall Parse(Queue<Token> Tokens, ASTExpression target)
        {
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
                File = openParenTok.FilePath,
                StartLine = openParenTok.Line,
                EndLine = closeParenTok.Line,
            };

            return new ASTVirtualFunctionCall(trace, target, arguments);
        }
    }

    public class ASTGenericFunctionCall : ASTFunctionCall
    {
        public readonly List<ASTType> GenericTypes;

        public ASTGenericFunctionCall(TraceData trace, StringRef functionName, List<ASTType> genericTypes, List<ASTExpression> arguments) : base(trace, functionName, arguments)
        {
            GenericTypes = genericTypes;
        }

        // NOTE: This is parsed in ASTFunction.Parse
        public static new ASTGenericFunctionCall Parse(Queue<Token> Tokens)
        {
            var nameTok = Tokens.Dequeue();
            if (nameTok.IsIdentifier == false) Fail(nameTok, "Expected identifier!");
            StringRef funcName = nameTok.Value;

            var openAngleTok = Tokens.Dequeue();
            if (openAngleTok.Type != TokenType.LessThan) Fail(openAngleTok, "Expected '<'");

            List<ASTType> genericTypes = new List<ASTType>();

            while (Tokens.Peek().Type != TokenType.GreaterThan)
            {
                var type = ASTType.Parse(Tokens);
                genericTypes.Add(type);

                // FIXME: This needs some cleanup
                var contToken = Tokens.Peek();
                if (contToken.Type != TokenType.Comma && contToken.Type == TokenType.GreaterThan) break;
                else if (contToken.Type != TokenType.Comma) Fail(contToken, "Expected ',' or a '>'");

                // Dequeue the comma
                Tokens.Dequeue();
            }

            var closeAngleTok = Tokens.Dequeue();
            if (closeAngleTok.Type != TokenType.GreaterThan) Fail(closeAngleTok, "Expected '>'");

            var openParenTok = Tokens.Dequeue();
            if (openParenTok.Type != TokenType.Open_parenthesis) Fail(openParenTok, "Expected '('");

            List<ASTExpression> arguments = new List<ASTExpression>();

            while (Tokens.Peek().Type != TokenType.Close_parenthesis)
            {
                var expr = ASTExpression.Parse(Tokens);
                arguments.Add(expr);

                // FIXME: This needs some cleanup
                var contToken = Tokens.Peek();
                if (contToken.Type != TokenType.Comma && contToken.Type == TokenType.Close_parenthesis) break;
                else if (contToken.Type != TokenType.Comma) Fail(contToken, $"Expected ',' or a ')'. Got: {contToken}");

                // Dequeue the comma
                Tokens.Dequeue();
            }

            var closeParenTok = Tokens.Dequeue();
            if (closeParenTok.Type != TokenType.Close_parenthesis) Fail(closeParenTok, "Expected ')'");

            var trace = new TraceData
            {
                File = nameTok.FilePath,
                StartLine = nameTok.Line,
                EndLine = closeParenTok.Line,
            };

            return new ASTGenericFunctionCall(trace, funcName, genericTypes, arguments);
        }

        public override string ToString()
        {
            return $"{FunctionName}<T...>(...)";
        }
    }

    public class ASTMemberExpression : ASTExpression
    {
        public readonly ASTExpression TargetExpr;
        public readonly StringRef MemberName;
        public readonly ASTExpression Assignment;
        public readonly bool Dereference;

        public ASTMemberExpression(TraceData trace, ASTExpression targetExpr, StringRef memberName, ASTExpression assignment, bool dereference) : base(trace)
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
            StringRef memberName = memberTok.Value;
            
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
                File = periodTok.FilePath,
                StartLine = periodTok.Line,
                EndLine = assignment?.Trace.EndLine ?? memberTok.Line,
            };

            return new ASTMemberExpression(trace, targetExpr, memberName, assignment, dereference);
        }

        public override string ToString()
        {
            // NOTE: Meybe the assignmen should be included
            return $"{TargetExpr}{(Dereference ? "->" : ".")}{MemberName}";
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
                File = sizeofTok.FilePath,
                StartLine = sizeofTok.Line,
                EndLine = closeParen.Line,
            };

            return new ASTSizeofTypeExpression(trace, type);
        }
    }

    public class ASTAddressOfExpression : ASTExpression
    {
        public readonly ASTExpression Expr;

        public ASTAddressOfExpression(TraceData trace, ASTExpression expr) : base(trace)
        {
            Expr = expr;
        }

        public static new ASTAddressOfExpression Parse(Queue<Token> Tokens)
        {
            var andTok = Tokens.Dequeue();
            if (andTok.Type != TokenType.And) Fail(andTok, "Expected '&'!");

            // NOTE: We are parsing an expression here,
            // but only a subset of all expressions are valid here.
            // Feels like we should do something about this while parsing.
            var expr = ASTExpression.ParseFactor(Tokens);

            var trace = new TraceData
            {
                File = andTok.FilePath,
                StartLine = andTok.Line,
                EndLine = expr.Trace.EndLine,
            };

            return new ASTAddressOfExpression(trace, expr);
        }
    }
    
    public class ASTTypeOfExpression : ASTExpression
    {
        public readonly ASTType Type;

        public ASTTypeOfExpression(TraceData trace, ASTType type) : base(trace)
        {
            Type = type;
        }

        public static new ASTTypeOfExpression Parse(Queue<Token> Tokens)
        {
            var typeofTok = Tokens.Dequeue();
            if (typeofTok.Type != TokenType.Keyword_Typeof) Fail(typeofTok, "Expected typeof!");

            var openParen = Tokens.Dequeue();
            if (openParen.Type != TokenType.Open_parenthesis) Fail(openParen, "Expected '('!");

            ASTType type = ASTType.Parse(Tokens);

            var closeParen = Tokens.Dequeue();
            if (closeParen.Type != TokenType.Close_parenthesis) Fail(closeParen, "Expected ')'!");

            var trace = new TraceData
            {
                File = typeofTok.FilePath,
                StartLine = typeofTok.Line,
                EndLine = closeParen.Line,
            };

            return new ASTTypeOfExpression(trace, type);
        }
    }

    public class ASTDefaultExpression : ASTExpression
    {
        public readonly ASTType Type;

        public ASTDefaultExpression(TraceData trace, ASTType type) : base(trace)
        {
            Type = type;
        }

        public static new ASTDefaultExpression Parse(Queue<Token> Tokens)
        {
            var defaultTok = Tokens.Dequeue();
            if (defaultTok.Type != TokenType.Keyword_Default) Fail(defaultTok, "Expected default!");

            var openParen = Tokens.Dequeue();
            if (openParen.Type != TokenType.Open_parenthesis) Fail(openParen, "Expected '('!");

            ASTType type = ASTType.Parse(Tokens);

            var closeParen = Tokens.Dequeue();
            if (closeParen.Type != TokenType.Close_parenthesis) Fail(closeParen, "Expected ')'!");

            var trace = new TraceData
            {
                File = defaultTok.FilePath,
                StartLine = defaultTok.Line,
                EndLine = closeParen.Line,
            };

            return new ASTDefaultExpression(trace, type);
        }
    }

    public class ASTInlineAssemblyExpression : ASTExpression
    {
        public readonly List<ASTStringLitteral> Assembly;
        public readonly ASTType ResultType;

        public ASTInlineAssemblyExpression(TraceData trace, List<ASTStringLitteral> assembly, ASTType resultType) : base(trace)
        {
            Assembly = assembly;
            ResultType = resultType;
        }

        public static new ASTInlineAssemblyExpression Parse(Queue<Token> Tokens)
        {
            var assemTok = Tokens.Dequeue();
            if (assemTok.Type != TokenType.Keyword_Assembly) Fail(assemTok, "Expected assembly!");

            var openParenthesis = Tokens.Dequeue();
            if (openParenthesis.Type != TokenType.Open_parenthesis) Fail(openParenthesis, "Expected '('");

            // NOTE: We might want to require colons?
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

            ASTType retType = ASTBaseType.Void;

            if (Tokens.Peek().Type == TokenType.Arrow)
            {
                // Dequeue arrow
                Tokens.Dequeue();

                // Parse a type and then we are done
                retType = ASTType.Parse(Tokens);
            }
            
            var trace = new TraceData
            {
                File = assemTok.FilePath,
                StartLine = assemTok.Line,
                EndLine = retType is ASTBaseType == false ? retType.Trace.EndLine : closeParenthesis.Line, // Base types have no usefull trace!
            };

            return new ASTInlineAssemblyExpression(trace, assembly, retType);
        }
    }
    
    // This is a internal representation of multiple expressions emitted like one big expression.
    public class ASTInternalCompoundExpression : ASTExpression
    {
        public readonly ASTType ResultType;
        public readonly List<ASTExpression> Expressions;
        // This doesn't come from the parsed file so it's ok that this is a normal string
        public readonly string Comment;

        public ASTInternalCompoundExpression(TraceData trace, ASTType result, List<ASTExpression> expressions, string comment = null) : base(trace)
        {
            ResultType = result;
            Expressions = expressions;
            Comment = comment;
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

        // This is fine because these AST nodes will only ever be generated by the compiler.
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

            var expression = ASTExpression.ParseFactor(Tokens);

            var trace = new TraceData
            {
                File = castTok.FilePath,
                StartLine = castTok.Line,
                EndLine = expression.Trace.EndLine,
            };

            return new ASTExplicitCast(trace, expression, castType);
        }

        public override string ToString()
        {
            return $"cast({To}) {From}";
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
            To = to;
        }
    }

    public class ASTStringToArrayCast : ASTCastExpression
    {
        public new readonly ASTArrayType To;

        public ASTStringToArrayCast(TraceData trace, ASTExpression from, ASTArrayType to) : base(trace, from, to)
        {
            To = to;
        }
    }

    #endregion

    #endregion

    #region Types

    public abstract class ASTType : ASTNode, IEquatable<ASTType>
    {
        public readonly StringRef TypeName;
        
        public ASTType(TraceData trace, StringRef type) : base(trace)
        {
            this.TypeName = type;
        }

        public override string ToString()
        {
            return TypeName.ToString();
        }
        
        public override bool Equals(object obj)
        {
            return Equals(obj as ASTType);
        }

        // TODO: Do some sophisticated more checking then just comparing strings...?
        // NOTE: This does not do the Extern type thing!!
        public bool Equals(ASTType other)
        {
            return other != null &&
                   TypeName == other.TypeName;
        }

        public override int GetHashCode()
        {
            return -448171650 + EqualityComparer<StringRef>.Default.GetHashCode(TypeName);
        }

        public static bool operator ==(ASTType type1, ASTType type2)
        {
            return EqualityComparer<ASTType>.Default.Equals(type1, type2);
        }

        public static bool operator !=(ASTType type1, ASTType type2)
        {
            return !(type1 == type2);
        }

        public static ASTType Parse(Queue<Token> Tokens)
        {
            var tok = Tokens.Dequeue();
            switch (tok.Type)
            {
                case TokenType.DollarSign:
                    {
                        // This is a function type!

                        List<ASTType> paramTypes = new List<ASTType>();

                        var openParenTok = Tokens.Peek();
                        if (openParenTok.Type == TokenType.Open_parenthesis)
                        {
                            // We have params, so we need to parse them

                            // Dequeue the open paren
                            Tokens.Dequeue();

                            // We peeked so we can handle this loop more uniform by always dequeueing at the start
                            var peek = Tokens.Peek();
                            while (peek.Type != TokenType.Close_parenthesis)
                            {
                                ASTType type = ASTType.Parse(Tokens);

                                paramTypes.Add(type);

                                // If it's a comma, continue
                                // If it's a closing bracked, break
                                // If it's anything else, error
                                peek = Tokens.Peek();

                                if (peek.Type == TokenType.Close_parenthesis) break;
                                else if (peek.Type != TokenType.Comma) Fail(peek, $"Unknown token '{peek}', expected comma or ')'");

                                // Dequeue the continuation comma and set peek
                                Tokens.Dequeue();
                                peek = Tokens.Peek();
                            }

                            var closeParenTok = Tokens.Dequeue();
                            if (closeParenTok.Type != TokenType.Close_parenthesis) Fail(closeParenTok, $"Expected ')'!");
                        }
                        
                        ASTType returnType = ASTBaseType.Void;

                        var arrowTok = Tokens.Peek();
                        if (arrowTok.Type == TokenType.Arrow)
                        {
                            // We have a return type
                            // Dequeue the arrow
                            Tokens.Dequeue();

                            returnType = ASTType.Parse(Tokens);
                        }

                        var trace = new TraceData
                        {
                            File = tok.FilePath,
                            StartLine = tok.Line,
                            EndLine = returnType.Trace.EndLine,
                        };

                        return new ASTFunctionPointerType(trace, paramTypes, returnType);
                    }
                case TokenType.Asterisk:
                    {
                        ASTType type = ASTType.Parse(Tokens);

                        var trace = new TraceData
                        {
                            File = tok.FilePath,
                            StartLine = tok.Line,
                            // Use the types line if it exists, NOTE: This should be removed when base types get traces!
                            EndLine = type.Trace.EndLine == -1 ? tok.Line : type.Trace.EndLine,
                        };

                        return new ASTPointerType(trace, type);
                    }
                case TokenType.Open_square_bracket:
                    {
                        // FIXME: Use ASTExpression for fixed arrays (we can constant fold now)
                        // Parse and make the current type the base for an array
                        var peek = Tokens.Peek();
                        if (peek.Type == TokenType.Numeric_Litteral)
                        {
                            var numLit = ASTNumericLitteral.Parse(Tokens);

                            var closeSquareTok = Tokens.Dequeue();
                            if (closeSquareTok.Type != TokenType.Close_squre_bracket) Fail(closeSquareTok, "Expected ']'!");

                            ASTType type = ASTType.Parse(Tokens);

                            var trace = new TraceData
                            {
                                File = tok.FilePath,
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
                                File = tok.FilePath,
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
                            // FIXME: We could create somekind of copy here that contains an actual trace!
                            type = baseType.WithTrace(TraceData.From(tok));
                            
                            if (Tokens.Peek().Type == TokenType.LessThan)
                                Fail(Tokens.Peek(), $"Base types cannot have generic arguments!!");
                        }
                        else
                        {
                            if (Tokens.Peek().Type == TokenType.LessThan)
                            {
                                // Remove open angle
                                Tokens.Dequeue();

                                List<ASTType> genericTypes = new List<ASTType>();

                                while (Tokens.Peek().Type != TokenType.GreaterThan)
                                {
                                    genericTypes.Add(ASTType.Parse(Tokens));

                                    // FIXME: This needs some cleanup
                                    var contToken = Tokens.Peek();
                                    if (contToken.Type != TokenType.Comma && contToken.Type == TokenType.GreaterThan) break;
                                    else if (contToken.Type != TokenType.Comma) Fail(contToken, "Expected ',' or a '>'");

                                    // Dequeue the comma
                                    Tokens.Dequeue();
                                }

                                // Dequeue close angle
                                var closeAngle = Tokens.Dequeue();

                                var trace = new TraceData
                                {
                                    File = tok.FilePath,
                                    StartLine = tok.Line,
                                    EndLine = closeAngle.Line,
                                };

                                type = new ASTGenericTypeRef(trace, tok.Value, genericTypes);
                            }
                            else
                            {
                                var trace = TraceData.From(tok);
                                type = new ASTTypeRef(trace, tok.Value);
                            }
                        }

                        return type;
                    }
            }
        }
    }

    public class ASTBaseType : ASTType
    {
        public static readonly StringRef VoidStr = (StringRef)"void";
        public static readonly StringRef WordStr = (StringRef)"word";
        public static readonly StringRef DWordStr = (StringRef)"dword";
        public static readonly StringRef UWordStr = (StringRef)"uword";
        public static readonly StringRef UDWordStr = (StringRef)"udword";
        public static readonly StringRef BoolStr = (StringRef)"bool";
        public static readonly StringRef CharStr = (StringRef)"char";
        public static readonly StringRef StringStr = (StringRef)"string";

        public static readonly Dictionary<StringRef, ASTBaseType> BaseTypeMap = new Dictionary<StringRef, ASTBaseType>()
        {
            { VoidStr, new ASTBaseType(VoidStr, 0) },
            { WordStr, new ASTBaseType(WordStr, 1) },
            { DWordStr, new ASTBaseType(DWordStr, 2) },
            { UWordStr, new ASTBaseType(UWordStr, 1) },
            { UDWordStr, new ASTBaseType(UDWordStr, 2) },
            { BoolStr, new ASTBaseType(BoolStr, 1) },
            { CharStr, new ASTBaseType(CharStr, 1) },
            // TODO? Move over to the 4 word strings with length and data pointer?
            { StringStr, new ASTBaseType(StringStr, 2) },
        };

        public static ASTBaseType Void => BaseTypeMap[VoidStr];
        public static ASTBaseType Word => BaseTypeMap[WordStr];
        public static ASTBaseType DoubleWord => BaseTypeMap[DWordStr];
        public static ASTBaseType UWord => BaseTypeMap[UWordStr];
        public static ASTBaseType UDoubleWord => BaseTypeMap[UDWordStr];
        public static ASTBaseType Bool => BaseTypeMap[BoolStr];
        public static ASTBaseType Char => BaseTypeMap[CharStr];
        public static ASTBaseType String => BaseTypeMap[StringStr];

        public readonly int Size;
        
        // FIXME: Trace data for internal types?
        private ASTBaseType(StringRef name, int size) : base(TraceData.Internal, name)
        {
            Size = size;
        }

        private ASTBaseType(StringRef name, int size, TraceData trace) : base(trace, name)
        {
            Size = size;
        }

        public static bool IsNumericType(ASTType type)
        {
            if (type is ASTBaseType == false)
            {
                return false;
            }
            else if (type == Word)
            {
                return true;
            }
            else if (type == DoubleWord)
            {
                return true;
            }
            // TODO: Should this really be considered a numeric type?....
            else if (type == Char)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public ASTBaseType WithTrace(TraceData trace) => new ASTBaseType(TypeName, Size, trace);
    }

    public abstract class ASTDereferenceableType : ASTType
    {
        /// <summary>
        /// The underlying type that is available when dereferencing this type.
        /// </summary>
        public abstract ASTType DerefType { get; }

        public ASTDereferenceableType(TraceData trace, StringRef type) : base(trace, type) { }
    }

    public class ASTPointerType : ASTDereferenceableType
    {
        public const int Size = 2;

        public readonly ASTType BaseType;
        public override ASTType DerefType => BaseType;

        public static ASTPointerType Of(ASTType type) => new ASTPointerType(type.Trace, type);

        public ASTPointerType(TraceData trace, ASTType baseType) : base(trace, (StringRef)$"*{baseType.TypeName}")
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
    
    public class ASTArrayType : ASTDereferenceableType
    {
        public const int Size = 4;

        public readonly ASTType BaseType;

        public override ASTType DerefType => BaseType;

        public static ASTArrayType Of(ASTType type) => new ASTArrayType(type.Trace, type);

        public ASTArrayType(TraceData trace, ASTType baseType) : this(trace, baseType, (StringRef)$"[]{baseType.TypeName}") { }

        protected ASTArrayType(TraceData trace, ASTType baseType, StringRef name) : base(trace, name)
        {
            BaseType = baseType;
        }
    }
    
    public class ASTFixedArrayType : ASTDereferenceableType
    {
        public readonly ASTType BaseType;
        public readonly ASTNumericLitteral Size;

        public override ASTType DerefType => BaseType;

        public ASTFixedArrayType(TraceData trace, ASTType baseType, ASTNumericLitteral size) : base(trace, (StringRef)$"[{size}]{baseType.TypeName}")
        {
            BaseType = baseType;
            Size = size;
        }
    }

    /// <summary>
    /// A named reference to a complex type with the given name.
    /// </summary>
    public class ASTTypeRef : ASTType
    {
        public readonly StringRef Name;
        
        public ASTTypeRef(TraceData trace, StringRef name) : base(trace, name)
        {
            Name = name;
        }
    }

    public class ASTGenericTypeRef : ASTTypeRef
    {
        public readonly List<ASTType> GenericTypes;

        public ASTGenericTypeRef(TraceData trace, StringRef name, List<ASTType> genericTypes) : base (trace, name)
        {
            GenericTypes = genericTypes;
        }

        public override string ToString()
        {
            return $"{base.ToString()}<...>";
        }
    }

    public class ASTAliasedType : ASTType
    {
        public readonly StringRef Alias;
        public readonly ASTType RealType;

        public ASTAliasedType(TraceData trace, StringRef alias, ASTType real) : base(trace, alias)
        {
            Alias = alias;
            RealType = real;
        }

        internal static ASTType Of(StringRef name, ASTType type)
        {
            return new ASTAliasedType(type.Trace, name, type);
        }
    }

    public class ASTStructType : ASTType
    {
        public readonly List<(ASTType Type, StringRef Name)> Members;

        public ASTStructType(TraceData trace, StringRef name, List<(ASTType, StringRef)> members) : base(trace, name)
        {
            Members = members;
        }
    }

    // FIXME: We probably want to convert over to using this!
    public class ASTGenericType : ASTType
    {
        public readonly ASTType Type;
        public readonly List<StringRef> GenericNames;

        public ASTGenericType(TraceData trace, ASTType type, List<StringRef> genericNames) : base(trace, (StringRef)$"{type.TypeName}<...>")
        {
            Type = type;
            GenericNames = genericNames;
        }

        public string GetFullTypeName()
        {
            return $"{Type.TypeName}<{string.Join(",", GenericNames)}>";
        }
    }
    
    public class ASTFunctionPointerType : ASTType, IEquatable<ASTFunctionPointerType>
    {
        public const int Size = 2;

        public static ASTFunctionPointerType Of(TraceData trace, ASTFunction function)
        {
            return new ASTFunctionPointerType(trace, function.Parameters.Select(p => p.Type).ToList(), function.ReturnType);
        }
        
        public readonly List<ASTType> ParamTypes;
        public readonly ASTType ReturnType;

        public ASTFunctionPointerType(TraceData trace, List<ASTType> paramTypes, ASTType returnType) : base(trace, (StringRef)$"$({string.Join(", ", paramTypes)}) -> {returnType}")
        {
            ParamTypes = paramTypes;
            ReturnType = returnType;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ASTFunctionPointerType);
        }

        public bool Equals(ASTFunctionPointerType other)
        {
            bool ComareParams()
            {
                if (ParamTypes.Count != other.ParamTypes.Count) return false;
                
                for (int i = 0; i < ParamTypes.Count; i++)
                {
                    if (ParamTypes[i] != other.ParamTypes[i]) return false;
                }

                return true;
            }
            
            return other != null &&
                   base.Equals(other) &&
                   ComareParams() &&
                   ReturnType == other.ReturnType;
        }

        public override int GetHashCode()
        {
            var hashCode = -480084024;
            hashCode = hashCode * -1521134295 + base.GetHashCode();
            hashCode = hashCode * -1521134295 + EqualityComparer<List<ASTType>>.Default.GetHashCode(ParamTypes);
            hashCode = hashCode * -1521134295 + EqualityComparer<ASTType>.Default.GetHashCode(ReturnType);
            return hashCode;
        }

        public static bool operator ==(ASTFunctionPointerType type1, ASTFunctionPointerType type2)
        {
            return EqualityComparer<ASTFunctionPointerType>.Default.Equals(type1, type2);
        }

        public static bool operator !=(ASTFunctionPointerType type1, ASTFunctionPointerType type2)
        {
            return !(type1 == type2);
        }
    }

    #endregion
}
