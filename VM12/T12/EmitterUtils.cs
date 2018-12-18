using System;
using System.Collections.Generic;
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


    static partial class Emitter
    {
        private static void AppendLineWithComment(this StringBuilder builder, string data, string comment = "")
        {
            if (comment != null && comment.Length > 0)
            {
                builder.AppendLine($"{data}\t; {comment}");
            }
            else
            {
                builder.AppendLine(data);
            }
        }

        private static void IncrementLocal(StringBuilder builder, TraceData trace, int localAddress, ASTType type, int typeSize, string comment = "")
        {
            if ((type == ASTBaseType.Word || type == ASTBaseType.DoubleWord) == false)
                Fail(trace, $"We don't support incrementing variables of type '{type}'!");

            switch (typeSize)
            {
                case 1:
                    builder.AppendLineWithComment($"\tinc {localAddress}", comment);
                    break;
                case 2:
                    builder.AppendLineWithComment($"\tlinc {localAddress}", comment);
                    break;
                default:
                    Fail(trace, $"This should not happen!!!");
                    break;
            }
        }

        private static void IncrementSP(StringBuilder builder, TraceData trace, ASTType type, int typeSize, string comment = "")
        {
            // NOTE: Atm we are duplicating this check...
            if ((type == ASTBaseType.Word || type == ASTBaseType.DoubleWord) == false)
                Fail(trace, $"We don't support incrementing variables of type '{type}'!");

            switch (typeSize)
            {
                case 1:
                    builder.AppendLineWithComment($"\tinc", comment);
                    break;
                case 2:
                    builder.AppendLineWithComment($"\tlinc", comment);
                    break;
                default:
                    Fail(trace, $"Cannot increment types with size larger than 2! Type '{type}' with size '{typeSize}'");
                    break;
            }
        }

        private static void DecrementLocal(StringBuilder builder, TraceData trace, int localAddress, ASTType type, int typeSize, string comment = "")
        {
            if ((type == ASTBaseType.Word || type == ASTBaseType.DoubleWord) == false)
                Fail(trace, $"We don't support decrementing variables of type '{type}'!");

            switch (typeSize)
            {
                case 1:
                    builder.AppendLineWithComment($"\tdec {localAddress}", comment);
                    break;
                case 2:
                    builder.AppendLineWithComment($"\tldec {localAddress}", comment);
                    break;
                default:
                    Fail(trace, $"This should not happen!!!");
                    break;
            }
        }

        private static void DecrementSP(StringBuilder builder, TraceData trace, ASTType type, int typeSize, string comment = "")
        {
            // NOTE: Atm we are duplicating this check...
            if ((type == ASTBaseType.Word || type == ASTBaseType.DoubleWord) == false)
                Fail(trace, $"We don't support decrementing variables of type '{type}'!");

            switch (typeSize)
            {
                case 1:
                    builder.AppendLineWithComment($"\tdec", comment);
                    break;
                case 2:
                    builder.AppendLineWithComment($"\tldec", comment);
                    break;
                default:
                    Fail(trace, $"Cannot decrement types with size larger than 2! Type '{type}' with size '{typeSize}'");
                    break;
            }
        }
        
        private static void DuplicateSP(StringBuilder builder, TraceData trace, int typeSize, string comment = "")
        {
            switch (typeSize)
            {
                case 1:
                    builder.AppendLineWithComment($"\tdup", comment);
                    break;
                case 2:
                    builder.AppendLineWithComment($"\tldup", comment);
                    break;
                default:
                    Fail(trace, $"We can't duplicate arbitrary structs on the stack yet! This is a compiler bug!");
                    break;
            }
        }
        
        private static void SwapSP(StringBuilder builder, TraceData trace, int typeASize, int typeBSize, string comment = "")
        {
            if (typeASize <= 0 || typeASize > 2 || typeBSize <= 0 || typeBSize > 2)
                Fail(trace, $"Cannot swap values of size '{typeASize}' and '{typeBSize}'!");

            switch (typeASize)
            {
                case 1:
                    switch (typeBSize)
                    {
                        case 1:
                            builder.AppendLineWithComment($"\tswap", comment);
                            break;
                        case 2:
                            builder.AppendLineWithComment($"\tslswap", comment);
                            break;
                    }
                    break;
                case 2:
                    switch (typeBSize)
                    {
                        case 1:
                            builder.AppendLineWithComment($"\tslswap slswap", comment);
                            break;
                        case 2:
                            builder.AppendLineWithComment($"\tlswap", comment);
                            break;
                    }
                    break;
            }
        }
        
        private static void OverSP(StringBuilder builder, TraceData trace, int targetSize, int overSize, string comment = "")
        {
            if (targetSize <= 0 || targetSize > 2 || overSize <= 0 || overSize > 2)
                Fail(trace, $"Cannot over a value of size '{targetSize}' over a value of '{overSize}'!");

            switch (targetSize)
            {
                case 1:
                    switch (overSize)
                    {
                        case 1:
                            builder.AppendLineWithComment($"\tover", comment);
                            break;
                        case 2:
                            builder.AppendLineWithComment($"\tsoverl", comment);
                            break;
                    }
                    break;
                case 2:
                    switch (overSize)
                    {
                        case 1:
                            builder.AppendLineWithComment($"\tlovers", comment);
                            break;
                        case 2:
                            builder.AppendLineWithComment($"\tlover", comment);
                            break;
                    }
                    break;
            }
        }

        private static void LoadSP(StringBuilder builder, int typeSize, string comment = "")
        {
            switch (typeSize)
            {
                case 1:
                    builder.AppendLineWithComment($"\tload [SP]", comment);
                    break;
                case 2:
                    builder.AppendLineWithComment($"\tloadl [SP]", comment);
                    break;
                default:
                    builder.AppendLine($"\t; {comment} ({typeSize})");

                    int wordsLeft = typeSize;
                    while (wordsLeft >= 2)
                    {
                        wordsLeft -= 2;

                        if (wordsLeft != 0)
                        {
                            builder.AppendLine("\tldup");
                        }

                        builder.AppendLine("\tloadl [SP]");

                        if (wordsLeft != 0)
                        {
                            builder.AppendLine("\tlswap\t; Swap the loaded value with the pointer underneath");
                            builder.AppendLine("\tlinc linc\t; Increment pointer");
                        }
                    }

                    if (typeSize % 2 != 0)
                    {
                        builder.AppendLine("\tloadl [SP]");
                    }

                    //for (int i = 0; i < typeSize / 2; i++)
                    //{
                    //    // TODO: This could probably be done better
                    //    // Duplicate the pointer, load the one word, swap the word with the pointer underneath, increment the pointer
                    //    // Only save the pointer if it is not the last value we are loading

                    //    bool lastValue = (i * 2) == typeSize;

                    //    if (lastValue == false)
                    //    {
                    //        builder.AppendLine("\tldup");
                    //    }

                    //    builder.AppendLine("\tloadl [SP]");

                    //    if (lastValue == false)
                    //    {
                    //        builder.AppendLine("\tlswap\t; Swap the loaded value with the pointer underneath");
                    //        builder.AppendLine("\tlinc linc\t; Increment pointer");
                    //    }
                    //}

                    //if (typeSize % 2 != 0)
                    //{
                    //    builder.AppendLine("\tloadl [SP]");
                    //}
                    break;
            }
        }

        private static void LoadVariable(StringBuilder builder, TraceData trace, VariableRef var, TypeMap typeMap, ConstMap constMap)
        {
            int typeSize = SizeOfType(var.Type, typeMap);
            switch (var.VariableType)
            {
                case VariableType.Local:
                    switch (typeSize)
                    {
                        case 1:
                            builder.AppendLineWithComment($"\tload {var.LocalAddress}", var.Comment);
                            break;
                        case 2:
                            builder.AppendLineWithComment($"\tloadl {var.LocalAddress}", var.Comment);
                            break;
                        default:
                            builder.AppendLine($"\t; {var.Comment} ({typeSize})");
                            for (int i = 0; i < typeSize; i++)
                            {
                                builder.AppendLine($"\tload {var.LocalAddress + i}\t; {var.Comment}:{i}");
                            }
                            break;
                    }
                    break;
                case VariableType.Pointer:
                    // If we are loading a pointer we assume the pointer is on the stack!
                    // We might what to change this!
                    LoadSP(builder, typeSize, var.Comment);
                    break;
                case VariableType.Global:
                    throw new NotImplementedException();
                case VariableType.Constant:
                    if (constMap.TryGetValue(var.ConstantName, out var constant) == false)
                        Fail(trace, $"No constant '{var.ConstantName}'! This is a compiler bug, we should not get!");
                    
                    if (constant.Value is ASTArrayLitteral)
                    {
                        builder.AppendLineWithComment($"\tload :{var.ConstantName}", var.Comment);
                    }
                    else
                    {
                        switch (typeSize)
                        {
                            case 1:
                                builder.AppendLineWithComment($"\tload #{var.ConstantName}", var.Comment);
                                break;
                            case 2:
                                builder.AppendLineWithComment($"\tloadl #{var.ConstantName}", var.Comment);
                                break;
                            default:
                                throw new NotImplementedException();
                        }
                    }
                    break;
                default:
                    Fail(trace, $"Unknown variable type '{var.VariableType}'!");
                    break;
            }
        }

        private static void StoreSP(StringBuilder builder, int typeSize, string comment = "")
        {
            // It will be hard to implement storing of things larger than 2 words!
            switch (typeSize)
            {
                case 1:
                    builder.AppendLineWithComment($"\tstore [SP]", comment);
                    break;
                case 2:
                    builder.AppendLineWithComment($"\tstorel [SP]", comment);
                    break;
                default:
                    Warning(default, $"Storing a type larger than 2 at a pointer on the stack! This is not optimized at all!! Approximatly {6 + (typeSize * 6)} wasted instructions");

                    if (comment.Length > 0) builder.AppendLine($"\t; {comment}");
                    builder.AppendLine($"\t[SP]");
                    builder.AppendLine($"\tloadl #{typeSize}");
                    builder.AppendLine($"\tlsub");
                    builder.AppendLine($"\tloadl [SP]");
                    builder.AppendLine($"\tloadl #{typeSize}");
                    builder.AppendLine($"\tladd");

                    while (typeSize > 0)
                    {
                        if (typeSize > 2)
                        {
                            builder.AppendLine($"\tlover ; Get the value over the pointer");
                            builder.AppendLine($"\tlover ; Get the pointer over the pointer");
                            builder.AppendLine($"\tlswap ; Place the pointer above the value");
                            builder.AppendLine($"\tstorel [SP] ; Store the value at the pointer");
                            builder.AppendLine($"\tldec ldec ; Decrement the pointer by two");
                            typeSize -= 2;
                        }
                        else if (typeSize == 2)
                        {
                            builder.AppendLine($"\tpop pop ; Remove the temp pointer");
                            builder.AppendLine($"\tstorel [SP]");
                            typeSize -= 2;
                        }
                        else
                        {
                            // We can do this because we only have one value left on the stack and pointer above that
                            builder.AppendLine($"\tpop pop ; Remove the temp pointer");
                            builder.AppendLine($"\tstore [SP] ; Store the value at the pointer");
                            typeSize -= 1;
                        }
                    }

                    break;
            }
        }

        private static void StoreVariable(StringBuilder builder, TraceData trace, VariableRef var, TypeMap typeMap)
        {
            int typeSize = SizeOfType(var.Type, typeMap);
            switch (var.VariableType)
            {
                case VariableType.Local:
                    switch (typeSize)
                    {
                        case 1:
                            builder.AppendLineWithComment($"\tstore {var.LocalAddress}", var.Comment);
                            break;
                        case 2:
                            builder.AppendLineWithComment($"\tstorel {var.LocalAddress}", var.Comment);
                            break;
                        default:
                            builder.AppendLine($"\t; {var.Comment} ({typeSize})");

                            // We assume the value is on the stack
                            // Because the stack is fifo the first value on the stack will be the last value of the type.
                            for (int i = typeSize - 1; i >= 0; i--)
                            {
                                builder.AppendLine($"\tstore {var.LocalAddress + i}\t; {var.Comment}:{i}");
                            }
                            break;
                    }
                    break;
                case VariableType.Pointer:
                    // NOTE: Here we assume the pointer is already on the stack
                    StoreSP(builder, typeSize, var.Comment);
                    break;
                case VariableType.Global:
                    // What do we do here!?
                    throw new NotImplementedException();
                case VariableType.Constant:
                    Fail(trace, $"Cannot modify constant '{var.ConstantName}'!");
                    break;
                default:
                    Fail(trace, $"Unknown variable type '{var.VariableType}'!");
                    break;
            }
        }

    }
}
