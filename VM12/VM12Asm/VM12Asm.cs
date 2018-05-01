using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Diagnostics;
using VM12_Opcode;
using SKON;

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
        
        class Macro
        {
            public string name;
            public string[] lines;
            public string[] args;
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

        class Constant
        {
            public string name;
            public RawFile file;
            public int line;
            public string value;

            public Constant(string name, RawFile file, int line, string value)
            {
                this.name = name;
                this.file = file;
                this.line = line;
                this.value = value;
            }
        }

        class Proc
        {
            public string name;
            public int line;
            public int? parameters;
            public int? locals;
            public string location_const;
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
            public readonly Dictionary<string, Constant> Constants;
            public readonly Dictionary<string, Proc> Procs;
            public readonly Dictionary<string, List<int>> Breakpoints;
            public readonly HashSet<string> Flags;

            public AsemFile(RawFile raw, Dictionary<string, string> usigns, Dictionary<string, Constant> constants, Dictionary<string, Proc> procs, Dictionary<string, List<int>> breakpoints, HashSet<string> flags)
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
            public readonly string Name;
            public readonly string Value;
            public readonly int Length;

            public AutoConst(string name, string value, int length)
            {
                this.Name = name;
                this.Value = value;
                this.Length = length;
            }
        }
        
        const int _12BIT_MASK = 0x0FFF;

        const int ROM_OFFSET = 0xA4B_000;

        const int ROM_SIZE = 5_984_256;

        const short ROM_OFFSET_UPPER_BITS = 0xA4B;

        const int VRAM_OFFSET = 0xA00_000;

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

            { new Regex("(?<!:)\\bloadlo\\s+#(\\S+)\\s*,\\s*#(\\S+)"), "load.lit.l $1 load.lit.l $2 ladd" },
            { new Regex("(?<!:)\\bloadlo\\s+#(\\S+)\\s*,\\s*(\\S+)"), "load.lit.l $1 load.local.l $2 ladd" },
            { new Regex("(?<!:)\\bloadlo\\s+(\\S+)\\s*,\\s*#(\\S+)"), "load.local.l $1 load.lit.l $2 ladd" },
            { new Regex("(?<!:)\\bloadlo\\s+(\\S+)\\s*,\\s*(\\S+)"), "load.local.l $1 load.local.l $2 ladd" },

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
            { new Regex("\\[PT\\]"), "pt" },
            { new Regex("\\[SP\\]"), "sp" },
        };

        static Regex using_statement = new Regex("&\\s*([A-Za-z][A-Za-z0-9_]*)\\s+(.*\\.12asm)");

        static Regex constant = new Regex("<([A-Za-z_][A-Za-z0-9_]*)\\s*=\\s*(.*)>");

        static Regex label = new Regex(":[A-Za-z_][A-Za-z0-9_]*");

        static Regex proc = new Regex("(:[A-Za-z_][A-Za-z0-9_]*)(\\s+@(.*))?");

        static Regex num = new Regex("(?<!\\S)(0x[0-9A-Fa-f_]+|8x[0-7_]+|0b[0-1_]+|-?[0-9_]+)(?!\\S)");

        static Regex chr = new Regex("'(.)'");

        static Regex str = new Regex("^\\s*\"[^\"\\\\]*(\\\\.[^\"\\\\]*)*\"$");

        static Regex auto = new Regex("^auto\\((.*)\\)$");

        static Regex const_expr = new Regex("^(extern(?:\\((#.+)\\))?|#\\((.*)\\)|sizeof(?:\\((#.+)\\))?|endof(?:\\((#.+)\\))?)$");

        static Regex extern_expr = new Regex("^extern(?:\\(#(.+)\\))?$");

        static Regex sizeof_expr = new Regex("^sizeof\\(#(.+)\\)$");

        static Regex endof_expr = new Regex("^endof\\(#(.+)\\)$");

        static Regex arith_expr = new Regex("^#\\((.*)\\)$");

        static Dictionary<string, Opcode> opcodes = new Dictionary<string, Opcode>()
        {
            { "nop", Opcode.Nop },
            { "pop", Opcode.Pop },
            { "fp", Opcode.Fp },
            { "pc", Opcode.Pc },
            { "pt", Opcode.Pt },
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
            { "write", Opcode.Write },
            { "read", Opcode.Read },
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

        static bool verbose_addr = false;

        static bool verbose_lit = false;

        static bool verbose_token = false;

        static bool dump_mem = false;
        
        static int pplines = 0;
        static int lines = 0;
        static int tokens = 0;

        static Dictionary<string, Constant> globalConstants = new Dictionary<string, Constant>();

        static Dictionary<string, AutoConst> autoConstants = new Dictionary<string, AutoConst>();

        struct WarningEntry
        {
            public readonly RawFile File;
            public readonly int Line;
            public readonly string WarningText;

            private FileInfo FileInfo;

            public WarningEntry(RawFile file, int line, string warning)
            {
                this.File = file;
                this.Line = line;
                this.WarningText = warning;

                FileInfo = new FileInfo(file.path);
            }

            public override string ToString()
            {
                return $"Warning in file \"{FileInfo.Name}\" at line {Line}: '{WarningText}'";
            }
        }

        static List<WarningEntry> Warnings = new List<WarningEntry>();
        
        static int autoStringIndex = 0;

        static Dictionary<string, string> autoStrings = new Dictionary<string, string>();

        static StringBuilder autoStringsFile = new StringBuilder(1000);

        static int autoVars = VRAM_OFFSET - 1;

        const int STACK_SIZE = 0x100_000;

        public static void Reset()
        {
            verbose = false;
            verbose_addr = false;
            verbose_lit = false;
            verbose_token = false;

            dump_mem = false;
            
            lines = 0;

            globalConstants = new Dictionary<string, Constant>();

            autoConstants = new Dictionary<string, AutoConst>();

            Warnings.Clear();
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
                    case "-vt":
                        verbose_token = true;
                        break;
                    case "-vv":
                        verbose_addr = true;
                        break;
                    case "-vl":
                        verbose_lit = true;
                        break;
                    case "-d":
                        dump_mem = true;
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

            autoStringsFile.AppendLine("!noprintouts");
            autoStringsFile.AppendLine("!global");
            autoStringsFile.AppendLine();

            Console.WriteLine($"Parsing...");
            
            #region Usings

            Dictionary<string, AsemFile> files = new Dictionary<string, AsemFile>();

            Stack<string> remainingUsings = new Stack<string>();

            remainingUsings.Push(Path.GetFileName(file));

            FileInfo fileInf = new FileInfo(file);
            DirectoryInfo dirInf = fileInf.Directory;
            
            FileInfo[] dirFiles = dirInf.GetFiles($"*.12asm", SearchOption.AllDirectories);
            
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
                rawFile.processedlines = PreProcess(rawFile.rawlines, fi.FullName);
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

            #region AutoStrings

            RawFile rawAutoStrings = new RawFile();
            rawAutoStrings.path = "AutoStrings";
            rawAutoStrings.rawlines = autoStringsFile.ToString().Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            rawAutoStrings.processedlines = rawAutoStrings.rawlines;

            lines -= rawAutoStrings.rawlines.Length;

            watch.Restart();
            AsemFile autoStringAsem = Parse(rawAutoStrings);
            watch.Stop();
            parseTime += watch.ElapsedTicks;

            files["AutoStrings.12asm"] = autoStringAsem;

            if (generateStringSource)
            {
                File.WriteAllText(Path.Combine(dirInf.FullName, "AutoStrings.12asm"), autoStringsFile.ToString());
            }

            #endregion

            if (verbose)
            {
                foreach (var f in files)
                {
                    Console.WriteLine(f);
                }
            }
            
            #endregion

            if (verbose && verbose_token)
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

                Console.WriteLine();
                Console.ForegroundColor = conColor;
            }
            else
            {
                Console.WriteLine($"Result ({libFile.UsedInstructions} used words ({((double)libFile.UsedInstructions / ROM_SIZE):P5}))");
            }

            Console.WriteLine($"Allocated {VRAM_OFFSET - 1 - autoVars} ({(((double)VRAM_OFFSET - 1 - autoVars) / (VRAM_OFFSET - 1 - STACK_SIZE)):P5}) words to auto() vars {autoVars - STACK_SIZE} words remaining");

            total.Stop();

            double preprocess_ms = ((double)preprocessTime / Stopwatch.Frequency) * 100;
            double parse_ms = ((double)parseTime / Stopwatch.Frequency) * 100;
            double assembly_ms = ((double)assemblyTime / Stopwatch.Frequency) * 100;
            double total_ms_sum = preprocess_ms + parse_ms + assembly_ms;
            double total_ms = ((double)total.ElapsedTicks / Stopwatch.Frequency) * 100;

            string warningString = $"Assembled with {Warnings.Count} warning{(Warnings.Count > 0 ? "" : "s")}.";
            Console.WriteLine($"Success! {warningString}");
            Console.WriteLine($"Preprocess: {preprocess_ms:F4} ms {pplines} lines");
            Console.WriteLine($"Parse: {parse_ms:F4} ms {lines} lines");
            Console.WriteLine($"Assembly: {assembly_ms:F4} ms {tokens} tokens");
            Console.WriteLine($"Sum: {total_ms_sum:F4} ms");
            Console.WriteLine($"Total: {total_ms:F4} ms");

            Console.WriteLine();
            Console.WriteLine($"Done! {warningString}");
            foreach (var warning in Warnings)
            {
                Console.WriteLine(warning);
            }

            // FIXME!! Do not write the compiled file to the same directory!

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
                    for (int pos = 0; pos < libFile.Instructions.Length; )
                    {
                        int skipped = 0;
                        while (pos < libFile.Instructions.Length && libFile.Instructions[pos] == 0)
                        {
                            pos++;
                            skipped++;
                        }

                        if (skipped > 0)
                        {
                            //Console.WriteLine($"Skipped {skipped} instructions");
                        }

                        int length = 0;
                        int zeroes = 0;

                        while (pos + length < libFile.Instructions.Length)
                        {
                            length++;
                            if (libFile.Instructions[pos + length] == 0)
                            {
                                zeroes++;
                                if (zeroes >= 3)
                                {
                                    length -= 2;
                                    break;
                                }
                            }
                            else
                            {
                                zeroes = 0;
                            }
                        }

                        if (length > 0)
                        {
                            // Write block

                            Console.WriteLine($"Writing block at pos {pos} and length {length} with fist value {libFile.Instructions[pos]} and last value {libFile.Instructions[pos + length - 1]}");

                            bw.Write(pos);
                            bw.Write(length);

                            for (int i = 0; i < length; i++)
                            {
                                bw.Write(libFile.Instructions[pos + i]);
                            }

                            pos += length;
                        }
                    }

                    /*
                    foreach (short s in libFile.Instructions)
                    {
                        bw.Write(s);
                    }
                    */
                }
            }

            FileInfo metaFile = new FileInfo(Path.Combine(dirInf.FullName, name + ".12meta"));
            FileInfo metaSKONFile = new FileInfo(Path.Combine(dirInf.FullName, name + ".skon"));

            metaFile.Delete();

            SKONObject skonObject = SKONObject.GetEmptyMap();
            
            using (FileStream stream = metaFile.Create())
            using (StreamWriter writer = new StreamWriter(stream))
            {
                SKONObject constants = SKONObject.GetEmptyArray();

                foreach (var constant in autoConstants)
                {
                    writer.WriteLine($"[constant:{{{constant.Key},{constant.Value.Length},{constant.Value.Value}}}]");

                    constants.Add(new Dictionary<string, SKONObject> {
                        { "name", constant.Key },
                        { "value", constant.Value.Value },
                        { "length", constant.Value.Length },
                    });
                }

                skonObject.Add("constants", constants);

                writer.WriteLine();

                SKONObject procs = SKONObject.GetEmptyArray();

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

                    procs.Add(new Dictionary<string, SKONObject> {
                        { "name", proc.name },
                        { "file", new FileInfo(proc.source.path).Name },
                        { "location", proc.location },
                        { "proc-line", proc.line },
                        { "break", proc.breaks },
                        { "link-lines", proc.linkedLines.Select(kvp => (SKONObject) $"{kvp.Key}:{kvp.Value}").ToList() },
                        { "size", proc.size }
                    });

                    skonObject.Add("procs", procs);
                }

                SKON.SKON.WriteToFile(metaSKONFile.FullName, skonObject);
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

        static List<Macro> globalMacros = new List<Macro>();

        static string[] PreProcess(string[] lines, string fileName)
        {
            pplines += lines.Length;

            RawFile file = new RawFile() { path = fileName, rawlines = lines };

            Regex macroDefLoose = new Regex("#def (.*?)\\(.*\\)");
            Regex macroDefStrict = new Regex("#def\\s[A-Za-z_][A-Za-z0-9_]*\\(((?:\\s*[A-Za-z_][A-Za-z0-9_]*,?)*)\\)");

            Regex macroDefEnd = new Regex("#end (.*?)");

            Regex macroUse = new Regex("^\\s*([A-Za-z0-9_]*?)\\(((?:\\s*.*?,?)*)\\)");

            List<Macro> macros = new List<Macro>();

            List<string> newLines = new List<string>(lines);

            int removedLines = 0;

            bool global = false;

            // Go through all lines and parse and replace macros
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] == "!global")
                {
                    global = true;
                    continue;
                }
                else if (lines[i] == "!private")
                {
                    global = false;
                    continue;
                }

                string line = Regex.Replace(lines[i], ";.*", "");
                
                var match = macroDefLoose.Match(line);
                if (match.Success)
                {
                    var strictMatch = macroDefStrict.Match(line);
                    if (strictMatch.Success)
                    {
                        int start = i;
                        int offset = 0;
                        while (!macroDefEnd.IsMatch(lines[i + offset]))
                        {
                            offset++;
                        }

                        offset++;

                        newLines.RemoveRange(i - removedLines, offset);

                        removedLines += offset;

                        Macro macro = new Macro();

                        macro.name = match.Groups[1].Value;

                        macro.lines = new string[offset - 2];
                        for (int lineNum = 0; lineNum < offset - 2; lineNum++)
                        {
                            macro.lines[lineNum] = lines[i + 1 + lineNum];
                        }

                        macro.args = strictMatch.Groups[1].Value.Split(',').Select(s => s.Trim()).ToArray();

                        if (global)
                        {
                            globalMacros.Add(macro);
                        }
                        else
                        {
                            macros.Add(macro);
                        }

                        if (verbose) Console.WriteLine($"Defined {(global?"global ":"")}macro '{macro.name}'");
                        //Console.WriteLine($"Start {i}, End {i + offset}, Name {match.Groups[1]}");
                        //Console.WriteLine(string.Join("\n", macro.lines));
                    }
                    else
                    {
                        Error(file, i, $"Syntax error for Macrodef {match.Groups[1]}");
                    }
                }
                else
                {
                    // Check if line maches macro
                    var useMatch = macroUse.Match(line);
                    if (useMatch.Success)
                    {
                        //Console.WriteLine($"Found macro use! Name '{useMatch.Groups[1]}' args '{useMatch.Groups[2]}'");

                        string useName = useMatch.Groups[1].Value;
                        string[] args = useMatch.Groups[2].Value.Split(',');

                        // Find a macro with the same name and numer of arguments
                        Macro macro = macros.Concat(globalMacros).FirstOrDefault(m => (m.name == useName) && (m.args.Length == args.Length));

                        if (macro == null)
                        {
                            Error(file, i, $"Unknown macro '{useName}'");
                        }
                        else
                        {
                            if (verbose) Console.WriteLine($"Found macrodef '{macro.name}'");
                            newLines.RemoveAt(i - removedLines);
                            List<string> macroLines = new List<string>(macro.lines);
                            for (int lineNum = 0; lineNum < macroLines.Count; lineNum++)
                            {
                                for (int arg = 0; arg < macro.args.Length; arg++)
                                {
                                    if (macroLines[lineNum].Contains(macro.args[arg]))
                                    {
                                        if (verbose) Console.WriteLine($"Line '{macroLines[lineNum]}' Arg '{macro.args[arg]}' Value '{args[arg]}'");
                                        macroLines[lineNum] = macroLines[lineNum].Replace(macro.args[arg], args[arg]);
                                    }
                                }
                            }
                            newLines.InsertRange(i - removedLines, macroLines);
                            removedLines -= macroLines.Count - 1;
                        }
                    }
                }
            }
            
            for (int i = 0; i < newLines.Count; i++)
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
            
            return newLines.ToArray();
        }
        
        static AsemFile Parse(RawFile file)
        {
            Dictionary<string, string> usings = new Dictionary<string, string>();
            Dictionary<string, Constant> constants = new Dictionary<string, Constant>();
            Dictionary<string, Proc> procs = new Dictionary<string, Proc>();
            Dictionary<string, List<int>> breakpoints = new Dictionary<string, List<int>>();
            HashSet<string> flags = new HashSet<string>();
            bool export_const = false;

            Proc currProc = new Proc();
            currProc.tokens = new List<Token>();

            int line_num = 0;
            foreach (var it_line in file.processedlines)
            {
                lines++;
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

                    Match mauto = auto.Match(value);
                    bool isAuto = mauto.Success;
                    
                    Constant constant = new Constant(c.Groups[1].Value, file, line_num, value);
                    constants[c.Groups[1].Value] = constant;

                    if (isAuto)
                    {
                        constants[c.Groups[1].Value + ".size"] = new Constant(c.Groups[1].Value + ".size", file, line_num, $"sizeof(#{c.Groups[1].Value})");
                        constants[c.Groups[1].Value + ".end"] = new Constant(c.Groups[1].Value + ".end", file, line_num, $"endof(#{c.Groups[1].Value})");
                    }

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

                        globalConstants[c.Groups[1].Value] = constant;

                        if (isAuto)
                        {
                            globalConstants[c.Groups[1].Value + ".size"] = new Constant(c.Groups[1].Value + ".size", file, line_num, $"sizeof(#{c.Groups[1].Value})");
                            globalConstants[c.Groups[1].Value + ".end"] = new Constant(c.Groups[1].Value + ".end", file, line_num, $"endof(#{c.Groups[1].Value})");

                            if (verbose) Console.WriteLine($"Adding .size and .end to auto var '{c.Groups[1].Value}'");
                        }
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
                                    if (verbose) Console.WriteLine($"Created inline string '{lableName}' with value {str}");
                                    Console.ForegroundColor = conColor;
                                }

                                t = new Token(line_num, TokenType.Label, lableName, breakpoint);
                            }
                            else
                            {
                                Error(file, line_num, $"Could not parse token: \"{token}\"");
                                t = new Token();
                            }
                            
                            // TODO: Remove
                            /*
                            if ((currProc.parameters == null || currProc.locals == null) && t.Type != TokenType.Litteral && currProc.location_const != null)
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
                            */

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
                            
                            if (l.Groups[3].Success)
                            {
                                currProc.location_const = l.Groups[3].Value; // ToInt(ParseLitteral(file, line_num, l.Groups[3].Value, constants));
                            }

                            currProc.tokens = new List<Token>();
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

            Dictionary<string, Dictionary<string, Constant>> fileConstants = files.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Constants);

            foreach (var file in files)
            {
                Console.WriteLine($"Evaluating {file.Value.Constants.Count} const expressions in file '{file.Key}'");

                foreach (var eval_expr in file.Value.Constants.ToList()/*.Where(kvp => const_expr.IsMatch(kvp.Value.value))*/)
                {
                    string result = EvalConstant(eval_expr.Value, file.Value.Constants, fileConstants);

                    if (eval_expr.Value.value != result)
                    {
                        Console.WriteLine($"Evaluated constant '{eval_expr.Value.value}' to constant '{result}' for constant '{eval_expr.Key}'");
                    }

                    file.Value.Constants[eval_expr.Key].value = result;
                }

                /*
                // Resolve all extern consts
                var externs = file.Value.Constants.Where(kvp => kvp.Value.Equals("extern")).Select(kvp => kvp.Key).ToList();

                foreach (var ext in externs)
                {
                    if (globalConstants.TryGetValue(ext, out Constant value))
                    {
                        file.Value.Constants[ext] = value;
                    }
                    else
                    {
                        Error(file.Value.Raw, 0, $"Could not solve value of extern const '{ext}'");
                    }
                }
                */

                offset = 0;

                bool temp_verbose = verbose;

                verbose = verbose && !file.Value.Flags.Contains("!noprintouts");

                verbose &= verbose_lit;

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

                    // Check that procs that should have params and locals that they have them

                    bool shouldHaveParamAndLocals = true;

                    if (proc.Value.tokens.Count <= 2)
                    {
                        //Warning(file.Value.Raw, proc.Value.line, $"Small proc '{proc.Value.name}'");
                        shouldHaveParamAndLocals = false;
                    }

                    if (proc.Value.name == ":start")
                    {
                        proc.Value.parameters = 0;
                        proc.Value.locals = 0;
                        shouldHaveParamAndLocals = false;
                    }

                    if (proc.Value.location_const != null)
                    {
                        int location = ToInt(ParseLitteral(file.Value.Raw, proc.Value.line, proc.Value.location_const, file.Value.Constants));

                        // Interrupts does not specify parameters and locals!
                        switch (location)
                        {
                            case 0xFFF_FF0:
                            case 0xFFF_FE0:
                            case 0xFFF_FD0:
                            case 0xFFF_FC0:
                                proc.Value.parameters = 0;
                                proc.Value.locals = 0;
                                shouldHaveParamAndLocals = false;
                                break;
                        }

                        if (location < 0x44B_000)
                        {
                            Error(file.Value.Raw, proc.Value.line, $"Procs can only be placed in ROM. The proc {proc.Value.name} was addressed to {proc.Value.location_const}!");
                        }

                        proc.Value.location = location;
                    }

                    if (shouldHaveParamAndLocals)
                    {
                        // Find the two first tokens in the proc
                        Token parameters = proc.Value.tokens[0];
                        Token locals = proc.Value.tokens[1];
                        if (parameters.Type != TokenType.Litteral && locals.Type != TokenType.Litteral)
                        {
                            Warning(file.Value.Raw, proc.Value.line, $"Defining proc {proc.Value.name} without specifying parameters and local use!");
                        }
                        else
                        {
                            proc.Value.parameters = ToInt(ParseLitteral(file.Value.Raw, proc.Value.line, parameters.Value, file.Value.Constants));
                            proc.Value.locals = ToInt(ParseLitteral(file.Value.Raw, proc.Value.line, locals.Value, file.Value.Constants));
                        }
                        

                        if (proc.Value.parameters == null || proc.Value.locals == null)
                        {
                            Error(file.Value.Raw, proc.Value.line, $"Trying to define proc {proc.Value.name} without specifying parameters and local use!");
                        }
                    }

                    VM12Asm.tokens += proc.Value.tokens.Count;

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
                                            short[] value = ParseLitteral(file.Value.Raw, current.Line, EvalConstant(file.Value.Raw, current.Line, peek.Value, file.Value.Constants, fileConstants), file.Value.Constants);

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
                                            short[] value = ParseLitteral(file.Value.Raw, current.Line, EvalConstant(file.Value.Raw, current.Line, peek.Value, file.Value.Constants, fileConstants), file.Value.Constants);

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
                if (asem.Key.location_const != null)
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
                        if (verbose_addr) Console.WriteLine($"{use.Value,-12} matched local at instruction: {metadata[proc.Key].location + use.Key:X6} Offset: {lbl_offset + metadata[proc.Key].location:X6}");
                        Console.ForegroundColor = conColor;
                    }
                    else if (metadata.ToDictionary(kvp => kvp.Key.name, kvp => kvp.Value.location).TryGetValue(use.Value, out int proc_offset))
                    {
                        short[] offset_inst = IntToShortArray(proc_offset + (executable ? ROM_OFFSET : 0));
                        proc.Value[use.Key] = offset_inst[1];
                        proc.Value[use.Key + 1] = offset_inst[0];
                        Console.ForegroundColor = ConsoleColor.DarkMagenta;
                        if (verbose_addr) Console.WriteLine($"{use.Value,-12} matched call at instruction: {metadata[proc.Key].location + use.Key:X6} Offset: {proc_offset:X6}");
                        Console.ForegroundColor = conColor;
                    }
                    else
                    {
                        Error(metadata[proc.Key].source, metadata[proc.Key].assembledSource.Procs[proc.Key.name].tokens.Find(t => t.Value == use.Value).Line, $"Could not solve label! {use.Value} in proc {proc.Key.name}");
                    }
                }

                // We do this here because we are shifting the breakpoints. If we dont shift breakpoints then we could do this much earlier
                if (metadata[proc.Key].assembledSource.Breakpoints.TryGetValue(proc.Key.name, out List<int> breaks))
                {
                    metadata[proc.Key].breaks = breaks;
                }
            }

            if (verbose) Console.WriteLine();
            
            short[] compiledInstructions = new short[ROM_SIZE];

            int usedInstructions = 0;

            foreach (var proc in assembledProcs)
            {
                usedInstructions += proc.Value.Count;

                proc.Value.CopyTo(compiledInstructions, metadata[proc.Key].location);
            }
            
            success = true;

            return new LibFile(compiledInstructions, metadata.Values.ToArray(), usedInstructions);
        }

        static string EvalConstant(RawFile file, int line, string constant, Dictionary<string, Constant> constants, Dictionary<string, Dictionary<string, Constant>> fileConstants)
        {
            return EvalConstant(new Constant("lit:" + constant, file, line, constant), constants, fileConstants);
        }

        static string EvalConstant(Constant expr, Dictionary<string, Constant> constants, Dictionary<string, Dictionary<string, Constant>> fileConstants)
        {
            string result = null;

            Match constant_expression = arith_expr.Match(expr.value);
            Match external_expression = extern_expr.Match(expr.value);
            Match sizeof_expression = sizeof_expr.Match(expr.value);
            Match endof_expression = endof_expr.Match(expr.value);
            Match auto_expression = auto.Match(expr.value);
            
            if (IsLitteral(expr.file, expr.line, expr.value, constants))
            {
                return expr.value;
            }
            else if (constant_expression.Success)
            {
                // We should parse a arithmetic constant

                string[] tokens = constant_expression.Groups[1].Value.Split(' ');

                Stack<string> stack = new Stack<string>(tokens.Length);

                int num = 0;
                foreach (var token in tokens)
                {
                    switch (token)
                    {
                        case "+":
                            int sum = ToInt(ParseNumber(expr.file, expr.line, stack.Pop())) + ToInt(ParseNumber(expr.file, expr.line, stack.Pop()));
                            stack.Push($"0x{sum:X}");
                            break;
                        case "-":
                            int diff = -ToInt(ParseNumber(expr.file, expr.line, stack.Pop())) + ToInt(ParseNumber(expr.file, expr.line, stack.Pop()));
                            stack.Push($"0x{diff:X}");
                            break;
                        case "*":
                            int product = ToInt(ParseNumber(expr.file, expr.line, stack.Pop())) * ToInt(ParseNumber(expr.file, expr.line, stack.Pop()));
                            stack.Push($"0x{product:X}");
                            break;
                        case "/":
                            string divisor = stack.Pop();
                            string dividend = stack.Pop();

                            int quoutent = ToInt(ParseNumber(expr.file, expr.line, dividend)) / ToInt(ParseNumber(expr.file, expr.line, divisor));

                            stack.Push($"{quoutent}");
                            break;
                        case "%":
                            divisor = stack.Pop();
                            dividend = stack.Pop();

                            int remainder = ToInt(ParseNumber(expr.file, expr.line, dividend)) % ToInt(ParseNumber(expr.file, expr.line, divisor));

                            stack.Push($"{remainder}");
                            break;
                        default:
                            stack.Push(EvalConstant(new Constant(expr.name + $"_arith{num++}", expr.file, expr.line, token), constants, fileConstants));
                            break;
                    }
                }

                if (stack.Count > 1)
                {
                    Error(expr.file, expr.line, $"Could not evaluate expression '{constant_expression.Groups[1].Value}', stack: {{{String.Join(", ", stack)}}}");
                }

                return stack.Pop();
            }
            else if (expr.value.StartsWith("#"))
            {
                return EvalConstant(constants[expr.value.Substring(1)], constants, fileConstants);
            }
            else if (external_expression.Success)
            {
                // We should resolve the external variable
                if (external_expression.Groups[1].Success)
                {
                    // Use the name in the braces as key in the globalConstants

                    if (globalConstants.TryGetValue(external_expression.Groups[1].Value, out Constant value))
                    {
                        return EvalConstant(value, fileConstants[Path.GetFileName(value.file.path)], fileConstants);
                    }
                    else
                    {
                        Error(expr.file, expr.line, $"Could not solve value of extern const '{external_expression.Groups[1].Value}' for const '{expr.name}'");
                    }
                }
                else
                {
                    if (globalConstants.TryGetValue(expr.name, out Constant value))
                    {
                        return EvalConstant(value, fileConstants[Path.GetFileName(value.file.path)], fileConstants);
                    }
                    else
                    {
                        Error(expr.file, expr.line, $"Could not solve value of extern const '{expr.name}'");
                    }
                }
            }
            else if (sizeof_expression.Success)
            {
                AutoConst c = autoConstants[sizeof_expression.Groups[1].Value];

                return $"{c.Length}";
            }
            else if (endof_expression.Success)
            {
                AutoConst c = autoConstants[endof_expression.Groups[1].Value];

                return $"0x{ToInt(ParseLitteral(expr.file, expr.line, EvalConstant(expr.file, expr.line, c.Value, constants, fileConstants), constants)):X}";
            }
            else if (auto_expression.Success)
            {
                AutoConst allocAutoConst(int size)
                {
                    if (size <= 0)
                    {
                        Error(expr.file, expr.line, $"Auto const ''{expr.name}' cannot be defined with a length of {size}!");
                    }

                    if (autoVars - size < STACK_SIZE)
                    {
                        Error(expr.file, expr.line, $"Auto const '{expr.name}' cannot be allocated! (Required: {size}, Available: {autoVars - STACK_SIZE}, Diff: {size - (autoVars - STACK_SIZE)})");
                    }

                    autoVars -= size;
                    string value = $"0x{(autoVars >> 12) & 0xFFF:X3}_{autoVars & 0xFFF:X3}";

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    if (verbose) Console.WriteLine($"Defined auto var '{expr.name}' of size {size} to addr {value}");
                    Console.ForegroundColor = conColor;

                    int end = autoVars + size;

                    AutoConst c = new AutoConst(expr.name, value, size);

                    autoConstants[expr.name] = c;
                    constants[expr.name + ".size"] = new Constant(expr.name + ".size", expr.file, expr.line, $"0x{(size >> 12) & 0xFFF:X3}_{size & 0xFFF:X3}");
                    constants[expr.name + ".end"] = new Constant(expr.name + ".end", expr.file, expr.line, $"0x{(end >> 12) & 0xFFF:X3}_{end & 0xFFF:X3}");

                    return c;
                }

                if (autoConstants.TryGetValue(expr.name, out AutoConst autoConst))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Using already allocated auto '{autoConst.Name}' with value {autoConst.Value}");
                    Console.ForegroundColor = conColor;
                    return autoConst.Value;
                }
                else if (const_expr.IsMatch(auto_expression.Groups[1].Value))
                {
                    Constant next = new Constant(expr.name + "_auto", expr.file, expr.line, auto_expression.Groups[1].Value);

                    int size = ToInt(ParseLitteral(expr.file, expr.line, EvalConstant(next, fileConstants[Path.GetFileName(next.file.path)], fileConstants), constants));

                    AutoConst autoc = allocAutoConst(size);

                    return autoc.Value;
                }
                else
                {
                    // Just do a substitutuion
                    int size = ToInt(ParseLitteral(expr.file, expr.line, EvalConstant(expr.file, expr.line, auto_expression.Groups[1].Value, constants, fileConstants), constants));

                    AutoConst autoc = allocAutoConst(size);

                    return autoc.Value;
                }
            }
            else
            {
                Error(expr.file, expr.line, $"Could not evaluate const_expr {expr.value} for constant {expr.name}");
            }

            return result;
        }

        static bool IsLitteral(RawFile file, int line, string litteral, Dictionary<string, Constant> constants)
        {
            Constant constant;
            short[] value = new short[0];
            if (num.IsMatch(litteral))
            {
                return IsNumber(file, line, litteral);
            }
            else if (chr.Match(litteral).Success)
            {
                return true;
            }
            else if (str.Match(litteral).Success)
            {
                return true;
            }
            else if (constants.TryGetValue(litteral, out constant))
            {
                return IsLitteral(file, line, constant.value, constants);
            }
            else if (globalConstants.TryGetValue(litteral, out constant))
            {
                return IsLitteral(file, line, constant.value, constants);
            }

            return false;
        }

        static short[] ParseLitteral(RawFile file, int line, string litteral, Dictionary<string, Constant> constants)
        {
            Constant constant;
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
            else if (constants.TryGetValue(litteral, out constant))
            {
                value = ParseNumber(file, line, constant.value);
            }
            else if (globalConstants.TryGetValue(litteral, out constant))
            {
                value = ParseNumber(file, line, constant.value);
            }

            return value;
        }

        static bool IsNumber(RawFile file, int line, string litteral)
        {
            // FIXME!! Do proper check
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
            catch (Exception)
            {
                return false;
            }

            return true;
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

            WarningEntry warn = new WarningEntry(file, line, warning);

            Console.WriteLine(warn);

            Warnings.Add(warn);

            Console.ForegroundColor = orig;
        }
        
        static void Error(RawFile file, int line, string error)
        {
            Console.ForegroundColor = ConsoleColor.Red;

            string message;

            if (file == null)
            {
                message = $"Error in unknown file at line {line}: '{error}'";
            }
            else if (File.Exists(file.path))
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