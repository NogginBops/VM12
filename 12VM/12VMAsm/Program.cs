using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;

namespace _12VMAsm
{
    class Program
    {
        enum Opcode : short
        {
            Nop,
            Load_addr,
            Load_lit,
            Load_lit_l,
            Load_sp,
            Store_pc,
            Store_sp,
            Call_sp,
            Call_pc,
            Ret,
            Dup,
            Over,
            Swap,
            Drop,
            Reclaim,
            Add,
            Sh_l,
            Sh_r,
            Not,
            Neg,
            Xor,
            And,
            Inc,
            Add_f,
            Neg_f,
            Jmp,
            Jpm_z,
            Jmp_nz,
            Jmp_cz,
            Jmp_fz,
        }
        
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

            public AsemFile(Dictionary<string, string> usigns, Dictionary<string, string> constants, Dictionary<string, List<Token>> procs)
            {
                Usings = usigns;
                Constants = constants;
                Procs = procs;
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

        static Dictionary<Regex, string> preprocessorConversions = new Dictionary<Regex, string>()
        {
            { new Regex(";.*"), "" },
            { new Regex("#reg.*"), "" },
            { new Regex("#endreg.*"), "" },
            { new Regex("shl"), "sh.l" },
            { new Regex("shr"), "sh.r" },
            { new Regex("fadd"), "add.f" },
            { new Regex("fneg"), "neg.f" },
            { new Regex("jz"), "jmp.z" },
            { new Regex("jnz"), "jmp.nz" },
            { new Regex("jcz"), "jmp.cz" },
            { new Regex("jfz"), "jmp.fz" },
            { new Regex("::\\[SP\\]"), "call.sp" },
            { new Regex("::(?!\\s)"), "call.pc :" },
            { new Regex("load\\s+@"), "load.addr " },
            { new Regex("load\\s+#"), "load.lit " },
            { new Regex("load\\s+:"), "load.lit :" },
            { new Regex("load\\s+\\[SP\\]"), "load.sp" },
            { new Regex("store\\s+\\[SP\\]"), "store.sp" },
            { new Regex("store\\s+@"), "store.pc " }
        };

        static Regex using_statement = new Regex("&\\s*([A-Za-z][A-Za-z0-9_]*)\\s+(.*\\.12asm)");

        static Regex constant = new Regex("<([A-Za-z_][A-Za-z0-9_]*)\\s*=\\s*(0x[0-9A-Fa-f_]+|8x[0-7_]+|[0-9_]+)>");

        static Regex label = new Regex(":[A-Za-z][A-Za-z0-9_]*");

        static Regex num = new Regex("\\b(0x[0-9A-Fa-f_]+|8x[0-7_]+|[0-9_]+)");

        static Dictionary<string, Opcode> opcodes = new Dictionary<string, Opcode>()
        {
            { "nop", Opcode.Nop },
            { "load.addr", Opcode.Load_addr },
            { "load.lit", Opcode.Load_lit },
            { "load.lit.l", Opcode.Load_lit_l },
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
            { "jmp.z", Opcode.Jpm_z },
            { "jmp.nz", Opcode.Jmp_nz },
            { "jmp.cz", Opcode.Jmp_cz },
            { "jmp.fz", Opcode.Jmp_fz },
        };

        static ConsoleColor conColor = Console.ForegroundColor;

        static void Main(string[] args)
        {
            string file = null;

            do
            {
                Console.Write("Input file: ");

                file = Console.ReadLine();

            } while (File.Exists(file) == false);

            string[] lines = File.ReadAllLines(file);

            Console.WriteLine();
            Console.WriteLine();

            Console.WriteLine("Preprocessing...");

            string[] newLines = PreProcess(lines);

            /*
            foreach (var line in newLines)
            {
                Console.WriteLine(line);
            }
            */

            Console.WriteLine("Parsing...");

            #region Usings

            Dictionary<string, AsemFile> files = new Dictionary<string, AsemFile>();

            Stack<string> remainingUsings = new Stack<string>();

            FileInfo fileInf = new FileInfo(file);
            DirectoryInfo dirInf = fileInf.Directory;
            
            FileInfo[] dirFiles = dirInf.GetFiles($".{Path.DirectorySeparatorChar}*.12asm", SearchOption.AllDirectories);

            AsemFile asmFile = Parse(newLines);

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
            }

            foreach (var f in files)
            {
                Console.WriteLine(f);
            }
            #endregion

            #region Print_Files

            foreach (var asm in files)
            {
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

                const string indent = "    ";

                foreach (var proc in asm.Value.Procs)
                {
                    Console.ForegroundColor = ConsoleColor.DarkMagenta;
                    Console.WriteLine(proc.Key);

                    foreach (var token in proc.Value)
                    {
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
                    }
                }
            }

            #endregion

            Console.ForegroundColor = conColor;

            Console.WriteLine();
            Console.WriteLine("Assembling...");
            
            LibFile libFile = Assemble(files);

            Console.WriteLine("Result: ");
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.White;

            const int instPerLine = 3;

            for (int i = 0; i < libFile.Instructions.Length; i++)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("{0:X6}: ", i);
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("{0:X4}{1,-12}", libFile.Instructions[i], "(" + (Opcode)libFile.Instructions[i] + ")");

                if ((i + 1) % instPerLine == 0)
                {
                    Console.WriteLine();
                }
            }

            Console.ForegroundColor = conColor;

            Console.WriteLine();
            Console.WriteLine();
            Console.Write("File name?: ");

            string name = Console.ReadLine();

            FileInfo resFile = new FileInfo(Path.Combine(dirInf.FullName, name + ".12lib"));

            if (resFile.Exists)
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

                byte[] ToBytes(short num)
                {
                    byte[] b = new byte[2];
                    b[0] = (byte)(num >> 8);
                    b[1] = (byte)(num & 255);
                    return b;
                }

                AppendToFile("{");

                AppendToFile(string.Join(",", libFile.ProcOffsets.Select(p => $"{p.Key},{p.Value}")));
                
                AppendToFile("}");

                AppendToFile("[");

                AppendToFile(string.Join(",", libFile.LabelUses));

                AppendToFile("]");

                byte[] inst = libFile.Instructions.Select(i => ToBytes(i)).Aggregate((sum, barr) => sum.Concat(barr).ToArray());

                stream.Write(inst, 0, inst.Length);

                stream.Flush();
            }

            Process.Start(dirInf.FullName);
            
            noFile:

            Console.ReadKey();
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

            string currProcName = null;
            List<Token> currProc = new List<Token>();

            foreach (var it_line in lines)
            {
                string line = it_line.Trim();

                if (line.Length < 0)
                {
                    continue;
                }

                if (char.IsWhiteSpace(it_line, 0))
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
                            else if (num.IsMatch(token) ||constants.ContainsKey(token))
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
                        Match l = label.Match(line);
                        if (l.Success)
                        {
                            if (currProcName != null)
                            {
                                procs[currProcName] = currProc;
                            }

                            currProcName = l.Value;

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

            return new AsemFile(usings, constants, procs);
        }

        static LibFile Assemble(Dictionary<string, AsemFile> files)
        {
            Dictionary<string, List<short>> assembledProcs = new Dictionary<string, List<short>>();

            int offset = 0;

            Dictionary<string, int> procOffests = new Dictionary<string, int>();

            Dictionary<string, Dictionary<string, int>> proc_label_instructions = new Dictionary<string, Dictionary<string, int>>();
            
            Dictionary<string, Dictionary<int, string>> proc_label_uses = new Dictionary<string, Dictionary<int, string>>();

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
                                        instructions.Add((short)current.Opcode);
                                        if (peek.Type == TokenType.Label)
                                        {
                                            local_label_uses[instructions.Count] = peek.Value;
                                            instructions.Add(0);
                                            instructions.Add(0);
                                        }
                                        instructions.Add(0x1000);
                                        tokens.MoveNext();
                                        break;
                                    case Opcode.Load_lit_l:
                                    case Opcode.Load_addr:
                                    case Opcode.Store_pc:
                                    case Opcode.Call_pc:
                                        if (current.Equals(peek) == false && (peek.Type == TokenType.Litteral || peek.Type == TokenType.Label))
                                        {
                                            // TOOD: check if litteral or lable is more than 12 bits

                                            Opcode op = current.Opcode ?? Opcode.Nop;

                                            if (peek.Type == TokenType.Label)
                                            {
                                                instructions.Add((short) op);
                                                local_label_uses[instructions.Count] = peek.Value;
                                                instructions.Add(0);
                                                instructions.Add(0);
                                                //FIXME!! We need to know where labels are located in memory to do this!
                                                Console.ForegroundColor = ConsoleColor.DarkGreen;
                                                Console.WriteLine($"Added label {peek.Value} using at {instructions.Count:X}");
                                                Console.ForegroundColor = conColor;
                                            }
                                            else if (peek.Type == TokenType.Litteral)
                                            {
                                                // FIXME: This should be one opcode with two value instructions after
                                                
                                                /*
                                                short[] value = new short[0];
                                                if (num.IsMatch(peek.Value))
                                                {
                                                    //value = ParseLitteral(peek.Value);
                                                }
                                                else if (file.Value.Constants.TryGetValue(peek.Value, out string val_str))
                                                {
                                                    //value = ParseLitteral(val_str);
                                                }

                                                foreach (var val in value)
                                                {
                                                    instructions.Add((short)op);
                                                    instructions.Add(val);
                                                }
                                                */

                                                instructions.Add((short)op);
                                                instructions.Add(0);
                                                instructions.Add(0);
                                            }

                                            tokens.MoveNext();
                                        }
                                        else
                                        {
                                            throw new FormatException($"{current.Opcode} instruction without any following litteral or label!");
                                        }
                                        break;
                                    case Opcode.Jmp:
                                    case Opcode.Jpm_z:
                                    case Opcode.Jmp_nz:
                                    case Opcode.Jmp_cz:
                                    case Opcode.Jmp_fz:
                                        instructions.Add((short)current.Opcode);
                                        // TODO: Reference this point as a label
                                        if (peek.Type == TokenType.Label)
                                        {
                                            local_label_uses[instructions.Count] = peek.Value;
                                            Console.ForegroundColor = ConsoleColor.DarkGreen;
                                            Console.WriteLine($"Added label {peek.Value} using at {instructions.Count:X}");
                                            Console.ForegroundColor = conColor;
                                        }

                                        instructions.Add(0);
                                        instructions.Add(0);

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

            foreach (var asem in assembledProcs)
            {
                Console.WriteLine($"Proc {asem.Key} at offset: {offset:X}");

                procOffests[asem.Key] = offset;
                offset += asem.Value.Count;
            }

            Console.WriteLine();

            foreach (var proc in assembledProcs)
            {
                foreach (var use in proc_label_uses[proc.Key])
                {
                    // FIXME: Offsets bigger than 4096
                    if (proc_label_instructions[proc.Key].TryGetValue(use.Value, out int lbl_offset))
                    {
                        proc.Value[use.Key + 1] = (short) (lbl_offset + procOffests[proc.Key]);
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Console.WriteLine($"{use.Value,-12} matched local at instruction: {procOffests[proc.Key] + use.Key:X6} Offset: {lbl_offset + procOffests[proc.Key]:X6}");
                        Console.ForegroundColor = conColor;
                    }
                    else if (procOffests.TryGetValue(use.Value, out int proc_offset))
                    {
                        proc.Value[use.Key + 1] = (short)proc_offset;
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
            }

            Console.WriteLine();
            
            short[] compiledInstructions = new short[offset];

            foreach (var proc in assembledProcs)
            {
                proc.Value.CopyTo(compiledInstructions, procOffests[proc.Key]);
            }

            List<int> lableUses = new List<int>();

            foreach (var lable_use in proc_label_uses)
            {
                lableUses.AddRange(lable_use.Value.Keys.Select(l => l + procOffests[lable_use.Key]));
            }

            return new LibFile(compiledInstructions, procOffests, lableUses);
        }
    }
}