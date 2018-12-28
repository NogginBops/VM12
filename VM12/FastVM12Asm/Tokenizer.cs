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

namespace FastVM12Asm
{
    public struct FileData
    {
        public string Path;
        public string Data;
    }
    
    public enum TokenType
    {
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

        Instruction, // ladd
        Identifier, // names of constants?
        Label,  // :<name>
        Call,
        Register,

        Number_litteral, // 234, 0xFFF ....
        String_litteral, // "something"
        Char_litteral, // 'a'

        // Is this really a type to have?
        File_litteral, // <name>.12asm or .t12?

        Comment,
    }

    public struct Token
    {
        public TokenType Type;
        public Opcode InstructionOpcode;

        public FileData File;
        public int Line;
        public int Index;
        public int Length;

        public override string ToString()
        {
            return $"{Type}{{{File.Data.Substring(Index, Length)}}}";
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
        
        private FileData CurrentFile;
        private string Data;
        private int Index;
        private int Line;
        private int LineStart;

        public Tokenizer(string path)
        {
            CurrentFile.Path = path;
            CurrentFile.Data = System.IO.File.ReadAllText(path);
            Data = CurrentFile.Data;
            Index = 0;
        }

        public int GetLines() => Line;

        public List<Token> Tokenize()
        {
            List<Token> Tokens = new List<Token>();
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
        
        private Token CreateToken(TokenType type, int start, int length)
        {
            return new Token
            {
                Type = type,
                Index = start,
                Length = length,
                File = CurrentFile,
                Line = Line,
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
            
            while (char.IsNumber(Peek())) Next();

            return CreateToken(TokenType.Number_litteral, start, Index - start);
        }

        private int ConsumeWhitespace()
        {
            int count = 0;
            while (HasNext() && char.IsWhiteSpace(Peek()))
            {
                Next();
                count++;
            }

            return count;
        }
        
        private void AdvanceLine(char lnChar)
        {
            if (lnChar == '\r') Expect('\n');
            Line++;
            LineStart = Index;
        }

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
        
        private char Peek()
        {
            if (Index >= Data.Length) Error("Reached end of file!");
            return Data[Index];
        }

        private char Peek(int tokens)
        {
            if (Index + (tokens - 1) >= Data.Length) Error("Reached end of file!");
            return Data[Index + (tokens  - 1)];
        }

        private bool HasNext()
        {
            return Index < Data.Length;
        }

        private bool IsNext(CharCategory category)
        {
            return IsInCharCategory(Peek(), category);
        }

        private bool IsNext(char c)
        {
            return Peek() == c;
        }
        
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

        private bool IsValidFirstIdentChar(char c)
        {
            return char.IsLetter(c) || c == '_' || c == '.';
        }

        private bool IsValidIdentChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '.';
        }

        // @Speed: This could probably be better?
        private bool IsInCharCategory(char c, CharCategory category)
        {
            if ((category & CharCategory.Letter) != 0)
                if (char.IsLetter(c)) return true;

            if ((category & CharCategory.Number) != 0)
                if (char.IsNumber(c)) return true;

            if ((category & CharCategory.Whitespace) != 0)
                if (char.IsWhiteSpace(c)) return true;

            if ((category & CharCategory.NewLine) != 0)
                if (c == '\r' || c == '\n') return true;
            
            if ((category & CharCategory.Punctuation) != 0)
                if (char.IsPunctuation(c)) return true;

            if ((category & CharCategory.Symbol) != 0)
                if (char.IsSymbol(c)) 
                    return true;

            if ((category & CharCategory.Invalid) != 0)
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
            Debugger.Break();
            Environment.Exit(1);
        }
    }
}
