using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using VM12_Opcode;

namespace FastVM12Asm
{
    public class BinFile
    {
        public short[] Instructions;
        public int UsedInstructions;
        public DynArray<Proc> Metadata;
    }

    public struct Proc
    {
        public StringRef ProcName;
        public int Address;
        public int Line;
        public List<LineLink> LinkedLines;
        public List<short> Instructions;
        public Trace Trace;

        public int Size => Instructions.Count;

        public override string ToString() => $"Proc{{{ProcName}}}";
    }

    public struct LineLink
    {
        public int Instruction;
        public int Line;

        public LineLink(int instruction, int line)
        {
            Instruction = instruction;
            Line = line;
        }
    }

    public class Emitter
    {
        static int autoVars = Constants.RAM_END;

        const int STACK_SIZE = Constants.STACK_MAX_ADDRESS;

        public Dictionary<string, ParsedFile> Files;
        public List<ParsedFile> FileList;

        public Dictionary<StringRef, string> AutoStrings;

        public Dictionary<StringRef, ConstantExpression> GlobalConstExprs = new Dictionary<StringRef, ConstantExpression>();
        public Dictionary<ConstantExpression, EvaluatedConstant> AllEvaluatedConstants = new Dictionary<ConstantExpression, EvaluatedConstant>();

        public Emitter(List<ParsedFile> files, Dictionary<StringRef, string> autoStrings)
        {
            Files = files.ToDictionaryGood(file => Path.GetFileName(file.File.Path));
            AutoStrings = autoStrings;

            bool foundStart = false;

            FileList = new List<ParsedFile>();
            foreach (var file in files)
            {
                foreach (var constExpr in file.ConstExprs)
                {
                    if (constExpr.Value.IsGlobal)
                    {
                        if (constExpr.Value.Type == ConstantExprType.Extern)
                            Error(constExpr.Value.Trace, "Cannot have a extern const be global!");

                        GlobalConstExprs.Add(constExpr.Key, constExpr.Value);
                    }
                }

                bool hasStart = false;
                foreach (var proc in file.Procs)
                {
                    if (proc.Key.ContentsMatch(":start"))
                        foundStart = hasStart = true;
                }

                if (hasStart) FileList.Insert(0, file);
                else FileList.Add(file);
            }

            if (foundStart == false)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("An executable needs to contain a :start proc!");
                Environment.Exit(1);
            }
        }

        public void Error(FileData file, string error)
        {
            Console.ForegroundColor = ConsoleColor.White;
            // FIXME:
            Console.WriteLine($"Error in file {Path.GetFileName(file.Path)}:");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"    {error}");
            //Debugger.Break();
            Console.ReadLine();
            Environment.Exit(1);
        }

        public void Error(Token tok, string error)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"In file {Path.GetFileName(tok.File.Path)} on line {tok.Line} character {tok.LineCharIndex}: '{tok.GetContents()}'");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"    {error}");
            //Debugger.Break();
            Console.ReadLine();
            Environment.Exit(1);
        }

        public void Error(Instruction inst, string error)
        {
            Console.ForegroundColor = ConsoleColor.White;
            // FIXME:
            //Console.WriteLine($"In file {Path.GetFileName(File.Path)} on line {tok.Line} character {tok.LineCharIndex}: '{tok.GetContents()}'");
            Console.WriteLine($"Inst: {inst}, Type: {inst.Type} {inst.Opcode} '{inst.StrArg.ToString()}'");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"    {error}");
            //Debugger.Break();
            Console.ReadLine();
            Environment.Exit(1);
        }

        public void Error(Trace trace, string error)
        {
            Console.ForegroundColor = ConsoleColor.White;
            // FIXME: When start and end line differ
            Console.WriteLine($"In file {Path.GetFileName(trace.File.Path)} on line {trace.StartLine}: '{trace.TraceString}'");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"    {error}");
            //Debugger.Break();
            Console.ReadLine();
            Environment.Exit(1);
        }

        public BinFile Emit()
        {
            Dictionary<Token, List<(int Offset, StringRef Proc)>> AllLabelUses = new Dictionary<Token, List<(int Offset, StringRef Proc)>>();

            Dictionary<Token, Dictionary<StringRef, int>> ProcLabels = new Dictionary<Token, Dictionary<StringRef, int>>();

            DynArray<Proc> ProcList = new DynArray<Proc>(100);

            foreach (var file in FileList)
            {
                Dictionary<StringRef, ConstantExpression> ImportedConstExprs = new Dictionary<StringRef, ConstantExpression>();

                Dictionary<StringRef, List<Instruction>> ImportedProcs = new Dictionary<StringRef, List<Instruction>>();

                foreach (var include in file.IncludeFiles)
                {
                    // Get the parsed file for this include
                    string fileName = include.GetContents();
                    if (Files.TryGetValue(fileName, out ParsedFile includeFile) == false)
                        Error(include, "Could not find file!");

                    // Go through all the global constants in the included file and put them into the map to resolve
                    foreach (var constExpr in includeFile.ConstExprs)
                    {
                        if (constExpr.Value.IsGlobal)
                            ImportedConstExprs.Add(constExpr.Key, constExpr.Value);
                    }

                    // Here we are creating our own map of the imported procs
                    foreach (var proc in includeFile.Procs)
                    {
                        // FIXME: Check if public!!
                        ImportedProcs.Add(proc.Key.ToStringRef(), proc.Value);
                    }
                }

                Dictionary<StringRef, EvaluatedConstant> LocalEvaluatedConstants = new Dictionary<StringRef, EvaluatedConstant>();

                // FIXME
                foreach (var constExpr in file.ConstExprs)
                {
                    // FIXME: Think about how to do this some more!
                    if (constExpr.Value.Type == ConstantExprType.Extern)
                    {
                        // FIXME: Here we should do something???
                        if (GlobalConstExprs.TryGetValue(constExpr.Key, out ConstantExpression expr) == false)
                            Error(constExpr.Value.Trace, "Could not find extern const!");

                        if (AllEvaluatedConstants.TryGetValue(expr, out var evalConst))
                            // Here there was an evaluated value for this
                            LocalEvaluatedConstants.Add(constExpr.Key, evalConst);
                        else
                            // Here there wasn't en evaluated value for this so we add it to the list of things to eval
                            if (ImportedConstExprs.ContainsKey(constExpr.Key) == false)
                                ImportedConstExprs.Add(constExpr.Key, constExpr.Value);
                        else Console.WriteLine("Woah!!");
                    }
                    else
                        ImportedConstExprs.Add(constExpr.Key, constExpr.Value);
                }

                // FIXME: Properly evaluate constants here!
                //Console.WriteLine($"Constants in file: {Path.GetFileName(file.File.Path)}");
                foreach (var constExpr in ImportedConstExprs)
                {
                    if (constExpr.Value.Type == ConstantExprType.Extern)
                        Error(constExpr.Value.Trace, "We should not get extern consts here!!");

                    if (AllEvaluatedConstants.TryGetValue(constExpr.Value, out var evalConst) == false)
                    {
                        // The constant was not resolved so we try to resolve it again!
                        evalConst = EvaluateConstantExpr(constExpr.Value);
                        AllEvaluatedConstants.Add(constExpr.Value, evalConst);
                    }

                    LocalEvaluatedConstants.Add(constExpr.Key, evalConst);
                }

                foreach (var proc in file.Procs)
                {
                    ImportedProcs.Add(proc.Key.ToStringRef(), proc.Value);
                }

                foreach (var proc in file.Procs)
                {
                    List<short> Instructions = new List<short>();
                    List<LineLink> LinkedLines = new List<LineLink>();

                    // A dictionary from label name to instruction index
                    Dictionary<StringRef, int> LocalLabels = new Dictionary<StringRef, int>();
                    List<(int, StringRef)> LabelUses = new List<(int, StringRef)>();

                    int currentSourceLine = 0;
                    foreach (var inst in proc.Value)
                    {
                        if (inst.Trace.StartLine > currentSourceLine && inst.Type != InstructionType.Label)
                        {
                            LinkedLines.Add(new LineLink() { Instruction = Instructions.Count + 1, Line = inst.Trace.StartLine });
                            currentSourceLine = inst.Trace.StartLine;
                        }

                        if (inst.Type == InstructionType.RawOpcode)
                        {
                            Instructions.Add((short)inst.Opcode);
                        }
                        else if (inst.Type == InstructionType.RawWord)
                        {
                            // FIXME: When is this used??
                        }
                        else if (inst.Type == InstructionType.Number)
                        {
                            short[] number = Util.ParseLargeNumber(inst.Trace, inst.StrArg.ToString());
                            for (int i = number.Length - 1; i >= 0; i--)
                            {
                                Instructions.Add(number[i]);
                            }
                        }
                        else if (inst.Type == InstructionType.AutoConstExpr)
                        {
                            if (LocalEvaluatedConstants.TryGetValue(inst.StrArg, out var evalConst) == false)
                                Error(inst, $"Could not find the constant '{inst.StrArg}'");

                            if (evalConst.IsString) Error(inst, "We don't support const strings here atm");

                            Instructions.Add((short)(evalConst.NumberValue.Number >> 12));
                            Instructions.Add((short)(evalConst.NumberValue.Number & 0xFFF));
                        }
                        else if (inst.Type == InstructionType.String)
                        {
                            // This is a string!!
                            if (proc.Value.Count != 1) Error(inst, "A string proc can only contain one string!");

                            // Convert this string into bits
                            int length = inst.StrArg.Length;
                            if (length > 16777216) Error(inst, "String is too long!");
                            Instructions.Add((short)(length >> 12));
                            Instructions.Add((short)(length & 0xFFF));

                            foreach (var c in inst.StrArg)
                            {
                                Instructions.Add((short)c);
                            }
                        }
                        else if (inst.Type == InstructionType.Label)
                        {
                            LocalLabels[inst.StrArg] = Instructions.Count;
                        }
                        else if (inst.Type == InstructionType.Identifier)
                        {
                            if (LocalEvaluatedConstants.TryGetValue(inst.StrArg, out var evalConst) == false)
                                Error(inst, $"Could not find constant '{inst.StrArg.ToString()}'!");

                            if (evalConst.IsString) Error(inst, "Cannot load a string here!!");

                            // FIXME: The number should be able to be bigger than an int?
                            Instructions.Add((short)(evalConst.NumberValue.Number >> 12));
                            Instructions.Add((short)(evalConst.NumberValue.Number & 0xFFF));
                        }
                        else if (inst.Type == InstructionType.WordArgOpcode)
                        {
                            Instructions.Add((short)inst.Opcode);
                            if (inst.Arg > 0xFFF) Error(inst, $"Arg '{inst.Arg}' is larger than a word!");
                            Instructions.Add((short)(inst.Arg & 0xFFF));
                        }
                        else if (inst.Type == InstructionType.DwordArgOpcode)
                        {
                            Instructions.Add((short)inst.Opcode);
                            if (inst.Arg > 0xFFF_FFF) Error(inst, $"Arg '{inst.Arg}' is larger than a dword!");
                            Instructions.Add((short)(inst.Arg >> 12));
                            Instructions.Add((short)(inst.Arg & 0xFFF));
                        }
                        else if (inst.Type == InstructionType.LabelArgOpcode)
                        {
                            Instructions.Add((short)inst.Opcode);
                            LabelUses.Add((Instructions.Count, inst.StrArg));
                            Instructions.Add(0);
                            Instructions.Add(0);
                        }
                        else if (inst.Type == InstructionType.IdentArgOpcode)
                        {
                            // Here we try to find the constant that we want
                            if (LocalEvaluatedConstants.TryGetValue(inst.StrArg, out var evalConst) == false)
                                Error(inst, "Could not find constant!");

                            if (evalConst.IsString == false)
                            {
                                var (value, size) = evalConst.NumberValue;

                                if (size > 2) Error(inst, "We don't support arguments larger than 2 atm!");

                                // FIXME: Here we need to know the max size of the thing we want!
                                //if (inst.Opcode == Opcode.Load_lit && size > 1) Error(inst, "Constant too big to be loaded as a word");
                                //if (inst.Opcode == Opcode.Load_lit_l && size > 2) Error(inst, "Constant too big to be loaded as a dword");

                                // FIXME: We need to know the size of the arg!!
                                Instructions.Add((short)inst.Opcode);
                                for (int i = 0; i < size; i++)
                                {
                                    Instructions.Add((short)(value & 0xFFF));
                                    value >>= 12;
                                }
                            }
                            else Error(inst, "We don't support string args atm!");
                        }
                        else if (inst.Type == InstructionType.AutoStringLoad)
                        {
                            // Here we are going to load an auto string
                            if (AutoStrings.TryGetValue(inst.StrArg, out var labelName) == false)
                            {
                                AutoStrings.Add(inst.StrArg, labelName);

                                Instructions.Add((short)inst.Opcode);

                                LabelUses.Add((Instructions.Count, labelName.ToRef()));
                                Instructions.Add(0);
                                Instructions.Add(0);

                                AutoStrings[inst.StrArg] = labelName;

                            }
                        }
                        else if (inst.Type == InstructionType.ConstExprArgOpcode)
                        {
                            if (LocalEvaluatedConstants.TryGetValue(inst.StrArg, out var evalConst) == false)
                                Error(inst, $"Couldn't find const expr '{inst.StrArg}'");

                            var (value, size) = evalConst.NumberValue;

                            if (size > 2) Error(inst, "We don't support arguments larger than 2 atm!");

                            // FIXME: Here we need to know the max size of the thing we want!
                            //if (inst.Opcode == Opcode.Load_lit && size > 1) Error(inst, "Constant to big to be loaded as a word");
                            //if (inst.Opcode == Opcode.Load_lit_l && size > 2) Error(inst, "Constant to big to be loaded as a dword");

                            // FIXME: We need to know the size of the arg!!
                            Instructions.Add((short)inst.Opcode);
                            for (int i = 0; i < size; i++)
                            {
                                Instructions.Add((short)(value & 0xFFF));
                                value >>= 12;
                            }
                        }
                        else if (inst.Type == InstructionType.Jump)
                        {
                            if (Enum.IsDefined(typeof(JumpMode), (JumpMode)inst.Arg) == false)
                                Error(inst, "Unknown jump mode!");

                            Instructions.Add((short)inst.Opcode);
                            Instructions.Add((short)inst.Arg);

                            LabelUses.Add((Instructions.Count, inst.StrArg));

                            // Add placeholder address
                            Instructions.Add(0);
                            Instructions.Add(0);
                        }
                        else if (inst.Opcode == Opcode.Call)
                        {
                            // Try get the proc
                            if (ImportedProcs.TryGetValue(inst.StrArg, out _) == false)
                                Error(inst, "Could not find proc!");

                            Instructions.Add((short)inst.Opcode);
                            LabelUses.Add((Instructions.Count, inst.StrArg));
                            Instructions.Add(0);
                            Instructions.Add(0);
                        }
                        else Error(inst, "Not implemented yet!");
                    }

                    Proc newProc = default;
                    newProc.ProcName = proc.Key.ToStringRef();
                    newProc.Line = proc.Key.Line;
                    // FIXME!! Actually emit this data!!!
                    newProc.LinkedLines = LinkedLines;
                    newProc.Instructions = Instructions;
                    // FIXME: This can be done better!
                    newProc.Trace = Trace.FromTrace(proc.Value.First().Trace, proc.Value.Last().Trace);

                    ProcList.Add(newProc);

                    AllLabelUses.Add(proc.Key, LabelUses);
                    ProcLabels.Add(proc.Key, LocalLabels);
                }

            }

            Dictionary<StringRef, Proc> ProcMap = new Dictionary<StringRef, Proc>();
            
            int offset = 0;
            // Here we place all of the procs after eachother
            for (int i = 0; i < ProcList.Count; i++)
            {
                ref var proc = ref ProcList.IndexByRef(i);

                proc.Address = offset + Constants.ROM_START;

                //Console.WriteLine($"{proc.ProcName,-15} Offset: {offset} Length: {proc.Size}");
                offset += proc.Size;

                ProcMap.Add(proc.ProcName, proc);
            }

            foreach (var procUses in AllLabelUses)
            {
                // This is the proc that we want edit
                if (ProcMap.TryGetValue(procUses.Key.ToStringRef(), out var procThatWantsTheLabel) == false)
                    Error(procUses.Key, "Could not find proc! This should not happen!");

                // Get all the lables defined in this proc
                if (ProcLabels.TryGetValue(procUses.Key, out var LablesInProc) == false)
                    Error(procUses.Key, $"Could not find labels defined in the proc '{procUses.Key.GetContents()}'!");

                foreach (var labelUse in procUses.Value)
                {
                    // procUses = A list of label uses that a specific proc uses
                    // labelUse = The struct containing the data of the used label

                    int labelAddress = default;
                    if (LablesInProc.TryGetValue(labelUse.Proc, out int labelOffset))
                    {
                        // The label was referencing a local label!
                        labelAddress = procThatWantsTheLabel.Address + labelOffset;
                        //Console.WriteLine($"Proc: {procUses.Key.GetContents()} Offset: {labelUse.Offset}, Used label: {labelUse.Proc} Address: 0x{labelAddress:X} (Offset: {labelOffset:X})");
                    }
                    else if (ProcMap.TryGetValue(labelUse.Proc, out var proc))
                    {
                        // The label we where referencing was a proc!
                        labelAddress = proc.Address;
                        //Console.WriteLine($"Proc: {procUses.Key.GetContents()} Offset: {labelUse.Offset}, Used proc: {labelUse.Proc} Address: 0x{labelAddress:X}");
                    }
                    // FIXME: Correct trace here!!
                    else Error(procUses.Key, $"Could not find the label '{labelUse.Proc}'");

                    procThatWantsTheLabel.Instructions[labelUse.Offset] = (short)((labelAddress >> 12) & 0xFFF);
                    procThatWantsTheLabel.Instructions[labelUse.Offset + 1] = (short)((labelAddress) & 0xFFF);
                }
            }

            short[] rom = new short[Constants.ROM_SIZE];

            int usedInstructions = 0;

            foreach (var proc in ProcMap)
            {
                usedInstructions += proc.Value.Size;
                proc.Value.Instructions.CopyTo(rom, proc.Value.Address - Constants.ROM_START);
            }

            // FIXME: Do this better!
            return new BinFile() { Instructions = rom, UsedInstructions = usedInstructions, Metadata = ProcList };
        }

        public struct EvaluatedConstant
        {
            public ConstantExpression Original;
            public bool IsString;
            public (int Number, int Size) NumberValue;
            public StringRef StringValue;

            public override string ToString() => $"EvalConst{{{(IsString ? NumberValue.ToString() : StringValue.ToString())}}}";
        }

        // FIXME: How should we handle delayed constants?
        public EvaluatedConstant EvaluateConstantExpr(ConstantExpression expr)
        {
            EvaluatedConstant evaluated = default;
            evaluated.Original = expr;
            switch (expr.Type)
            {
                case ConstantExprType.NumberLit:
                    evaluated.NumberValue = expr.NumberLit;
                    break;
                case ConstantExprType.CharLit:
                    evaluated.NumberValue = (expr.CharLit, 1);
                    break;
                case ConstantExprType.StringLit:
                    evaluated.IsString = true;
                    evaluated.StringValue = expr.StringLit;
                    break;
                case ConstantExprType.Auto:
                    {
                        var sizeResult = EvaluateConstantExpr(expr.AutoExpr);
                        var (number, _) = sizeResult.NumberValue;
                        autoVars -= number;
                        evaluated.NumberValue = (autoVars, 2);
                    }
                    break;
                case ConstantExprType.Extern:
                    Error(expr.Trace, "FIXME: What should we do here!?");
                    break;
                case ConstantExprType.Compound:
                    {
                        Stack<EvaluatedConstant> Stack = new Stack<EvaluatedConstant>();

                        TokenQueue Queue = new TokenQueue(expr.CompoundExpr);
                        while (Queue.Count > 0)
                        {
                            var tok = Queue.Dequeue();
                            switch (tok.Type)
                            {
                                case TokenType.Plus:
                                    {
                                        var res = ApplyOperation(Stack.Pop().NumberValue, Stack.Pop().NumberValue, ConstOperation.Addition);
                                        Stack.Push(new EvaluatedConstant() { NumberValue = res });
                                        break;
                                    }
                                case TokenType.Minus:
                                    {
                                        var n1 = Stack.Pop();
                                        var res = ApplyOperation(Stack.Pop().NumberValue, n1.NumberValue, ConstOperation.Subtraction);
                                        Stack.Push(new EvaluatedConstant() { NumberValue = res });
                                        break;
                                    }
                                case TokenType.Asterisk:
                                    {
                                        var res = ApplyOperation(Stack.Pop().NumberValue, Stack.Pop().NumberValue, ConstOperation.Multiplication);
                                        Stack.Push(new EvaluatedConstant() { NumberValue = res });
                                        break;
                                    }
                                case TokenType.Slash:
                                    {
                                        var n1 = Stack.Pop();
                                        var res = ApplyOperation(Stack.Pop().NumberValue, n1.NumberValue, ConstOperation.Division);
                                        Stack.Push(new EvaluatedConstant() { NumberValue = res });
                                        break;
                                    }
                                case TokenType.Percent:
                                    {
                                        var n1 = Stack.Pop();
                                        var res = ApplyOperation(Stack.Pop().NumberValue, n1.NumberValue, ConstOperation.Modulo);
                                        Stack.Push(new EvaluatedConstant() { NumberValue = res });
                                        break;
                                    }
                                case TokenType.Numbersign:
                                    {
                                        // Parse a constant
                                        continue;
                                    }
                                default:
                                    {
                                        // FIXME: Parse sizeof and similar here!!!
                                        if (tok.ContentsMatch("sizeof"))
                                        {
                                            var openParenTok = Queue.Dequeue();
                                            if (openParenTok.Type != TokenType.Open_paren)
                                                Error(openParenTok, "Expected '('!");

                                            var labelTok = Queue.Dequeue();
                                            if (labelTok.Type != TokenType.Label)
                                                Error(labelTok, "Expected label!");

                                            // FIXME: Get the size of the proc!!!
                                            // This needs to be delayed!!

                                            var closeParenTok = Queue.Dequeue();
                                            if (closeParenTok.Type != TokenType.Close_paren)
                                                Error(closeParenTok, "Expected ')'!");

                                            Stack.Push(new EvaluatedConstant() { NumberValue = (1, 1) });
                                            //Error(Trace.FromToken(tok, closeParenTok), "We don't support delayed constants atm!");
                                        }
                                        else
                                        {
                                            var evalConst = ResolveTokenToEvalConst(tok);
                                            if (evalConst.IsString) Error(tok, "A compound constant can't contain strings!");
                                            Stack.Push(evalConst);
                                        }
                                        break;
                                    }
                            }
                        }

                        if (Stack.Count > 1) Error(expr.Trace , $"Could not evaluate compound expression! There where still values on the stack! Stack: {{{string.Join(", ", Stack)}}}");

                        if (Stack.Count == 0) Error(expr.Trace, "Compound expression resulted in zero values on the stack!");
                    }
                    break;
                default:
                    Error(expr.Trace, $"Unknown const expr type '{expr.Type}'!");
                    break;
            }
            return evaluated;
        }

        public EvaluatedConstant ResolveTokenToEvalConst(Token tok)
        {
            switch (tok.Type)
            {
                case TokenType.Identifier:
                    if (GlobalConstExprs.TryGetValue(tok.ToStringRef(), out var constExpr) == false)
                        Error(tok, $"Could not resolve ident '{tok.GetContents()}'");

                    // FIXME: This is probably going to execute auto consts multiple times!
                    return EvaluateConstantExpr(constExpr);
                case TokenType.Label:
                    // FIXME: This is because we don't have the label placements yet!
                    //Error(tok, "Resolve label to number not implemented yet!");

                    return new EvaluatedConstant() { NumberValue = (0x00BADF0D, 2) };
                case TokenType.Number_litteral:
                    return new EvaluatedConstant() { NumberValue = tok.ParseNumber() };
                case TokenType.Char_litteral:
                    return new EvaluatedConstant() { NumberValue = (tok.GetFirstChar(), 1) };
                default:
                    Error(tok, $"Can't resolve token of type '{tok.Type}' to number!");
                    return default;
            }
        }

        public (int Number, int Size) ApplyOperation((int Number, int Size) n1, (int Number, int Size) n2, ConstOperation operation)
        {
            int res;
            switch (operation)
            {
                case ConstOperation.Addition:
                    res = n1.Number + n2.Number;
                    break;
                case ConstOperation.Subtraction:
                    res = n1.Number - n2.Number;
                    break;
                case ConstOperation.Multiplication:
                    res = n1.Number * n2.Number;
                    break;
                case ConstOperation.Division:
                    res = n1.Number / n2.Number;
                    break;
                case ConstOperation.Modulo:
                    res = n1.Number % n2.Number;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown operation '{operation}'!");
            }

            int size = Util.Log2(res);
            size /= 12;
            size++;
            size = Math.Max(size, Math.Max(n1.Size, n2.Size));
            return (res, size);
        }
    }
}
