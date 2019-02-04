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
                case ASTExplicitCast explicitCast:
                    return FoldExplicitCastExpression(explicitCast, scope, typeMap, functionMap, constMap, globalMap);
                case ASTFunctionCall functionCall:
                    // We don't constant-fold function calls
                    return functionCall;
                case ASTAddressOfExpression addressOfExpression:
                    {
                        ASTExpression foldedAddr = ConstantFold(addressOfExpression.Expr, scope, typeMap, functionMap, constMap, globalMap);
                        return new ASTAddressOfExpression(addressOfExpression.Trace, foldedAddr);
                    }
                case ASTMemberExpression memberExpression:
                    {
                        // We can't constant-fold assigments!
                        if (memberExpression.Assignment != null)
                            return memberExpression;

                        var foldedTarget = ConstantFold(memberExpression.TargetExpr, scope, typeMap, functionMap, constMap, globalMap);

                        // NOTE: We could potentally load the member directly of the target is a constant or something...

                        // NOTE: Do we care if we are derefing or not?
                        return new ASTMemberExpression(memberExpression.Trace, foldedTarget, memberExpression.MemberName, null, memberExpression.Dereference);
                    }
                case ASTPointerExpression pointerExpression:
                    {
                        // We can't constant-fold assigments!
                        if (pointerExpression.Assignment != null)
                            return pointerExpression;

                        var foldedPointer = ConstantFold(pointerExpression.Pointer, scope, typeMap, functionMap, constMap, globalMap);
                        var foldedOffset = ConstantFold(pointerExpression.Offset, scope, typeMap, functionMap, constMap, globalMap);

                        return new ASTPointerExpression(pointerExpression.Trace, foldedPointer, foldedOffset, null);
                    }
                case ASTPointerToVoidPointerCast pointerCast:
                    {
                        // FIXME!!!! Make the implicit pointer casting not happen twice!
                        // See GenerateBinaryCast(...) for details!
                        return pointerCast;

                        var foldedPointer = ConstantFold(pointerCast.From, scope, typeMap, functionMap, constMap, globalMap);
                        return new ASTPointerToVoidPointerCast(pointerCast.Trace, foldedPointer, pointerCast.FromType);
                    }
                case ASTInlineAssemblyExpression assemblyExpression:
                    return assemblyExpression;
                case ASTConditionalExpression conditionalExpression:
                    {
                        var foldedCondition = ConstantFold(conditionalExpression.Condition, scope, typeMap, functionMap, constMap, globalMap);

                        if (foldedCondition is ASTBoolLitteral boolLitteral)
                        {
                            if (boolLitteral.BoolValue == true)
                            {
                                return conditionalExpression.IfTrue;
                            }
                            else
                            {
                                return conditionalExpression.IfFalse;
                            }
                        }

                        // For now we don't constant-fold the brances to avoid the double cast thing with pointer arithmetic!

                        var ifTrue = conditionalExpression.IfTrue;
                        var ifFalse = conditionalExpression.IfFalse;
                        if (ifTrue is ASTNumericLitteral trueLit && ifFalse is ASTNumericLitteral falseLit)
                        {
                            if (trueLit.IntValue == 1 && falseLit.IntValue == 0)
                            {
                                // This is the same as a cast to bool!
                                return new ASTExplicitCast(conditionalExpression.Trace, new ASTExplicitCast(conditionalExpression.Trace, foldedCondition, ASTBaseType.Bool), ASTBaseType.Word);
                            }
                            else if (trueLit.IntValue == 0 && falseLit.IntValue == 1)
                            {
                                // NOTE! This might not be optimal... we don't for now.
                                //return ConstantFold(new ASTUnaryOp(expr.Trace, ASTUnaryOp.UnaryOperationType.Logical_negation, foldedCondition), scope, typeMap, functionMap, constMap, globalMap);
                            }
                        }

                        return new ASTConditionalExpression(conditionalExpression.Trace, foldedCondition, conditionalExpression.IfTrue, conditionalExpression.IfFalse);
                    }
                default:
                    Warning(expr.Trace, $"Trying to constant fold unknown expression of type '{expr.GetType()}'");
                    return expr;
            }
        }

        public static ASTExpression FoldUnaryOp(ASTUnaryOp unaryOp, VarMap scope, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap)
        {
            var foldedExpr = ConstantFold(unaryOp.Expr, scope, typeMap, functionMap, constMap, globalMap);

            if (unaryOp.OperatorType == ASTUnaryOp.UnaryOperationType.Negation && foldedExpr is ASTNumericLitteral numLit)
            {
                return ASTNumericLitteral.From(unaryOp.Trace, -numLit.IntValue);
            }

            // By definition increment and decrement can't be constant folded, because we can't increment a constant

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
                            return ASTWordLitteral.From(wordLitteral.Trace, -wordLitteral.IntValue, wordLitteral.NumberFromat);
                        case ASTDoubleWordLitteral dwordLitteral:
                            return ASTDoubleWordLitteral.From(dwordLitteral.Trace, -dwordLitteral.IntValue, dwordLitteral.NumberFromat);
                        default:
                            return foldedExpr;
                    }
                case ASTUnaryOp.UnaryOperationType.Compliment:
                    switch (foldedExpr)
                    {
                        case ASTWordLitteral wordLitteral:
                            return ASTWordLitteral.From(wordLitteral.Trace, (~wordLitteral.IntValue) & 0xFFF, wordLitteral.NumberFromat);
                        case ASTDoubleWordLitteral dwordLitteral:
                            return ASTDoubleWordLitteral.From(dwordLitteral.Trace, (~dwordLitteral.IntValue) & 0xFFF_FFF, dwordLitteral.NumberFromat);
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
            // FIXME!! Constant folding tries to generate a binary cast
            // But because the cast dosn't actually change the type
            // the emitter tries to cast again! This is a problem for
            // pointers where we multiply the addend with the size of the pointer!
            // 

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
                            return ASTWordLitteral.From(binaryOp.Trace, value, ASTNumericLitteral.CombineFormats(leftWord.NumberFromat, rightWord.NumberFromat));
                        }
                        else if (foldedLeft is ASTDoubleWordLitteral leftDWord && foldedRight is ASTDoubleWordLitteral rightDWord)
                        {
                            int value = leftDWord.IntValue + rightDWord.IntValue;
                            return ASTDoubleWordLitteral.From(binaryOp.Trace, value, ASTNumericLitteral.CombineFormats(leftDWord.NumberFromat, rightDWord.NumberFromat));
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
                            return ASTWordLitteral.From(binaryOp.Trace, value, ASTNumericLitteral.CombineFormats(leftWord.NumberFromat, rightWord.NumberFromat));
                        }
                        else if (foldedLeft is ASTDoubleWordLitteral leftDWord && foldedRight is ASTDoubleWordLitteral rightDWord)
                        {
                            int value = leftDWord.IntValue - rightDWord.IntValue;
                            return ASTDoubleWordLitteral.From(binaryOp.Trace, value, ASTNumericLitteral.CombineFormats(leftDWord.NumberFromat, rightDWord.NumberFromat));
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
                            // This can result in a value that is bigger than a word so we allow it to become a dword
                            // This might not be right if it really should be a word, but then we do casting or something...
                            // TODO: Check if constant folding will put a value bigger than max word size in a constant of type word
                            return ASTNumericLitteral.From(binaryOp.Trace, value, ASTNumericLitteral.CombineFormats(leftWord.NumberFromat, rightWord.NumberFromat));
                        }
                        else if (foldedLeft is ASTDoubleWordLitteral leftDWord && foldedRight is ASTDoubleWordLitteral rightDWord)
                        {
                            int value = leftDWord.IntValue * rightDWord.IntValue;
                            return ASTDoubleWordLitteral.From(binaryOp.Trace, value, ASTNumericLitteral.CombineFormats(leftDWord.NumberFromat, rightDWord.NumberFromat));
                        }
                        else
                        {
                            return CreateFoldedBinOp();
                        }
                    }
                case ASTBinaryOp.BinaryOperatorType.Bitwise_Or:
                case ASTBinaryOp.BinaryOperatorType.Not_equal:
                case ASTBinaryOp.BinaryOperatorType.Division:
                case ASTBinaryOp.BinaryOperatorType.Modulo:
                case ASTBinaryOp.BinaryOperatorType.Bitwise_And:
                case ASTBinaryOp.BinaryOperatorType.Logical_Or:
                    // FIXME: Implement these foldings!
                    return CreateFoldedBinOp();
                case ASTBinaryOp.BinaryOperatorType.Equal:
                    {
                        if (foldedLeft is ASTNumericLitteral leftNum && leftNum.IntValue == 0)
                        {
                            return new ASTExplicitCast(binaryOp.Trace, foldedRight, ASTBaseType.Bool);
                        }

                        if (foldedRight is ASTNumericLitteral rightNum && rightNum.IntValue == 0)
                        {
                            return new ASTExplicitCast(binaryOp.Trace, foldedLeft, ASTBaseType.Bool);
                        }

                        // We can compare values statically
                        if (foldedLeft is ASTLitteral leftLit && foldedRight is ASTLitteral rightLit)
                        {
                            return new ASTBoolLitteral(binaryOp.Trace, leftLit.Value == rightLit.Value);
                        }

                        return CreateFoldedBinOp();
                    }
                default:
                    Warning(binaryOp.Trace, $"Trying to constant fold unknown binary operator of type '{binaryOp.OperatorType}'");
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

                        // NOTE: We don't constant fold this atm because it breaks indexing constant arrays
                        // Because that needs the actual constant and not the litteral to be able to take the
                        // address of the array.
                        if (constant.Type is ASTFixedArrayType)
                            return variableExpression;

                        var foldedConst = ConstantFold(constant.Value, scope, typeMap, functionMap, constMap, globalMap);

                        if (foldedConst is ASTLitteral litteral)
                        {
                            switch (litteral)
                            {
                                case ASTNumericLitteral numericLitteral:
                                    if (constant.Type == ASTBaseType.Word)
                                    {
                                        // FIXME: How to handle this? (we wan't a constant name but still keep format info)
                                        // We probably implement '#(...)' constants...
                                        return new ASTWordLitteral(numericLitteral.Trace, variable.ConstantName, numericLitteral.IntValue, numericLitteral.NumberFromat);
                                    }
                                    else if (constant.Type == ASTBaseType.DoubleWord)
                                    {
                                        return new ASTDoubleWordLitteral(numericLitteral.Trace, variable.ConstantName, numericLitteral.IntValue, numericLitteral.NumberFromat);
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
                return ASTDoubleWordLitteral.From(sizeofTypeExpression.Trace, typeSize, ASTNumericLitteral.NumberFormat.Decimal);
            }
            else
            {
                return ASTWordLitteral.From(sizeofTypeExpression.Trace, typeSize, ASTNumericLitteral.NumberFormat.Decimal);
            }
        }

        public static ASTExpression FoldImplicitCastExpression(ASTImplicitCast implicitCast, VarMap scope, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap)
        {
            var foldedFrom = ConstantFold(implicitCast.From, scope, typeMap, functionMap, constMap, globalMap);
            
            // The case where the from is a word litteral casted to a dword
            if (foldedFrom is ASTWordLitteral wordLitteral)
            {
                if (implicitCast.To == ASTBaseType.DoubleWord)
                {
                    // Return it as a double word litteral
                    return new ASTDoubleWordLitteral(wordLitteral.Trace, wordLitteral.Value, wordLitteral.IntValue, wordLitteral.NumberFromat);
                }
                else if (implicitCast.To == ASTBaseType.Char)
                {
                    return new ASTCharLitteral(wordLitteral.Trace, wordLitteral.Value, (char)wordLitteral.IntValue);
                }
            }

            // Here we have nothing smart to do and need the emitter to actaully do the cast
            return implicitCast;
        }
        
        public static ASTExpression FoldExplicitCastExpression(ASTExplicitCast explicitCast, VarMap scope, TypeMap typeMap, FunctionMap functionMap, ConstMap constMap, GlobalMap globalMap)
        {
            var foldedFrom = ConstantFold(explicitCast.From, scope, typeMap, functionMap, constMap, globalMap);

            ASTExpression GenerateDefault() => new ASTExplicitCast(explicitCast.Trace, foldedFrom, explicitCast.To);

            // NOTE: We take the type of the original expression here
            // because it is not garaneed that the folded will have the same type...
            var fromType = CalcReturnType(explicitCast.From, scope, typeMap, functionMap, constMap, globalMap);

            if (foldedFrom is ASTVariableExpression varExpr && fromType is ASTPointerType && explicitCast.To is ASTPointerType)
            {
                if (TryResolveVariable(varExpr.Name, scope, globalMap, constMap, functionMap, typeMap, out var variable) == false)
                    return GenerateDefault();

                switch (variable.VariableType)
                {
                    case VariableType.Constant:
                        {
                            if (constMap.TryGetValue(variable.ConstantName, out var constant) == false)
                                return GenerateDefault();

                            // We kind of want the thing to actually have a new type when we return here...
                            return ConstantFold(constant.Value, scope, typeMap, functionMap, constMap, globalMap);
                        }
                    case VariableType.Function:
                        // We kind of want the thing to actually have a new type when we return here...
                        return new ASTDoubleWordLitteral(explicitCast.Trace, $":{variable.FunctionName}", -1, ASTNumericLitteral.NumberFormat.Hexadecimal);
                    // NOTE: We should maybe error of the options that should not
                    // be a result of TryResolveVariable (pointer...)
                    case VariableType.Local:
                    case VariableType.Pointer:
                    case VariableType.Global:
                    default:
                        return GenerateDefault();
                }
            }
            else if (fromType is ASTFunctionPointerType && explicitCast.To == ASTPointerType.Of(ASTBaseType.Void))
            {
                // We kind of want the thing to actually have a new type when we return here...
                return foldedFrom;
            }
            else if (fromType == ASTBaseType.DoubleWord && explicitCast.To is ASTPointerType)
            {
                // We can cast a dword to a pointer statically
                // We kind of want the thing to actually have a new type when we return here...
                return foldedFrom;
            }
            else if (fromType == ASTBaseType.DoubleWord && explicitCast.To is ASTFixedArrayType)
            {
                // We can cast a dword to a fixed array statically
                // We really want the thing to actually have a new type when we return here...
                return foldedFrom;
            }
            else if (fromType is ASTPointerType && explicitCast.To is ASTFixedArrayType)
            {
                // We can cast a dword to a fixed array statically
                // We really want the thing to actually have a new type when we return here...
                return foldedFrom;
            }
            else if (foldedFrom is ASTNumericLitteral numLit && fromType is ASTPointerType fromPointerType && explicitCast.To is ASTPointerType toPointerType)
            {
                // We can cast a dword to a fixed array statically
                return new ASTPointerLitteral(numLit.Trace, numLit.Value, numLit.IntValue, toPointerType);
            }
            else
            {
                return GenerateDefault();
            }
        }
    }
}
