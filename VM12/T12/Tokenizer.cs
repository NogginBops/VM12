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
        
        Open_angle_bracket,
        Close_angle_bracket,
        
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

        Keyword_Public,
        Keyword_Private,
        Keyword_Use,
        Keyword_Import,
        Keyword_As,
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
        public readonly string Value;

        public readonly string File;
        public readonly int Line;

        public readonly int CharIndex;
        public readonly int CharLength;
        
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

        public Token(TokenType Type, string Value, string file, int line, int startIndex, int charLength)
        {
            this.Type = Type;
            this.Value = Value;
            this.File = file;
            this.Line = line;
            this.CharIndex = startIndex;
            this.CharLength = charLength;
        }

        public override string ToString()
        {
            return $"{Type}: {Value}";
        }
    }

    public class Tokenizer
    {
        private static readonly List<(TokenType Type, Regex Regex)> TokenRegexes = new List<(TokenType, Regex)>
        {
            ( TokenType.Comment, new Regex("\\G\\/\\/.*") ),

            ( TokenType.Open_brace, new Regex("\\G{") ),
            ( TokenType.Close_brace, new Regex("\\G}") ),

            ( TokenType.Open_parenthesis, new Regex("\\G\\(") ),
            ( TokenType.Close_parenthesis, new Regex("\\G\\)") ),

            ( TokenType.Open_square_bracket, new Regex("\\G\\[") ),
            ( TokenType.Close_squre_bracket, new Regex("\\G\\]") ),

            ( TokenType.DoubleColon, new Regex("\\G::") ),
            ( TokenType.Contains, new Regex("\\G=><=") ),

            ( TokenType.Semicolon, new Regex("\\G;") ),
            ( TokenType.Period, new Regex("\\G\\.") ),
            ( TokenType.Comma, new Regex("\\G,") ),
            ( TokenType.Colon, new Regex("\\G:") ),
            ( TokenType.Numbersign, new Regex("\\G#") ),
            ( TokenType.Arrow, new Regex("\\G->") ),

            ( TokenType.ShiftLeft, new Regex("\\G<<") ),
            ( TokenType.ShiftRight, new Regex("\\G>>") ),

            ( TokenType.DoubleAnd, new Regex("\\G&&") ),
            ( TokenType.DoublePipe, new Regex("\\G\\|\\|") ),
            ( TokenType.DoubleEqual, new Regex("\\G==") ),
            ( TokenType.NotEqual, new Regex("\\G!=") ),

            ( TokenType.Less_than_or_equal, new Regex("\\G<=") ),
            ( TokenType.Greater_than_or_equal, new Regex("\\G>=") ),
            ( TokenType.Open_angle_bracket, new Regex("\\G<") ),
            ( TokenType.Close_angle_bracket, new Regex("\\G>") ),

            ( TokenType.Equal, new Regex("\\G=") ),
            ( TokenType.PlusEqual, new Regex("\\G\\+=") ),
            ( TokenType.MinusEqual, new Regex("\\G\\-=") ),
            ( TokenType.AsteriskEqual, new Regex("\\G\\*=") ),
            ( TokenType.SlashEqual, new Regex("\\G\\/=") ),
            ( TokenType.PercentEqual, new Regex("\\G%=") ),
            ( TokenType.AndEqual, new Regex("\\G&=") ),
            ( TokenType.PipeEqual, new Regex("\\G\\|=") ),
            ( TokenType.CaretEqual, new Regex("\\G\\^=") ),

            ( TokenType.PlusPlus, new Regex("\\G\\+\\+") ),
            ( TokenType.MinusMinus, new Regex("\\G\\-\\-") ),

            ( TokenType.Plus, new Regex("\\G\\+") ),
            ( TokenType.Minus, new Regex("\\G\\-") ),
            ( TokenType.Asterisk, new Regex("\\G\\*") ),
            ( TokenType.Slash, new Regex("\\G\\/") ),
            ( TokenType.Percent, new Regex("\\G%") ),

            ( TokenType.And, new Regex("\\G&") ),
            ( TokenType.Pipe, new Regex("\\G\\|") ),
            ( TokenType.Caret, new Regex("\\G\\^") ),
            ( TokenType.DollarSign, new Regex("\\G\\$") ),

            ( TokenType.Tilde, new Regex("\\G~") ),
            ( TokenType.Exclamationmark, new Regex("\\G!") ),
            ( TokenType.Questionmark, new Regex("\\G\\?") ),
            
            ( TokenType.Keyword_Void, new Regex("\\Gvoid\\b") ),
            ( TokenType.Keyword_Word, new Regex("\\Gword\\b") ),
            ( TokenType.Keyword_DWord, new Regex("\\Gdword\\b") ),
            ( TokenType.Keyword_Bool, new Regex("\\Gbool\\b") ),
            ( TokenType.Keyword_Char, new Regex("\\Gchar\\b") ),
            ( TokenType.Keyword_String, new Regex("\\Gstring\\b") ),
            ( TokenType.Keyword_Return, new Regex("\\Greturn\\b") ),
            ( TokenType.Keyword_If, new Regex("\\Gif\\b") ),
            ( TokenType.Keyword_Else, new Regex("\\Gelse\\b") ),
            ( TokenType.Keyword_For, new Regex("\\Gfor\\b") ),
            ( TokenType.Keyword_While, new Regex("\\Gwhile\\b") ),
            ( TokenType.Keyword_Do, new Regex("\\Gdo\\b") ),
            ( TokenType.Keyword_Break, new Regex("\\Gbreak\\b") ),
            ( TokenType.Keyword_Continue, new Regex("\\Gcontinue\\b") ),
            ( TokenType.Keyword_Cast, new Regex("\\Gcast\\b") ),
            ( TokenType.Keyword_Namespace, new Regex("\\Gnamespace\\b") ),
            ( TokenType.Keyword_Sizeof, new Regex("\\Gsizeof\\b") ),

            ( TokenType.Keyword_Public, new Regex("\\Gpublic\\b") ),
            ( TokenType.Keyword_Private, new Regex("\\Gprivate\\b") ),
            ( TokenType.Keyword_Use, new Regex("\\Guse\\b") ),
            ( TokenType.Keyword_Import, new Regex("\\Gimport\\b") ),
            ( TokenType.Keyword_As, new Regex("\\Gas\\b") ),
            ( TokenType.Keyword_Extern, new Regex("\\Gextern\\b") ),
            ( TokenType.Keyword_Const, new Regex("\\Gconst\\b") ),
            ( TokenType.Keyword_Global, new Regex("\\Gglobal\\b") ),
            ( TokenType.Keyword_Struct, new Regex("\\Gstruct\\b") ),

            ( TokenType.Keyword_True, new Regex("\\Gtrue\\b") ),
            ( TokenType.Keyword_False, new Regex("\\Gfalse\\b") ),

            ( TokenType.Keyword_Null, new Regex("\\Gnull\\b") ),

            ( TokenType.Keyword_Assembly, new Regex("\\Gassembly\\b") ),
            ( TokenType.Keyword_Interrupt, new Regex("\\Ginterrupt\\b") ),
            ( TokenType.Keyword_Intrinsic, new Regex("\\Gintrinsic\\b") ),

            ( TokenType.Identifier, new Regex("\\G[a-zA-Z_]\\w*") ),
            // TODO: We can do better dword litterals
            ( TokenType.Numeric_Litteral, new Regex("\\G(0b[0-1_]*[0-1]+|8x[0-7_]*[0-7]+|0x[0-9a-fA-F_]*[0-9a-fA-F]+|[0-9_]*[0-9]+(W|w|D|d)?)") ),
            ( TokenType.Char_Litteral, new Regex("\\G'.'") ),
            // TODO: Fix this?
            ( TokenType.String_Litteral, new Regex("\\G\\\"(?:\\\\.|[^\"\\\\])*\\\"") ),
        };

        public static Queue<Token> Tokenize(string code, string file)
        {
            var tokens = new Queue<Token>();
            
            int line = 1;
            //while (string.IsNullOrWhiteSpace(code) == false)
            //{
            //    tokens.Enqueue(MatchToken(file, code, ref line, out code));
            //}
            
            int index = 0;
            while (index < code.Length)
            {
                // Eat all whitespace
                while (char.IsWhiteSpace(code[index]))
                {
                    if (code[index] == '\n')
                    {
                        line++;
                    }

                    index++;

                    if (index >= code.Length)
                    {
                        // Here we have read till the end of the file!
                        goto ret;
                    }
                }

                // TODO: We want a optimization for things we know we can just string compare to know!

                var (match, type) = FindFirstMatch(code, index);
                
                Token tok = new Token(type, match.Value, file, line, index, index + match.Length - 1);
                index += match.Length;

                tokens.Enqueue(tok);
            }

            ret:
            return tokens;
        }

        private static Token MatchToken(string file, string text, ref int line, out string remaining)
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
            
            int skip = 0;
            while (char.IsWhiteSpace(text[skip]))
            {
                if (text[skip] == '\n')
                {
                    line++;
                }

                skip++;
            }

            text = text.Substring(skip);

            var (match, type) = FindFirstMatch();
            
            remaining = text.Substring(match.Length);
            return new Token(type, match.Value, file, line, 0, match.Length);
        }

        private static (Match, TokenType) FindFirstMatch(string data, int startat)
        {
            foreach (var expr in TokenRegexes)
            {
                // FIXME: The 
                Match m = expr.Regex.Match(data, startat);
                if (m.Success)
                {
                    return (m, expr.Type);
                }
            }

            return default;
        }
    }
}
