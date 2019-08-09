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
        // FIXME: Add metadata!
        public short[] Instructions;
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
            // FIXME:
            Console.WriteLine($"In file {Path.GetFileName(trace.FilePath)} on line {trace.Line}: '{trace.TraceString}'");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"    {error}");
            //Debugger.Break();
            Console.ReadLine();
            Environment.Exit(1);
        }

        public BinFile Emit()
        {
            foreach (var file in FileList)
            {
                // FIXME: Using strings here causes a lot of unnessesary GetContent() calls! Both when creating and when using!
                Dictionary<string, List<Instruction>> ImportedProcs = new Dictionary<string, List<Instruction>>();

                // FIXME: Using strings here causes a lot of unnessesary GetContent() calls! Both when creating and when using!
                Dictionary<string, ConstantExpression> ImportedConstExprs = new Dictionary<string, ConstantExpression>();

                foreach (var include in file.IncludeFiles)
                {
                    string fileName = include.GetContents();
                    if (Files.TryGetValue(fileName, out ParsedFile includeFile) == false)
                        Error(include, "Could not find file!");

                    foreach (var constExpr in includeFile.ConstExprs)
                    {
                        if (constExpr.Value.Flags.HasFlag(ConstFlags.Public))
                            ImportedConstExprs.Add(constExpr.Key.GetContents(), constExpr.Value);
                    }

                    foreach (var proc in includeFile.Procs)
                    {
                        // FIXME: Check if public!!
                        ImportedProcs.Add(proc.Key.GetContents(), proc.Value);
                    }
                }

                Dictionary<string, EvaluatedConstant> EvaluatedConstants = new Dictionary<string, EvaluatedConstant>();

                // FIXME: Properly evaluate constants here!
                Console.WriteLine($"Constants in file: {Path.GetFileName(file.File.Path)}");
                foreach (var constExpr in file.ConstExprs)
                {
                    if (constExpr.Value.Flags.HasFlag(ConstFlags.Extern))
                    {
                        Console.WriteLine($"<{constExpr.Key.GetContents()} = extern>");

                        if (ImportedConstExprs.TryGetValue(constExpr.Key.GetContents(), out ConstantExpression expr) == false)
                            Error(constExpr.Key, "Could not find extern const!");

                        // FIXME: Put this in the map of constant expressions
                    }
                    else if (constExpr.Value.Flags.HasFlag(ConstFlags.Auto))
                    {
                        Console.WriteLine($"<{constExpr.Key.GetContents()} = auto({string.Join("", constExpr.Value.Expression)})>");

                        // FIXME: Figure out the size of the auto
                        var result = EvaluateConstantExpr(constExpr.Value);

                        EvaluatedConstants.Add(constExpr.Key.GetContents(), result);
                    }
                    else
                    {
                        Console.WriteLine($"<{constExpr.Key.GetContents()} = {string.Join("", constExpr.Value.Expression)}>");
                    }
                }

                foreach (var proc in file.Procs)
                {
                    ImportedProcs.Add(proc.Key.GetContents(), proc.Value);
                }

                foreach (var constExpr in file.ConstExprs)
                {
                    // We don't import externs here!
                    if (constExpr.Value.Flags.HasFlag(ConstFlags.Extern) != true)
                    {
                        ImportedConstExprs.Add(constExpr.Key.GetContents(), constExpr.Value);
                    }
                }

                foreach (var proc in file.Procs)
                {
                    List<short> Instructions = new List<short>();

                    foreach (var inst in proc.Value)
                    {
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
                            else Error(inst, "Not implemented yet!");
                        }
                        else if (inst.Opcode == Opcode.Load_lit || inst.Opcode == Opcode.Load_lit_l)
                        {
                            if (inst.Flags.HasFlag(InstructionFlags.WordArg))
                            {
                                Instructions.Add((short)inst.Opcode);
                                if (inst.Opcode != Opcode.Load_lit_l)
                                    Instructions.Add(0);
                                Instructions.Add((short)(inst.Arg & 0xFFF));
                            }
                            else if (inst.Flags.HasFlag(InstructionFlags.DwordArg))
                            {
                                if (inst.Opcode != Opcode.Load_lit) Error(inst, $"Cannot load a dword arg with the {Opcode.Load_lit} instruction");
                                Instructions.Add((short)(inst.Arg >> 12));
                                Instructions.Add((short)(inst.Arg & 0xFFF));
                            }
                            else if (inst.Flags.HasFlag(InstructionFlags.IdentArg))
                            {
                                // Here we try to find the constant that we want
                                if (EvaluatedConstants.TryGetValue(inst.StrArg.ToString(), out var evalConst) == false)
                                    Error(inst, "Could not find constant!");

                                if (evalConst.LitteralValue.Type == TokenType.Number_litteral)
                                {
                                    var (value, size) = evalConst.LitteralValue.ParseNumber();

                                    if (inst.Opcode == Opcode.Load_lit && size > 1) Error(inst, "Constant to big to be loaded as a word");
                                    if (inst.Opcode == Opcode.Load_lit_l && size > 2) Error(inst, "Constant to big to be loaded as a dword");

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
                        else if (inst.Opcode == Opcode.Call)
                        {
                            // This is a call to some proc
                            Instructions.Add((short)inst.Opcode);

                            // Try get the proc
                            if (ImportedProcs.TryGetValue(inst.StrArg.ToString(), out _) == false)
                                Error(inst, "Could not find proc!");
                        }
                        else Error(inst, "Not implemented yet!");
                    }
                }

                Console.WriteLine();
            }

            return default;
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
                evaluated.LitteralValue = "0";
            }
            if (expr.Flags.HasFlag(ConstFlags.Litteral))
            {
                // This case is easy!
                
                evaluated.LitteralValue = expr.Expression[0].GetContents();
            }
            else Error(expr.Trace, "Not implemented yet!");

            return evaluated;
        }

    }
}
