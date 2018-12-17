using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace T12
{
    using ConstMap = Dictionary<string, ASTConstDirective>;
    using FunctionMap = Dictionary<string, List<ASTFunction>>;
    using GlobalMap = Dictionary<string, ASTGlobalDirective>;
    using ImportMap = Dictionary<string, ASTFile>;
    using TypeMap = Dictionary<string, ASTType>;
    using VarList = List<(string Name, int Offset, ASTType Type)>;
    using VarMap = Dictionary<string, (int Offset, ASTType Type)>;

    using static Emitter;

    static class ConstantFolding
    {
        // TODO: There is the problem where a lot of comments are lost 
        // because of constant folding
        // We wan't something where we only fold constants and other things if they can be folded
        // Or some other thing where we still get comments or constant names
        // Like doing all constants in the  #(...) form.
        // This is really the solution i want to find!
        public static ASTExpression ConstantFold(ASTExpression expr, VarMap scope, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap)
        {
            switch (expr)
            {
                case ASTLitteral litteral:
                    return litteral;
                case ASTUnaryOp unaryOp:
                    return FoldUnaryOp(unaryOp, scope, typeMap, functionMap, constMap, globalMap);
                case ASTBinaryOp binaryOp:
                    return FoldBinaryOp(binaryOp, scope, typeMap, functionMap, constMap, globalMap);
                case ASTVariableExpression variableExpression:
                    return FoldVariableExpression(variableExpression, scope, typeMap, functionMap, constMap, globalMap);
                case ASTSizeofTypeExpression sizeofTypeExpression:
                    return FoldSizeOfTypeExpression(sizeofTypeExpression, scope, typeMap, functionMap, constMap, globalMap);
                case ASTImplicitCast implicitCast:
                    return FoldImplicitCastExpression(implicitCast, scope, typeMap, functionMap, constMap, globalMap);
                default:
                    //Warning(expr.Trace, $"Trying to constant fold unknown expression of type '{expr.GetType()}'");
                    return expr;
            }
        }

        public static ASTExpression FoldUnaryOp(ASTUnaryOp unaryOp, VarMap scope, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap)
        {
            var foldedExpr = ConstantFold(unaryOp.Expr, scope, typeMap, functionMap, constMap, globalMap);

            // FIXME: For now we do this!
            return new ASTUnaryOp(unaryOp.Trace, unaryOp.OperatorType, foldedExpr);

            switch (unaryOp.OperatorType)
            {
                case ASTUnaryOp.UnaryOperationType.Identity:
                    return foldedExpr;
                case ASTUnaryOp.UnaryOperationType.Negation:
                    // TODO: Should we do something with chars?
                    switch (foldedExpr)
                    {
                        case ASTWordLitteral wordLitteral:
                            return new ASTWordLitteral(wordLitteral.Trace, "-" + wordLitteral.Value, -wordLitteral.IntValue);
                        case ASTDoubleWordLitteral dwordLitteral:
                            return new ASTDoubleWordLitteral(dwordLitteral.Trace, "-" + dwordLitteral.Value, -dwordLitteral.IntValue);
                        default:
                            return foldedExpr;
                    }
                case ASTUnaryOp.UnaryOperationType.Compliment:
                    switch (foldedExpr)
                    {
                        case ASTWordLitteral wordLitteral:
                            return new ASTWordLitteral(wordLitteral.Trace, "~" + wordLitteral.Value, (~wordLitteral.IntValue) & 0xFFF);
                        case ASTDoubleWordLitteral dwordLitteral:
                            return new ASTDoubleWordLitteral(dwordLitteral.Trace, "~" + dwordLitteral.Value, (~dwordLitteral.IntValue) & 0xFFF_FFF);
                        default:
                            return foldedExpr;
                    }
                case ASTUnaryOp.UnaryOperationType.Logical_negation:
                    switch (foldedExpr)
                    {
                        case ASTBoolLitteral boolLitteral:
                            return new ASTBoolLitteral(boolLitteral.Trace, !boolLitteral.BoolValue);
                        default:
                            return foldedExpr;
                    }
                case ASTUnaryOp.UnaryOperationType.Dereference:
                    return new ASTUnaryOp(unaryOp.Trace, ASTUnaryOp.UnaryOperationType.Dereference, foldedExpr);
                case ASTUnaryOp.UnaryOperationType.Increment:
                case ASTUnaryOp.UnaryOperationType.Increment_post:
                case ASTUnaryOp.UnaryOperationType.Decrement:
                case ASTUnaryOp.UnaryOperationType.Decrement_post:
                    // By definition increment and decrement can't be constant folded, because we can't increment a constant
                    return foldedExpr;
                default:
                    Warning(unaryOp.Trace, $"Trying to constant fold unknown unary operator of type '{unaryOp.OperatorType}'");
                    return foldedExpr;
            }
        }

        public static ASTExpression FoldBinaryOp(ASTBinaryOp binaryOp, VarMap scope, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap)
        {
            // We could cast before everything else so that that becomes part of the left and right folding!
            var binaryCast = GenerateBinaryCast(binaryOp, scope, typeMap, functionMap, constMap, globalMap);
            
            var foldedLeft = ConstantFold(binaryCast.Left, scope, typeMap, functionMap, constMap, globalMap);
            var foldedRight = ConstantFold(binaryCast.Right, scope, typeMap, functionMap, constMap, globalMap);

            ASTBinaryOp CreateFoldedBinOp()
            {
                return new ASTBinaryOp(binaryOp.Trace, binaryOp.OperatorType, foldedLeft, foldedRight);
            }

            // TODO: Implement more constant folding!!
            // FIXME: Handle overflow!!
            switch (binaryOp.OperatorType)
            {
                case ASTBinaryOp.BinaryOperatorType.Addition:
                    {
                        if (foldedLeft is ASTWordLitteral leftWord && foldedRight is ASTWordLitteral rightWord)
                        {
                            int value = leftWord.IntValue + rightWord.IntValue;
                            return new ASTWordLitteral(binaryOp.Trace, $"{value}", value);
                        }
                        else if (foldedLeft is ASTDoubleWordLitteral leftDWord && foldedRight is ASTDoubleWordLitteral rightDWord)
                        {
                            int value = leftDWord.IntValue + rightDWord.IntValue;
                            return new ASTDoubleWordLitteral(binaryOp.Trace, $"{value}", value);
                        }
                        else if (foldedLeft is ASTNumericLitteral leftNumLit && leftNumLit.IntValue == 0)
                        {
                            return foldedRight;
                        }
                        else if (foldedRight is ASTNumericLitteral rightNumLit && rightNumLit.IntValue == 0)
                        {
                            return foldedLeft;
                        }
                        else
                        {
                            return CreateFoldedBinOp();
                        }
                    }
                case ASTBinaryOp.BinaryOperatorType.Subtraction:
                    {
                        if (foldedLeft is ASTWordLitteral leftWord && foldedRight is ASTWordLitteral rightWord)
                        {
                            int value = leftWord.IntValue - rightWord.IntValue;
                            return new ASTWordLitteral(binaryOp.Trace, $"{value}", value);
                        }
                        else if (foldedLeft is ASTDoubleWordLitteral leftDWord && foldedRight is ASTDoubleWordLitteral rightDWord)
                        {
                            int value = leftDWord.IntValue - rightDWord.IntValue;
                            return new ASTDoubleWordLitteral(binaryOp.Trace, $"{value}", value);
                        }
                        else if (foldedLeft is ASTNumericLitteral leftNumLit && leftNumLit.IntValue == 0)
                        {
                            // FIXME: Negate the right part!!
                            return CreateFoldedBinOp();
                        }
                        else if (foldedRight is ASTNumericLitteral rightNumLit && rightNumLit.IntValue == 0)
                        {
                            return foldedLeft;
                        }
                        else
                        {
                            return CreateFoldedBinOp();
                        }
                    }
                case ASTBinaryOp.BinaryOperatorType.Multiplication:
                    {
                        // FIXME: This can probably be refactored to be much nicer


                        if (foldedLeft is ASTNumericLitteral leftNum)
                        {
                            switch (leftNum.IntValue)
                            {
                                case 0:
                                    return leftNum;
                                case 1:
                                    return foldedRight;
                            }
                        }

                        if (foldedRight is ASTNumericLitteral rightNum)
                        {
                            switch (rightNum.IntValue)
                            {
                                case 0:
                                    return rightNum;
                                case 1:
                                    return foldedLeft;
                            }
                        }


                        if (foldedLeft is ASTWordLitteral leftWord && foldedRight is ASTWordLitteral rightWord)
                        {
                            int value = leftWord.IntValue * rightWord.IntValue;
                            return new ASTWordLitteral(binaryOp.Trace, $"{value}", value);
                        }
                        else if (foldedLeft is ASTDoubleWordLitteral leftDWord && foldedRight is ASTDoubleWordLitteral rightDWord)
                        {
                            int value = leftDWord.IntValue * rightDWord.IntValue;
                            return new ASTDoubleWordLitteral(binaryOp.Trace, $"{value}", value);
                        }
                        else
                        {
                            return CreateFoldedBinOp();
                        }
                    }
                case ASTBinaryOp.BinaryOperatorType.Equal:
                case ASTBinaryOp.BinaryOperatorType.Not_equal:
                case ASTBinaryOp.BinaryOperatorType.Division:
                case ASTBinaryOp.BinaryOperatorType.Modulo:
                case ASTBinaryOp.BinaryOperatorType.Bitwise_And:
                case ASTBinaryOp.BinaryOperatorType.Logical_Or:
                    // FIXME: Implement these foldings!
                    return CreateFoldedBinOp();
                default:
                    Warning(binaryOp.Trace, $"Trying to constant fold unknown unary operator of type '{binaryOp.OperatorType}'");
                    return CreateFoldedBinOp();
            }
        }
        
        public static ASTExpression FoldVariableExpression(ASTVariableExpression variableExpression, VarMap scope, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap)
        {
            // If there is an assignment we can't really constant fold...
            if (variableExpression.AssignmentExpression != null)
                return variableExpression;
            
            // If we can't find the variable, we do nothing here and let the emitter error out
            if (TryResolveVariable(variableExpression.Name, scope, globalMap, constMap, functionMap, typeMap, out var variable) == false)
                return variableExpression;

            switch (variable.VariableType)
            {
                case VariableType.Constant:
                    {
                        if (constMap.TryGetValue(variable.ConstantName, out var constant) == false)
                            Fail(variableExpression.Trace, $"This should not happen! Resolved const '{variable.ConstantName}' but didn't find it in const map!");

                        // If the const does not have a value we just return the const
                        // We could try to make the constant folding generate a 12asm constant
                        // but that seems hard
                        if (constant.Value == null)
                            return variableExpression;

                        var foldedConst = ConstantFold(constant.Value, scope, typeMap, functionMap, constMap, globalMap);

                        if (foldedConst is ASTLitteral litteral)
                        {
                            switch (litteral)
                            {
                                case ASTNumericLitteral numericLitteral:
                                    if (constant.Type == ASTBaseType.Word)
                                    {
                                        return new ASTWordLitteral(numericLitteral.Trace, variable.ConstantName, numericLitteral.IntValue);
                                    }
                                    else if (constant.Type == ASTBaseType.DoubleWord)
                                    {
                                        return new ASTDoubleWordLitteral(numericLitteral.Trace, variable.ConstantName, numericLitteral.IntValue);
                                    }
                                    break;
                                case ASTCharLitteral charLitteral:
                                    return new ASTCharLitteral(variableExpression.Trace, variable.ConstantName, charLitteral.CharValue);
                                default:
                                    return foldedConst;
                            }
                        }

                        return foldedConst;
                    }
                case VariableType.Global:
                    // We don't know the value of the global so we can't constant fold
                case VariableType.Local:
                    // Can't constant fold locals...
                case VariableType.Pointer:
                    // This should not happen
                case VariableType.Function:
                    // Here we might know how to constant fold....
                    return variableExpression;
                default:
                    Warning(variableExpression.Trace, $"Unknown variable type '{variable.VariableType}' when constant folding!!");
                    return variableExpression;
            }
        }
        
        public static ASTExpression FoldSizeOfTypeExpression(ASTSizeofTypeExpression sizeofTypeExpression, VarMap scope, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap)
        {
            int typeSize = SizeOfType(sizeofTypeExpression.Type, typeMap);

            if (typeSize > ASTWordLitteral.WORD_MAX_VALUE)
            {
                return new ASTDoubleWordLitteral(sizeofTypeExpression.Trace, $"{typeSize}", typeSize);
            }
            else
            {
                return new ASTWordLitteral(sizeofTypeExpression.Trace, $"{typeSize}", typeSize);
            }
        }

        public static ASTExpression FoldImplicitCastExpression(ASTImplicitCast implicitCast, VarMap scope, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap)
        {
            var foldedFrom = ConstantFold(implicitCast.From, scope, typeMap, functionMap, constMap, globalMap);
            
            // The case where the from is a word litteral casted to a dword
            if (foldedFrom is ASTWordLitteral wordLitteral && implicitCast.To == ASTBaseType.DoubleWord)
            {
                // Return it as a double word litteral
                return new ASTDoubleWordLitteral(wordLitteral.Trace, wordLitteral.Value, wordLitteral.IntValue);
            }
            else
            {
                // Here we have nothing smart to do and need the emitter to actaully do the cast
                return implicitCast;
            }
        }
    }
}
