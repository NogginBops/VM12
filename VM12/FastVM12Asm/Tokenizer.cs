using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM12_Opcode;
using System.IO;
using Util;
using System.Globalization;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace FastVM12Asm
{
    public class FileData
    {
        public string Path;
        public string Data;
    }
    
    public enum TokenType
    {
        Invalid,
        // This means that there was a tab at the start of the line!
        StartOfLineTab,

        // TODO: Should this be instructions or just different types of tokens?
        Flag, // !<flag>

        // NOTE: Maybe fix better name
        Open_angle,
        Close_angle,
        Open_paren,
        Close_paren,
        Equals,
        And,
        Comma,
        Numbersign,
        Asterisk,
        Plus,
        Minus,

        Identifier, // names of constants?
        Label,  // :<name>
        Call,
        Register,

        Number_litteral, // 234, 0xFFF ....
        String_litteral, // "something"
        Char_litteral, // 'a'

        Comment,
    }

    [Flags]
    public enum TokenFlag
    {
        None = 0,
        Hexadecimal = 1 << 1,
        Octal = 1 << 2,
        Binary = 1 << 3,
    }

    public struct Token
    {
        public TokenType Type;
        public TokenFlag Flags;

        public FileData File;
        public int Line;
        // How many characters into the line this token is
        public int LineCharIndex;

        public int Index;
        public int Length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public char GetFirstChar() => File.Data[Index];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public char GetLastChar() => File.Data[Index + Length - 1];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public char GetChar(int i) => File.Data[Index + i];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string GetContents()
        {
            return File.Data.Substring(Index, Length);
        }

        public (int Value, int Size) ParseNumber()
        {
            if (Type != TokenType.Number_litteral) Debugger.Break();
            string data = File.Data;
            int result = 0;
            int size = 0;
            if (Flags.HasFlag(TokenFlag.Hexadecimal))
            {
                // Parse hex
                for (int i = 2; i < Length; i++)
                {
                    char c = data[Index + i];
                    if (c == '_') continue;

                    size++;

                    result *= 16;
                    result += Util.HexToInt(c);
                }

                size /= 3;
                // Count digits!
            }
            else if (Flags.HasFlag(TokenFlag.Octal))
            {
                // Parse octal
                for (int i = 2; i < Length; i++)
                {
                    char c = data[Index + i];
                    if (c == '_') continue;
                    size++;
                    result *= 8;
                    result += Util.OctalToInt(c);
                }

                size %= 4;
            }
            else if (Flags.HasFlag(TokenFlag.Binary))
            {
                for (int i = 2; i < Length; i++)
                {
                    char c = data[Index + i];
                    if (c == '_') continue;
                    size++;
                    result *= 2;
                    result += Util.BinaryToInt(c);
                }

                size %= 12;
            }
            else
            {
                for (int i = 0; i < Length; i++)
                {
                    char c = data[Index + i];
                    if (c == '_') continue;
                    result *= 10;
                    result += (int)char.GetNumericValue(c);
                }

                size = Util.Log2(result);
                size /= 12;
                //size = result / (1 << 12);
                size++;
            }

            return (result, size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContentsMatch(string str)
        {
            if (Length != str.Length) return false;

            string data = File.Data;
            for (int i = 0; i < str.Length; i++)
            {
                if (data[Index + i] != str[i]) return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool StartsWith(string str)
        {
            if (str.Length > Length) return false;

            string data = File.Data;
            for (int i = 0; i < str.Length; i++)
            {
                if (data[Index + i] != str[i]) return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool EndsWith(string str)
        {
            if (str.Length > Length) return false;

            string data = File.Data;
            int offset = Index + (Length - str.Length);
            for (int i = 0; i < str.Length; i++)
            {
                if (data[offset + i] != str[i]) return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public StringRef ToStringRef(int startOffset = 0)
        {
            StringRef strRef;
            strRef.Data = File.Data;
            strRef.Index = Index + startOffset;
            strRef.Length = Length - startOffset;
            return strRef;
        }

        public override string ToString()
        {
            if (Type == TokenType.StartOfLineTab)
            {
                return $"{Type}{{{@"\t"}}}";
            }
            else
            {
                return $"{Type}{{{File.Data.Substring(Index, Length)}}}";
            }
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
        
        public FileData CurrentFile;
        private string Data;
        private int Index;
        private int Line;
        private int LineStart;

        public List<Token> Tokens = new List<Token>();

        public Tokenizer(string path)
        {
            CurrentFile = new FileData
            {
                Path = path,
                Data = System.IO.File.ReadAllText(path)
            };
            Data = CurrentFile.Data;
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
                    case '<':
                        Tokens.Add(CreateToken(TokenType.Open_angle, Index, 1));
                        Next();
                        break;
                    case '>':
                        Tokens.Add(CreateToken(TokenType.Close_angle, Index, 1));
                        Next();
                        break;
                    case '(':
                        Tokens.Add(CreateToken(TokenType.Open_paren, Index, 1));
                        Next();
                        break;
                    case ')':
                        Tokens.Add(CreateToken(TokenType.Close_paren, Index, 1));
                        Next();
                        break;
                    case '=':
                        Tokens.Add(CreateToken(TokenType.Equals, Index, 1));
                        Next();
                        break;
                    case '&':
                        Tokens.Add(CreateToken(TokenType.And, Index, 1));
                        Next();
                        break;
                    case ',':
                        Tokens.Add(CreateToken(TokenType.Comma, Index, 1));
                        Next();
                        break;
                    case '#':
                        Tokens.Add(CreateToken(TokenType.Numbersign, Index, 1));
                        Next();
                        break;
                    case '*':
                        Tokens.Add(CreateToken(TokenType.Asterisk, Index, 1));
                        Next();
                        break;
                    case '+':
                        Tokens.Add(CreateToken(TokenType.Plus, Index, 1));
                        Next();
                        break;
                    case '-':
                        Tokens.Add(CreateToken(TokenType.Minus, Index, 1));
                        Next();
                        break;
                    case ':':
                        // We know this must be a label or a function call!
                        if (Peek(2) == ':')
                            // This is a call
                            Tokens.Add(ReadCall());
                        else
                            // This is a label
                            Tokens.Add(ReadLabel());
                        break;
                    case '!':
                        Tokens.Add(ReadFlag());
                        break;
                    case '@': // We currently use @ to prefix a raw string
                    case '"':
                        // This is a string lit!
                        Tokens.Add(ReadString());
                        break;
                    case '\'':
                        // This is a char lit
                        Tokens.Add(ReadChar());
                        break;
                    case ';':
                        // This is a comment!
                        Tokens.Add(ReadComment());
                        break;
                    case '[':
                        Tokens.Add(ReadRegister());
                        break;
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
        
        private Token CreateToken(TokenType type, int start, int length, TokenFlag flags = TokenFlag.None)
        {
            return new Token
            {
                Type = type,
                Flags = flags,
                Index = start,
                Length = length,
                File = CurrentFile,
                Line = Line,
                LineCharIndex = start - LineStart,
            };
        }

        private Token ReadRegister()
        {
            int start = Index;

            if (Expect('[') == false) Error("Expected '['!");

            // FIXME: Ensure alteast one!
            while (IsNext(']') == false) Next();

            if (Expect(']') == false) Error("Expected ']'!");

            return CreateToken(TokenType.Register, start, Index - start);
        }

        private Token ReadChar()
        {
            int start = Index;

            if (Expect('\'') == false) Error("Expected '\''");

            // Here we could also handle escapes better.
            if (IsNext('\\')) Next();
            Next();

            if (Expect('\'') == false) Error("Expected '\''");

            return CreateToken(TokenType.Char_litteral, start, Index - start);
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

            return CreateToken(TokenType.String_litteral, start, Index - start);
        }

        private Token ReadFlag()
        {
            int start = Index;
            if (Expect('!') == false) Error("Expected '!'");

            ExpectName();

            return CreateToken(TokenType.Flag, start, Index - start);
        }

        private Token ReadCall()
        {
            int start = Index;

            if (Expect("::") == false) Error("Expected '::'!");

            ExpectName();

            return CreateToken(TokenType.Call, start, Index - start);
        }

        private Token ReadLabel()
        {
            int start = Index;

            if (Expect(':') == false) Error("Expected ':'!");

            ExpectName();

            // This is for label references
            Expect('*');

            return CreateToken(TokenType.Label, start, Index - start);
        }

        private Token ReadComment()
        {
            int start = Index;

            if (Expect(';') == false) Error("Expected ';'!");
            
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

            TokenFlag flags = default;

            char c1 = Peek();
            char c2 = Peek(2);
            if (c2 == 'x')
            {
                if (c1 != '0' && c1 != '8') Error($"Unknown number format specifier '{c1}{c2}'!");

                // Read the two first characters
                Next();
                Next();

                if (c1 == '0')
                {
                    flags |= TokenFlag.Hexadecimal;
                    while (Util.IsHexOrUnderscore(Peek())) Next();
                }
                else
                {
                    flags |= TokenFlag.Octal;
                    while (Util.IsOctalOrUnderscore(Peek())) Next();
                }
            }
            else if (c2 == 'b')
            {
                if (c1 != '0') Error($"Unknown number format specifier '{c1}{c2}'!");

                // Read the two first characters
                Next();
                Next();

                flags |= TokenFlag.Binary;
                while (Util.IsBinaryOrUnderscore(Peek())) Next();
            }
            else
            {
                // Do normal parsing
                while (Util.IsDecimalOrUnderscore(Peek())) Next();
            }
            
            if (char.IsLetterOrDigit(Peek())) Error($"Could not parse number! '{CurrentFile.Data.Substring(start, (Index + 1) - start)}'");

            return CreateToken(TokenType.Number_litteral, start, Index - start, flags);
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
        private void AdvanceLine(char lnChar)
        {
            if (lnChar == '\r') Expect('\n');
            Line++;
            LineStart = Index;

            if (HasNext() && Peek() == '\t') Tokens.Add(CreateToken(TokenType.StartOfLineTab, Index, 1));
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
            if (Index + (tokens - 1) >= Data.Length) Error("Reached end of file!");
            return Data[Index + (tokens  - 1)];
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsValidFirstIdentChar(char c)
        {
            return char.IsLetter(c) || c == '_' || c == '.';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsValidIdentChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '.';
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
            Console.WriteLine($"In file {Path.GetFileName(CurrentFile.Path)} on line {Line} character {Index - LineStart}:");
            Console.WriteLine($"    {message}");
            //Debugger.Break();
            Console.ReadLine();
            Environment.Exit(1);
        }
    }
}
