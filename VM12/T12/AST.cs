using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

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
        public readonly ReadOnlyCollection<ASTFunction> Functions;

        public ASTProgram(IList<ASTFunction> Functions)
        {
            this.Functions = new ReadOnlyCollection<ASTFunction>(Functions);
        }

        public static ASTProgram Parse(Queue<Token> Tokens)
        {
            List<ASTFunction> functions = new List<ASTFunction>();

            while (Tokens.Count > 0)
            {
                functions.Add(ASTFunction.Parse(Tokens));
            }

            return new ASTProgram(functions);
        }
    }

    public class ASTFunction : ASTNode
    {
        public readonly string Name;
        public readonly ASTType ReturnType;
        public readonly ReadOnlyCollection<(ASTType Type, string Name)> Parameters;
        
        public readonly ReadOnlyCollection<ASTStatement> Body;

        public ASTFunction(string Name, ASTType ReturnType, List<(ASTType, string)> Parameters, List<ASTStatement> Body)
        {
            this.Name = Name;
            this.ReturnType = ReturnType;
            this.Parameters = new ReadOnlyCollection<(ASTType Type, string Name)>(Parameters);
            this.Body = new ReadOnlyCollection<ASTStatement>(Body);
        }

        public static ASTFunction Parse(Queue<Token> Tokens)
        {
            if (Tokens.Peek().IsType == false) Fail("Expected a type!");
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
                if (cont_token.Type != TokenType.Comma) break;
                // Dequeue the comma
                Tokens.Dequeue();
                
                peek = Tokens.Peek();
            }

            var close_paren_tok = Tokens.Dequeue();
            if (close_paren_tok.Type != TokenType.Close_parenthesis) Fail("Expected ')'");

            var open_brace_tok = Tokens.Dequeue();
            if (open_brace_tok.Type != TokenType.Open_brace) Fail("Expected '{'");

            List<ASTStatement> body = new List<ASTStatement>();

            peek = Tokens.Peek();
            while (peek.Type != TokenType.Close_brace)
            {
                body.Add(ASTStatement.Parse(Tokens));
                if (!Tokens.TryPeek(out peek)) Fail("Expected a closing brace!");
            }

            // Dequeue the closing brace
            var close_brace_tok = Tokens.Dequeue();
            if (close_brace_tok.Type != TokenType.Close_brace) Fail("Expected closing brace");

            return new ASTFunction(name, returnType, parameters, body);
        }
    }
    
    public abstract class ASTStatement : ASTNode
    {
        public static ASTStatement Parse(Queue<Token> Tokens)
        {
            // Here we need to figure out what statement we need to parse
            var peek = Tokens.Peek();
            
            // We switch on the token trying to find what kind of statement this is.
            // TODO: Add more kinds of statements
            switch (peek.Type)
            {
                case TokenType.Keyword_Return:
                    return ASTReturnStatement.Parse(Tokens);
                case TokenType.Keyword_Word:
                    return ASTVariableDeclaration.Parse(Tokens);
                case TokenType.Identifier:
                    return ASTVariableAssignment.Parse(Tokens);
                default:
                    Fail($"Unexpected token '{peek}'");
                    return default;
            }
        }
    }

    public class ASTVariableDeclaration : ASTStatement
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
    
    public class ASTVariableAssignment : ASTStatement
    {
        public readonly string VariableName;
        public readonly ASTExpression AssignmentExpression;

        public ASTVariableAssignment(string VariableName, ASTExpression AssignmentExpression)
        {
            this.VariableName = VariableName;
            this.AssignmentExpression = AssignmentExpression;
        }

        public static new ASTVariableAssignment Parse(Queue<Token> Tokens)
        {
            var ident_tok = Tokens.Dequeue();
            if (ident_tok.IsIdentifier == false) Fail("Expected identifier!");
            string name = ident_tok.Value;

            var equals_tok = Tokens.Dequeue();
            if (equals_tok.Type != TokenType.Equal) Fail("Expected an equals in varable assignment");

            var expr = ASTExpression.Parse(Tokens);

            var semicolon_tok = Tokens.Dequeue();
            if (semicolon_tok.Type != TokenType.Semicolon) Fail("A statement must end in a semicolon!");

            return new ASTVariableAssignment(name, expr);
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

            var retValExpr = ASTExpression.Parse(Tokens);

            var semicolon_tok = Tokens.Dequeue();
            if (semicolon_tok.Type != TokenType.Semicolon) Fail("A statement must end in a semicolon!");

            return new ASTReturnStatement(retValExpr);
        }
    }

    public class ASTExpression : ASTNode
    {
        public static ASTExpression Parse(Queue<Token> Tokens)
        {
            // We start by parsing logical or which has the lowerst precedence
            // It will then go through all levels of precedence
            return ParseLogicalOr(Tokens);
        }

        // The nineth (lowest) level of precedence (||)
        public static ASTExpression ParseLogicalOr(Queue<Token> Tokens)
        {
            var term = ParseLogicalAnd(Tokens);

            var peek = Tokens.Peek();
            while (peek.Type == TokenType.DoublePipe)
            {
                var op_type = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var next_term = ParseLogicalAnd(Tokens);
                term = new ASTBinaryOp(op_type, term, next_term);
                peek = Tokens.Peek();
            }

            return term;
        }

        // The eigth level of precedence (&&)
        public static ASTExpression ParseLogicalAnd(Queue<Token> Tokens)
        {
            var term = ParseBitwiseOr(Tokens);

            var peek = Tokens.Peek();
            while (peek.Type == TokenType.DoubleAnd)
            {
                var op_type = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var next_term = ParseBitwiseOr(Tokens);
                term = new ASTBinaryOp(op_type, term, next_term);
                peek = Tokens.Peek();
            }

            return term;
        }

        // The seventh level of precedence (|)
        public static ASTExpression ParseBitwiseOr(Queue<Token> Tokens)
        {
            var term = ParseBitwiseXor(Tokens);

            var peek = Tokens.Peek();
            while (peek.Type == TokenType.Pipe)
            {
                var op_type = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var next_term = ParseBitwiseXor(Tokens);
                term = new ASTBinaryOp(op_type, term, next_term);
                peek = Tokens.Peek();
            }

            return term;
        }

        // The sixth level of precedence (^)
        public static ASTExpression ParseBitwiseXor(Queue<Token> Tokens)
        {
            var term = ParseBitwiseAnd(Tokens);

            var peek = Tokens.Peek();
            while (peek.Type == TokenType.Caret)
            {
                var op_type = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var next_term = ParseBitwiseAnd(Tokens);
                term = new ASTBinaryOp(op_type, term, next_term);
                peek = Tokens.Peek();
            }

            return term;
        }

        // The fifth level of precedence (&)
        public static ASTExpression ParseBitwiseAnd(Queue<Token> Tokens)
        {
            var term = ParseEqual(Tokens);

            var peek = Tokens.Peek();
            while (peek.Type == TokenType.And)
            {
                var op_type = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var next_term = ParseEqual(Tokens);
                term = new ASTBinaryOp(op_type, term, next_term);
                peek = Tokens.Peek();
            }

            return term;
        }

        // The fourth level of precedence (!= | ==)
        public static ASTExpression ParseEqual(Queue<Token> Tokens)
        {
            var term = ParseRelational(Tokens);

            var peek = Tokens.Peek();
            while (peek.Type == TokenType.NotEqual || peek.Type == TokenType.DoubleEqual)
            {
                var op_type = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var next_term = ParseRelational(Tokens);
                term = new ASTBinaryOp(op_type, term, next_term);
                peek = Tokens.Peek();
            }

            return term;
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

        //
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

                return expr;
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
            else if (peek.IsIdentifier)
            {
                return ASTVariableExpression.Parse(Tokens);
            }
            else
            {
                Fail($"Could not parse factor. Didn't know what to do with token '{peek}'");
                return default;
            }
        }
    }
    
    public class ASTAssignExpression : ASTExpression
    {
        public readonly string VariableName;
        public readonly ASTExpression ValueExpression;

        public ASTAssignExpression(string VariableName, ASTExpression ValueExpression)
        {
            this.VariableName = VariableName;
            this.ValueExpression = ValueExpression;
        }

        public static new ASTAssignExpression Parse(Queue<Token> Tokens)
        {
            var ident_tok = Tokens.Dequeue();
            if (ident_tok.Type != TokenType.Identifier) Fail("Expected an identifier!");
            string name = ident_tok.Value;

            var equals_tok = Tokens.Dequeue();
            if (equals_tok.Type != TokenType.Equal) Fail("Expected equals in assignment!");

            var expr = ASTExpression.Parse(Tokens);

            return new ASTAssignExpression(name, expr);
        }
    }

    public class ASTLitteral : ASTExpression
    {
        public static new ASTLitteral Parse(Queue<Token> Tokens)
        {
            var peek = Tokens.Peek();
            switch (peek.Type)
            {
                case TokenType.Word_Litteral:
                    return ASTWordLitteral.Parse(Tokens);
                default:
                    Fail($"Expected litteral, got '{peek}'");
                    return default;
            }
        }
    }

    public class ASTWordLitteral : ASTLitteral
    {
        public readonly string Value;
        public readonly int IntValue;

        public ASTWordLitteral(string Value, int IntValue)
        {
            this.Value = Value;
            this.IntValue = IntValue;
        }

        public static new ASTWordLitteral Parse(Queue<Token> Tokens)
        {
            var tok = Tokens.Dequeue();
            if (tok.Type != TokenType.Word_Litteral) Fail("Expected word litteral!");

            if (int.TryParse(tok.Value, out int value) == false) Fail($"Could not parse int '{tok.Value}'");

            return new ASTWordLitteral(tok.Value, value);
        }
    }

    public class ASTVariableExpression : ASTExpression
    {
        public readonly string VariableName;

        public ASTVariableExpression(string VariableName)
        {
            this.VariableName = VariableName;
        }

        public static new ASTVariableExpression Parse(Queue<Token> Tokens)
        {
            var ident_tok = Tokens.Dequeue();
            if (ident_tok.IsIdentifier == false) Fail("Expected an identifier!");
            string name = ident_tok.Value;

            return new ASTVariableExpression(name);
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
    }

    public class ASTType : ASTNode
    {
        public readonly string TypeName;

        public ASTType(string Type)
        {
            this.TypeName = Type;
        }

        public static ASTType Parse(Queue<Token> Tokens)
        {
            var tok = Tokens.Dequeue();
            if (!tok.IsType) Fail("Exptected type identifier!");

            // Temporary
            if (tok.Type != TokenType.Keyword_Word) Fail("We only support word variables atm!");
            
            // Special classes for primitive types?

            return new ASTType(tok.Value);
        }
    }
}
