using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

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

        Semicolon,
        Period,
        Comma,
        Colon,

        DoubleAnd,
        DoublePipe,
        DoubleEqual,
        NotEqual,

        Less_than_or_equal,
        Greater_than_or_equal,

        Equal,
        PlusEqual,
        MinusEqual,
        AsteriskEqual,
        SlashEqual,
        PercentEqual,
        AndEqual,
        PipeEqual,
        CaretEqual,

        Plus,
        Minus,
        Asterisk,
        Slash,
        Percent,

        And,
        Pipe,
        Caret,

        Tilde,
        Exclamationmark,
        Questionmark,
        
        Open_angle_bracket,
        Close_angle_bracket,
        
        Keyword_Void,
        Keyword_Word,
        Keyword_DWord,
        Keyword_Bool,
        Keyword_Return,
        Keyword_If,
        Keyword_Else,
        Keyword_For,
        Keyword_While,
        Keyword_Do,
        Keyword_Break,
        Keyword_Continue,

        Keyword_Use,
        Keyword_Extern,
        Keyword_Const,
        Keyword_Global,
        
        Keyword_True,
        Keyword_False,

        Identifier,
        Word_Litteral,

        // DWord_Litteral,
        // Bool_Litteral,

        // String_Litteral,
    }
    
    public struct Token
    {
        public readonly TokenType Type;
        public readonly string Value;

        // NOTE: This is not 100% to be true, change name?
        public bool IsType =>
            Type == TokenType.Keyword_Void ||
            Type == TokenType.Keyword_Word ||
            Type == TokenType.Keyword_DWord ||
            Type == TokenType.Keyword_Bool ||
            Type == TokenType.Identifier;

        public bool IsBaseType =>
            Type == TokenType.Keyword_Void ||
            Type == TokenType.Keyword_Word ||
            Type == TokenType.Keyword_DWord ||
            Type == TokenType.Keyword_Bool;

        public bool IsUnaryOp =>
            Type == TokenType.Minus ||
            Type == TokenType.Tilde ||
            Type == TokenType.Exclamationmark;

        public bool IsBinaryOp =>
            Type == TokenType.Plus ||
            Type == TokenType.Minus ||
            Type == TokenType.Asterisk ||
            Type == TokenType.Slash ||
            Type == TokenType.DoubleAnd ||
            Type == TokenType.DoublePipe ||
            Type == TokenType.DoubleEqual ||
            Type == TokenType.NotEqual ||
            Type == TokenType.Open_angle_bracket ||
            Type == TokenType.Close_angle_bracket ||
            Type == TokenType.Less_than_or_equal ||
            Type == TokenType.Greater_than_or_equal ||
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
            Type == TokenType.Word_Litteral ||
            Type == TokenType.Keyword_True ||
            Type == TokenType.Keyword_False;

        public bool IsIdentifier => 
            Type == TokenType.Identifier;

        public bool IsDirectiveKeyword => 
            Type == TokenType.Keyword_Use ||
            Type == TokenType.Keyword_Extern || 
            Type == TokenType.Keyword_Const ||
            Type == TokenType.Keyword_Global;

        public Token(TokenType Type, string Value)
        {
            this.Type = Type;
            this.Value = Value;
        }

        public override string ToString()
        {
            return $"{Type}: {Value}";
        }
    }

    public class Tokenizer
    {
        private static List<(TokenType Type, Regex Regex)> TokenRegexes = new List<(TokenType, Regex)>
        {
            ( TokenType.Comment, new Regex("^\\/\\/.*") ),

            ( TokenType.Open_brace, new Regex("^{") ),
            ( TokenType.Close_brace, new Regex("^}") ),

            ( TokenType.Open_parenthesis, new Regex("^\\(") ),
            ( TokenType.Close_parenthesis, new Regex("^\\)") ),

            ( TokenType.Open_square_bracket, new Regex("^\\[") ),
            ( TokenType.Close_squre_bracket, new Regex("^\\]") ),

            ( TokenType.Semicolon, new Regex("^;") ),
            ( TokenType.Period, new Regex("^\\.") ),
            ( TokenType.Comma, new Regex("^,") ),
            ( TokenType.Colon, new Regex("^:") ),

            ( TokenType.DoubleAnd, new Regex("^&&") ),
            ( TokenType.DoublePipe, new Regex("^\\|\\|") ),
            ( TokenType.DoubleEqual, new Regex("^==") ),
            ( TokenType.NotEqual, new Regex("^!=") ),

            ( TokenType.Less_than_or_equal, new Regex("^<=") ),
            ( TokenType.Greater_than_or_equal, new Regex("^>=") ),
            ( TokenType.Open_angle_bracket, new Regex("^<") ),
            ( TokenType.Close_angle_bracket, new Regex("^>") ),

            ( TokenType.Equal, new Regex("^=") ),
            ( TokenType.PlusEqual, new Regex("^\\+=") ),
            ( TokenType.MinusEqual, new Regex("^\\-=") ),
            ( TokenType.AsteriskEqual, new Regex("^\\*=") ),
            ( TokenType.SlashEqual, new Regex("^\\/=") ),
            ( TokenType.PercentEqual, new Regex("^%=") ),
            ( TokenType.AndEqual, new Regex("^&=") ),
            ( TokenType.PipeEqual, new Regex("^\\|=") ),
            ( TokenType.CaretEqual, new Regex("^\\^=") ),

            ( TokenType.Plus, new Regex("^\\+") ),
            ( TokenType.Minus, new Regex("^\\-") ),
            ( TokenType.Asterisk, new Regex("^\\*") ),
            ( TokenType.Slash, new Regex("^\\/") ),
            ( TokenType.Percent, new Regex("^%") ),

            ( TokenType.And, new Regex("^&") ),
            ( TokenType.Pipe, new Regex("^\\|") ),
            ( TokenType.Caret, new Regex("^\\^") ),

            ( TokenType.Tilde, new Regex("^~") ),
            ( TokenType.Exclamationmark, new Regex("^!") ),
            ( TokenType.Questionmark, new Regex("^\\?") ),
            
            ( TokenType.Keyword_Void, new Regex("^void\\b") ),
            ( TokenType.Keyword_Word, new Regex("^word\\b") ),
            ( TokenType.Keyword_DWord, new Regex("^dword\\b") ),
            ( TokenType.Keyword_Bool, new Regex("^bool\\b") ),
            ( TokenType.Keyword_Return, new Regex("^return\\b") ),
            ( TokenType.Keyword_If, new Regex("^if\\b") ),
            ( TokenType.Keyword_Else, new Regex("^else\\b") ),
            ( TokenType.Keyword_For, new Regex("^for\\b") ),
            ( TokenType.Keyword_While, new Regex("^while\\b") ),
            ( TokenType.Keyword_Do, new Regex("^do\\b") ),
            ( TokenType.Keyword_Break, new Regex("^break\\b") ),
            ( TokenType.Keyword_Continue, new Regex("^continue\\b") ),

            ( TokenType.Keyword_Use, new Regex("^use\\b") ),
            ( TokenType.Keyword_Extern, new Regex("^extern\\b") ),
            ( TokenType.Keyword_Const, new Regex("^const\\b") ),
            ( TokenType.Keyword_Global, new Regex("^global\\b") ),

            ( TokenType.Keyword_True, new Regex("^true\\b") ),
            ( TokenType.Keyword_False, new Regex("^false\\b") ),

            ( TokenType.Identifier, new Regex("^[a-zA-Z]\\w*") ),
            ( TokenType.Word_Litteral, new Regex("^[0-9]+") ),
        };

        public static Queue<Token> Tokenize(string code)
        {
            var tokens = new Queue<Token>();
            
            while (string.IsNullOrWhiteSpace(code) == false)
            {
                tokens.Enqueue(MatchToken(code, out code));
            }

            return tokens;
        }

        private static Token MatchToken(string text, out string remaining)
        {
            (Match, TokenType) FindFirstMatch()
            {
                foreach (var expr in TokenRegexes)
                {
                    Match m = expr.Regex.Match(text);
                    if (m.Success)
                    {
                        return (m, expr.Type);
                    }
                }

                return default;
            }

            text = text.TrimStart();

            var (match, type) = FindFirstMatch();
            
            remaining = text.Substring(match.Length);
            return new Token(type, match.Value);
        }
    }
}
