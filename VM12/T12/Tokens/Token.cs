using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace T12.Tokens
{
    enum TokenType
    {
        Comment,
        Litteral_number,
        Litteral_string,
        Litteral_character,
        Keyword,
        Identifier,
        Open_parenthesis,
        Closed_parenthesis,
        Equals,
        Plus,
        Minus,
    }

    enum NumberLitteralFormat
    {
        Decimal,
        Hexadecimal,
        Binary,
    }


    struct Location
    {
        public string File;
        public int Line;

        public Location(string file, int line) => (File, Line) = (file, line);
    }

    class Token
    {
        public TokenType Type { get; protected set; }
        public string Value { get; protected set; }
        public Location Location { get; protected set; }
        
        public Token(Location location, TokenType type, string value = null) => (Location, Type, Value) = (location, type, value);
    }
}
