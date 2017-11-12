using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Diagnostics;
using VM12_Opcode;

namespace VM12Asm
{
    public class VM12Asm
    {
        enum TokenType
        {
            Instruction,
            Litteral,
            Label,
            Argument
        }

        struct Token
        {
            public readonly int Line;
            public readonly TokenType Type;
            public readonly bool Breakpoint;
            public readonly string Value;
            public readonly Opcode? Opcode;

            public Token(int line, TokenType type, string value, bool breakpoint, Opcode? opcode = null)
            {
                Line = line;
                Type = type;
                Breakpoint = breakpoint;
                Value = value;
                Opcode = opcode;
            }

            public bool Equals(Token t)
            {
                return (Line == t.Line) && (Type == t.Type) && (Value == t.Value) && (Opcode == t.Opcode);
            }

            public override string ToString()
            {
                return $"{{{Type}: {(Type == TokenType.Instruction ? Opcode.ToString() : Value)}}}";
            }
        }

        class Proc
        {
            public string name;
            public int line;
            public int? parameters;
            public int? locals;
            public int? location;
            public List<Token> tokens;
        }
        
        class RawFile
        {
            public string path;
            public string[] rawlines;
            public string[] processedlines;
        }

        class AsemFile
        {
            public readonly RawFile Raw;
            public readonly Dictionary<string, string> Usings;
            public readonly Dictionary<string, string> Constants;
            public readonly Dictionary<string, Proc> Procs;
            public readonly Dictionary<string, List<int>> Breakpoints;
            public readonly HashSet<string> Flags;

            public AsemFile(RawFile raw, Dictionary<string, string> usigns, Dictionary<string, string> constants, Dictionary<string, Proc> procs, Dictionary<string, List<int>> breakpoints, HashSet<string> flags)
            {
                Raw = raw;
                Usings = usigns;
                Constants = constants;
                Procs = procs;
                Breakpoints = breakpoints;
                Flags = flags;
            }

            public override string ToString()
            {
                return $"{{AsemFile: Procs: {Procs.Count}, Breaks: {Breakpoints.Count}, Flags: '{string.Join(",", Flags)}' }}";
            }
        }

        class LibFile
        {
            public readonly ProcMetadata[] Metadata;
            public readonly short[] Instructions;
            public readonly int UsedInstructions;

            public LibFile(short[] instructions, ProcMetadata[] metadata, int usedInstructions)
            {
                Instructions = instructions;
                Metadata = metadata;
                UsedInstructions = usedInstructions;
            }
        }

        class ProcMetadata
        {
            public RawFile source;
            public AsemFile assembledSource;
            public int location;
            public string name;
            public int size;
            public int line;
            public Dictionary<int, int> linkedLines;
            public List<int> breaks;
        }

        struct AutoConst
        {
            public readonly string Value;
            public readonly int Length;

            public AutoConst(string value, int length)
            {
                this.Value = value;
                this.Length = length;
            }
        }

        const int _12BIT_MASK = 0x0FFF;

        const int ROM_OFFSET = 0x44B_000;

        const int ROM_SIZE = 12275712;

        const short ROM_OFFSET_UPPER_BITS = 0x44B;

        const int VRAM_OFFSET = 0x400_000;

        delegate string TemplateFormater(params object[] values);

        static TemplateFormater sname = (o) => string.Format("(?<!:)\\b{0}\\b", o);

        static Dictionary<Regex, string> preprocessorConversions = new Dictionary<Regex, string>()
        {
            { new Regex(";.*"), "" },
            { new Regex("#reg.*"), "" },
            { new Regex("#endreg.*"), "" },
            { new Regex("(?<!:)\\bload\\s+#(\\S+)"), "load.lit $1" },
            { new Regex("(?<!:)\\bloadl\\s+#(\\S+)"), "load.lit.l $1" },
            { new Regex("(?<!:)\\bload\\s+(:\\S+)"), "load.lit $1" },
            { new Regex("(?<!:)\\bloadl\\s+(:\\S+)"), "load.lit.l $1" },
            { new Regex("(?<!:)\\bload\\s+(\\d+)"), "load.local $1" },
            { new Regex("(?<!:)\\bloadl\\s+(\\d+)"), "load.local.l $1" },
            { new Regex("(?<!:)\\bload\\s+@(\\S+)"), "load.lit.l $1 load.sp" },
            { new Regex("(?<!:)\\bloadl\\s+@(\\S+)"), "load.lit.l $1 load.sp.l" },
            { new Regex("(?<!:)\\bload\\s+\\[SP\\]"), "load.sp" },
            { new Regex("(?<!:)\\bloadl\\s+\\[SP\\]"), "load.sp.l" },
            { new Regex("(?<!:)\\bload\\s+('.')"), "load.lit $1" },
            { new Regex("(?<!:)\\bload\\s+(\".*?\")"), "load.lit.l $1" },
            { new Regex("(?<!:)\\bstore\\s+\\[SP\\]"), "store.sp" },
            { new Regex("(?<!:)\\bstorel\\s+\\[SP\\]"), "store.sp.l" },
            { new Regex("(?<!:)\\bstore\\s+(\\d+)"), "store.local $1" },
            { new Regex("(?<!:)\\bstorel\\s+(\\d+)"), "store.local.l $1" },
            { new Regex("(?<!:)\\bstore\\s+#(\\S+)\\s+@(\\S+)"), "load.lit.l $2 load.lit $1 store.sp" },
            { new Regex("(?<!:)\\bstorel\\s+#(\\S+)\\s+@(\\S+)"), "load.lit.l $2 load.lit.l $1 store.sp.l" },
            { new Regex("(?<!:)\\bstore\\s+#(\\S+)"), "load.lit $1 store.sp" },
            { new Regex("(?<!:)\\bstorel\\s+#(\\S+)"), "load.lit.l $1 store.sp.l" },
            { new Regex("(?<!:)\\bstore\\s+@(\\S+)"), "load.lit.l $1 swap.s.l store.sp" },
            { new Regex("(?<!:)\\bstorel\\s+@(\\S+)"), "load.lit.l $1 swap.l store.sp.l" },
            { new Regex("(?<!:)\\bset\\s+\\[SP\\]"), "set.sp" },
            { new Regex("::\\[SP\\]"), "call.v" },
            { new Regex("::(?!\\s)"), "call :" },
            { new Regex(sname("lswap")), "swap.l" },
            { new Regex(sname("slswap")), "swap.s.l" },
            { new Regex(sname("ldup")), "dup.l" },
            { new Regex(sname("lover")), "over.l.l" },
            { new Regex(sname("lovers")), "over.l.s" },
            { new Regex(sname("ladd")), "add.l" },
            { new Regex(sname("adc")), "add.c" },
            { new Regex(sname("lsub")), "sub.l" },
            { new Regex(sname("lneg")), "neg.l" },
            { new Regex(sname("linc")), "inc.l" },
            { new Regex(sname("ldec")), "dec.l" },
            { new Regex(sname("css")), "c.ss" },
            { new Regex(sname("cse")), "c.se" },
            { new Regex(sname("ccl")), "c.cl" },
            { new Regex(sname("cflp")), "c.flp" },
            { new Regex(sname("rtcl")), "rot.l.c" },
            { new Regex(sname("rtcr")), "rot.r.c" },
            { new Regex(sname("jmp")), "jmp JM.Jmp" },
            { new Regex(sname("jz")), "jmp JM.Z" },
            { new Regex(sname("jnz")), "jmp JM.Nz" },
            { new Regex(sname("jc")), "jmp JM.C" },
            { new Regex(sname("jcz")), "jmp JM.Cz" },
            { new Regex(sname("jgz")), "jmp JM.Gz" },
            { new Regex(sname("jlz")), "jmp JM.Lz" },
            { new Regex(sname("jge")), "jmp JM.Ge" },
            { new Regex(sname("jle")), "jmp JM.Le" },
            { new Regex(sname("jeq")), "jmp JM.Eq" },
            { new Regex(sname("jneq")), "jmp JM.Neq" },
            { new Regex(sname("jro")), "jmp JM.Ro" },
            { new Regex(sname("jzl")), "jmp JM.Z.l" },
            { new Regex(sname("jnzl")), "jmp JM.Nz.l" },
            { new Regex(sname("jgzl")), "jmp JM.Gz.l" },
            { new Regex(sname("jlzl")), "jmp JM.Lz.l" },
            { new Regex(sname("jgel")), "jmp JM.Ge.l" },
            { new Regex(sname("jlel")), "jmp JM.Le.l" },
            { new Regex(sname("jeql")), "jmp JM.Eq.l" },
            { new Regex(sname("jneql")), "jmp JM.Neq.l" },
            { new Regex(sname("jrol")), "jmp JM.Ro.l" },
            { new Regex(sname("ret1")), "ret.1" },
            { new Regex(sname("ret2")), "ret.2" },
            { new Regex(sname("retv")), "ret.v" },
            { new Regex("(?<!:)\\binc\\s+(\\d+)"), "inc.local $1" },
            { new Regex("(?<!:)\\blinc\\s+(\\d+)"), "inc.local.l $1" },
            { new Regex("(?<!:)\\binc.l\\s+(\\d+)"), "inc.local.l $1" },
            { new Regex("(?<!:)\\bdec\\s+(\\d+)"), "dec.local $1" },
            { new Regex("(?<!:)\\bldec\\s+(\\d+)"), "dec.local.l $1" },
            { new Regex("(?<!:)\\bdec.l\\s+(\\d+)"), "dec.local.l $1" },
            { new Regex(sname("mul2")), "mul.2" },
            { new Regex(sname("fcb")), "fc.b" },
            { new Regex(sname("lmul")), "mul.l" },
            { new Regex(sname("lmul2")), "mul.2.l" },
            { new Regex(sname("ldiv")), "div.l" },
            { new Regex(sname("lmod")), "mod.l" },
            { new Regex(sname("blitm")), "blit.mask" },

            { new Regex("\\[FP\\]"), "fp" },
            { new Regex("\\[PC\\]"), "pc" },
            { new Regex("\\[SP\\]"), "sp" },
        };

        static Regex using_statement = new Regex("&\\s*([A-Za-z][A-Za-z0-9_]*)\\s+(.*\\.12asm)");

        static Regex constant = new Regex("<([A-Za-z_][A-Za-z0-9_]*)\\s*=\\s*(0x[0-9A-Fa-f_]+|8x[0-7_]+|0b[0-1_]+|[0-9_]+|extern|auto\\((0x[0-9A-Fa-f_]+|8x[0-7_]+|0b[0-1_]+|[0-9_]+)\\))>");

        static Regex label = new Regex(":[A-Za-z_][A-Za-z0-9_]*");

        static Regex proc = new Regex("(:[A-Za-z_][A-Za-z0-9_]*)(\\s+@(.*))?");

        static Regex num = new Regex("(?<!\\S)(0x[0-9A-Fa-f_]+|8x[0-7_]+|0b[0-1_]+|-?[0-9_]+)(?!\\S)");

        static Regex chr = new Regex("'(.)'");

        static Regex str = new Regex("^\\s*\"[^\"\\\\]*(\\\\.[^\"\\\\]*)*\"$");

        static Regex auto = new Regex("auto\\((.*)\\)");

        static Dictionary<string, Opcode> opcodes = new Dictionary<string, Opcode>()
        {
            { "nop", Opcode.Nop },
            { "pop", Opcode.Pop },
            { "fp", Opcode.Fp },
            { "pc", Opcode.Pc },
            { "sp", Opcode.Sp },
            { "set.sp", Opcode.Set_sp },
            { "load.lit", Opcode.Load_lit },
            { "load.lit.l", Opcode.Load_lit_l },
            { "load.sp", Opcode.Load_sp },
            { "load.sp.l", Opcode.Load_sp_l },
            { "load.local", Opcode.Load_local },
            { "load.local.l", Opcode.Load_local_l },
            { "store.sp", Opcode.Store_sp },
            { "store.sp.l", Opcode.Store_sp_l },
            { "store.local", Opcode.Store_local },
            { "store.local.l", Opcode.Store_local_l },
            { "swap", Opcode.Swap },
            { "swap.l", Opcode.Swap_l },
            { "swap.s.l", Opcode.Swap_s_l },
            { "dup", Opcode.Dup },
            { "dup.l", Opcode.Dup_l },
            { "over", Opcode.Over },
            { "over.l.l", Opcode.Over_l_l },
            { "over.l.s", Opcode.Over_l_s },
            { "add", Opcode.Add },
            { "add.l", Opcode.Add_l },
            { "sub", Opcode.Sub },
            { "sub.l", Opcode.Sub_l },
            { "neg", Opcode.Neg },
            { "neg.l", Opcode.Neg_l },
            { "inc", Opcode.Inc },
            { "inc.l", Opcode.Inc_l },
            { "dec", Opcode.Dec },
            { "dec.l", Opcode.Dec_l },
            { "or", Opcode.Or },
            { "xor", Opcode.Xor },
            { "and", Opcode.And },
            { "not", Opcode.Not },
            { "c.ss", Opcode.C_ss },
            { "c.se", Opcode.C_se },
            { "c.cl", Opcode.C_cl },
            { "c.flp", Opcode.C_flp },
            { "rot.l.c", Opcode.Rot_l_c },
            { "rot.r.c", Opcode.Rot_r_c },
            { "mul", Opcode.Mul },
            { "div", Opcode.Div },
            { "eni", Opcode.Eni },
            { "dsi", Opcode.Dsi },
            { "hlt", Opcode.Hlt },
            { "jmp", Opcode.Jmp },
            { "call", Opcode.Call },
            { "call.v", Opcode.Call_v },
            { "ret", Opcode.Ret },
            { "ret.1", Opcode.Ret_1 },
            { "ret.2", Opcode.Ret_2 },
            { "ret.v", Opcode.Ret_v },
            { "memc", Opcode.Memc },

            { "inc.local", Opcode.Inc_local },
            { "inc.local.l", Opcode.Inc_local_l },
            { "dec.local", Opcode.Dec_local },
            { "dec.local.l", Opcode.Dec_local_l },
            { "mul.2", Opcode.Mul_2 },
            { "fc", Opcode.Fc },
            { "fc.b", Opcode.Fc_b },
            { "mul.l", Opcode.Mul_l },
            { "mul.2.l", Opcode.Mul_2_l },
            { "div.l", Opcode.Div_l },
            { "mod", Opcode.Mod },
            { "mod.l", Opcode.Mod_l },
            { "blit", Opcode.Blit },
            { "blit.mask", Opcode.Blit_mask },
        };

        static Dictionary<string, int> arguments = new Dictionary<string, int>()
        {
            { "JM.Jmp", (int) JumpMode.Jmp },
            { "JM.Z", (int) JumpMode.Z },
            { "JM.Nz", (int) JumpMode.Nz },
            { "JM.C", (int) JumpMode.C },
            { "JM.Cz", (int) JumpMode.Cz },
            { "JM.Gz", (int) JumpMode.Gz },
            { "JM.Lz", (int) JumpMode.Lz },
            { "JM.Ge", (int) JumpMode.Ge },
            { "JM.Le", (int) JumpMode.Le },
            { "JM.Eq", (int) JumpMode.Eq },
            { "JM.Neq", (int) JumpMode.Neq },
            { "JM.Ro", (int) JumpMode.Ro },
            
            { "JM.Z.l", (int) JumpMode.Z_l },
            { "JM.Nz.l", (int) JumpMode.Nz_l },
            { "JM.Gz.l", (int) JumpMode.Gz_l },
            { "JM.Lz.l", (int) JumpMode.Lz_l },
            { "JM.Ge.l", (int) JumpMode.Ge_l },
            { "JM.Le.l", (int) JumpMode.Le_l },
            { "JM.Eq.l", (int) JumpMode.Eq_l },
            { "JM.Neq.l", (int) JumpMode.Neq_l },
            { "JM.Ro.l", (int) JumpMode.Ro_l },

            { "BM.Black", (int) BlitMode.Black },
        };

        static ConsoleColor conColor = Console.ForegroundColor;

        static bool verbose = false;

        static bool dump_mem = true;
        
        static Dictionary<string, string> globalConstants = new Dictionary<string, string>();

        static Dictionary<string, AutoConst> autoConstants = new Dictionary<string, AutoConst>();

        static int warnings = 0;

        static int autoStringIndex = 0;

        static Dictionary<string, string> autoStrings = new Dictionary<string, string>();

        static StringBuilder autoStringsFile = new StringBuilder(1000);

        static int autoVars = VRAM_OFFSET - 1;

        const int STACK_SIZE = 0x100_000;

        public static void Reset()
        {
            verbose = false;

            dump_mem = false;

            globalConstants = new Dictionary<string, string>();

            autoConstants = new Dictionary<string, AutoConst>();

            warnings = 0;
            autoVars = VRAM_OFFSET - 1;
        }

        public static void Main(params string[] args)
        {
            Console.ForegroundColor = conColor;
            
            IEnumerator<string> enumerator = args.AsEnumerable().GetEnumerator();
            
            string file = null;
            string name = null;
            bool generateStringSource = true;
            bool executable = true;
            bool overwrite = false;
            bool hold = false;
            bool open = false;
            
            long preprocessTime = 0;
            long parseTime = 0;
            long assemblyTime = 0;
            Stopwatch watch = new Stopwatch();
            Stopwatch total = new Stopwatch();
            total.Start();

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
            
            Console.WriteLine();
            Console.WriteLine();

            Console.WriteLine("Preprocessing...");

            autoStringsFile.AppendLine("!global");
            autoStringsFile.AppendLine();

            Console.WriteLine("Parsing...");
            
            #region Usings

            Dictionary<string, AsemFile> files = new Dictionary<string, AsemFile>();

            Stack<string> remainingUsings = new Stack<string>();

            remainingUsings.Push(Path.GetFileName(file));

            FileInfo fileInf = new FileInfo(file);
            DirectoryInfo dirInf = fileInf.Directory;
            
            FileInfo[] dirFiles = dirInf.GetFiles($".{Path.DirectorySeparatorChar}*.12asm", SearchOption.AllDirectories);
            
            // TODO: Files with the same name but differnet directories
            while (remainingUsings.Count > 0)
            {
                string use = remainingUsings.Pop();

                if (files.ContainsKey(use))
                {
                    continue;
                }

                FileInfo fi = dirFiles.First(f => f.Name == use);

                RawFile rawFile = new RawFile();

                rawFile.path = fi.FullName;

                rawFile.rawlines = File.ReadAllLines(fi.FullName);

                watch.Restart();
                rawFile.processedlines = PreProcess(rawFile.rawlines);
                watch.Stop();
                preprocessTime += watch.ElapsedTicks;

                watch.Restart();
                AsemFile asmFile = Parse(rawFile);
                watch.Stop();
                parseTime += watch.ElapsedTicks;

                files[use] = asmFile;

                foreach (var u in asmFile.Usings.Reverse())
                {
                    if (files.ContainsKey(u.Key) == false)
                    {
                        remainingUsings.Push(u.Value);
                    }
                }
            }

            RawFile rawAutoStrings = new RawFile();
            rawAutoStrings.path = "AutoStrings";
            rawAutoStrings.rawlines = autoStringsFile.ToString().Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            rawAutoStrings.processedlines = rawAutoStrings.rawlines;

            watch.Restart();
            AsemFile autoStringAsem = Parse(rawAutoStrings);
            watch.Stop();
            parseTime += watch.ElapsedTicks;

            files["AutoStrings.12asm"] = autoStringAsem;

            if (generateStringSource)
            {
                File.WriteAllText(Path.Combine(dirInf.FullName, "AutoStrings.12asm"), autoStringsFile.ToString());
            }

            if (verbose)
            {
                foreach (var f in files)
                {
                    Console.WriteLine(f);
                }
            }
            
            #endregion

            if (verbose)
            {
                #region Print_Files

                foreach (var asm in files)
                {
                    if (asm.Value.Flags.Contains("!noprintouts"))
                    {
                        continue;
                    }

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
                        Console.WriteLine(proc.Key + $" (params: {proc.Value.parameters}, locals: {proc.Value.locals}) [{proc.Value.tokens.Count} instructions]");

                        int i = 0;
                        foreach (var token in proc.Value.tokens)
                        {
                            if (token.Breakpoint)
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
                                    Console.WriteLine("{0,-14}{1,-14}", token.Value, token.Opcode);
                                    break;
                                case TokenType.Litteral:
                                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                                    Console.WriteLine(token.Value);
                                    break;
                                case TokenType.Label:
                                    Console.ForegroundColor = ConsoleColor.Magenta;
                                    Console.WriteLine("\t" + token.Value);
                                    break;
                                case TokenType.Argument:
                                    Console.ForegroundColor = ConsoleColor.Cyan;
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

                Console.ForegroundColor = conColor;
                Console.WriteLine();

                #endregion
            }
            
            Console.WriteLine("Assembling...");

            watch.Restart();
            LibFile libFile = Assemble(files, executable, out bool success);
            watch.Stop();
            assemblyTime += watch.ElapsedTicks;

            if (success == false)
            {
                goto noFile;
            }

            total.Stop();

            double preprocess_ms = ((double)preprocessTime / Stopwatch.Frequency) * 100;
            double parse_ms = ((double)parseTime / Stopwatch.Frequency) * 100;
            double assembly_ms = ((double)assemblyTime / Stopwatch.Frequency) * 100;
            double total_ms_sum = preprocess_ms + parse_ms + assembly_ms;
            double total_ms = ((double)total.ElapsedTicks / Stopwatch.Frequency) * 100;

            string warningString = $"Assembled with {warnings} warning{(warnings > 0 ? "" : "s")}.";
            Console.WriteLine($"Success! {warningString}");
            Console.WriteLine($"Preprocess: {preprocess_ms:F4} ms");
            Console.WriteLine($"Parse: {parse_ms:F4} ms");
            Console.WriteLine($"Assembly: {assembly_ms:F4} ms");
            Console.WriteLine($"Sum: {total_ms_sum:F4} ms");
            Console.WriteLine($"Total: {total_ms:F4} ms");

            if (dump_mem)
            {
                bool toFile = false;
                TextWriter output = Console.Out;
                if (toFile)
                {
                    output = new StreamWriter(File.Create(Path.Combine(dirInf.FullName, "output.txt")), Encoding.ASCII, 2048);
                }

                output.WriteLine($"Result ({libFile.UsedInstructions} used words ({((double) libFile.UsedInstructions / ROM_SIZE):P5})): ");
                output.WriteLine();

                Console.ForegroundColor = ConsoleColor.White;

                const int instPerLine = 3;
                for (int i = 0; i < libFile.Instructions.Length; i++)
                {
                    if ((libFile.Instructions[i] & 0xF000) != 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        output.Write("¤");
                    }

                    Console.ForegroundColor = ConsoleColor.Green;
                    output.Write("{0:X6}: ", i + (executable ? ROM_OFFSET : 0));
                    Console.ForegroundColor = ConsoleColor.White;
                    output.Write("{0:X3}{1,-14}", libFile.Instructions[i] & 0xFFF, "(" + (Opcode)(libFile.Instructions[i] & 0xFFF) + ")");

                    if ((i + 1) % instPerLine == 0)
                    {
                        output.WriteLine();

                        int sum = 0;
                        for (int j = 0; j < instPerLine; j++)
                        {
                            if (i + j < libFile.Instructions.Length)
                            {
                                sum += libFile.Instructions[i + j];
                            }
                        }

                        if (sum == 0)
                        {
                            int instructions = 0;
                            while (i + instructions < libFile.Instructions.Length && libFile.Instructions[i + instructions] == 0)
                            {
                                instructions++;
                            }

                            i += instructions;
                            i -= i % instPerLine;
                            i--;

                            output.WriteLine($"... ({instructions} instructions)");
                            continue;
                        }
                    }
                }

                Console.ForegroundColor = conColor;
            }
            
            Console.WriteLine();
            Console.WriteLine($"Done! {warningString}");

            FileInfo resFile = new FileInfo(Path.Combine(dirInf.FullName, name + ".12exe"));

            if (resFile.Exists && !overwrite)
            {
                Console.WriteLine("File already exsists! Replace (y/n)? ");
                if (char.ToLower(Console.ReadLine()[0]) != 'y')
                {
                    goto noFile;
                }
            }

            resFile.Delete();

            using (FileStream stream = resFile.Create())
            {
                if (executable == false)
                {
                    RawFile nullFile = new RawFile();
                    Error(nullFile, -1, "There is currently no support for non executable files at this time!");
                }

                using (BinaryWriter bw = new BinaryWriter(stream))
                {
                    foreach (short s in libFile.Instructions)
                    {
                        bw.Write(s);
                    }
                }
            }

            FileInfo metaFile = new FileInfo(Path.Combine(dirInf.FullName, name + ".12meta"));

            metaFile.Delete();

            using (FileStream stream = metaFile.Create())
            using (StreamWriter writer = new StreamWriter(stream))
            {
                foreach (var constant in autoConstants)
                {
                    writer.WriteLine($"[constant:{{{constant.Key},{constant.Value.Length},{constant.Value.Value}}}]");
                }

                writer.WriteLine();

                foreach (var proc in libFile.Metadata)
                {
                    writer.WriteLine(proc.name);
                    writer.WriteLine($"[file:{new FileInfo(proc.source.path).Name}]");
                    writer.WriteLine($"[location:{proc.location}]");
                    //writer.WriteLine($"[link?]");
                    writer.WriteLine($"[proc-line:{proc.line}]");
                    if (proc.breaks?.Count > 0)
                    {
                        writer.WriteLine($"[break:{{{string.Join(",", proc?.breaks)}}}]");
                    }
                    writer.WriteLine($"[link-lines:{{{string.Join(",", proc.linkedLines.Select(kvp => $"{kvp.Key}:{kvp.Value}"))}}}]");
                    writer.WriteLine($"[size:{proc.size}]");
                    writer.WriteLine();
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
            
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (var conversion in preprocessorConversions)
                {
                    newLines[i] = conversion.Key.Replace(newLines[i], conversion.Value);
                }

                if (newLines[i].EndsWith("\\"))
                {
                    newLines[i] = newLines[i].TrimEnd().Substring(0, newLines[i].Length - 1);
                    bool cont = false;
                    int forward = i + 1;
                    do
                    {
                        foreach (var conversion in preprocessorConversions)
                        {
                            newLines[forward] = conversion.Key.Replace(newLines[forward], conversion.Value);
                        }

                        cont = newLines[forward].EndsWith("\\");
                        newLines[i] += newLines[forward].Substring(0, newLines[forward].Length - (cont ? 1 : 0)).Trim();
                        newLines[forward] = "";
                        forward++;
                    } while (cont);
                    
                    i = forward - 1;
                }
            }

            return newLines;
        }
        
        static AsemFile Parse(RawFile file)
        {
            Dictionary<string, string> usings = new Dictionary<string, string>();
            Dictionary<string, string> constants = new Dictionary<string, string>();
            Dictionary<string, Proc> procs = new Dictionary<string, Proc>();
            Dictionary<string, List<int>> breakpoints = new Dictionary<string, List<int>>();
            HashSet<string> flags = new HashSet<string>();
            bool export_const = false;

            Proc currProc = new Proc();
            currProc.tokens = new List<Token>();

            int line_num = 0;
            foreach (var it_line in file.processedlines)
            {
                line_num++;

                if (it_line.Trim(new[] { ' ', '\t', '¤' }).Length <= 0)
                {
                    continue;
                }

                bool breakpoint = false;
                if (it_line[0] == '¤')
                {
                    if (breakpoints.ContainsKey(currProc.name) == false)
                    {
                        breakpoints[currProc.name] = new List<int>();
                    }

                    breakpoint = true;

                    // FIXME: We are filtering out all lables and litterals, this means we need to shift the breakpoints in relevant instructions!
                    breakpoints[currProc.name].Add(currProc.tokens.Where(t => t.Type == TokenType.Instruction).Count());
                }

                if (it_line[0] == '!')
                {
                    flags.Add(it_line);
                    if (it_line.Trim().Equals("!global"))
                    {
                        export_const = true;
                    }
                    else if (it_line.Trim().Equals("!private"))
                    {
                        export_const = false;
                    }
                    continue;
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
                    string value = c.Groups[2].Value;

                    Match mauto;
                    if ((mauto = auto.Match(value)).Success)
                    {
                        int size = ToInt(ParseLitteral(file, line_num, mauto.Groups[1].Value, constants));
                        if (size <= 0)
                        {
                            Error(file, line_num, $"Auto const ''{c.Groups[1].Value}' cannot be defined with a length of {size}!");
                        }

                        if (autoVars - size < STACK_SIZE)
                        {
                            Error(file, line_num, $"Auto variable '{c.Groups[1].Value}' cannot be allocated! (Required: {size}, Available: {autoVars - STACK_SIZE}, Diff: {size - (autoVars - STACK_SIZE)})");
                        }

                        autoVars -= size;
                        value = $"0x{(autoVars >> 12) & 0xFFF:X3}_{autoVars & 0xFFF:X3}";

                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($"Defined auto var '{c.Groups[1].Value}' of size {size} to addr {value}");
                        Console.ForegroundColor = conColor;

                        autoConstants[c.Groups[1].Value] = new AutoConst(value, size);
                    }

                    constants[c.Groups[1].Value] = value;
                    if (export_const)
                    {
                        if (value.Equals("extern"))
                        {
                            Error(file, line_num, $"Exporing extern constant '{c.Groups[1].Value}'");
                        }

                        if (globalConstants.ContainsKey(c.Groups[1].Value))
                        {
                            Warning(file, line_num, $"Redefining global constant '{c.Groups[1].Value}'");
                        }
                        globalConstants[c.Groups[1].Value] = value;
                    }
                }
                else if ((c = str.Match(line)).Success)
                {
                    Token string_tok = new Token(line_num, TokenType.Litteral, c.Value.Trim(), breakpoint);

                    currProc.tokens.Add(string_tok);
                }
                else
                {
                    if (char.IsWhiteSpace(line, 0))
                    {
                        string[] SplitNotStrings(string input, char[] chars)
                        {
                            return Regex.Matches(input, @"([\""].*?[\""]|'.')|[^ ]+")
                                .Cast<Match>()
                                .Select(m => m.Value)
                                .ToArray();
                        }

                        string[] tokens = SplitNotStrings(line, new[] { ' ', '\t' }); //line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                        foreach (var token in tokens)
                        {
                            Token t;
                            Match l;
                            if (opcodes.TryGetValue(token, out Opcode opcode))
                            {
                                t = new Token(line_num, TokenType.Instruction, token, breakpoint, opcode);
                            }
                            else if (arguments.TryGetValue(token, out int arg))
                            {
                                t = new Token(line_num, TokenType.Argument, token, breakpoint);
                            }
                            else if ((l = label.Match(token)).Success)
                            {
                                t = new Token(line_num, TokenType.Label, l.Value, breakpoint);
                            }
                            else if (num.IsMatch(token) || constants.ContainsKey(token))
                            {
                                t = new Token(line_num, TokenType.Litteral, token, breakpoint);
                            }
                            else if ((l = chr.Match(token)).Success)
                            {
                                t = new Token(line_num, TokenType.Litteral, l.Value, breakpoint);
                            }
                            else if ((l = str.Match(token)).Success)
                            {
                                string str = l.Value.Trim();
                                string lableName;
                                if (autoStrings.TryGetValue(str, out lableName) == false)
                                {
                                    lableName = $":__str_{autoStringIndex++}__";
                                    autoStringsFile.AppendLine(lableName);
                                    autoStringsFile.Append('\t').AppendLine(str);
                                    autoStrings[str] = lableName;

                                    Console.ForegroundColor = ConsoleColor.Magenta;
                                    Console.WriteLine($"Created inline string '{lableName}' with value {str}");
                                    Console.ForegroundColor = conColor;
                                }

                                t = new Token(line_num, TokenType.Label, lableName, breakpoint);
                            }
                            else
                            {
                                Error(file, line_num, $"Could not parse token: \"{token}\"");
                                t = new Token();
                            }

                            if ((currProc.parameters == null || currProc.locals == null) && t.Type != TokenType.Litteral)
                            {
                                Error(file, line_num, $"Trying to define proc {currProc.name} without specifying parameters and local use!");
                            }
                            else
                            {
                                if (currProc.parameters == null) {
                                    currProc.parameters = ToInt(ParseLitteral(file, line_num, t.Value, constants));
                                }
                                else if (currProc.locals == null)
                                {
                                    currProc.locals = ToInt(ParseLitteral(file, line_num, t.Value, constants));
                                }
                            }

                            currProc.tokens.Add(t);
                        }
                    }
                    else if (line[0] == ':')
                    {
                        Match l = proc.Match(line);
                        if (l.Success)
                        {
                            currProc = new Proc();

                            currProc.line = line_num;

                            currProc.name = l.Groups[1].Value;

                            procs[currProc.name] = currProc;

                            if (currProc.name == ":start")
                            {
                                currProc.parameters = 0;
                                currProc.locals = 0;
                            }

                            if (l.Groups[3].Success)
                            {
                                currProc.location = ToInt(ParseLitteral(file, line_num, l.Groups[3].Value, constants));

                                // Interrupts does not specify parameters and locals!
                                switch (currProc.location)
                                {
                                    case 0xFFF_FF0:
                                    case 0xFFF_FE0:
                                    case 0xFFF_FD0:
                                    case 0xFFF_FC0:
                                        currProc.parameters = 0;
                                        currProc.locals = 0;
                                        break;
                                }

                                if (currProc.location < 0x44B_000)
                                {
                                    Error(file, line_num, $"Procs can only be placed in ROM. The proc {currProc.name} was addressed to {currProc.location}!");
                                }
                            }

                            currProc.tokens = new List<Token>();;
                        }
                        else
                        {
                            Error(file, line_num, $"Invalid label: \"{line}\"");
                        }
                    }
                    else
                    {
                        Error(file, line_num, $"Could not parse line \"{line}\"");
                    }
                }
            }
            
            if(currProc.name != null)
            {
                procs[currProc.name] = currProc;
            }
            else if (currProc.tokens.Count > 0)
            {
                Warning(file, currProc.line, $"File contains code but has no proc lable! (This code will be ignored)");
            }

            return new AsemFile(file, usings, constants, procs, breakpoints, flags);
        }

        static LibFile Assemble(Dictionary<string, AsemFile> files, bool executable, out bool success)
        {
            Dictionary<Proc, List<short>> assembledProcs = new Dictionary<Proc, List<short>>();

            Dictionary<Proc, ProcMetadata> metadata = new Dictionary<Proc, ProcMetadata>();

            int offset = 0;
            
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
                // Resolve all extern consts
                var externs = file.Value.Constants.Where(kvp => kvp.Value.Equals("extern")).Select(kvp => kvp.Key).ToList();

                foreach (var ext in externs)
                {
                    if (globalConstants.TryGetValue(ext, out string value))
                    {
                        file.Value.Constants[ext] = value;
                    }
                    else
                    {
                        Error(file.Value.Raw, 0, $"Could not solve value of extern const '{ext}'");
                    }
                }

                offset = 0;

                bool temp_verbose = verbose;

                verbose = verbose && !file.Value.Flags.Contains("!noprintouts");

                foreach (var proc in file.Value.Procs)
                {
                    if (verbose)
                    {
                        Console.WriteLine($"Assembling proc {proc.Key}");
                        Console.WriteLine();
                    }

                    Dictionary<string, int> local_labels = new Dictionary<string, int>();
                    Dictionary<int, string> local_label_uses = new Dictionary<int, string>();

                    List<short> instructions = new List<short>();

                    ProcMetadata procmeta = new ProcMetadata();
                    procmeta.name = proc.Value.name;
                    procmeta.source = file.Value.Raw;
                    procmeta.assembledSource = file.Value;
                    procmeta.line = proc.Value.line;
                    procmeta.linkedLines = new Dictionary<int, int>();

                    IEnumerator<Token> tokens = proc.Value.tokens.GetEnumerator();
                    
                    if (tokens.MoveNext() == false)
                    {
                        continue;
                    }
                    
                    Token current = tokens.Current;

                    Token peek = tokens.Current;

                    int currentSourceLine = -1;

                    while (tokens.MoveNext() || !peek.Equals(tokens.Current))
                    {
                        peek = tokens.Current;

                        if (current.Line > currentSourceLine && (current.Type != TokenType.Label))
                        {
                            procmeta.linkedLines.Add(instructions.Count + 1, current.Line);
                            currentSourceLine = current.Line;
                        }

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

                                            instructions.Add((short)Opcode.Load_lit_l);
                                            local_label_uses[instructions.Count] = peek.Value;
                                            instructions.Add(0);
                                            instructions.Add(0);
                                        }
                                        else if (peek.Type == TokenType.Litteral)
                                        {
                                            short[] value = ParseLitteral(file.Value.Raw, current.Line, peek.Value, file.Value.Constants);

                                            if (value.Length == 2)
                                            {
                                                ShiftBreakpoints(file.Value, proc.Key, instructions.Count, 2);
                                                instructions.Add((short)Opcode.Load_lit_l);
                                                instructions.Add(value[1]);
                                                instructions.Add(value[0]);
                                            }
                                            else
                                            {
                                                // Shift breakpoints before adding instructions
                                                ShiftBreakpoints(file.Value, proc.Key, instructions.Count, (value.Length * 2) - 1);

                                                foreach (var val in value.Reverse())
                                                {
                                                    instructions.Add((short)current.Opcode);
                                                    instructions.Add(val);
                                                }
                                            }

                                            if (verbose) Console.WriteLine($"Parsed load litteral with litteral {peek.Value}!");
                                        }
                                        else
                                        {
                                            Error(file.Value.Raw, current.Line, $"{current.Opcode} can only be followed by a label or litteral!");
                                        }
                                        tokens.MoveNext();
                                        break;
                                    case Opcode.Load_lit_l:
                                        ShiftBreakpoints(file.Value, proc.Key, instructions.Count, 2);

                                        if (peek.Type == TokenType.Label)
                                        {
                                            instructions.Add((short) current.Opcode);
                                            local_label_uses[instructions.Count] = peek.Value;
                                            instructions.Add(0);
                                            instructions.Add(0);

                                            tokens.MoveNext();
                                        }
                                        else if (peek.Type == TokenType.Litteral)
                                        {
                                            short[] value = ParseLitteral(file.Value.Raw, current.Line, peek.Value, file.Value.Constants);

                                            if (value.Length <= 2)
                                            {
                                                instructions.Add((short)current.Opcode);
                                                instructions.Add(value.Length < 2 ? (short) (value[0] == 0xFFF ? 0xFFF :  0) : value[1]);
                                                instructions.Add(value[0]);
                                            }
                                            else
                                            {
                                                Error(file.Value.Raw, current.Line, $"{current.Opcode} cannot be followe by a constant bigger than two words!");
                                            }

                                            tokens.MoveNext();
                                        }
                                        else
                                        {
                                            Error(file.Value.Raw, current.Line, $"{current.Opcode} must be followed by either a label or litteral!");
                                        }
                                        break;
                                    case Opcode.Jmp:
                                        // Shift breakpoints before adding instructions
                                        ShiftBreakpoints(file.Value, proc.Key, instructions.Count, 2);

                                        instructions.Add((short)current.Opcode);

                                        JumpMode mode = JumpMode.Jmp;
                                        if (peek.Type == TokenType.Argument)
                                        {
                                            mode = (JumpMode) arguments[peek.Value];
                                            if (Enum.IsDefined(typeof(JumpMode), mode))
                                            {
                                                instructions.Add((short)mode);
                                                tokens.MoveNext();
                                                peek = tokens.Current;
                                            }
                                            else
                                            {
                                                Error(file.Value.Raw, current.Line, $"{current.Opcode} must be followed by an argument of type {nameof(JumpMode)}! Got: \"{mode}\"");
                                            }
                                        }
                                        else
                                        {
                                            Error(file.Value.Raw, current.Line, $"{current.Opcode} must be followed by a {nameof(JumpMode)}!");
                                        }

                                        if (mode == JumpMode.Ro || mode == JumpMode.Ro_l)
                                        {

                                        }
                                        else if (peek.Type == TokenType.Label)
                                        {
                                            local_label_uses[instructions.Count] = peek.Value;
                                            Console.ForegroundColor = ConsoleColor.DarkGreen;
                                            if (verbose) Console.WriteLine($"Added label {peek.Value} using at {instructions.Count:X}");
                                            Console.ForegroundColor = conColor;
                                            instructions.Add(0);
                                            instructions.Add(0);
                                            tokens.MoveNext();
                                        }
                                        else if (peek.Type == TokenType.Litteral)
                                        {
                                            short[] value = ParseLitteral(file.Value.Raw, current.Line, peek.Value, file.Value.Constants);
                                            if (value.Length > 2)
                                            {
                                                Error(file.Value.Raw, current.Line, $"The litteral {peek.Value} does not fit in 24-bits! {current.Opcode} only takes 24-bit arguments!");
                                            }
                                            instructions.Add(value[1]);
                                            instructions.Add(value[0]);
                                            tokens.MoveNext();
                                        }
                                        else
                                        {
                                            Error(file.Value.Raw, current.Line, $"{current.Opcode} must be followed by a label or litteral!");
                                        }
                                        break;
                                    case Opcode.Call:
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
                                                if (verbose) Console.WriteLine($"Added label {peek.Value} using at {instructions.Count:X}");
                                                Console.ForegroundColor = conColor;
                                            }
                                            else if (peek.Type == TokenType.Litteral)
                                            {
                                                short[] value = ParseLitteral(file.Value.Raw, current.Line, peek.Value, file.Value.Constants);

                                                if (value.Length > 2)
                                                {
                                                    Error(file.Value.Raw, current.Line, $"The litteral {peek.Value} does not fit in 24-bits! {current.Opcode} only takes 24-bit arguments!");
                                                }

                                                instructions.Add(value.Length < 2 ? (short) 0 : value[1]);
                                                instructions.Add(value[0]);
                                            }

                                            tokens.MoveNext();
                                        }
                                        else
                                        {
                                            Error(file.Value.Raw, current.Line, $"{current.Opcode} instruction without any following litteral or label!");
                                        }

                                        /*
                                        if (peek.Type == TokenType.Label)
                                        {
                                            ShiftBreakpoints(file.Value, proc.Key, instructions.Count, 2);

                                            instructions.Add((short) current.Opcode);

                                            local_label_uses[instructions.Count] = peek.Value;
                                            Console.ForegroundColor = ConsoleColor.DarkGreen;
                                            if (verbose) Console.WriteLine($"Added label {peek.Value} using at {instructions.Count:X}");
                                            Console.ForegroundColor = conColor;

                                            instructions.Add(0);
                                            instructions.Add(0);
                                        }
                                        else
                                        {
                                            Error(file.Value.Raw, current.Line, $"{current.Opcode} must be followed by a lable!");
                                        }
                                        */
                                        break;
                                    case Opcode.Load_local:
                                    case Opcode.Load_local_l:
                                    case Opcode.Store_local:
                                    case Opcode.Store_local_l:
                                    case Opcode.Ret_v:
                                    case Opcode.Inc_local:
                                    case Opcode.Inc_local_l:
                                    case Opcode.Dec_local:
                                    case Opcode.Dec_local_l:
                                        if (peek.Type == TokenType.Litteral)
                                        {
                                            short[] value = ParseLitteral(file.Value.Raw, current.Line, peek.Value, file.Value.Constants);
                                            if (value.Length > 1)
                                            {
                                                Error(file.Value.Raw, current.Line, $"{current.Opcode} only takes a single word argument!");
                                            }

                                            ShiftBreakpoints(file.Value, proc.Key, instructions.Count, 1);

                                            instructions.Add((short)current.Opcode);
                                            instructions.Add(value[0]);

                                            tokens.MoveNext();
                                        }
                                        else
                                        {
                                            Error(file.Value.Raw, current.Line, $"{current.Opcode} must be followed by a littera!");
                                        }
                                        break;
                                    case Opcode.Blit:
                                    case Opcode.Blit_mask:
                                        // Shift breakpoints before adding instructions
                                        ShiftBreakpoints(file.Value, proc.Key, instructions.Count, 2);

                                        instructions.Add((short)current.Opcode);

                                        if (peek.Type == TokenType.Argument)
                                        {
                                            BlitMode blt_mode = (BlitMode)arguments[peek.Value];
                                            // TODO: Figure out 3ary boolean functions
                                            if (Enum.IsDefined(typeof(BlitMode), blt_mode))
                                            {
                                                instructions.Add((short)blt_mode);
                                                tokens.MoveNext();
                                                peek = tokens.Current;
                                            }
                                            else
                                            {
                                                Error(file.Value.Raw, current.Line, $"{current.Opcode} must be followed by an argument of type {nameof(BlitMode)}! Got: \"{blt_mode}\"");
                                            }
                                        }
                                        else
                                        {
                                            Error(file.Value.Raw, current.Line, $"{current.Opcode} must be followed by a {nameof(BlitMode)}!");
                                        }
                                        break;
                                    default:
                                        instructions.Add((short) (current.Opcode ?? Opcode.Nop));
                                        break;
                                }
                                break;
                            case TokenType.Litteral:
                                if (verbose) Console.WriteLine($"Litteral {current.Value}");

                                short[] values = ParseLitteral(file.Value.Raw, current.Line, current.Value, file.Value.Constants);
                                
                                ShiftBreakpoints(file.Value, proc.Key, instructions.Count, values.Length);

                                for (int i = values.Length - 1; i >= 0; i--)
                                {
                                    instructions.Add(values[i]);
                                }
                                break;
                            case TokenType.Label:
                                local_labels[current.Value] = instructions.Count;

                                Console.ForegroundColor = ConsoleColor.DarkCyan;
                                if (verbose) Console.WriteLine($"Found label def {current.Value} at index: {instructions.Count:X}");
                                Console.ForegroundColor = conColor;
                                break;
                            case TokenType.Argument:
                                Error(file.Value.Raw, current.Line, $"Unhandled argument: \"{current.Value}\"!");
                                break;
                        }
                        
                        current = tokens.Current;
                    }

                    offset = instructions.Count;
                    
                    proc_label_instructions[proc.Key] = local_labels;

                    proc_label_uses[proc.Key] = local_label_uses;

                    assembledProcs[proc.Value] = instructions;

                    procmeta.size = instructions.Count;
                    
                    metadata[proc.Value] = procmeta;

                    if (verbose) Console.WriteLine("----------------------");
                }

                verbose = temp_verbose;
            }

            offset = 0;

            if (verbose) Console.WriteLine();
            
            foreach (var asem in assembledProcs)
            {
                if (asem.Key.location != null)
                {
                    int location = asem.Key.location ?? ROM_OFFSET;
                    if (verbose) Console.WriteLine($"Proc {asem.Key.name} at specified offset: {location:X}");

                    // Shift location to be relative to ROM_START
                    location -= ROM_OFFSET;

                    metadata[asem.Key].location = location;
                    // FIXME: Procs can overlap!!!
                }
                else
                {
                    if (verbose) Console.WriteLine($"Proc {asem.Key.name} at offset: {offset:X}");

                    metadata[asem.Key].location = offset;
                    offset += asem.Value.Count;
                }
            }

            var metadataArray = metadata.Values.ToArray();

            for (int i = 0; i < metadataArray.Length - 1; i++)
            {
                for (int j = i + 1; j < metadataArray.Length; j++)
                {
                    var meta1 = metadataArray[i];
                    var meta2 = metadataArray[j];

                    if (meta1.location <= meta2.location + (meta2.size - 1) && meta2.location <= meta1.location + (meta1.size - 1))
                    {
                        Warning(meta1.source, meta1.line, $"The procs {meta1.name} and {meta2.name} overlap!");
                    }
                }
            }

            if (verbose) Console.WriteLine();

            foreach (var proc in assembledProcs)
            {
                foreach (var use in proc_label_uses[proc.Key.name])
                {
                    if (proc_label_instructions[proc.Key.name].TryGetValue(use.Value, out int lbl_offset))
                    {
                        short[] offset_inst = IntToShortArray(lbl_offset + metadata[proc.Key].location + (executable ? ROM_OFFSET : 0));
                        proc.Value[use.Key] = offset_inst[1];
                        proc.Value[use.Key + 1] = offset_inst[0];
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        if (verbose) Console.WriteLine($"{use.Value,-12} matched local at instruction: {metadata[proc.Key].location + use.Key:X6} Offset: {lbl_offset + metadata[proc.Key].location:X6}");
                        Console.ForegroundColor = conColor;
                    }
                    else if (metadata.ToDictionary(kvp => kvp.Key.name, kvp => kvp.Value.location).TryGetValue(use.Value, out int proc_offset))
                    {
                        short[] offset_inst = IntToShortArray(proc_offset + (executable ? ROM_OFFSET : 0));
                        proc.Value[use.Key] = offset_inst[1];
                        proc.Value[use.Key + 1] = offset_inst[0];
                        Console.ForegroundColor = ConsoleColor.DarkMagenta;
                        if (verbose) Console.WriteLine($"{use.Value,-12} matched call at instruction: {metadata[proc.Key].location + use.Key:X6} Offset: {proc_offset:X6}");
                        Console.ForegroundColor = conColor;
                    }
                    else
                    {
                        Error(metadata[proc.Key].source, metadata[proc.Key].assembledSource.Procs[proc.Key.name].tokens.Find(t => t.Value == use.Value).Line, $"Could not solve label! {use.Value} in proc {proc.Key.name}");
                    }
                }

                // We do this here because we are shifting the breaskpoints. If we dont shift breakpoints then we could do this much earlier
                if (metadata[proc.Key].assembledSource.Breakpoints.TryGetValue(proc.Key.name, out List<int> breaks))
                {
                    metadata[proc.Key].breaks = breaks;
                }
            }

            if (verbose) Console.WriteLine();
            
            short[] compiledInstructions = new short[12275712];

            int usedInstructions = 0;

            foreach (var proc in assembledProcs)
            {
                usedInstructions += proc.Value.Count;

                proc.Value.CopyTo(compiledInstructions, metadata[proc.Key].location);
            }
            
            success = true;

            return new LibFile(compiledInstructions, metadata.Values.ToArray(), usedInstructions);
        }

        static short[] ParseLitteral(RawFile file, int line, string litteral, Dictionary<string, string> constants)
        {
            string val_str;
            short[] value = new short[0];
            if (num.IsMatch(litteral))
            {
                value = ParseNumber(file, line, litteral);
            }
            else if (chr.Match(litteral).Success)
            {
                value = ParseString(file, line, litteral, true);
            }
            else if (str.Match(litteral).Success)
            {
                // TODO: Have a "raw" flag!
                value = ParseString(file, line, litteral, false);
            }
            else if (constants.TryGetValue(litteral, out val_str))
            {
                value = ParseNumber(file, line, val_str);
            }
            else if (globalConstants.TryGetValue(litteral, out val_str))
            {
                value = ParseNumber(file, line, val_str);
            }

            return value;
        }

        static short[] ParseNumber(RawFile file, int line, string litteral)
        {
            litteral = litteral.Replace("_", "");

            short[] ret = null;

            try
            {
                if (litteral.StartsWith("0x"))
                {
                    litteral = litteral.Substring(2);
                    if (litteral.Length % 3 != 0)
                    {
                        litteral = new string('0', 3 - (litteral.Length % 3)) + litteral;
                    }
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
                    if (litteral.Length % 4 != 0)
                    {
                        litteral = new string('0', 4 - (litteral.Length % 4)) + litteral;
                    }
                    ret = Enumerable.Range(0, litteral.Length)
                        .GroupBy(x => x / 4)
                        .Select(g => g.Select(i => litteral[i]))
                        .Select(s => String.Concat(s))
                        .Select(s => Convert.ToInt16(s, 8))
                        .Reverse()
                        .ToArray();
                }
                else if (litteral.StartsWith("0b"))
                {
                    litteral = litteral.Substring(2);
                    if (litteral.Length % 12 != 0)
                    {
                        litteral = new string('0', 12 - (litteral.Length % 12)) + litteral;
                    }
                    ret = Enumerable.Range(0, litteral.Length)
                        .GroupBy(x => x / 12)
                        .Select(g => g.Select(i => litteral[i]))
                        .Select(s => String.Concat(s))
                        .Select(s => Convert.ToInt16(s, 2))
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
            }
            catch (Exception e)
            {
                Error(file, line, $"Error when parsing number '{litteral}': '{e}'");
#if DEBUG
                throw e;
#endif
            }
            
            return ret;
        }

        static short[] ParseString(RawFile file, int line, string litteral, bool raw)
        {
            // This might return weird things for weird strings. But this compiler isn't made to be robust
            short[] data = null;
            try
            {
                data = Array.ConvertAll(Encoding.ASCII.GetBytes(litteral.Substring(1, litteral.Length - 2)), b => (short)b);
                Array.Reverse(data);
                if (!raw)
                {
                    int str_length = data.Length;
                    Array.Resize(ref data, data.Length + 2);
                    data[data.Length - 2] = (short)(str_length & 0xFFF);
                    data[data.Length - 1] = (short)((str_length << 12) & 0xFFF);
                }
            }
            catch (Exception e)
            {
                Error(file, line, $"Error when parsing string '{litteral}': '{e.Message}'");
#if DEBUG
                throw e;
#endif
            }

            return data;
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

        static void Warning(RawFile file, int line, string warning)
        {
            ConsoleColor orig = Console.ForegroundColor;

            Console.ForegroundColor = ConsoleColor.Yellow;

            FileInfo info = new FileInfo(file.path);

            Console.WriteLine($"Warning in file \"{info.Name}\" at line {line}: '{warning}'");

            warnings++;

            Console.ForegroundColor = orig;
        }
        
        static void Error(RawFile file, int line, string error)
        {
            Console.ForegroundColor = ConsoleColor.Red;

            string message;

            if (File.Exists(file.path))
            {
                FileInfo info = new FileInfo(file.path);

                message = $"Error in file \"{info.Name}\" at line {line}: '{error}'";

            }
            else
            {
                message = $"Error in generated file \"{file.path}\" at line {line}: '{error}'";
            }

            Console.WriteLine(message);

#if DEBUG
            Debugger.Break();
            Console.ForegroundColor = conColor;
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
#endif

            Environment.Exit(1);
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