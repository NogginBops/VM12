﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Diagnostics;

using VM12;

namespace VM12Asm
{
    public class VM12Asm
    {
        enum TokenType
        {
            Instruction,
            Litteral,
            Label
        }

        struct Token
        {
            public readonly TokenType Type;
            public readonly string Value;
            public readonly Opcode? Opcode;

            public Token(TokenType type, string value, Opcode? opcode = null)
            {
                Type = type;
                Value = value;
                Opcode = opcode;
            }

            public bool Equals(Token t)
            {
                return (Type == t.Type) && (Value == t.Value) && (Opcode == t.Opcode);
            }
        }

        struct AsemFile
        {
            public readonly Dictionary<string, string> Usings;
            public readonly Dictionary<string, string> Constants;
            public readonly Dictionary<string, List<Token>> Procs;
            public readonly Dictionary<string, List<int>> Breakpoints;
            public readonly Dictionary<string, int> ProcLoactaions;

            public AsemFile(Dictionary<string, string> usigns, Dictionary<string, string> constants, Dictionary<string, List<Token>> procs, Dictionary<string, List<int>> breakpoints, Dictionary<string, int> procLocations)
            {
                Usings = usigns;
                Constants = constants;
                Procs = procs; //.OrderBy(proc => proc.Key == "start" ? 1 : 0).ToDictionary(kpv => kpv.Key, kpv => kpv.Value);
                Breakpoints = breakpoints;
                ProcLoactaions = procLocations;
            }
        }

        struct LibFile
        {
            public readonly short[] Instructions;
            public readonly Dictionary<string, int> ProcOffsets;
            public readonly List<int> LabelUses;

            public LibFile(short[] instructions, Dictionary<string, int> procOffsets, List<int> labelUses)
            {
                Instructions = instructions;
                ProcOffsets = procOffsets;
                LabelUses = labelUses;
            }
        }

        const int _12BIT_MASK = 0x0FFF;

        const int ROM_OFFSET = 0x44B_000;

        const short ROM_OFFSET_UPPER_BITS = 0x44B;
        
        static Dictionary<Regex, string> preprocessorConversions = new Dictionary<Regex, string>()
        {
            { new Regex(";.*"), "" },
            { new Regex("#reg.*"), "" },
            { new Regex("#endreg.*"), "" },
            { new Regex("(?<!:)\\bshl\\b"), "sh.l" },
            { new Regex("(?<!:)\\bshr\\b"), "sh.r" },
            { new Regex("(?<!:)\\bfadd\\b"), "add.f" },
            { new Regex("(?<!:)\\bfneg\\b"), "neg.f" },
            { new Regex("(?<!:)\\bjz\\b"), "jmp.z" },
            { new Regex("(?<!:)\\bjnz\\b"), "jmp.nz" },
            { new Regex("(?<!:)\\bjcz\\b"), "jmp.cz" },
            { new Regex("(?<!:)\\bjfz\\b"), "jmp.fz" },
            { new Regex("::\\[SP\\]"), "call.sp" },
            { new Regex("::(?!\\s)"), "call.pc :" },
            { new Regex("(?<!:)\\bload\\s+@"), "load.addr " },
            { new Regex("(?<!:)\\bload\\s+#"), "load.lit " },
            { new Regex("(?<!:)\\bload\\s+:"), "load.lit :" },
            { new Regex("(?<!:)\\bload\\s+\\[SP\\]"), "load.sp" },
            { new Regex("(?<!:)\\bstore\\s+\\[SP\\]"), "store.sp" },
            { new Regex("(?<!:)\\bstore\\s+@"), "store.pc " }
        };

        static Regex using_statement = new Regex("&\\s*([A-Za-z][A-Za-z0-9_]*)\\s+(.*\\.12asm)");

        static Regex constant = new Regex("<([A-Za-z_][A-Za-z0-9_]*)\\s*=\\s*(0x[0-9A-Fa-f_]+|8x[0-7_]+|[0-9_]+)>");

        static Regex label = new Regex(":[A-Za-z][A-Za-z0-9_]*");

        static Regex proc = new Regex("(:[A-Za-z][A-Za-z0-9_]*)(\\s+@(.*))?");

        static Regex num = new Regex("\\b(0x[0-9A-Fa-f_]+|8x[0-7_]+|[0-9_]+)");

        static Dictionary<string, Opcode> opcodes = new Dictionary<string, Opcode>()
        {
            { "nop", Opcode.Nop },
            { "load.addr", Opcode.Load_addr },
            { "load.lit", Opcode.Load_lit },
            { "load.sp", Opcode.Load_sp },
            { "store.pc", Opcode.Store_pc },
            { "store.sp", Opcode.Store_sp },
            { "call.sp", Opcode.Call_sp },
            { "call.pc", Opcode.Call_pc },
            { "ret", Opcode.Ret },
            { "dup", Opcode.Dup },
            { "over", Opcode.Over },
            { "swap", Opcode.Swap },
            { "drop", Opcode.Drop },
            { "reclaim", Opcode.Reclaim },
            { "add", Opcode.Add },
            { "sh.l", Opcode.Sh_l },
            { "sh.r", Opcode.Sh_r },
            { "not", Opcode.Not },
            { "neg", Opcode.Neg },
            { "xor", Opcode.Xor },
            { "and", Opcode.And },
            { "inc", Opcode.Inc },
            { "add.f", Opcode.Add_f },
            { "neg.f", Opcode.Neg_f },
            { "jmp", Opcode.Jmp },
            { "jmp.z", Opcode.Jmp_z },
            { "jmp.nz", Opcode.Jmp_nz },
            { "jmp.cz", Opcode.Jmp_cz },
            { "jmp.fz", Opcode.Jmp_fz },
            { "eni", Opcode.Eni },
            { "dsi", Opcode.Dsi },
            { "hlt", Opcode.Hlt }
        };

        static ConsoleColor conColor = Console.ForegroundColor;

        public static void Main(params string[] args)
        {
            Console.ForegroundColor = conColor;
            
            IEnumerator<string> enumerator = args.AsEnumerable().GetEnumerator();
            
            string file = null;
            string name = null;
            bool executable = true;
            bool overwrite = false;
            bool hold = false;
            bool open = false;
            bool verbose = false;

            while (enumerator.MoveNext())
            {
                switch (enumerator.Current)
                {
                    case "-src":
                        enumerator.MoveNext();
                        file = enumerator.Current;
                        break;
                    case "-dst":
                        enumerator.MoveNext();
                        name = enumerator.Current;
                        break;
                    case "-e":
                        executable = true;
                        break;
                    case "-o":
                        overwrite = true;
                        break;
                    case "-h":
                        hold = true;
                        break;
                    case "-p":
                        open = true;
                        break;
                    case "-v":
                        verbose = true;
                        break;
                    default:
                        break;
                }
            }

            while (File.Exists(file) == false)
            {
                Console.Write("Input file: ");
                file = Console.ReadLine();
            }

            while (name == null || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                Console.Write("Destination filename: ");
                name = Console.ReadLine();
            }

            string[] lines = File.ReadAllLines(file);

            Console.WriteLine();
            Console.WriteLine();

            Console.WriteLine("Preprocessing...");

            string[] newLines = PreProcess(lines);
            
            Console.WriteLine("Parsing...");

            AsemFile asmFile = Parse(newLines);

            #region Usings

            Dictionary<string, AsemFile> files = new Dictionary<string, AsemFile>();

            Stack<string> remainingUsings = new Stack<string>();

            FileInfo fileInf = new FileInfo(file);
            DirectoryInfo dirInf = fileInf.Directory;
            
            FileInfo[] dirFiles = dirInf.GetFiles($".{Path.DirectorySeparatorChar}*.12asm", SearchOption.AllDirectories);
            
            foreach (var use in asmFile.Usings.Values)
            {
                remainingUsings.Push(use);
            }

            // TODO: Files with the same name but differnet directories
            files[new FileInfo(file).Name] = asmFile;

            while (remainingUsings.Count > 0)
            {
                string use = remainingUsings.Pop();

                if (files.ContainsKey(use))
                {
                    continue;
                }

                FileInfo fi = dirFiles.First(f => f.Name == use);

                lines = File.ReadAllLines(fi.FullName);

                newLines = PreProcess(lines);

                asmFile = Parse(newLines);

                files[use] = asmFile;

                foreach (var u in asmFile.Usings)
                {
                    if (files.ContainsKey(u.Key) == false)
                    {
                        remainingUsings.Push(u.Value);
                    }
                }
            }

            foreach (var f in files)
            {
                Console.WriteLine(f);
            }

            #endregion

            if (verbose)
            {
                #region Print_Files

                foreach (var asm in files)
                {
                    Console.ForegroundColor = conColor;
                    Console.WriteLine();
                    Console.WriteLine($"--------------------- {asm.Key} ---------------------");
                    Console.WriteLine();

                    foreach (var use in asm.Value.Usings)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGreen;
                        Console.Write(use.Key);
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine("\t" + use.Value);
                    }

                    if (asm.Value.Usings.Count > 0)
                    {
                        Console.WriteLine();
                    }

                    foreach (var constant in asm.Value.Constants)
                    {
                        Console.ForegroundColor = ConsoleColor.Blue;
                        Console.Write(constant.Key);
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Console.WriteLine($"\t= {constant.Value}");
                    }

                    if (asm.Value.Constants.Count > 0)
                    {
                        Console.WriteLine();
                    }

                    const string indent = "\t";

                    foreach (var proc in asm.Value.Procs)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkMagenta;
                        Console.WriteLine(proc.Key);

                        int i = 0;
                        foreach (var token in proc.Value)
                        {
                            if (asm.Value.Breakpoints.TryGetValue(proc.Key, out List<int> breaks) && breaks.Contains(i))
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.Write("¤");
                            }

                            Console.ForegroundColor = ConsoleColor.DarkYellow;
                            Console.Write(indent + token.Type + "\t");
                            switch (token.Type)
                            {
                                case TokenType.Instruction:
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.WriteLine("{0,-10}{1,-10}", token.Value, token.Opcode);
                                    break;
                                case TokenType.Litteral:
                                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                                    Console.WriteLine(token.Value);
                                    break;
                                case TokenType.Label:
                                    Console.ForegroundColor = ConsoleColor.Magenta;
                                    Console.WriteLine(token.Value);
                                    break;
                                default:
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine("!!ERROR!!");
                                    break;
                            }

                            i++;
                        }
                    }
                }

                #endregion
            }

            Console.ForegroundColor = conColor;

            Console.WriteLine();
            Console.WriteLine("Assembling...");
            
            LibFile libFile = Assemble(files, executable, out bool success);

            if (success == false)
            {
                goto noFile;
            }
            
            if (verbose)
            {
                Console.WriteLine($"Result ({libFile.Instructions.Length} words): ");
                Console.WriteLine();

                Console.ForegroundColor = ConsoleColor.White;

                const int instPerLine = 3;
                for (int i = 0; i < libFile.Instructions.Length; i++)
            {
                if ((libFile.Instructions[i] & 0xF000) != 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("¤");
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("{0:X6}: ", i + (executable ? ROM_OFFSET : 0));
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("{0:X3}{1,-12}", libFile.Instructions[i] & 0xFFF, "(" + (Opcode)(libFile.Instructions[i] & 0xFFF) + ")");

                if ((i + 1) % instPerLine == 0)
                {
                    Console.WriteLine();

                    if (libFile.Instructions.Skip(i).Take(instPerLine).Sum(s => s) == 0)
                    {
                        int instructions = libFile.Instructions.Skip(i).TakeWhile(s => s == 0).Count();
                        i += instructions;
                        i -= i % instPerLine;
                        i--;

                        Console.WriteLine($"... ({instructions} instructions)");
                        continue;
                    }
                }
            }

                Console.ForegroundColor = conColor;
            }
            
            Console.WriteLine();

            FileInfo resFile = new FileInfo(Path.Combine(dirInf.FullName, name + (executable ? ".12exe" : ".12lib")));

            if (resFile.Exists && !overwrite)
            {
                Console.WriteLine("File already exsists! Replace (y/n)? ");
                if (char.ToLower(Console.ReadLine()[0]) != 'y')
                {
                    goto noFile;
                }
            }
            
            using (FileStream stream = File.Open(resFile.FullName, FileMode.Create))
            {
                void AppendToFile(string str)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(str);
                    stream.Write(bytes, 0, bytes.Length);
                }
                
                if (executable == false)
                {

                    AppendToFile("{");

                    AppendToFile(string.Join(",", libFile.ProcOffsets.Select(p => $"{p.Key},{p.Value}")));

                    AppendToFile("}");

                    AppendToFile("[");

                    AppendToFile(string.Join(",", libFile.LabelUses));

                    AppendToFile("]");

                }

                using (BinaryWriter bw = new BinaryWriter(stream))
                {
                    foreach (short s in libFile.Instructions)
                    {
                        bw.Write(s);
                    }
                }
            }

            if (open)
            {
                Process.Start(dirInf.FullName);
            }

            noFile:

            if (hold)
            {
                Console.ReadKey();
            }
        }

        static string[] PreProcess(string[] lines)
        {
            string[] newLines = new string[lines.Length];

            lines.CopyTo(newLines, 0);

            for (int i = 0; i < newLines.Length; i++)
            {
                foreach (var conversion in preprocessorConversions)
                {
                    newLines[i] = conversion.Key.Replace(newLines[i], conversion.Value);
                }
            }

            return newLines.Where(s => s.Trim().Length > 0).ToArray();
        }
        
        static AsemFile Parse(string[] lines)
        {
            Dictionary<string, string> usings = new Dictionary<string, string>();
            Dictionary<string, string> constants = new Dictionary<string, string>();
            Dictionary<string, List<Token>> procs = new Dictionary<string, List<Token>>();
            Dictionary<string, List<int>> breakpoints = new Dictionary<string, List<int>>();
            Dictionary<string, int> procLocations = new Dictionary<string, int>();

            string currProcName = null;
            List<Token> currProc = new List<Token>();

            foreach (var it_line in lines)
            {
                if (it_line[0] == '¤')
                {
                    if (breakpoints.ContainsKey(currProcName) == false)
                    {
                        breakpoints[currProcName] = new List<int>();
                    }
                    
                    // FIXME: We are filtering out all lables and litterals, this means we need to shift the breakpoints in relevant instructions!
                    breakpoints[currProcName].Add(currProc.Where(t => t.Type == TokenType.Instruction).Count());
                }

                string line = it_line.Trim(new[]{ ' ', '\t', '¤' });

                if (line.Length < 0)
                {
                    continue;
                }

                if (char.IsWhiteSpace(it_line, 0) || it_line[0] == '¤')
                {
                    line = " " + line;
                }

                Match c;
                if ((c = using_statement.Match(line)).Success)
                {
                    usings[c.Groups[1].Value] = c.Groups[2].Value;
                }
                else if ((c = constant.Match(line)).Success)
                {
                    constants[c.Groups[1].Value] = c.Groups[2].Value;
                }
                else
                {
                    if (char.IsWhiteSpace(line, 0))
                    {
                        string[] tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                        foreach (var token in tokens)
                        {
                            Token t;
                            Match l;
                            if (opcodes.TryGetValue(token, out Opcode opcode))
                            {
                                t = new Token(TokenType.Instruction, token, opcode);
                            }
                            else if ((l = label.Match(token)).Success)
                            {
                                t = new Token(TokenType.Label, l.Value);
                            }
                            else if (num.IsMatch(token) || constants.ContainsKey(token))
                            {
                                t = new Token(TokenType.Litteral, token);
                            }
                            else
                            {
                                throw new Exception($"Could not parse token: \"{token}\"");
                            }

                            currProc.Add(t);
                        }
                    }
                    else if (line[0] == ':')
                    {
                        Match l = proc.Match(line);
                        if (l.Success)
                        {
                            if (currProcName != null)
                            {
                                procs[currProcName] = currProc;
                            }

                            currProcName = l.Groups[1].Value;

                            if (l.Groups[3].Success)
                            {
                                procLocations[currProcName] = ToInt(ParseLitteral(l.Groups[3].Value, constants));

                                if (procLocations[currProcName] < 0x44B_000)
                                {
                                    throw new Exception($"Procs can only be placed in ROM. The proc {currProcName} was addressed to {procLocations[currProcName]}!");
                                }
                            }

                            currProc = new List<Token>();
                        }
                        else
                        {
                            throw new FormatException($"Invalid label: \"{line}\"");
                        }
                    }
                    else
                    {
                        throw new FormatException($"Could not parse line \"{line}\"");
                    }
                }
            }

            procs[currProcName] = currProc;

            return new AsemFile(usings, constants, procs, breakpoints, procLocations);
        }

        static LibFile Assemble(Dictionary<string, AsemFile> files, bool executable, out bool success)
        {
            Dictionary<string, List<short>> assembledProcs = new Dictionary<string, List<short>>();

            int offset = 0;

            Dictionary<string, int> procOffests = new Dictionary<string, int>();

            Dictionary<string, Dictionary<string, int>> proc_label_instructions = new Dictionary<string, Dictionary<string, int>>();
            
            Dictionary<string, Dictionary<int, string>> proc_label_uses = new Dictionary<string, Dictionary<int, string>>();

            if (executable && files.Values.SelectMany(f => f.Procs.Keys).Any(p => p == ":start") == false)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("An executable needs to contain a :start proc!");
                success = false;
                return default(LibFile);
            }

            foreach (var file in files)
            {
                offset = 0;

                foreach (var proc in file.Value.Procs)
                {
                    Console.WriteLine($"Assembling proc {proc.Key}");
                    Console.WriteLine();

                    Dictionary<string, int> local_labels = new Dictionary<string, int>();
                    Dictionary<int, string> local_label_uses = new Dictionary<int, string>();

                    List<short> instructions = new List<short>();

                    IEnumerator<Token> tokens = proc.Value.GetEnumerator();
                    
                    if (tokens.MoveNext() == false)
                    {
                        continue;
                    }

                    Token current = tokens.Current;

                    Token peek = tokens.Current;
                    
                    while (tokens.MoveNext() || !peek.Equals(tokens.Current))
                    {
                        peek = tokens.Current;
                        
                        switch (current.Type)
                        {
                            case TokenType.Instruction:
                                switch (current.Opcode ?? Opcode.Nop)
                                {
                                    case Opcode.Load_lit:
                                        if (peek.Type == TokenType.Label)
                                        {
                                            // Shift breakpoints before adding instructions
                                            ShiftBreakpoints(file.Value, proc.Key, instructions.Count, 2);

                                            // FIXME: Loading lables does not work!
                                            local_label_uses[instructions.Count] = peek.Value;
                                            instructions.Add((short) Opcode.Load_lit);
                                            instructions.Add(0);
                                            instructions.Add(0);
                                        }
                                        else if (peek.Type == TokenType.Litteral)
                                        {
                                            short[] value = ParseLitteral(peek.Value, file.Value.Constants);
                                            
                                            // Shift breakpoints before adding instructions
                                            ShiftBreakpoints(file.Value, proc.Key, instructions.Count, (value.Length * 2) - 1);

                                            foreach (var val in value.Reverse())
                                            {
                                                instructions.Add((short) current.Opcode);
                                                instructions.Add(val);
                                            }

                                            Console.WriteLine($"Parsed load litteral with litteral {peek.Value}!");
                                        }
                                        else
                                        {
                                            throw new FormatException($"{current.Opcode} can only be followed by a label or litteral!");
                                        }
                                        tokens.MoveNext();
                                        break;
                                    case Opcode.Load_addr:
                                    case Opcode.Store_pc:
                                    case Opcode.Call_pc:
                                        if (current.Equals(peek) == false && (peek.Type == TokenType.Litteral || peek.Type == TokenType.Label))
                                        {
                                            Opcode op = current.Opcode ?? Opcode.Nop;

                                            // Shift breakpoints before adding instructions
                                            ShiftBreakpoints(file.Value, proc.Key, instructions.Count, 2);
                                            
                                            instructions.Add((short)op);

                                            if (peek.Type == TokenType.Label)
                                            {
                                                local_label_uses[instructions.Count] = peek.Value;
                                                instructions.Add(0);
                                                instructions.Add(0);

                                                Console.ForegroundColor = ConsoleColor.DarkGreen;
                                                Console.WriteLine($"Added label {peek.Value} using at {instructions.Count:X}");
                                                Console.ForegroundColor = conColor;
                                            }
                                            else if (peek.Type == TokenType.Litteral)
                                            {
                                                short[] value = ParseLitteral(peek.Value, file.Value.Constants);
                                                
                                                if (value.Length > 2)
                                                {
                                                    throw new FormatException("The litteral {} does not fit in 24-bits! {} only takes 24-bit arguments!");
                                                }
                                                
                                                instructions.Add(value.Length < 2 ? (short) 0 : value[1]);
                                                instructions.Add(value[0]);
                                            }

                                            tokens.MoveNext();
                                        }
                                        else
                                        {
                                            throw new FormatException($"{current.Opcode} instruction without any following litteral or label!");
                                        }
                                        break;
                                    case Opcode.Jmp:
                                    case Opcode.Jmp_z:
                                    case Opcode.Jmp_nz:
                                    case Opcode.Jmp_cz:
                                    case Opcode.Jmp_fz:
                                        // Shift breakpoints before adding instructions
                                        ShiftBreakpoints(file.Value, proc.Key, instructions.Count, 2);

                                        instructions.Add((short)current.Opcode);

                                        if (peek.Type == TokenType.Label)
                                        {
                                            local_label_uses[instructions.Count] = peek.Value;
                                            Console.ForegroundColor = ConsoleColor.DarkGreen;
                                            Console.WriteLine($"Added label {peek.Value} using at {instructions.Count:X}");
                                            Console.ForegroundColor = conColor;
                                            instructions.Add(0);
                                            instructions.Add(0);
                                        }
                                        else if (peek.Type == TokenType.Litteral)
                                        {
                                            short[] value = ParseLitteral(peek.Value, file.Value.Constants);
                                            instructions.Add(value[1]);
                                            instructions.Add(value[0]);
                                        }
                                        else
                                        {
                                            throw new FormatException($"{current.Opcode} must be followed by a label or litteral!");
                                        }
                                        tokens.MoveNext();
                                        break;
                                    default:
                                        instructions.Add((short) (current.Opcode ?? Opcode.Nop));
                                        break;
                                }
                                break;
                            case TokenType.Litteral:
                                Console.WriteLine($"Litteral {current.Value}");
                                break;
                            case TokenType.Label:
                                local_labels[current.Value] = instructions.Count;

                                Console.ForegroundColor = ConsoleColor.DarkCyan;
                                Console.WriteLine($"Found label def {current.Value} at index: {instructions.Count:X}");
                                Console.ForegroundColor = conColor;
                                break;
                        }
                        
                        current = tokens.Current;
                    }

                    offset = instructions.Count;
                    
                    proc_label_instructions[proc.Key] = local_labels;

                    proc_label_uses[proc.Key] = local_label_uses;

                    assembledProcs[proc.Key] = instructions;

                    Console.WriteLine("----------------------");
                }
            }

            offset = 0;

            Console.WriteLine();

            Dictionary<string, int> procLocations = files.SelectMany(f => f.Value.ProcLoactaions).ToDictionary(kvs => kvs.Key, kvs => kvs.Value);

            foreach (var asem in assembledProcs)
            {
                if (procLocations.TryGetValue(asem.Key, out int location))
                {
                    location -= 0x44B_000;
                    Console.WriteLine($"Proc {asem.Key} at specified offset: {location:X}");

                    procOffests[asem.Key] = location;
                    // FIXME: Procs can overlap!!!
                }
                else
                {
                    Console.WriteLine($"Proc {asem.Key} at offset: {offset:X}");

                    procOffests[asem.Key] = offset;
                    offset += asem.Value.Count;
                }
            }

            Console.WriteLine();

            foreach (var proc in assembledProcs)
            {
                foreach (var use in proc_label_uses[proc.Key])
                {
                    if (proc_label_instructions[proc.Key].TryGetValue(use.Value, out int lbl_offset))
                    {
                        short[] offset_inst = IntToShortArray(lbl_offset + procOffests[proc.Key] + (executable ? ROM_OFFSET : 0));
                        proc.Value[use.Key] = offset_inst[1];
                        proc.Value[use.Key + 1] = offset_inst[0];
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Console.WriteLine($"{use.Value,-12} matched local at instruction: {procOffests[proc.Key] + use.Key:X6} Offset: {lbl_offset + procOffests[proc.Key]:X6}");
                        Console.ForegroundColor = conColor;
                    }
                    else if (procOffests.TryGetValue(use.Value, out int proc_offset))
                    {
                        short[] offset_inst = IntToShortArray(proc_offset + (executable ? ROM_OFFSET : 0));
                        proc.Value[use.Key] = offset_inst[1];
                        proc.Value[use.Key + 1] = offset_inst[0];
                        Console.ForegroundColor = ConsoleColor.DarkMagenta;
                        Console.WriteLine($"{use.Value,-12} matched call  at instruction: {procOffests[proc.Key] + use.Key:X6} Offset: {proc_offset:X6}");
                        Console.ForegroundColor = conColor;
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Could not solve label! {use.Value} in proc {proc.Key}");
                        Console.ForegroundColor = conColor;
                    }
                }

                if (files.Select(f => f.Value).SelectMany(f => f.Breakpoints).ToDictionary(kvs => kvs.Key, kvs => kvs.Value).TryGetValue(proc.Key, out List<int> breaks))
                {
                    foreach (var brek in breaks)
                    {
                        // FIXME: When loads are extended these values need to be updated!
                        proc.Value[brek] |= 0x7000;
                    }
                }
            }
            
            Console.WriteLine();
            
            short[] compiledInstructions = new short[12275712];

            foreach (var proc in assembledProcs)
            {
                proc.Value.CopyTo(compiledInstructions, procOffests[proc.Key]);
            }

            List<int> lableUses = new List<int>();

            foreach (var lable_use in proc_label_uses)
            {
                lableUses.AddRange(lable_use.Value.Keys.Select(l => l + procOffests[lable_use.Key]));
            }

            success = true;

            return new LibFile(compiledInstructions, procOffests, lableUses);
        }

        static short[] ParseLitteral(string litteral, Dictionary<string, string> constants)
        {
            short[] value = new short[0];
            if (num.IsMatch(litteral))
            {
                value = ParseNumber(litteral);
            }
            else if (constants.TryGetValue(litteral, out string val_str))
            {
                value = ParseNumber(val_str);
            }

            return value;
        }

        static short[] ParseNumber(string litteral)
        {
            litteral = litteral.Replace("_", "");

            short[] ret;
            
            if (litteral.StartsWith("0x"))
            {
                litteral = litteral.Substring(2);
                litteral = new string('0', litteral.Length % 3) + litteral;
                ret = Enumerable.Range(0, litteral.Length)
                    .GroupBy(x => x / 3)
                    .Select(g => g.Select(i => litteral[i]))
                    .Select(s => String.Concat(s))
                    .Select(s => Convert.ToInt16(s, 16))
                    .Reverse()
                    .ToArray();
            }
            else if (litteral.StartsWith("8x"))
            {
                litteral = litteral.Substring(2);
                litteral = new string('0', litteral.Length % 4) + litteral;
                ret = Enumerable.Range(0, litteral.Length)
                    .GroupBy(x => x / 4)
                    .Select(g => g.Select(i => litteral[i]))
                    .Select(s => String.Concat(s))
                    .Select(s => Convert.ToInt16(s, 8))
                    .Reverse()
                    .ToArray();
            }
            else
            {
                List<short> values = new List<short>();
                int value = Convert.ToInt32(litteral, 10);
                int itt = 0;
                do
                {
                    values.Add((short)((value >> (12 * itt)) & _12BIT_MASK));
                } while ((value >> (12 * itt++)) >= 4096);

                ret = values.ToArray();
            }
            
            return ret;
        }

        static short[] IntToShortArray(int i)
        {
            short[] res = new short[2];
            res[0] = (short) (i & _12BIT_MASK);
            res[1] = (short) ((i >> 12) & _12BIT_MASK);
            return res;
        }

        static int ToInt(short[] array)
        {
            switch (array.Length)
            {
                case 0:
                    return 0;
                case 1:
                    return array[0];
                case 2:
                    return (ushort)array[0] | ((ushort)array[1] << 12);
                default:
                    return -1;
            }
        }

        static void ShiftBreakpoints(AsemFile file, string proc, int instructions, int breakpoint_offset)
        {
            if (file.Breakpoints.TryGetValue(proc, out List<int> breakpoints))
            {
                file.Breakpoints[proc] = breakpoints.Select(b => b > instructions ? b + breakpoint_offset : b).ToList();
            }
        }
    }
}