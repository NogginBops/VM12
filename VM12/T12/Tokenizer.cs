using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using VM12Util;

namespace T12
{
    public enum TokenType
    {
        Comment,

        Open_brace,
        Close_brace,

        Open_parenthesis,
        Close_parenthesis,

        Open_square_bracket,
        Close_squre_bracket,

        DoubleColon,
        Contains,

        Semicolon,
        Period,
        Comma,
        Colon,
        Numbersign,
        Arrow,

        ShiftLeft,
        ShiftRight,
        
        DoubleAnd,
        DoublePipe,
        DoubleEqual,
        NotEqual,

        LessThanOrEqual,
        GreaterThanOrEqual,

        Equal,
        PlusEqual,
        MinusEqual,
        AsteriskEqual,
        SlashEqual,
        PercentEqual,
        AndEqual,
        PipeEqual,
        CaretEqual,

        PlusPlus,
        MinusMinus,
        
        Plus,
        Minus,
        Asterisk,
        Slash,
        Percent,

        And,
        Pipe,
        Caret,
        DollarSign,

        Tilde,
        Exclamationmark,
        Questionmark,
        
        LessThan,
        GreaterThan,
        
        Keyword_Void,
        Keyword_Word,
        Keyword_DWord,
        Keyword_Bool,
        Keyword_Char,
        Keyword_String,
        Keyword_Return,
        Keyword_If,
        Keyword_Else,
        Keyword_For,
        Keyword_While,
        Keyword_Do,
        Keyword_Break,
        Keyword_Continue,
        Keyword_Cast,
        Keyword_Namespace,
        Keyword_Sizeof,
        Keyword_Typeof,
        Keyword_Default,

        Keyword_Public,
        Keyword_Private,
        Keyword_Use,
        Keyword_Import,
        Keyword_Extern,
        Keyword_Const,
        Keyword_Global,
        Keyword_Struct,
        
        Keyword_True,
        Keyword_False,

        Keyword_Null,

        Keyword_Assembly,
        Keyword_Interrupt,
        Keyword_Intrinsic,

        Identifier,
        Numeric_Litteral,
        Char_Litteral,
        String_Litteral,
    }
    
    public struct Token
    {
        public readonly TokenType Type;
        public readonly StringRef Value;

        public readonly string FilePath;
        public readonly int Line;
        
        // NOTE: This is not 100% to be true, change name?
        public bool IsType =>
            Type == TokenType.Keyword_Void ||
            Type == TokenType.Keyword_Word ||
            Type == TokenType.Keyword_DWord ||
            Type == TokenType.Keyword_Bool ||
            Type == TokenType.Keyword_Char ||
            Type == TokenType.Keyword_String ||
            Type == TokenType.Identifier ||
            Type == TokenType.Open_square_bracket ||
            Type == TokenType.Asterisk ||
            Type == TokenType.DollarSign;

        public bool IsBaseType =>
            Type == TokenType.Keyword_Void ||
            Type == TokenType.Keyword_Word ||
            Type == TokenType.Keyword_DWord ||
            Type == TokenType.Keyword_Bool ||
            Type == TokenType.Keyword_Char ||
            Type == TokenType.Keyword_String;

        public bool IsTypePrefix =>
            Type == TokenType.Open_square_bracket ||
            Type == TokenType.Asterisk ||
            Type == TokenType.DollarSign;

        public bool IsUnaryOp =>
            Type == TokenType.Minus ||
            Type == TokenType.Tilde ||
            Type == TokenType.Exclamationmark ||
            Type == TokenType.ShiftLeft ||
            Type == TokenType.PlusPlus ||
            Type == TokenType.MinusMinus;

        public bool IsBinaryOp =>
            Type == TokenType.Plus ||
            Type == TokenType.Minus ||
            Type == TokenType.Asterisk ||
            Type == TokenType.Slash ||
            Type == TokenType.DoubleAnd ||
            Type == TokenType.DoublePipe ||
            Type == TokenType.DoubleEqual ||
            Type == TokenType.NotEqual ||
            Type == TokenType.LessThan ||
            Type == TokenType.GreaterThan ||
            Type == TokenType.LessThanOrEqual ||
            Type == TokenType.GreaterThanOrEqual ||
            Type == TokenType.Percent ||
            Type == TokenType.And ||
            Type == TokenType.Pipe ||
            Type == TokenType.Caret;

        public bool IsAssignmentOp => 
            Type == TokenType.Equal ||
            Type == TokenType.PlusEqual ||
            Type == TokenType.MinusEqual ||
            Type == TokenType.AsteriskEqual ||
            Type == TokenType.SlashEqual ||
            Type == TokenType.PercentEqual ||
            Type == TokenType.AndEqual ||
            Type == TokenType.PipeEqual ||
            Type == TokenType.CaretEqual;

        public bool IsLitteral => 
            Type == TokenType.Numeric_Litteral ||
            Type == TokenType.Keyword_True ||
            Type == TokenType.Keyword_False ||
            Type == TokenType.Keyword_Null ||
            Type == TokenType.Char_Litteral ||
            Type == TokenType.String_Litteral ||
            // NOTE: The open brace does feel out of place here
            Type == TokenType.Open_brace;

        public bool IsIdentifier => 
            Type == TokenType.Identifier;

        public bool IsDirectiveKeyword =>
            Type == TokenType.Keyword_Public ||
            Type == TokenType.Keyword_Private ||
            Type == TokenType.Keyword_Use ||
            Type == TokenType.Keyword_Import ||
            Type == TokenType.Keyword_Extern || 
            Type == TokenType.Keyword_Const ||
            Type == TokenType.Keyword_Global ||
            Type == TokenType.Keyword_Struct;

        public bool IsPostfixOperator =>
            Type == TokenType.Open_parenthesis ||
            Type == TokenType.Open_square_bracket ||
            Type == TokenType.Period ||
            Type == TokenType.Arrow ||
            Type == TokenType.PlusPlus ||
            Type == TokenType.MinusMinus;

        public Token(TokenType Type, StringRef Value, string filePath, int line, int lineIndex)
        {
            this.Type = Type;
            this.Value = Value;
            this.FilePath = filePath;
            this.Line = line;
            // TODO: LineIndex??
        }

        public override string ToString()
        {
            return $"{Type}: {Value}";
        }
    }

    public class Tokenizer
    {
        [Flags]
        public enum CharCategory
        {
            Letter = 1,
            Number = 2,
            NewLine = 4,
            Whitespace = 8,
            Punctuation = 16,
            Symbol = 32,
            IdentLetter = 64,

            // Used for characters we don't handle
            Invalid = 32768,
        }

        const CharCategory Letter = CharCategory.Letter;

        const CharCategory Number = CharCategory.Number;
        // Whitespace maches newline too..
        const CharCategory NewLine = CharCategory.NewLine;
        const CharCategory Whitespace = CharCategory.Whitespace;
        const CharCategory Punctuation = CharCategory.Punctuation;
        const CharCategory Symbol = CharCategory.Symbol;

        public string CurrentFilePath;
        private string Data;
        private int Index;
        private int Line;
        private int LineStart;

        public List<Token> Tokens = new List<Token>();

        public Tokenizer(string path, string data)
        {
            CurrentFilePath = path;
            Data = data;
            Index = 0;
            Line = 1;
            Tokens = new List<Token>(10000);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetLines() => Line;

        public List<Token> Tokenize()
        {
            while (HasNext())
            {
                // First we eat all whitespace
                ConsumeWhitespace();

                // If we read to the end
                if (HasNext() == false) break;

                int prevIndex = Index;

                switch (Peek())
                {
                    case '/':
                        if (Peek(1) == '/') Tokens.Add(ReadComment());
                        else if (Peek(1) == '=') Tokens.Add(CreateTokenAndAdvanceLength(TokenType.SlashEqual, 2));
                        else Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.Slash));
                        break;
                    case '{': Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.Open_brace)); break;
                    case '}': Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.Close_brace)); break;
                    case '(': Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.Open_parenthesis)); break;
                    case ')': Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.Close_parenthesis)); break;
                    case '[': Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.Open_square_bracket)); break;
                    case ']': Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.Close_squre_bracket)); break;
                    case ':':
                        if (Peek(1) == ':') Tokens.Add(CreateTokenAndAdvanceLength(TokenType.DoubleColon, 2));
                        else Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.Colon));
                        break;
                    case '=':
                        if (IsNext("=><=")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Contains, "=><=".Length));
                        else if (Peek(1) == '=') Tokens.Add(CreateTokenAndAdvanceLength(TokenType.DoubleEqual, 2));
                        else Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.Equal));
                        break;
                    case ';': Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.Semicolon)); break;
                    case '.': Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.Period)); break;
                    case ',': Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.Comma)); break;
                    case '#': Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.Numbersign)); break;
                    case '-':
                        if (Peek(1) == '>') Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Arrow, 2));
                        else if (Peek(1) == '=') Tokens.Add(CreateTokenAndAdvanceLength(TokenType.MinusEqual, 2));
                        else if (Peek(1) == '-') Tokens.Add(CreateTokenAndAdvanceLength(TokenType.MinusMinus, 2));
                        else Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.Minus));
                        break;
                    case '<':
                        if (Peek(1) == '<') Tokens.Add(CreateTokenAndAdvanceLength(TokenType.ShiftLeft, 2));
                        else if (Peek(1) == '=') Tokens.Add(CreateTokenAndAdvanceLength(TokenType.LessThanOrEqual, 2));
                        else Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.LessThan));
                        break;
                    case '>':
                        if (Peek(1) == '>') Tokens.Add(CreateTokenAndAdvanceLength(TokenType.ShiftRight, 2));
                        else if (Peek(1) == '=') Tokens.Add(CreateTokenAndAdvanceLength(TokenType.GreaterThanOrEqual, 2));
                        else Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.GreaterThan));
                        break;
                    case '&':
                        if (Peek(1) == '&') Tokens.Add(CreateTokenAndAdvanceLength(TokenType.DoubleAnd, 2));
                        else if (Peek(1) == '=') Tokens.Add(CreateTokenAndAdvanceLength(TokenType.AndEqual, 2));
                        else Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.And));
                        break;
                    case '|':
                        if (Peek(1) == '|') Tokens.Add(CreateTokenAndAdvanceLength(TokenType.DoublePipe, 2));
                        else if (Peek(1) == '=') Tokens.Add(CreateTokenAndAdvanceLength(TokenType.PipeEqual, 2));
                        else Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.Pipe));
                        break;
                    case '!':
                        if (Peek(1) == '=') Tokens.Add(CreateTokenAndAdvanceLength(TokenType.NotEqual, 2));
                        else Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.Exclamationmark));
                        break;
                    case '+':
                        if (Peek(1) == '=') Tokens.Add(CreateTokenAndAdvanceLength(TokenType.PlusEqual, 2));
                        else if (Peek(1) == '+') Tokens.Add(CreateTokenAndAdvanceLength(TokenType.PlusPlus, 2));
                        else Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.Plus));
                        break;
                    case '*':
                        if (Peek(1) == '=') Tokens.Add(CreateTokenAndAdvanceLength(TokenType.AsteriskEqual, 2));
                        else Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.Asterisk));
                        break;
                    case '%':
                        if (Peek(1) == '=') Tokens.Add(CreateTokenAndAdvanceLength(TokenType.PercentEqual, 2));
                        else Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.Percent));
                        break;
                    case '^':
                        if (Peek(1) == '=') Tokens.Add(CreateTokenAndAdvanceLength(TokenType.CaretEqual, 2));
                        else Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.Caret));
                        break;
                    case '$': Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.DollarSign)); break;
                    case '~': Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.Tilde)); break;
                    case '?': Tokens.Add(CreateOneCharTokenAndAdvance(TokenType.Questionmark)); break;
                    case '"': Tokens.Add(ReadString()); break;
                    case '\'': Tokens.Add(ReadChar()); break;
                    #region Keywords
                    case 'v':
                        if (IsNextWithDelimiter("void")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Void, "void".Length));
                        else Tokens.Add(ReadIdent());
                        break;
                    case 'w':
                        if (IsNextWithDelimiter("word")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Word, "word".Length));
                        else if (IsNextWithDelimiter("while")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_While, "while".Length));
                        else Tokens.Add(ReadIdent());
                        break;
                    case 'd':
                        if (IsNextWithDelimiter("dword")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_DWord, "dword".Length));
                        else if (IsNextWithDelimiter("do")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Do, "do".Length));
                        else if (IsNextWithDelimiter("default")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Default, "default".Length));
                        else Tokens.Add(ReadIdent());
                        break;
                    case 'b':
                        if (IsNextWithDelimiter("bool")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Bool, "bool".Length));
                        else if (IsNextWithDelimiter("break")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Break, "break".Length));
                        else Tokens.Add(ReadIdent());
                        break;
                    case 'c':
                        if (IsNextWithDelimiter("char")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Char, "char".Length));
                        else if (IsNextWithDelimiter("continue")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Continue, "continue".Length));
                        else if (IsNextWithDelimiter("cast")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Cast, "cast".Length));
                        else if (IsNextWithDelimiter("const")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Const, "const".Length));
                        else Tokens.Add(ReadIdent());
                        break;
                    case 's':
                        if (IsNextWithDelimiter("string")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_String, "string".Length));
                        else if (IsNextWithDelimiter("sizeof")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Sizeof, "sizeof".Length));
                        else if (IsNextWithDelimiter("struct")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Struct, "struct".Length));
                        else Tokens.Add(ReadIdent());
                        break;
                    case 'r':
                        if (IsNextWithDelimiter("return")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Return, "return".Length));
                        else Tokens.Add(ReadIdent());
                        break;
                    case 'i':
                        if (IsNextWithDelimiter("if")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_If, "if".Length));
                        else if (IsNextWithDelimiter("import")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Import, "import".Length));
                        else if (IsNextWithDelimiter("interrupt")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Interrupt, "interrupt".Length));
                        else if (IsNextWithDelimiter("intrinsic")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Intrinsic, "intrinsic".Length));
                        else Tokens.Add(ReadIdent());
                        break;
                    case 'e':
                        if (IsNextWithDelimiter("else")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Else, "else".Length));
                        else if (IsNextWithDelimiter("extern")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Extern, "extern".Length));
                        else Tokens.Add(ReadIdent());
                        break;
                    case 'f':
                        if (IsNextWithDelimiter("for")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_For, "for".Length));
                        else if (IsNextWithDelimiter("false")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_False, "false".Length));
                        else Tokens.Add(ReadIdent());
                        break;
                    case 'n':
                        if (IsNextWithDelimiter("namespace")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Namespace, "namespace".Length));
                        else if (IsNextWithDelimiter("null")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Null, "null".Length));
                        else Tokens.Add(ReadIdent());
                        break;
                    case 't':
                        if (IsNextWithDelimiter("typeof")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Typeof, "typeof".Length));
                        else if (IsNextWithDelimiter("true")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_True, "true".Length));
                        else Tokens.Add(ReadIdent());
                        break;
                    case 'p':
                        if (IsNextWithDelimiter("public")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Public, "public".Length));
                        else if (IsNextWithDelimiter("private")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Private, "private".Length));
                        else Tokens.Add(ReadIdent());
                        break;
                    case 'u':
                        if (IsNextWithDelimiter("use")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Use, "use".Length));
                        else Tokens.Add(ReadIdent());
                        break;
                    case 'a':
                        if (IsNextWithDelimiter("assembly")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Assembly, "assembly".Length));
                        else Tokens.Add(ReadIdent());
                        break;
                    case 'g':
                        if (IsNextWithDelimiter("global")) Tokens.Add(CreateTokenAndAdvanceLength(TokenType.Keyword_Global, "global".Length));
                        else Tokens.Add(ReadIdent());
                        break;
                    #endregion
                    default:
                        // Here we need to do idents, instructions and numbers!
                        if (IsValidFirstIdentChar(Peek()))
                            Tokens.Add(ReadIdent());
                        else if (IsNext(Number))
                            Tokens.Add(ReadNumber());
                        else
                            Error($"Got unknown char '{Peek()}'");
                        break;
                }

                if (Index == prevIndex)
                    Debugger.Break();
            }

            return Tokens;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Token CreateOneCharTokenAndAdvance(TokenType type)
        {
            var tok = new Token(type, new StringRef(Data, Index, 1), CurrentFilePath, Line, Index - LineStart);
            Next();
            return tok;
        }

        private Token CreateTokenAndAdvanceLength(TokenType type, int length)
        {
            var tok = new Token(type, new StringRef(Data, Index, length), CurrentFilePath, Line, Index - LineStart);
            for (int i = 0; i < length; i++) Next();
            return tok;
        }

        /// <summary>This is used for multi-line tokens</summary> 
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Token CreateToken(TokenType type, int start, int length, int line)
        {
            return new Token(type, new StringRef(Data, start, length), CurrentFilePath, line, start - LineStart);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Token CreateToken(TokenType type, int start, int length)
        {
            return new Token(type, new StringRef(Data, start, length), CurrentFilePath, Line, start - LineStart);
        }

        private Token ReadChar()
        {
            int start = Index;

            if (Expect('\'') == false) Error("Expected '\''");

            // Here we could also handle escapes better.
            if (IsNext('\\')) Next();
            Next();

            if (Expect('\'') == false) Error("Expected '\''");

            return CreateToken(TokenType.Char_Litteral, start, Index - start);
        }

        private Token ReadString()
        {
            int start = Index;

            if (Peek() == '@') Next();
            if (Expect('"') == false) Error("Expected '\"'");

            // Read chars as long as the next one is not the closing '"'
            while (Peek() != '"')
            {
                // We jump over escapes
                // TODO: Atm we only do simple one char escapes
                // but if we wanted to do more advanced ones we
                // would fix that here
                if (Peek() == '\\')
                    Next();

                Next();
            }

            if (Expect('"') == false) Error("Expected '\"' at the end of the string!");

            return CreateToken(TokenType.String_Litteral, start, Index - start);
        }

        private Token ReadComment()
        {
            int start = Index;

            if (Expect("//") == false) Error("Expected '//'!");

            // PERF: Here we are duplicating work!
            // We could have something like ExpectNotNewLine() or something
            while (IsNextNewLine() == false) Next();

            return CreateToken(TokenType.Comment, start, Index - start);
        }

        private Token ReadIdent()
        {
            int Start = Index;

            ExpectName();

            return CreateToken(TokenType.Identifier, Start, Index - Start);
        }

        private Token ReadNumber()
        {
            int start = Index;
            int startLine = Line;

            ExpectNumber();

            // FIXME: We can't have this safety check unless we intruduce parsing for 12asm as a keyword
            // which is worth considering...
            //if (char.IsLetterOrDigit(Peek())) Error($"Could not parse number! '{Data.Substring(start, (Index + 1) - start)}'");

            return CreateToken(TokenType.Numeric_Litteral, start, Index - start, startLine);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ConsumeWhitespace()
        {
            int count = 0;
            //var peek = Peek();
            while (HasNext() && char.IsWhiteSpace(Peek()) /*&& peek != '\t'*/)
            {
                Next();
                count++;
                //peek = Peek();
            }

            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ConsumeWhitespaceNoNewline()
        {
            int count = 0;
            char peek = Peek();
            while (HasNext() && char.IsWhiteSpace(peek) && peek != '\n' && peek != '\r')
            {
                Next();
                count++;
                peek = Peek();
            }

            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AdvanceLine(char lnChar)
        {
            if (lnChar == '\r') Expect('\n');
            Line++;
            LineStart = Index;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private char Next()
        {
            // NOTE: We might want to return -1 so we can have better error messsages.
            // Or we introduce a context string that tells us what we are currently doing
            // E.g "when tokenizing number"
            if (Index >= Data.Length) Error("Reached end of file!");

            char c = Data[Index++];
            if (c == '\r' || c == '\n')
                AdvanceLine(c);

            return c;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private char Peek()
        {
            if (Index >= Data.Length) Error("Reached end of file!");
            return Data[Index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private char Peek(int tokens)
        {
            if (Index + tokens >= Data.Length) Error("Reached end of file!");
            return Data[Index + tokens];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasNext()
        {
            return Index < Data.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsNext(CharCategory category)
        {
            return IsInCharCategory(Peek(), category);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsNext(char c)
        {
            return Peek() == c;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsNext(string toMatch)
        {
            // NOTE: The toMatch string should not contain new lines!
            if (Data.Length - Index < toMatch.Length) return false;

            for (int i = 0; i < toMatch.Length; i++)
            {
                if (Data[Index + i] != toMatch[i]) return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsNextWithDelimiter(string toMatch)
        {
            // NOTE: The toMatch string should not contain new lines!
            if (Data.Length - Index < toMatch.Length) return false;

            for (int i = 0; i < toMatch.Length; i++)
            {
                if (Data[Index + i] != toMatch[i]) return false;
            }

            const string DELIMITERS = " ,;:\r\n\t()[]{}-+*/";

            // Check that the last character is one of the delimiter characters
            char c = Data[Index + toMatch.Length];
            for (int i = 0; i < DELIMITERS.Length; i++)
            {
                if (c == DELIMITERS[i]) return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsNextNewLine()
        {
            char c = Peek();
            return c == '\r' || c == '\n';
        }

        /// <summary>
        /// Matches some string and if true advances the reader.
        /// </summary>
        /// <param name="toMatch">The stirng to match.</param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Expect(string toMatch)
        {
            // NOTE: The toMatch string should not contain new lines!
            if (Data.Length - Index < toMatch.Length) return false;

            for (int i = 0; i < toMatch.Length; i++)
            {
                if (Data[Index + i] != toMatch[i]) return false;
            }

            Index += toMatch.Length;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Expect(char c)
        {
            if (Peek() == c)
            {
                Index++;
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Expects one character of this unicode category
        /// </summary>
        /// <param name="unicodeCategory"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Expect(CharCategory category)
        {
            char c = Peek();
            if (IsInCharCategory(c, category))
            {
                Index++;
                return true;
            }
            else
            {
                return false;
            }
        }

        // Reads a valid name
        // [A-Za-z_...][0-9A-Za-z_...]*
        private void ExpectName()
        {
            if (IsValidFirstIdentChar(Peek()) == false)
                Error($"A name cannot start with '{Peek()}'");

            while (IsValidIdentChar(Peek())) Next();
        }

        private void ExpectNumber()
        {
            char c1 = Peek();
            char c2 = Peek(1);
            if (c2 == 'x')
            {
                if (c1 != '0' && c1 != '8') Error($"Unknown number format specifier '{c1}{c2}'!");

                // Read the two first characters
                Next();
                Next();

                if (c1 == '0')
                    while (Util.IsHexOrUnderscore(Peek())) Next();
                else while (Util.IsOctalOrUnderscore(Peek())) Next();
            }
            else if (c2 == 'b')
            {
                if (c1 != '0') Error($"Unknown number format specifier '{c1}{c2}'!");

                // Read the two first characters
                Next();
                Next();

                while (Util.IsBinaryOrUnderscore(Peek())) Next();
            }
            else
            {
                if (c1 == '-') Next();

                // Do normal parsing
                while (Util.IsDecimalOrUnderscore(Peek())) Next();

                // Optionally consume the trailing type specifier
                char p = Peek();
                if (p == 'W' || p == 'w' || p == 'd' || p == 'D') Next();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsValidFirstIdentChar(char c)
        {
            return char.IsLetter(c) || c == '_';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsValidIdentChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }

        // @Speed: This could probably be better?
        private bool IsInCharCategory(char c, CharCategory category)
        {
            if (category.HasFlag(CharCategory.Letter))
                if (char.IsLetter(c)) return true;

            if (category.HasFlag(CharCategory.Number))
                if (char.IsNumber(c)) return true;

            if (category.HasFlag(CharCategory.Whitespace))
                if (char.IsWhiteSpace(c)) return true;

            if (category.HasFlag(CharCategory.NewLine))
                if (c == '\r' || c == '\n') return true;

            if (category.HasFlag(CharCategory.Punctuation))
                if (char.IsPunctuation(c)) return true;

            if (category.HasFlag(CharCategory.Symbol))
                if (char.IsSymbol(c))
                    return true;

            if (category.HasFlag(CharCategory.Invalid))
                return true;

            return false;
        }

        private bool MatchString(string data, int startIndex, string toMatch)
        {
            if (data.Length - startIndex < toMatch.Length) return false;

            for (int i = 0; i < toMatch.Length; i++)
            {
                if (data[startIndex + i] != toMatch[i]) return false;
            }

            return true;
        }

        private void Error(string message)
        {
            Console.WriteLine($"In file {Path.GetFileName(CurrentFilePath)} on line {Line} character {Index - LineStart}:");
            Console.WriteLine($"    {message}");
            //Debugger.Break();
            Console.ReadLine();
            Environment.Exit(1);
        }
    }
}
