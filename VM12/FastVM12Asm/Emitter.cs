using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

        public Emitter(List<ParsedFile> files)
        {
            Files = files.ToDictionary(file => file.File.Path);

            bool foundStart = false;

            FileList = new List<ParsedFile>();
            foreach (var file in files)
            {
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
            Console.WriteLine($"Inst: {inst}, Flags: {inst.Flags.ToString()} {inst.Opcode} '{inst.StrArg.ToString()}'");
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
            Dictionary<Token, List<(int Offset, StringRef Proc)>> AllProcUses = new Dictionary<Token, List<(int Offset, StringRef Proc)>>();
            Dictionary<Token, List<(int Offset, StringRef Proc)>> AllLabelUses = new Dictionary<Token, List<(int Offset, StringRef Proc)>>();

            Dictionary<Token, Dictionary<StringRef, int>> ProcLabels = new Dictionary<Token, Dictionary<StringRef, int>>();

            DynArray<Proc> ProcList = new DynArray<Proc>(100);

            foreach (var file in FileList)
            {
                // FIXME: Using strings here causes a lot of unnessesary GetContent() calls! Both when creating and when using!
                Dictionary<StringRef, List<Instruction>> ImportedProcs = new Dictionary<StringRef, List<Instruction>>();

                // FIXME: Using strings here causes a lot of unnessesary GetContent() calls! Both when creating and when using!
                Dictionary<StringRef, ConstantExpression> ImportedConstExprs = new Dictionary<StringRef, ConstantExpression>();

                // FIXME
                foreach (var include in file.IncludeFiles)
                {
                    string fileName = include.GetContents();
                    if (Files.TryGetValue(fileName, out ParsedFile includeFile) == false)
                        Error(include, "Could not find file!");

                    foreach (var constExpr in includeFile.ConstExprs)
                    {
                        if (constExpr.Value.Flags.HasFlag(ConstFlags.Public))
                            ImportedConstExprs.Add(constExpr.Key, constExpr.Value);
                    }

                    foreach (var proc in includeFile.Procs)
                    {
                        // FIXME: Check if public!!
                        ImportedProcs.Add(proc.Key.ToStringRef(), proc.Value);
                    }
                }

                // FIXME
                foreach (var constExpr in file.ConstExprs)
                {
                    // We don't import externs here! Because we have already added them!
                    // FIXME: This will trigger extern auto consts to allocate a new block of memory!!!!!

                    if (constExpr.Value.Flags.HasFlag(ConstFlags.Extern) != true)
                    {
                        ImportedConstExprs.Add(constExpr.Key, constExpr.Value);
                    }
                }

                Dictionary<StringRef, EvaluatedConstant> EvaluatedConstants = new Dictionary<StringRef, EvaluatedConstant>();

                // FIXME: Properly evaluate constants here!
                Console.WriteLine($"Constants in file: {Path.GetFileName(file.File.Path)}");
                foreach (var constExpr in ImportedConstExprs)
                {
                    if (constExpr.Value.Flags.HasFlag(ConstFlags.Extern))
                    {
                        Console.WriteLine($"<{constExpr.Key} = extern>");

                        if (ImportedConstExprs.TryGetValue(constExpr.Key, out ConstantExpression expr) == false)
                            Error(constExpr.Value.Trace, "Could not find extern const!");

                        // FIXME: Put this in the map of constant expressions
                    }
                    else if (constExpr.Value.Flags.HasFlag(ConstFlags.Auto))
                    {
                        Console.WriteLine($"<{constExpr.Key} = auto({string.Join("", constExpr.Value.AutoExpr.Expression)})>");

                        // FIXME: Figure out the size of the auto
                        var result = EvaluateConstantExpr(constExpr.Value);

                        EvaluatedConstants.Add(constExpr.Key, result);
                    }
                    else if (constExpr.Value.Flags.HasFlag(ConstFlags.Litteral))
                    {
                        EvaluatedConstants.Add(constExpr.Key, new EvaluatedConstant() { LitteralValue = constExpr.Value.Expression[0].GetContents() });
                    }
                    else
                    {
                        Console.WriteLine($"<{constExpr.Key} = {string.Join("", constExpr.Value.Expression)}>");
                    }
                }

                foreach (var proc in file.Procs)
                {
                    ImportedProcs.Add(proc.Key.ToStringRef(), proc.Value);
                }

                foreach (var proc in file.Procs)
                {
                    List<short> Instructions = new List<short>();
                    List<LineLink> LinkedLines = new List<LineLink>();

                    // Unclear if StringRef is right for this...
                    List<(int, StringRef)> ProcUses = new List<(int, StringRef)>();

                    Dictionary<StringRef, int> LocalLabels = new Dictionary<StringRef, int>();
                    List<(int, StringRef)> LocalLabelUses = new List<(int, StringRef)>();

                    int currentSourceLine = 0;
                    foreach (var inst in proc.Value)
                    {
                        if (inst.Trace.StartLine > currentSourceLine && inst.Flags.HasFlag(InstructionFlags.Label) == false)
                        {
                            LinkedLines.Add(new LineLink() { Instruction = Instructions.Count + 1, Line = inst.Trace.StartLine });
                            currentSourceLine = inst.Trace.StartLine;
                        }

                        if (inst.Flags.HasFlag(InstructionFlags.RawOpcode))
                        {
                            Instructions.Add((short)inst.Opcode);
                        }
                        else if (inst.Opcode == Opcode.Nop)
                        {
                            // This is not an instruction this is something else e.g label, raw number etc
                            if (inst.Flags.HasFlag(InstructionFlags.RawNumber))
                            {
                                if (inst.Flags.HasFlag(InstructionFlags.WordArg))
                                {
                                    Instructions.Add((short)(inst.Arg & 0xFFF));
                                }
                                else if (inst.Flags.HasFlag(InstructionFlags.DwordArg))
                                {
                                    Instructions.Add((short)(inst.Arg >> 12));
                                    Instructions.Add((short)(inst.Arg & 0xFFF));
                                }
                                else Error(inst, "Unknown raw number type!");
                            }
                            else if (inst.Flags.HasFlag(InstructionFlags.Label))
                            {
                                LocalLabels[inst.StrArg] = Instructions.Count;
                            }
                            else Error(inst, "Not implemented yet!");
                        }
                        else if (inst.Opcode == Opcode.Load_lit || inst.Opcode == Opcode.Load_lit_l)
                        {
                            if (inst.Flags.HasFlag(InstructionFlags.WordArg))
                            {
                                Instructions.Add((short)inst.Opcode);
                                if (inst.Opcode == Opcode.Load_lit_l)
                                    Instructions.Add(0);
                                Instructions.Add((short)(inst.Arg & 0xFFF));
                            }
                            else if (inst.Flags.HasFlag(InstructionFlags.DwordArg))
                            {
                                if (inst.Opcode != Opcode.Load_lit) Error(inst, $"Cannot load a dword arg with the {Opcode.Load_lit} instruction");
                                Instructions.Add((short)inst.Opcode);
                                Instructions.Add((short)(inst.Arg >> 12));
                                Instructions.Add((short)(inst.Arg & 0xFFF));
                            }
                            else if (inst.Flags.HasFlag(InstructionFlags.IdentArg))
                            {
                                // Here we try to find the constant that we want
                                if (EvaluatedConstants.TryGetValue(inst.StrArg, out var evalConst) == false)
                                    Error(inst, "Could not find constant!");

                                if (evalConst.LitteralValue != null)
                                {
                                    var (value, size) = evalConst.LitteralValue.ParseNumber();

                                    if (inst.Opcode == Opcode.Load_lit && size > 1) Error(inst, "Constant to big to be loaded as a word");
                                    if (inst.Opcode == Opcode.Load_lit_l && size > 2) Error(inst, "Constant to big to be loaded as a dword");

                                    Instructions.Add((short)inst.Opcode);
                                    for (int i = 0; i < size; i++)
                                    {
                                        Instructions.Add((short)(value & 0xFFF));
                                        value >>= 12;
                                    }
                                }
                                else Error(inst, "Unknown const type!");
                            }
                            else Error(inst, "Unknown load arg!");
                        }
                        else if (inst.Opcode == Opcode.Load_local || inst.Opcode == Opcode.Load_local_l)
                        {
                            if (inst.Flags.HasFlag(InstructionFlags.WordArg))
                            {
                                Instructions.Add((short)inst.Opcode);
                                Instructions.Add((short)(inst.Arg & 0xFFF));
                            }
                            else if (inst.Flags.HasFlag(InstructionFlags.DwordArg)) Error(inst, $"Cannot use a dword to refer to a local!");
                            else Error(inst, "Unknown load local arg!");
                        }
                        else if (inst.Opcode == Opcode.Store_local || inst.Opcode == Opcode.Store_local_l)
                        {
                            if (inst.Flags.HasFlag(InstructionFlags.WordArg))
                            {
                                Instructions.Add((short)inst.Opcode);
                                Instructions.Add((short)(inst.Arg & 0xFFF));
                            }
                            else if (inst.Flags.HasFlag(InstructionFlags.DwordArg)) Error(inst, $"Cannot use a dword to refer to a local!");
                            else Error(inst, "Unknown store local arg!");
                        }
                        else if (inst.Opcode == Opcode.Inc_local || inst.Opcode == Opcode.Inc_local_l || 
                                 inst.Opcode == Opcode.Dec_local || inst.Opcode == Opcode.Dec_local_l)
                        {
                            if (inst.Flags.HasFlag(InstructionFlags.WordArg))
                            {
                                Instructions.Add((short)inst.Opcode);
                                Instructions.Add((short)(inst.Arg & 0xFFF));
                            }
                            else if (inst.Flags.HasFlag(InstructionFlags.DwordArg)) Error(inst, $"Cannot use a dword to refer to a local!");
                            else Error(inst, "Unknown inc/dec local arg!");
                        }
                        else if (inst.Opcode == Opcode.Call)
                        {
                            // This is a call to some proc
                            Instructions.Add((short)inst.Opcode);

                            // Try get the proc
                            if (ImportedProcs.TryGetValue(inst.StrArg, out _) == false)
                                Error(inst, "Could not find proc!");

                            // Here we note that the next two instructions need to be filled in later
                            ProcUses.Add((Instructions.Count, inst.StrArg));

                            // Add two zeros to stand in for the address of the proc
                            Instructions.Add(0);
                            Instructions.Add(0);
                        }
                        else if (inst.Opcode == Opcode.Jmp)
                        {
                            if (Enum.IsDefined(typeof(JumpMode), (JumpMode)inst.Arg) == false)
                                Error(inst, "Unknown jump mode!");

                            Instructions.Add((short)inst.Opcode);
                            Instructions.Add((short)inst.Arg);

                            LocalLabelUses.Add((Instructions.Count, inst.StrArg));

                            // Add placeholder address
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

                    AllProcUses.Add(proc.Key, ProcUses);
                    ProcList.Add(newProc);

                    AllLabelUses.Add(proc.Key, LocalLabelUses);
                    ProcLabels.Add(proc.Key, LocalLabels);
                }

                Console.WriteLine();
            }

            Dictionary<StringRef, Proc> ProcMap = new Dictionary<StringRef, Proc>();
            
            int offset = 0;
            // Here we place all of the procs after eachother
            for (int i = 0; i < ProcList.Count; i++)
            {
                ref var proc = ref ProcList.IndexByRef(i);

                proc.Address = offset + Constants.ROM_START;

                Console.WriteLine($"{proc.ProcName,-15} Offset: {offset} Length: {proc.Size}");
                offset += proc.Size;

                ProcMap.Add(proc.ProcName, proc);
            }

            foreach (var procUses in AllProcUses)
            {
                foreach (var use in procUses.Value)
                {
                    Console.WriteLine($"Proc: {procUses.Key.GetContents()} Offset: {use.Offset}, Used proc: {use.Proc}");

                    // This is the proc that we want edit
                    if (ProcMap.TryGetValue(procUses.Key.ToStringRef(), out var procMeta) == false)
                        Error(procUses.Key, "Could not find proc! This should not happen!");

                    // This is the proc we want the address from
                    if (ProcMap.TryGetValue(use.Proc, out Proc proc) == false)
                        Error(procUses.Key, "Could not find proc! This should not happen!");

                    procMeta.Instructions[use.Offset] = (short)((proc.Address >> 12) & 0xFFF);
                    procMeta.Instructions[use.Offset + 1] = (short)((proc.Address) & 0xFFF);
                }
            }

            foreach (var procUses in AllLabelUses)
            {
                foreach (var labelUse in procUses.Value)
                {
                    // This is the proc that we want edit
                    if (ProcMap.TryGetValue(procUses.Key.ToStringRef(), out var procMeta) == false)
                        Error(procUses.Key, "Could not find proc! This should not happen!");

                    // This is the proc we want the address from
                    if (ProcMap.TryGetValue(procUses.Key.ToStringRef(), out var proc) == false)
                        Error(procUses.Key, "Could not find proc! This should not happen!");

                    if (ProcLabels.TryGetValue(procUses.Key, out var labels) == false)
                        Error(procUses.Key, "Could not find proc! This should not happen!");

                    if (labels.TryGetValue(labelUse.Proc, out int labelOffset) == false)
                        Error(procUses.Key, "Could not find proc! This should not happen!");

                    int labelAddress = proc.Address + labelOffset;

                    procMeta.Instructions[labelUse.Offset] = (short)((labelAddress >> 12) & 0xFFF);
                    procMeta.Instructions[labelUse.Offset + 1] = (short)((labelAddress) & 0xFFF);

                    Console.WriteLine($"Proc: {procUses.Key.GetContents()} Offset: {labelUse.Offset}, Used label: {labelUse.Proc} Address: 0x{labelAddress:X} (Offset: {labelOffset:X})");
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
            public string LitteralValue;
        }

        public EvaluatedConstant EvaluateConstantExpr(ConstantExpression expr)
        {
            EvaluatedConstant evaluated = default;
            if (expr.Flags.HasFlag(ConstFlags.Auto))
            {
                var sizeResult = EvaluateConstantExpr(expr.AutoExpr);
                var (number, _) = sizeResult.LitteralValue.ParseNumber();

                autoVars -= number;

                evaluated.LitteralValue = $"0x{autoVars:X6}";
            }
            else if (expr.Flags.HasFlag(ConstFlags.Litteral))
            {
                // This case is easy!
                
                evaluated.LitteralValue = expr.Expression[0].GetContents();
            }
            else Error(expr.Trace, "Not implemented yet!");

            return evaluated;
        }

    }
}
