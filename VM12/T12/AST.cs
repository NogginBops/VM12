using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Linq;

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
            Tokens = new Queue<Token>(Tokens.Where(tok => tok.Type != TokenType.Comment));

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
        
        public readonly ReadOnlyCollection<ASTBlockItem> Body;

        public ASTFunction(string Name, ASTType ReturnType, List<(ASTType, string)> Parameters, List<ASTBlockItem> Body)
        {
            this.Name = Name;
            this.ReturnType = ReturnType;
            this.Parameters = new ReadOnlyCollection<(ASTType Type, string Name)>(Parameters);
            this.Body = new ReadOnlyCollection<ASTBlockItem>(Body);
        }

        public static ASTFunction Parse(Queue<Token> Tokens)
        {
            var type_tok = Tokens.Peek();
            if (type_tok.IsType == false) Fail("Expected a type!");
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

            List<ASTBlockItem> body = new List<ASTBlockItem>();

            peek = Tokens.Peek();
            while (peek.Type != TokenType.Close_brace)
            {
                body.Add(ASTBlockItem.Parse(Tokens));
                if (!Tokens.TryPeek(out peek)) Fail("Expected a closing brace!");
            }

            // Dequeue the closing brace
            var close_brace_tok = Tokens.Dequeue();
            if (close_brace_tok.Type != TokenType.Close_brace) Fail("Expected closing brace");

            return new ASTFunction(name, returnType, parameters, body);
        }
    }

    public abstract class ASTBlockItem : ASTNode
    {
        public static ASTBlockItem Parse(Queue<Token> Tokens)
        {
            var peek = Tokens.Peek();
            switch (peek.Type)
            {
                case TokenType.Keyword_Word:
                    return ASTDeclaration.Parse(Tokens);
                default:
                    return ASTStatement.Parse(Tokens);
            }
        }
    }

    #region Declarations

    public abstract class ASTDeclaration : ASTBlockItem
    {
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

    // Is this the right way to do this?
    public class ASTTypeDeclaration : ASTDeclaration
    {

    }

    #endregion

    #region Statements

    public abstract class ASTStatement : ASTBlockItem
    {
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
                default:
                    return ASTExpressionStatement.Parse(Tokens);
            }
        }
    }
    
    public class ASTEmptyStatement : ASTStatement
    {
        public ASTEmptyStatement() { }

        public static new ASTEmptyStatement Parse(Queue<Token> Tokens)
        {
            var semicolon_tok = Tokens.Dequeue();
            if (semicolon_tok.Type != TokenType.Semicolon) Fail("Expected a semicolon!");

            return new ASTEmptyStatement();
        }
    }

    public class ASTExpressionStatement : ASTStatement
    {
        public readonly ASTExpression Expr;

        public ASTExpressionStatement(ASTExpression Expr)
        {
            this.Expr = Expr;
        }

        public static new ASTExpressionStatement Parse(Queue<Token> Tokens)
        {
            var expr = ASTExpression.Parse(Tokens);

            var semicolon_tok = Tokens.Dequeue();
            if (semicolon_tok.Type != TokenType.Semicolon) Fail("Expected semicolon!");

            return new ASTExpressionStatement(expr);
        }
    }

    public class ASTAssignmentStatement : ASTStatement
    {
        public readonly ReadOnlyCollection<string> VariableNames;
        public readonly ASTExpression AssignmentExpression;

        public ASTAssignmentStatement(List<string> VariableNames, ASTExpression AssignmentExpression)
        {
            this.VariableNames = new ReadOnlyCollection<string>(VariableNames);
            this.AssignmentExpression = AssignmentExpression;
        }

        public static new ASTAssignmentStatement Parse(Queue<Token> Tokens)
        {
            List<string> ids = new List<string>();

            var ident_tok = Tokens.Dequeue();
            if (ident_tok.IsIdentifier == false) Fail("Expected identifier!");
            ids.Add(ident_tok.Value);

            ASTExpression expr = null;

            var peek = Tokens.Peek();
            while (peek.Type == TokenType.Equal)
            {
                // Dequeue equals
                Tokens.Dequeue();

                var cont_ident_tok = Tokens.Peek();
                if (cont_ident_tok.IsIdentifier)
                {
                    // Here we add another value to assign to.
                    ids.Add(cont_ident_tok.Value);
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

            if (expr == null) Fail("Assignment must end in an expression!");

            var semicolon_tok = Tokens.Dequeue();
            if (semicolon_tok.Type != TokenType.Semicolon) Fail("A statement must end in a semicolon!");
            
            return new ASTAssignmentStatement(ids, expr);
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

    public class ASTIfStatement : ASTStatement
    {
        public readonly ASTExpression Condition;
        public readonly ASTStatement IfTrue;
        public readonly ASTStatement IfFalse;

        public ASTIfStatement(ASTExpression Condition, ASTStatement IfTrue, ASTStatement IfFalse)
        {
            this.Condition = Condition;
            this.IfTrue = IfTrue;
            this.IfFalse = IfFalse;
        }

        public static new ASTIfStatement Parse(Queue<Token> Tokens)
        {
            var if_tok = Tokens.Dequeue();
            if (if_tok.Type != TokenType.Keyword_If) Fail("Expected if keyword!");

            var open_paren_tok = Tokens.Dequeue();
            if (open_paren_tok.Type != TokenType.Open_parenthesis) Fail("Expected a opening parenthesis!");

            var expr = ASTExpression.Parse(Tokens);

            var close_paren_tok = Tokens.Dequeue();
            if (close_paren_tok.Type != TokenType.Close_parenthesis) Fail("Expected a closing parenthesis!");

            var ifTrue = ASTStatement.Parse(Tokens);

            var ifFalse = (ASTStatement) null;

            var else_peek = Tokens.Peek();
            if (else_peek.Type == TokenType.Keyword_Else)
            {
                // Dequeue the else keyword
                Tokens.Dequeue();

                ifFalse = ASTStatement.Parse(Tokens);
            }

            return new ASTIfStatement(expr, ifTrue, ifFalse);
        }
    }

    public class ASTCompoundStatement : ASTStatement
    {
        public readonly ReadOnlyCollection<ASTBlockItem> Block;

        public ASTCompoundStatement(List<ASTBlockItem> Block)
        {
            this.Block = new ReadOnlyCollection<ASTBlockItem>(Block);
        }

        public static new ASTCompoundStatement Parse(Queue<Token> Tokens)
        {
            var open_brace_tok = Tokens.Dequeue();
            if (open_brace_tok.Type != TokenType.Open_brace) Fail("Expected opening brace!");

            List<ASTBlockItem> blockItems = new List<ASTBlockItem>();

            var peek = Tokens.Peek();
            while (peek.Type != TokenType.Close_brace)
            {
                blockItems.Add(ASTBlockItem.Parse(Tokens));

                peek = Tokens.Peek();
            }

            var close_brace_tok = Tokens.Dequeue();
            if (close_brace_tok.Type != TokenType.Close_brace) Fail("Expected closing brace!");

            return new ASTCompoundStatement(blockItems);
        }
    }

    public class ASTForWithDeclStatement : ASTStatement
    {
        public readonly ASTVariableDeclaration Declaration;
        public readonly ASTExpression Condition;
        public readonly ASTExpression PostExpression;
        public readonly ASTStatement Body;

        public ASTForWithDeclStatement(ASTVariableDeclaration Declaration, ASTExpression Condition, ASTExpression PostExpression, ASTStatement Body)        {
            this.Declaration = Declaration;
            this.Condition = Condition;
            this.PostExpression = PostExpression;
            this.Body = Body;
        }

        public static new ASTForWithDeclStatement Parse(Queue<Token> Tokens)
        {
            var for_tok = Tokens.Dequeue();
            if (for_tok.Type != TokenType.Keyword_For) Fail("Expected token for!");

            var open_paren_tok = Tokens.Dequeue();
            if (open_paren_tok.Type != TokenType.Open_parenthesis) Fail("Expected opening parenthesis!");

            var declaration = ASTVariableDeclaration.Parse(Tokens);

            var condition = ASTExpression.Parse(Tokens);

            var semicolon_tok = Tokens.Dequeue();
            if (semicolon_tok.Type != TokenType.Semicolon) Fail("Expected a semicolon!");

            var postExpr = ASTExpression.Parse(Tokens);
            
            var close_paren_tok = Tokens.Dequeue();
            if (close_paren_tok.Type != TokenType.Close_parenthesis) Fail("Expected closing parenthesis!");

            var body = ASTStatement.Parse(Tokens);

            return new ASTForWithDeclStatement(declaration, condition, postExpr, body);
        }
    }

    public class ASTWhileStatement : ASTStatement
    {

    }

    public class ASTDoWhileStatement : ASTStatement
    {

    }

    public class ASTBreakStatement : ASTStatement
    {

    }

    public class ASTContinueStatement : ASTStatement
    {

    }

    #endregion

    #region Expressions

    public class ASTExpression : ASTNode
    {
        public static ASTExpression Parse(Queue<Token> Tokens)
        {
            // We start by parsing logical or which has the lowerst precedence
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

                var colon_tok = Tokens.Dequeue();
                if (colon_tok.Type != TokenType.Colon) Fail("Expected a colon!");

                var ifFalse = ASTExpression.Parse(Tokens);

                expr = new ASTConditionalExpression(expr, ifTrue, ifFalse);
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
                var op_type = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var next_term = ParseLogicalAnd(Tokens);
                expr = new ASTBinaryOp(op_type, expr, next_term);
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
                var op_type = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var next_term = ParseBitwiseOr(Tokens);
                expr = new ASTBinaryOp(op_type, expr, next_term);
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
                var op_type = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var next_term = ParseBitwiseXor(Tokens);
                expr = new ASTBinaryOp(op_type, expr, next_term);
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
                var op_type = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var next_term = ParseBitwiseAnd(Tokens);
                expr = new ASTBinaryOp(op_type, expr, next_term);
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
                var op_type = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var next_term = ParseEqual(Tokens);
                expr = new ASTBinaryOp(op_type, expr, next_term);
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
                var op_type = ASTBinaryOp.TokenToOperatorType(Tokens.Dequeue());
                var next_term = ParseRelational(Tokens);
                expr = new ASTBinaryOp(op_type, expr, next_term);
                peek = Tokens.Peek();
            }

            return expr;
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

        public readonly string VariableName;
        public readonly ASTExpression AssignmentExpression;

        public ASTVariableExpression(string VariableName, ASTExpression AssignmentExpression)
        {
            this.VariableName = VariableName;
            this.AssignmentExpression = AssignmentExpression;
        }

        public static new ASTVariableExpression Parse(Queue<Token> Tokens)
        {
            var ident_tok = Tokens.Dequeue();
            if (ident_tok.IsIdentifier == false) Fail("Expected an identifier!");
            string name = ident_tok.Value;

            ASTExpression expr = null;

            var peek = Tokens.Peek();
            if (peek.IsAssignmentOp)
            {
                var op_tok = Tokens.Dequeue();

                expr = ASTExpression.Parse(Tokens);

                if (op_tok.Type != TokenType.Equal)
                {
                    // Replace expr with the appropriate bin op
                    var op_type = TokenToOperatorType(op_tok);

                    expr = new ASTBinaryOp(op_type, new ASTVariableExpression(name, null), expr);
                }
            }

            return new ASTVariableExpression(name, expr);
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
                    Fail($"Token '{token}' is not a assignment operator!");
                    return ASTBinaryOp.BinaryOperatorType.Unknown;
            }
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

    public class ASTConditionalExpression : ASTExpression
    {
        public readonly ASTExpression Condition;
        public readonly ASTExpression IfTrue;
        public readonly ASTExpression IfFalse;

        public ASTConditionalExpression(ASTExpression Condition, ASTExpression IfTrue, ASTExpression IfFalse)
        {
            this.Condition = Condition;
            this.IfTrue = IfTrue;
            this.IfFalse = IfFalse;
        }
    }
    
    #endregion

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
