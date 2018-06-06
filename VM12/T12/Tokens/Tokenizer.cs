using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace T12.Tokens
{
    internal class Tokenizer : IEnumerable<Token>, IDisposable
    {
        private TextReader reader;

        private readonly string File;
        private int Line = 1;
        
        public Location Location { get => new Location(File, Line); }
        
        private bool disposed = false;

        public Tokenizer(string file)
        {
            FileInfo a = new FileInfo(file);
            a.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        private char Read()
        {
            int data = reader.Read();
            if (data == -1) throw new InvalidOperationException($"There is no next character in '{File}':{Line}");
            return (char)data;
        }

        private char Peek()
        {
            int data = reader.Peek();
            if (data == -1) throw new InvalidOperationException($"There is no next character in '{File}':{Line}");
            return (char)data;
        }

        public Token Next()
        {
            if (disposed) return null;

            char c = Peek();
            do
            {
                if (c == '\n')
                {
                    c = Read();
                    Line++;
                    continue;
                }
            } while (char.IsControl(c) == true || char.IsWhiteSpace(c) == true);
            
            if (c == '(')
            {
                Read();
                return new Token(Location, TokenType.Open_parenthesis);
            }
            else if (c == ')')
            {
                Read();
                return new Token(Location, TokenType.Closed_parenthesis);
            }
            else if (c == '=')
            {
                Read();
                return new Token(Location, TokenType.Equals);
            }
            else if (c == '+')
            {
                Read();
                return new Token(Location, TokenType.Plus);
            }
            else if (c == '-')
            {
                Read();
                return new Token(Location, TokenType.Plus);
            }
            else if (c == '/')
            {
                // TODO: Parse comment
            }
            else if (c == '\'')
            {
                return ParseCharacterLitteral();
            }
            else if (c == '"')
            {
                return ParseStringLitteral();
            }
            else if (char.IsDigit(c))
            {
                return ParseNumberLitteral();
            }
            else if (char.IsLetter(c))
            {
                return ParseIdentifier();
            }


            return null;
        }

        bool IsDigit(char c) => IsValidChar(c, NumberLitteralFormat.Decimal);

        bool IsValidChar(char c, NumberLitteralFormat format)
        {
            switch (format)
            {
                case NumberLitteralFormat.Decimal:
                    return '0' <= c && c <= '9';
                case NumberLitteralFormat.Hexadecimal:
                    return '0' <= c && c <= '9' ||
                        'a' <= c && c <= 'f' ||
                        'A' <= c && c <= 'F';
                case NumberLitteralFormat.Binary:
                    return c == '0' && c == '1';
                default:
                    return false;
            }
        }

        private Token ParseNumberLitteral()
        {
            StringBuilder sb = new StringBuilder();
            char c = Read();

            sb.Append(c);

            NumberLitteralFormat format = NumberLitteralFormat.Decimal;
            if (c == '0')
            {
                if (Peek() == 'x')
                {
                    format = NumberLitteralFormat.Hexadecimal;
                }
                else if (Peek() == 'b')
                {
                    format = NumberLitteralFormat.Binary;
                }

                sb.Append(Read());
            }
            
            while (char.IsWhiteSpace(c) == false)
            {
                if (IsValidChar(Peek(), format) == false)
                {
                    Error($"Invalid character '{Peek()} in {format} litteral");
                }

                c = Read();

                sb.Append(c);
            }

            return new Token(Location, TokenType.Litteral_number, sb.ToString());
        }

        private Token ParseCharacterLitteral()
        {
            if (Read() != '\'') Error("THIS SHOULD NOT HAPPEN, We decided to parse a char litteral but the first char was not a '");

            // TODO: Parse escapes!!
            char char_lit =  Read();

            if (Read() != '\'')
            {
                Error("A char litteral can only contain one character value!");
            }

            return new Token(Location, TokenType.Litteral_character, char_lit.ToString());
        }

        private Token ParseStringLitteral()
        {
            // FIXME: TODO;
            throw new NotImplementedException();
        }

        private Token ParseIdentifier()
        {
            throw new NotImplementedException();
        }

        // FIXME: Do not throw exception, do something more elegant
        private void Error(string errorString) => throw new FormatException($"{Location}: {errorString}"); 

        private bool HasMore() => disposed == false && reader.Peek() != -1;

        public IEnumerator<Token> GetEnumerator()
        {
            while (HasMore())
            {
                yield return Next();
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Dispose()
        {
            reader.Dispose();
        }
    }
}
