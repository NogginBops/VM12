using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Diagnostics;
using VM12Opcode;
using SKON;
using VM12Util;

namespace VM12Asm
{
    public class VM12Asm
    {
        public enum TokenType
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

            public override string ToString()
            {
                return $"{name}({string.Join(", ", args)})";
            }
        }

        public struct Token
        {
            public readonly int Line;
            public readonly TokenType Type;
            public readonly bool Breakpoint;
            public readonly string Value;
            public readonly Opcode? Opcode;
            public readonly bool Use;
            public readonly bool Raw;

            public static Token InstToken(int line, string value, bool breakpoint, Opcode opcode)
            {
                return new Token(line, TokenType.Instruction, value, breakpoint, opcode, false, false);
            }

            public static Token LitToken(int line, string value, bool breakpoint)
            {
                return new Token(line, TokenType.Litteral, value, breakpoint, null, false, false);
            }

            public static Token LitToken(int line, string value, bool breakpoint, bool raw)
            {
                return new Token(line, TokenType.Litteral, value, breakpoint, null, false, raw);
            }

            public static Token LabelToken(int line, string value, bool breakpoint, bool use)
            {
                return new Token(line, TokenType.Label, value, breakpoint, null, use, false);
            }

            public static Token ArgToken(int line, string value, bool breakpoint)
            {
                return new Token(line, TokenType.Argument, value, breakpoint, null, false, false);
            }

            private Token(int line, TokenType type, string value, bool breakpoint, Opcode? opcode, bool use, bool raw)
            {
                Line = line;
                Type = type;
                Breakpoint = breakpoint;
                Value = value;
                Opcode = opcode;
                Use = use;
                Raw = raw;
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

        public class Constant
        {
            public string name;
            public RawFile file;
            public int line;
            public string value;
            public Proc proc;

            public Constant(string name, RawFile file, int line, string value, Proc proc = default)
            {
                this.name = name;
                this.file = file;
                this.line = line;
                this.value = value;
                this.proc = proc;
            }

            public override string ToString()
            {
                return value;
            }
        }

        public class Proc
        {
            public string name;
            public int line;
            public int? parameters;
            public int? locals;
            public string location_const;
            public int? location;
            public List<Token> tokens;
            public AsemFile file;

            public override string ToString()
            {
                return $"{name}({Path.GetFileName(file.Raw.path)}:{line})";
            }
        }

        public class RawFile
        {
            public string path;
            public string[] rawlines;
            public string[] processedlines;
        }

        public class AsemFile
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

                // FIXME: This is real spaghetti
                foreach (var proc in procs.Values)
                {
                    proc.file = this;
                }
            }

            public override string ToString()
            {
                return $"{{AsemFile: Procs: {Procs.Count}, Breaks: {Breakpoints.Count}, Flags: '{string.Join(",", Flags)}' }}";
            }
        }

        public class LibFile
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

        public class ProcMetadata
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
            public readonly int Location;
            public readonly int Length;

            public AutoConst(string name, string value, int location, int length)
            {
                this.Name = name;
                this.Value = value;
                this.Location = location;
                this.Length = length;
            }
        }

        const int _12BIT_MASK = 0x0FFF;

        const int ROM_OFFSET = Constants.ROM_START;

        const int ROM_SIZE = Constants.ROM_SIZE;

        const short ROM_OFFSET_UPPER_BITS = Constants.ROM_START >> 12;
        
        const string compiler_generated_warning = "; This is a compiler generated file! Changes to this file will be lost.\n\n";

        delegate string TemplateFormater(params object[] values);

        private static readonly TemplateFormater sname = (o) => string.Format("(?<!:)\\b{0}\\b", o);

        private static readonly Dictionary<Regex, string> preprocessorConversions = new Dictionary<Regex, string>()
        {
            //{ new Regex(";.*"), "" },
            { new Regex("#reg.*"), "" },
            { new Regex("#endreg.*"), "" },
            { new Regex("(?<!:)\\bload\\s+(#\\S+)"), "load.lit $1" },
            { new Regex("(?<!:)\\bloadl\\s+(#\\S+)"), "load.lit.l $1" },
            { new Regex("(?<!:)\\bload\\s+(:\\S+)"), "load.lit $1" },
            { new Regex("(?<!:)\\bloadl\\s+(:\\S+)"), "load.lit.l $1" },
            { new Regex("(?<!:)\\bload\\s+(\\d+)"), "load.local $1" },
            { new Regex("(?<!:)\\bloadl\\s+(\\d+)"), "load.local.l $1" },
            { new Regex("(?<!:)\\bload\\s+@(?!\")(\\S+)"), "load.lit.l $1 load.sp" },
            { new Regex("(?<!:)\\bloadl\\s+@(?!\")(\\S+)"), "load.lit.l $1 load.sp.l" },
            // For raw strings
            { new Regex("(?<!:)\\bload\\s+(@\".*?\")"), "load.lit.l $1" },
            { new Regex("(?<!:)\\bloadl\\s+(@\".*?\")"), "load.lit.l $1" },
            { new Regex("(?<!:)\\bload\\s+\\[SP\\]"), "load.sp" },
            { new Regex("(?<!:)\\bloadl\\s+\\[SP\\]"), "load.sp.l" },
            { new Regex("(?<!:)\\bload\\s+('.')"), "load.lit $1" },
            { new Regex("(?<!:)\\bload\\s+(\".*?\")"), "load.lit.l $1" },

            { new Regex("(?<!:)\\bstore\\s+\\[SP\\]"), "store.sp" },
            { new Regex("(?<!:)\\bstorel\\s+\\[SP\\]"), "store.sp.l" },
            { new Regex("(?<!:)\\bstore\\s+(\\d+)"), "store.local $1" },
            { new Regex("(?<!:)\\bstorel\\s+(\\d+)"), "store.local.l $1" },
            { new Regex("(?<!:)\\bstore\\s+(#\\S+)\\s+@(\\S+)"), "load.lit.l $2 load.lit $1 store.sp" },
            { new Regex("(?<!:)\\bstorel\\s+(#\\S+)\\s+@(\\S+)"), "load.lit.l $2 load.lit.l $1 store.sp.l" },
            { new Regex("(?<!:)\\bstore\\s+(#\\S+)"), "load.lit $1 store.sp" },
            { new Regex("(?<!:)\\bstorel\\s+(#\\S+)"), "load.lit.l $1 store.sp.l" },
            { new Regex("(?<!:)\\bstore\\s+@(\\S+)"), "load.lit.l $1 swap.s.l store.sp" },
            { new Regex("(?<!:)\\bstorel\\s+@(\\S+)"), "load.lit.l $1 swap.l store.sp.l" },
            { new Regex("(?<!:)\\bset\\s+\\[SP\\]"), "set.sp" },
            { new Regex("(?<!:)\\bset\\s+\\[FP\\]"), "set.fp" },
            { new Regex("(?<!:)\\bladd\\s+\\[SP\\]\\s+(#\\S+)"), "add.sp.lit.l $1" },
            { new Regex("::\\[SP\\]"), "call.v" },
            { new Regex("::(?!\\s)"), "call :" },
            { new Regex(sname("lswap")), "swap.l" },
            { new Regex(sname("slswap")), "swap.s.l" },
            { new Regex(sname("ldup")), "dup.l" },
            { new Regex(sname("lover")), "over.l.l" },
            { new Regex(sname("lovers")), "over.l.s" },
            { new Regex(sname("soverl")), "over.s.l" },
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
            { new Regex(sname("muladd")), "mul.add" },
            { new Regex(sname("lmuladd")), "mul.add.l" },

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
            
            { new Regex(sname("setz")),   "set ST.Z" },
            { new Regex(sname("setnz")),  "set ST.Nz" },
            { new Regex(sname("setc")),   "set ST.C" },
            { new Regex(sname("setnc")),  "set ST.Cz" },
            { new Regex(sname("setgz")),  "set ST.Gz" },
            { new Regex(sname("setge")),  "set ST.Ge" },
            { new Regex(sname("setlz")),  "set ST.Lz" },
            { new Regex(sname("setle")),  "set ST.Le" },
            { new Regex(sname("lsetz")),  "set ST.Z.l" },
            { new Regex(sname("lsetnz")), "set ST.Nz.l" },
            { new Regex(sname("lsetc")),  "set ST.C.l" },
            { new Regex(sname("lsetnc")), "set ST.Cz.l" },
            { new Regex(sname("lsetgz")), "set ST.Gz.l" },
            { new Regex(sname("lsetge")), "set ST.Ge.l" },
            { new Regex(sname("lsetlz")), "set ST.Lz.l" },
            { new Regex(sname("lsetle")), "set ST.Le.l" },
            
            { new Regex(sname("graf_clear")), "graf.clear" },
            { new Regex(sname("graf_fill")), "graf.fill" },

            { new Regex(sname("strt_coproc")), "coproc.start" },
            { new Regex(sname("hlt_coproc")), "coproc.hlt" },
            { new Regex(sname("int_coproc")), "coproc.int" },

            { new Regex(sname("int_snd_chip")), "int.snd.chip" },
            
            { new Regex("\\[FP\\]"), "fp" },
            { new Regex("\\[PC\\]"), "pc" },
            { new Regex("\\[PT\\]"), "pt" },
            { new Regex("\\[SP\\]"), "sp" },
        };

        static Regex using_statement = new Regex("&\\s*([A-Za-z][A-Za-z0-9_]*)\\s+(.*\\.(?:12asm|t12))");

        static Regex constant = new Regex("<([A-Za-z_][A-Za-z0-9_]*)\\s*=\\s*(.*)>");

        static Regex label = new Regex("^(:[A-Za-z0-9_\\.]+)(\\*)?");

        static Regex proc = new Regex("(:[A-Za-z0-9_\\.]+)(\\s+@(.*))?");

        static Regex num = new Regex("^(?<!\\S)(0x[0-9A-Fa-f_]+|8x[0-7_]+|0b[0-1_]+|-?[0-9_]+)(?!\\S)$");

        static Regex chr = new Regex("^'(.)'$");

        static Regex str = new Regex("^\\s*(@)?(\"[^\"\\\\]*(\\\\.[^\"\\\\]*)*\")$");

        static Regex auto = new Regex("^auto\\((.*)\\)$");

        static Regex const_expr = new Regex("^(extern(?:\\((#.+)\\))?|#\\((.*)\\)|sizeof(?:\\((#.+)\\))?|endof(?:\\((#.+)\\))?)$");

        static Regex extern_expr = new Regex("^extern(?:\\(#(.+)\\))?$");

        static Regex sizeof_expr = new Regex("^sizeof\\(#(.+)\\)$");

        static Regex endof_expr = new Regex("^endof\\(#(.+)\\)$");

        static Regex sizeof_proc_expr = new Regex("^sizeof\\((:.+)\\)$");

        static Regex endof_proc_expr = new Regex("^endof\\((:.+)\\)$");

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
            { "set.fp", Opcode.Set_fp },
            { "add.sp.lit.l", Opcode.Add_sp_lit_l },
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
            { "over.s.l", Opcode.Over_s_l },
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
            { "mul.add", Opcode.Mul_Add },
            { "mul.add.l", Opcode.Mul_Add_l },

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
            { "write", Opcode.Write },
            { "read", Opcode.Read },

            { "clz", Opcode.Clz },
            { "ctz", Opcode.Ctz },
            { "selz", Opcode.Selz },
            { "selgz", Opcode.Selgz },
            { "selge", Opcode.Selge },
            { "selc", Opcode.Selc },
            
            { "set", Opcode.Set },

            { "brk", Opcode.Brk },

            { "graf.clear", Opcode.Graf_clear },
            { "graf.fill", Opcode.Graf_fill },

            { "coproc.start", Opcode.Start_coproc },
            { "coproc.hlt", Opcode.Hlt_coproc },
            { "coproc.int", Opcode.Int_coproc },

            { "int.snd.chip", Opcode.Int_snd_chip },
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

            { "ST.Z",    (int) SetMode.Z },
            { "ST.Nz",   (int) SetMode.Nz },
            { "ST.C",    (int) SetMode.C },
            { "ST.Cz",   (int) SetMode.Cz },
            { "ST.Gz",   (int) SetMode.Gz },
            { "ST.Ge",   (int) SetMode.Ge },
            { "ST.Lz",   (int) SetMode.Lz },
            { "ST.Le",   (int) SetMode.Le },
            { "ST.Z.l",  (int) SetMode.Z_l },
            { "ST.Nz.l", (int) SetMode.Nz_l },
            { "ST.C.l",  (int) SetMode.C_l },
            { "ST.Cz.l", (int) SetMode.Cz_l },
            { "ST.Gz.l", (int) SetMode.Gz_l },
            { "ST.Ge.l", (int) SetMode.Ge_l },
            { "ST.Lz.l", (int) SetMode.Lz_l },
            { "ST.Le.l", (int) SetMode.Le_l },
        };

        private static readonly ConsoleColor conColor = Console.ForegroundColor;

        static bool verbose = false;

        static bool verbose_addr = false;

        static bool verbose_lit = false;

        static bool verbose_expr = false;

        static bool verbose_token = false;

        static bool dump_mem = false;

        static int pplines = 0;
        static int macroUses = 0;
        static int lines = 0;
        static int tokens = 0;

        private static readonly Encoding charEncoding = Encoding.GetEncoding(437);

        static List<Macro> globalMacros = new List<Macro>();

        static Dictionary<string, Constant> globalConstants = new Dictionary<string, Constant>();

        static Dictionary<string, AutoConst> autoConstants = new Dictionary<string, AutoConst>();

        static Dictionary<string, List<(string, int)>> sizeOfProcUse = new Dictionary<string, List<(string, int)>>();

        static Dictionary<string, List<(string, int)>> endOfProcUse = new Dictionary<string, List<(string, int)>>();

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
                return $"Warning in file \"{FileInfo?.Name ?? "internal"}\" at line {Line}: '{WarningText}'";
            }
        }

        static List<WarningEntry> Warnings = new List<WarningEntry>();

        static int autoStringIndex = 0;

        static Dictionary<string, string> autoStrings = new Dictionary<string, string>();

        static StringBuilder autoStringsFile = new StringBuilder(10_000);

        static StringBuilder procMapFile = new StringBuilder(10_000);

        static int autoVars = Constants.RAM_END;
        
        const int STACK_SIZE = Constants.STACK_MAX_ADDRESS;
        
        public static void Reset()
        {
            verbose = false;
            verbose_addr = false;
            verbose_lit = false;
            verbose_expr = false;
            verbose_token = false;
            dump_mem = false;

            pplines = 0;
            macroUses = 0;
            lines = 0;
            tokens = 0;

            globalMacros.Clear();

            globalConstants.Clear();
            autoConstants.Clear();
            sizeOfProcUse.Clear();
            endOfProcUse.Clear();

            autoStrings.Clear();
            autoStringIndex = 0;
            autoStringsFile.Clear();
            procMapFile.Clear();

            Warnings.Clear();
            autoVars = Constants.RAM_END;
        }

        public static void Main(params string[] args)
        {
            if (Constants.VALID_MEM_PARTITIONS == false)
            {
                throw new Exception("Memory partitions don't match the memory size!!");
            }

            Console.ForegroundColor = conColor;

            IEnumerator<string> enumerator = args.AsEnumerable().GetEnumerator();

            string file = null;
            string name = null;
            bool generateStringSource = true;
            bool generateProcMapSource = true;
            bool generateTypeMapSource = true;
            bool executable = true;
            bool overwrite = false;
            bool hold = false;
            bool open = false;

            long t12Time = 0;
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
                    case "-ve":
                        verbose_expr = true;
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

            autoStringsFile.Clear();

            autoStringsFile.AppendLine("!noprintouts");
            autoStringsFile.AppendLine("!global");
            autoStringsFile.AppendLine("!no_map");
            autoStringsFile.AppendLine();

            Console.WriteLine($"Parsing...");

            #region Usings

            Dictionary<string, AsemFile> files = new Dictionary<string, AsemFile>();

            Stack<string> remainingUsings = new Stack<string>();

            HashSet<string> parsedFiles = new HashSet<string>();

            remainingUsings.Push(Path.GetFileName(file));

            FileInfo fileInf = new FileInfo(file);
            DirectoryInfo dirInf = fileInf.Directory;

            FileInfo[] dirFiles = dirInf.GetFilesByExtensions(".12asm", ".t12").ToArray();

            // TODO: Files with the same name but differnet directories
            while (remainingUsings.Count > 0)
            {
                string use = remainingUsings.Pop();

                if (files.ContainsKey(use))
                {
                    continue;
                }
                else if (use.EndsWith("t12", StringComparison.InvariantCultureIgnoreCase) && files.ContainsKey(Path.ChangeExtension(use, "12asm")))
                {
                    Log(verbose, $"T12 file {use} was already compiled. Skipping!");
                    continue;
                }
                
                FileInfo fi = dirFiles.First(f => f.Name == use);

                if (Path.GetExtension(fi.FullName) == ".t12")
                {
                    void HandleMessage(T12.Compiler.MessageData data)
                    {
                        switch (data.Level)
                        {
                            case T12.Compiler.MessageLevel.Error:
                                // For now we do nothing
                                break;
                            case T12.Compiler.MessageLevel.Warning:
                                RawFile messageFile = new RawFile
                                {
                                    path = data.File,
                                };
                                Warning(messageFile, data.StartLine, data.Message);
                                break;
                            default:
                                Error(null, -1, $"Unknown message level!! '{data.Level}'");
                                break;
                        }
                    }

                    Log(verbose, $"Compiling t12 file '{fi.Name}'.");
                    
                    watch.Restart();

                    if (T12.Compiler.Compiling == false)
                    {
                        T12.Compiler.StartCompiling(dirInf, HandleMessage);
                    }
                        
                    // We need to invoke the t12 compiler!
                    T12.Compiler.Compile(fi);

                    watch.Stop();
                    t12Time += watch.ElapsedTicks;
                    
                    fi = new FileInfo(Path.ChangeExtension(fi.FullName, ".12asm"));
                }

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
                
                files[fi.Name] = asmFile;

                // FIXME: Reverse here is really weird!!
                foreach (var u in asmFile.Usings.Reverse())
                {
                    if (files.ContainsKey(u.Key) == false)
                    {
                        remainingUsings.Push(u.Value);
                    }
                }
            }

            #region Compiler generated files

            // Auto strings
            {
                RawFile rawAutoStrings = new RawFile();
                rawAutoStrings.path = "AutoStrings.12asm";
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
                    File.WriteAllText(Path.Combine(dirInf.FullName, "AutoStrings.12asm"), compiler_generated_warning + autoStringsFile.ToString());
                }
            }

            // Proc map
            {
                procMapFile.Clear();

                procMapFile.AppendLine("!noprintouts");
                procMapFile.AppendLine("!no_map");
                procMapFile.AppendLine("!global");
                procMapFile.AppendLine("");

                int insertIndex = procMapFile.Length;

                StringBuilder sb = new StringBuilder();
                procMapFile.AppendLine($":__proc_map__");

                int procsMapped = 0;

                foreach (var f in files)
                {
                    if (f.Value.Flags.Contains("!no_map") == false)
                    {
                        foreach (var proc in f.Value.Procs)
                        {
                            int offset = sb.Length;
                            procMapFile.AppendLine($"\t{proc.Key}* #(sizeof({proc.Key})) 0x{offset:X6} 0x{proc.Key.Length:X6}");
                            sb.Append(proc.Key);
                            procsMapped++;
                        }
                    }
                }

                procMapFile.AppendLine();

                procMapFile.AppendLine($":__proc_map_strings__");
                procMapFile.AppendLine($"\t@\"{sb}\"");

                procMapFile.Insert(insertIndex, $"<proc_map_entries = {procsMapped}>\n" +
                                                $"<proc_map_strings_length = {sb.Length}>\n" +
                                                $"<proc_map_length = #({procsMapped} 8 *)>\n\n");

                RawFile rawProcMap = new RawFile();
                rawProcMap.path = "ProcMapData.12asm";
                rawProcMap.rawlines = procMapFile.ToString().Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                rawProcMap.processedlines = rawProcMap.rawlines;

                lines -= rawProcMap.rawlines.Length;

                watch.Restart();
                AsemFile procMapAsem = Parse(rawProcMap);
                watch.Stop();
                parseTime += watch.ElapsedTicks;

                files["ProcMapData.12asm"] = procMapAsem;

                if (generateProcMapSource)
                {
                    File.WriteAllText(Path.Combine(dirInf.FullName, "ProcMapData.12asm"), compiler_generated_warning + procMapFile.ToString());
                }
            }

            // Type map
            {
                RawFile rawTypeMap = new RawFile();
                rawTypeMap.path = "TypeMapData.12asm";
                string typeMapString = T12.Compiler.GetTypeMapData();
                rawTypeMap.rawlines = typeMapString.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                rawTypeMap.processedlines = rawTypeMap.rawlines;

                lines -= rawTypeMap.processedlines.Length;

                watch.Restart();
                AsemFile typeMapAsem = Parse(rawTypeMap);
                watch.Stop();
                parseTime += watch.ElapsedTicks;

                files["TypeMapData.12asm"] = typeMapAsem;

                if (generateTypeMapSource)
                {
                    File.WriteAllText(Path.Combine(dirInf.FullName, "TypeMapData.12asm"), compiler_generated_warning + typeMapString);
                }
            }

            #endregion

            if (T12.Compiler.Compiling == true)
            {
                T12.Compiler.StopCompiling();
            }

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

                output.WriteLine($"Result ({libFile.UsedInstructions} used words ({((double)libFile.UsedInstructions / ROM_SIZE):P5})): ");
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

            Console.WriteLine($"Allocated {Constants.RAM_END - autoVars} ({(((double)Constants.RAM_END - autoVars) / (Constants.RAM_END - STACK_SIZE)):P5}) words to auto() vars {autoVars - STACK_SIZE} words remaining");

            total.Stop();

            double t12_ms = ((double)t12Time / Stopwatch.Frequency) * 1000;
            double t12_tokenize_ms = ((double)T12.Compiler.TokenizerTime / Stopwatch.Frequency) * 1000;
            double t12_parse_ms = ((double)T12.Compiler.ParserTime / Stopwatch.Frequency) * 1000;
            double t12_emit_ms = ((double)T12.Compiler.EmitterTime / Stopwatch.Frequency) * 1000;
            double t12_misc_ms = ((double)T12.Compiler.MiscTime / Stopwatch.Frequency) * 1000;

            double preprocess_ms = ((double)preprocessTime / Stopwatch.Frequency) * 1000;
            double parse_ms = ((double)parseTime / Stopwatch.Frequency) * 1000;
            double assembly_ms = ((double)assemblyTime / Stopwatch.Frequency) * 1000;
            double total_ms_sum = t12_ms + preprocess_ms + parse_ms + assembly_ms;
            double total_ms = ((double)total.ElapsedTicks / Stopwatch.Frequency) * 1000;

            string warningString = $"Assembled with {Warnings.Count} warning{(Warnings.Count > 0 ? "" : "s")}.";
            Console.WriteLine($"Success! {warningString}");
            Console.WriteLine($"Compiled {T12.Compiler.CompiledFiles} t12 files of {T12.Compiler.CompiledLines} lines compiled to {T12.Compiler.ResultLines} lines 12asm (T12 has saved you from typing {T12.Compiler.ResultLines - T12.Compiler.CompiledLines} lines 12asm). x{T12.Compiler.ResultLines / (float) T12.Compiler.CompiledLines} increase!");
            Console.WriteLine($"There where {T12.Compiler.AppendageLines} lines of generated appendages.");
            Console.WriteLine($"T12: {t12_ms:F0} ms, (Tokenizer: {t12_tokenize_ms:F0} ms, Parser: {t12_parse_ms:F0} ms, Emitter: {t12_emit_ms:F0} ms, Misc: {t12_misc_ms:F0} ms, Sum: {(t12_tokenize_ms + t12_parse_ms + t12_emit_ms + t12_misc_ms):F0} ms)  {T12.Compiler.CompiledLines} lines");
            Console.WriteLine($"Preprocess: {preprocess_ms:F0} ms {pplines} lines");
            Console.WriteLine($"Parse: {parse_ms:F0} ms {lines} lines");
            Console.WriteLine($"Assembly: {assembly_ms:F0} ms {files.Count} files, {libFile.Metadata.Length} procs, {lines} lines or {tokens} tokens");
            Console.WriteLine($"Sum: {total_ms_sum:F2} ms");
            Console.WriteLine($"Total: {total_ms:F2} ms");

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
                    for (int pos = 0; pos < libFile.Instructions.Length;)
                    {
                        int skipped = 0;
                        while (pos < libFile.Instructions.Length && libFile.Instructions[pos] == 0)
                        {
                            pos++;
                            skipped++;
                        }

#if DEBUG_BINFORMAT
                        if (skipped > 0)
                        {
                            Console.WriteLine($"Skipped {skipped} instructions");
                        }
#endif

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

#if DEBUG_BINFORMAT
                            Console.WriteLine($"Writing block at pos {pos} and length {length} with fist value {libFile.Instructions[pos]} and last value {libFile.Instructions[pos + length - 1]}");
#endif

                            bw.Write(pos);
                            bw.Write(length);

                            for (int i = 0; i < length; i++)
                            {
                                bw.Write(libFile.Instructions[pos + i]);
                            }

                            pos += length;
                        }
                    }
                }
            }

            FileInfo metaFile = new FileInfo(Path.Combine(dirInf.FullName, name + ".12meta"));
            FileInfo metaSKONFile = new FileInfo(Path.Combine(dirInf.FullName, name + ".skon"));

            metaFile.Delete();

            //SKONObject skonObject = SKONObject.GetEmptyMap();

            using (FileStream stream = metaFile.Create())
            using (StreamWriter writer = new StreamWriter(stream))
            {
                //SKONObjec   onstants = SKONObject.GetEmptyArray();

                foreach (var constant in autoConstants)
                {
                    writer.WriteLine($"[constant:{{{constant.Key},{constant.Value.Length},{constant.Value.Value}}}]");

                    /*constants.Add(new Dictionary<string, SKONObject> {
                        { "name", constant.Key },
                        { "value", constant.Value.Value },
                        { "length", constant.Value.Length },
                    });*/
                }

                //skonObject.Add("constants", constants);

                writer.WriteLine();

                //SKONObject procs = SKONObject.GetEmptyArray();

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

                    /*
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
                    */
                }

                //SKON.SKON.WriteToFile(metaSKONFile.FullName, skonObject);
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

        public static string[] PreProcess(string[] lines, string fileName)
        {
            // FIXME: This feels very slow and does a lot of allocations!!!
            string RemoveCommnents(string removeCommentLine)
            {
                string noCommentLine;

                if (removeCommentLine.Contains(';'))
                {
                    string preCommentRemovedLine = removeCommentLine;
                    StringBuilder commentRemovedLine = new StringBuilder(preCommentRemovedLine.Length);

                    int quoteCounter = 0;
                    for (int index = 0; index < preCommentRemovedLine.Length; index++)
                    {
                        if (preCommentRemovedLine[index] == '"') quoteCounter++;
                        else if (preCommentRemovedLine[index] == ';' && quoteCounter % 2 == 0) break;

                        commentRemovedLine.Append(preCommentRemovedLine[index]);
                    }

                    noCommentLine = commentRemovedLine.ToString();
                }
                else
                {
                    noCommentLine = removeCommentLine;
                }

                return noCommentLine;
            }

            pplines += lines.Length;

            RawFile file = new RawFile() { path = fileName, rawlines = lines };

            //Regex macroDefLoose = new Regex("#def (.*?)\\(.*\\)");
            //Regex macroDefStrict = new Regex("#def\\s[A-Za-z_][A-Za-z0-9_]*\\(((?:\\s*[A-Za-z_][A-Za-z0-9_]*,?)*)\\)");

            //Regex macroDefEnd = new Regex("#end (.*?)");

            // FIXME: The '#' in the pre part is a bad hack
            //Regex macroUse = new Regex("^[^<\\n\\r#]*?(\\b(?<!#\\(|#)[A-Za-z0-9_]+)\\(((?:\\s*.*?,?)*)\\)");

            //List<Macro> macros = new List<Macro>();

            List<string> newLines = new List<string>(lines);

            //bool global = false;

            // Go through all lines and parse and replace macros
            /**
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
                
                string line = RemoveCommnents(lines[i]);

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

                        for (int l = 0; l < offset; l++)
                        {
                            newLines[i + l] = "";
                        }

                        Macro macro = new Macro();

                        macro.name = match.Groups[1].Value;

                        macro.lines = new string[offset - 2];
                        for (int lineNum = 0; lineNum < offset - 2; lineNum++)
                        {
                            macro.lines[lineNum] = lines[i + 1 + lineNum];
                        }

                        macro.args = strictMatch.Groups[1].Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();

                        if (global)
                        {
                            globalMacros.Add(macro);
                        }
                        else
                        {
                            macros.Add(macro);
                        }

                        Log(verbose, $"Defined {(global ? "global " : "")}macro '{macro.name}'");
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
                        string useName = useMatch.Groups[1].Value;
                        string[] args = useMatch.Groups[2].Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();

                        // Find a macro with the same name and numer of arguments
                        Macro macro = macros.Concat(globalMacros).FirstOrDefault(m => (m.name == useName) && (m.args.Length == args.Length));

                        if (macro == null)
                        {
                            Error(file, i, $"Unknown macro '{useName}'");
                        }
                        else
                        {
                            Log(verbose, $"Found macro use '{macro.name}'");
                            List<string> macroLines = new List<string>(macro.lines);
                            for (int lineNum = 0; lineNum < macroLines.Count; lineNum++)
                            {
                                for (int arg = 0; arg < macro.args.Length; arg++)
                                {
                                    if (macroLines[lineNum].Contains(macro.args[arg]))
                                    {
                                        Log(verbose, $"Line '{macroLines[lineNum]}' Arg '{macro.args[arg]}' Value '{args[arg]}'");
                                        macroLines[lineNum] = macroLines[lineNum].Replace(macro.args[arg], args[arg]);
                                    }
                                }
                            }

                            string macroResult = string.Join(" ", macroLines);

                            StringBuilder newLineString = new StringBuilder(line);
                            newLines[i] = newLineString.Replace(useMatch.Value, macroResult, useMatch.Index, useMatch.Length).ToString();

                            macroUses++;
                        }
                    }
                }
            }
            */

            for (int i = 0; i < newLines.Count; i++)
            {
                newLines[i] = RemoveCommnents(newLines[i]);
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
                        newLines[forward] = RemoveCommnents(newLines[forward]);
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

        public static AsemFile Parse(RawFile file)
        {
            Log(verbose, $"Parsing {Path.GetFileName(file.path)}");

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

                string line = it_line.Trim(new[] { ' ', '\t', '¤' });

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
                    // FIXME: If two usings specify a different name
                    // we are going to compile that file twice!
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
                            Error(file, line_num, $"Exporting extern constant '{c.Groups[1].Value}'");
                        }

                        if (globalConstants.TryGetValue(c.Groups[1].Value, out Constant existing_const))
                        {
                            Warning(file, line_num, $"Redefining global constant '{c.Groups[1].Value}' already defined in '{Path.GetFileName(existing_const.file.path)} at line {existing_const.line}'!");
                        }

                        globalConstants[c.Groups[1].Value] = constant;

                        if (isAuto)
                        {
                            globalConstants[c.Groups[1].Value + ".size"] = new Constant(c.Groups[1].Value + ".size", file, line_num, $"sizeof(#{c.Groups[1].Value})");
                            globalConstants[c.Groups[1].Value + ".end"] = new Constant(c.Groups[1].Value + ".end", file, line_num, $"endof(#{c.Groups[1].Value})");

                            Log(verbose, $"Adding .size and .end to auto var '{c.Groups[1].Value}'");
                        }
                    }
                }
                else if ((c = str.Match(line)).Success)
                {
                    Token string_tok = Token.LitToken(line_num, c.Groups[2].Value.Trim(), breakpoint, c.Groups[1].Success);

                    currProc.tokens.Add(string_tok);
                }
                else
                {
                    if (char.IsWhiteSpace(line, 0))
                    {
                        string[] SplitNotStringsOrLitterals(string input, char[] chars)
                        {
                            return Regex.Matches(input, @"(@?[\""].*?[\""]|'.'|#?\(.*\))|[^ ]+")
                                .Cast<Match>()
                                .Select(m => m.Value)
                                .ToArray();
                        }

                        string[] tokens = SplitNotStringsOrLitterals(line, new[] { ' ', '\t' });

                        foreach (var token in tokens)
                        {
                            Token t;
                            Match l;

                            if (opcodes.TryGetValue(token, out Opcode opcode))
                            {
                                t = Token.InstToken(line_num, token, breakpoint, opcode);
                            }
                            else if (arguments.TryGetValue(token, out int arg))
                            {
                                t = Token.ArgToken(line_num, token, breakpoint);
                            }
                            else if ((l = label.Match(token)).Success)
                            {
                                t = Token.LabelToken(line_num, l.Groups[1].Value, breakpoint, l.Groups[2].Success);
                            }
                            else if (token.StartsWith("#("))
                            {
                                t = Token.LitToken(line_num, token, breakpoint);
                            }
                            else if (num.IsMatch(token) || constants.ContainsKey(token))
                            {
                                t = Token.LitToken(line_num, token, breakpoint);
                            }
                            else if (token.StartsWith("#") && (num.IsMatch(token.Substring(1)) || constants.ContainsKey(token.Substring(1))))
                            {
                                t = Token.LitToken(line_num, token.Substring(1), breakpoint);
                            }
                            else if ((l = chr.Match(token)).Success)
                            {
                                t = Token.LitToken(line_num, l.Value, breakpoint);
                            }
                            else if ((l = str.Match(token)).Success)
                            {
                                string str = l.Value.Trim();
                                string labelName;
                                if (autoStrings.TryGetValue(str, out labelName) == false)
                                {
                                    labelName = $":__str_{autoStringIndex++}__";
                                    autoStringsFile.AppendLine(labelName);
                                    autoStringsFile.Append('\t').AppendLine(str);
                                    autoStrings[str] = labelName;
                                    
                                    Log(verbose, ConsoleColor.Magenta, $"Created inline string '{labelName}' with value {str}");
                                }

                                t = Token.LabelToken(line_num, labelName, breakpoint, false);
                            }
                            else
                            {
                                Error(file, line_num, $"Could not parse token: \"{token}\"");
                                t = new Token();
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
            if (currProc.name != null)
            {
                procs[currProc.name] = currProc;
            }
            else if (currProc.tokens.Count > 0)
            {
                Warning(file, currProc.line, $"File contains code but has no proc lable! (This code will be ignored)");
            }

            return new AsemFile(file, usings, constants, procs, breakpoints, flags);
        }

        public static LibFile Assemble(Dictionary<string, AsemFile> files, bool executable, out bool success)
        {
            // TODO: We should really have all the data we are itterating in the same place
            // because with our use of Dictionary atm we are not optimizing for itteration!

            // FIXME: This should actually be a list of (Proc, List<short>)!
            Dictionary<Proc, List<short>> assembledProcs = new Dictionary<Proc, List<short>>();

            Dictionary<string, ProcMetadata> metadata = new Dictionary<string, ProcMetadata>();

            Dictionary<string, Proc> procMap = new Dictionary<string, Proc>();

            int offset = 0;

            Dictionary<string, Dictionary<string, int>> proc_label_instructions = new Dictionary<string, Dictionary<string, int>>();

            Dictionary<string, Dictionary<int, string>> proc_label_uses = new Dictionary<string, Dictionary<int, string>>();

            Dictionary<string, Dictionary<int, (Constant constant, int size)>> delayed_const_uses = new Dictionary<string, Dictionary<int, (Constant, int)>>();

            if (executable && files.Values.SelectMany(f => f.Procs.Keys).Any(p => p == ":start") == false)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("An executable needs to contain a :start proc!");
                success = false;
                return default(LibFile);
            }

            Dictionary<string, Dictionary<string, Constant>> fileConstants = files.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Constants);

            Dictionary<Proc, List<int>> breakpoints = new Dictionary<Proc, List<int>>();

            foreach (var file in files)
            {
                Log(verbose_expr, $"Evaluating {file.Value.Constants.Count} const expressions in file '{file.Key}'");

                foreach (var eval_expr in file.Value.Constants.ToList())
                {
                    string result = EvalConstant(eval_expr.Value, file.Value.Constants, fileConstants, null, out bool delay);

                    if (delay)
                    {
                        Error(eval_expr.Value.file, eval_expr.Value.line, "Const cannot be delayed!");
                    }

                    if (eval_expr.Value.value != result)
                    {
                        Log(verbose_expr, $"Evaluated constant '{eval_expr.Value.value}' to constant '{result}' for constant '{eval_expr.Key}'");
                    }

                    file.Value.Constants[eval_expr.Key].value = result;
                }

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
                    Dictionary<int, (Constant constant, int size)> local_delayed_const_uses = new Dictionary<int, (Constant, int)>();

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
                        int location = ToInt(ParseLitteral(file.Value.Raw, proc.Value.line, proc.Value.location_const, false, file.Value.Constants));

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

                    if (proc.Value.tokens.All(t => t.Type != TokenType.Instruction))
                    {
                        shouldHaveParamAndLocals = false;
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
                            proc.Value.parameters = ToInt(ParseLitteral(file.Value.Raw, proc.Value.line, parameters.Value, false, file.Value.Constants));
                            proc.Value.locals = ToInt(ParseLitteral(file.Value.Raw, proc.Value.line, locals.Value, false, file.Value.Constants));
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

                        if (current.Breakpoint == true)
                        {
                            if (breakpoints.ContainsKey(proc.Value) == false)
                            {
                                breakpoints[proc.Value] = new List<int>();
                            }

                            // We should break on the next added instruction
                            breakpoints[proc.Value].Add(instructions.Count);
                        }

                        switch (current.Type)
                        {
                            case TokenType.Instruction:
                                switch (current.Opcode ?? Opcode.Nop)
                                {
                                    case Opcode.Add_sp_lit_l:
                                        if (peek.Type == TokenType.Litteral)
                                        {
                                            string evalRes = EvalConstant(file.Value.Raw, current.Line, peek.Value, file.Value.Constants, fileConstants, null, out bool delay);

                                            if (delay)
                                            {
                                                instructions.Add((short)current.Opcode);
                                                local_delayed_const_uses[instructions.Count] = (new Constant($"{proc.Key}_load_lit_{instructions.Count}", file.Value.Raw, peek.Line, peek.Value), 2);
                                                instructions.Add(0);
                                                instructions.Add(0);

                                                Log(verbose, $"Delayed evaluation of constant '{peek.Value}' for add_sp_lit_l");
                                            }
                                            else
                                            {
                                                short[] value = ParseLitteral(file.Value.Raw, current.Line, evalRes, peek.Raw, file.Value.Constants);

                                                if (value.Length <= 2)
                                                {
                                                    instructions.Add((short)current.Opcode);

                                                    // FIXME: We used to do sign extension here... 
                                                    // We should probably do some kind of thing here but for now we don't
                                                    // instructions.Add(value.Length < 2 ? (short)(value[0] == 0xFFF ? 0xFFF : 0) : value[1]);

                                                    instructions.Add(value.Length < 2 ? (short)0 : value[1]);
                                                    instructions.Add(value[0]);
                                                }
                                                else
                                                {
                                                    Error(file.Value.Raw, current.Line, $"{current.Opcode} cannot be followed by a constant bigger than two words!");
                                                }

                                                Log(verbose, $"Parsed load litteral with litteral {peek.Value}!");
                                            }
                                            tokens.MoveNext();
                                        }
                                        else
                                        {
                                            Error(file.Value.Raw, current.Line, $"{current.Opcode} must be followed by a litteral! Got: {peek}");
                                        }
                                        break;
                                    case Opcode.Load_lit:
                                        if (peek.Type == TokenType.Label)
                                        {
                                            instructions.Add((short)Opcode.Load_lit_l);
                                            local_label_uses[instructions.Count] = peek.Value;
                                            instructions.Add(0);
                                            instructions.Add(0);
                                        }
                                        else if (peek.Type == TokenType.Litteral)
                                        {
                                            string evalRes = EvalConstant(file.Value.Raw, current.Line, peek.Value, file.Value.Constants, fileConstants, null, out bool delay);

                                            if (delay)
                                            {
                                                instructions.Add((short)Opcode.Load_lit);
                                                local_delayed_const_uses[instructions.Count] = (new Constant($"{proc.Key}_load_lit_{instructions.Count}", file.Value.Raw, peek.Line, peek.Value), 1);
                                                instructions.Add(0);

                                                Log(verbose, $"Delayed evaluation of constant '{peek.Value}' for loadl_lit_l");
                                            }
                                            else
                                            {
                                                short[] value = ParseLitteral(file.Value.Raw, current.Line, evalRes, peek.Raw, file.Value.Constants);

                                                if (value.Length == 2)
                                                {
                                                    instructions.Add((short)Opcode.Load_lit_l);
                                                    instructions.Add(value[1]);
                                                    instructions.Add(value[0]);
                                                }
                                                else
                                                {
                                                    foreach (var val in value.Reverse())
                                                    {
                                                        instructions.Add((short)current.Opcode);
                                                        instructions.Add(val);
                                                    }
                                                }

                                                Log(verbose, $"Parsed load litteral with litteral {peek.Value}!");
                                            }
                                        }
                                        else
                                        {
                                            Error(file.Value.Raw, current.Line, $"{current.Opcode} can only be followed by a label or litteral!");
                                        }
                                        tokens.MoveNext();
                                        break;
                                    case Opcode.Load_lit_l:
                                        if (peek.Type == TokenType.Label)
                                        {
                                            instructions.Add((short)current.Opcode);
                                            local_label_uses[instructions.Count] = peek.Value;
                                            instructions.Add(0);
                                            instructions.Add(0);

                                            tokens.MoveNext();
                                        }
                                        else if (peek.Type == TokenType.Litteral)
                                        {
                                            string evalRes = EvalConstant(file.Value.Raw, current.Line, peek.Value, file.Value.Constants, fileConstants, null, out bool delay);

                                            if (delay)
                                            {
                                                instructions.Add((short)current.Opcode);
                                                local_delayed_const_uses[instructions.Count] = (new Constant($"{proc.Key}_load_lit_l_{instructions.Count}", file.Value.Raw, peek.Line, peek.Value), 2);
                                                instructions.Add(0);
                                                instructions.Add(0);

                                                Log(verbose, $"Delayed evaluation of constant '{peek.Value}' for loadl_lit_l");
                                            }
                                            else
                                            {
                                                short[] value = ParseLitteral(file.Value.Raw, current.Line, evalRes, peek.Raw, file.Value.Constants);

                                                if (value.Length <= 2)
                                                {
                                                    instructions.Add((short)current.Opcode);

                                                    // FIXME: We used to do sign extension here... 
                                                    // We should probably do some kind of thing here but for now we don't
                                                    // instructions.Add(value.Length < 2 ? (short)(value[0] == 0xFFF ? 0xFFF : 0) : value[1]);
                                                    
                                                    instructions.Add(value.Length < 2 ? (short)0 : value[1]);
                                                    instructions.Add(value[0]);
                                                }
                                                else
                                                {
                                                    Error(file.Value.Raw, current.Line, $"{current.Opcode} cannot be followed by a constant bigger than two words!");
                                                }
                                            }

                                            tokens.MoveNext();
                                        }
                                        else
                                        {
                                            Error(file.Value.Raw, current.Line, $"{current.Opcode} must be followed by either a label or litteral!");
                                        }
                                        break;
                                    case Opcode.Jmp:
                                        instructions.Add((short)current.Opcode);

                                        JumpMode mode = JumpMode.Jmp;
                                        if (peek.Type == TokenType.Argument)
                                        {
                                            mode = (JumpMode)arguments[peek.Value];
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
                                            // These don't take a label as the next instruction
                                        }
                                        else if (peek.Type == TokenType.Label)
                                        {
                                            local_label_uses[instructions.Count] = peek.Value;
                                            Log(verbose, ConsoleColor.DarkGreen, $"Added label {peek.Value} using at {instructions.Count:X}");
                                            instructions.Add(0);
                                            instructions.Add(0);
                                            tokens.MoveNext();
                                        }
                                        else if (peek.Type == TokenType.Litteral)
                                        {
                                            short[] value = ParseLitteral(file.Value.Raw, current.Line, peek.Value, peek.Raw, file.Value.Constants);
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
                                    case Opcode.Set:
                                        instructions.Add((short)current.Opcode);
                                        
                                        // Use the next token to figure out the argument
                                        if (peek.Type == TokenType.Argument)
                                        {
                                            SetMode set_mode = (SetMode)arguments[peek.Value];
                                            if (Enum.IsDefined(typeof(SetMode), set_mode))
                                            {
                                                instructions.Add((short)set_mode);
                                                tokens.MoveNext();
                                                peek = tokens.Current;
                                            }
                                            else
                                            {
                                                Error(file.Value.Raw, current.Line, $"{current.Opcode} must be followed by an argument of type {nameof(SetMode)}! Got: \"{set_mode}\"");
                                            }
                                        }
                                        else
                                        {
                                            Error(file.Value.Raw, current.Line, $"{current.Opcode} must be followed by a {nameof(SetMode)}!");
                                        }
                                        break;
                                    case Opcode.Call:
                                        if (current.Equals(peek) == false && (peek.Type == TokenType.Litteral || peek.Type == TokenType.Label))
                                        {
                                            Opcode op = current.Opcode ?? Opcode.Nop;
                                            
                                            instructions.Add((short)op);

                                            if (peek.Type == TokenType.Label)
                                            {
                                                local_label_uses[instructions.Count] = peek.Value;
                                                instructions.Add(0);
                                                instructions.Add(0);
                                                Log(verbose, ConsoleColor.DarkGreen, $"Added label {peek.Value} using at {instructions.Count:X}");
                                            }
                                            else if (peek.Type == TokenType.Litteral)
                                            {
                                                short[] value = ParseLitteral(file.Value.Raw, current.Line, peek.Value, peek.Raw, file.Value.Constants);

                                                if (value.Length > 2)
                                                {
                                                    Error(file.Value.Raw, current.Line, $"The litteral {peek.Value} does not fit in 24-bits! {current.Opcode} only takes 24-bit arguments!");
                                                }

                                                instructions.Add(value.Length < 2 ? (short)0 : value[1]);
                                                instructions.Add(value[0]);
                                            }

                                            // FIXME: This is to remove generation of weird nop:s at the end of some procs
                                            if (tokens.MoveNext() == false) goto instruction_loop_done;
                                        }
                                        else
                                        {
                                            Error(file.Value.Raw, current.Line, $"{current.Opcode} instruction without any following litteral or label!");
                                        }
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
                                            short[] value = ParseLitteral(file.Value.Raw, current.Line, peek.Value, peek.Raw, file.Value.Constants);
                                            if (value.Length > 1)
                                            {
                                                Error(file.Value.Raw, current.Line, $"{current.Opcode} only takes a single word argument!");
                                            }
                                            
                                            instructions.Add((short)current.Opcode);
                                            instructions.Add(value[0]);

                                            // FIXME: This is to remove generation of weird nop:s at the end of some procs
                                            if (tokens.MoveNext() == false) goto instruction_loop_done;
                                        }
                                        else
                                        {
                                            Error(file.Value.Raw, current.Line, $"{current.Opcode} must be followed by a littera!");
                                        }
                                        break;
                                    default:
                                        instructions.Add((short)(current.Opcode ?? Opcode.Nop));
                                        break;
                                }
                                break;
                            case TokenType.Litteral:
                                {
                                    Log(verbose, $"Litteral {current.Value}");

                                    string evalRes = EvalConstant(file.Value.Raw, current.Line, current.Value, file.Value.Constants, fileConstants, null, out bool delay);

                                    if (delay)
                                    {
                                        // FIXME: Delayed consts can only be w2s!!
                                        local_delayed_const_uses[instructions.Count] = (new Constant($"{proc.Key}_lit_{instructions.Count}", file.Value.Raw, current.Line, current.Value), 2);
                                        instructions.Add(0);
                                        instructions.Add(0);
                                    }
                                    else
                                    {
                                        short[] values = ParseLitteral(file.Value.Raw, current.Line, current.Value, current.Raw, file.Value.Constants);
                                        
                                        for (int i = values.Length - 1; i >= 0; i--)
                                        {
                                            instructions.Add(values[i]);
                                        }
                                    }
                                    break;
                                }
                            case TokenType.Label:
                                if (current.Use == false)
                                {
                                    if (local_labels.TryGetValue(current.Value, out var local_lbl))
                                        Error(file.Value.Raw, current.Line, $"Redining label '{current.Value}'!");

                                    local_labels[current.Value] = instructions.Count;
                                    
                                    Log(verbose, ConsoleColor.DarkCyan, $"Found label def {current.Value} at index: {instructions.Count:X}");
                                }
                                else
                                {
                                    local_label_uses[instructions.Count] = current.Value;

                                    instructions.Add(0);
                                    instructions.Add(0);
                                    
                                    Log(verbose, ConsoleColor.DarkCyan, $"Found label pointer use {current.Value} at index: {instructions.Count:X}");
                                }
                                break;
                            case TokenType.Argument:
                                Error(file.Value.Raw, current.Line, $"Unhandled argument: \"{current.Value}\"!");
                                break;
                        }

                        current = tokens.Current;
                    }
                    instruction_loop_done:

                    offset = instructions.Count;
                    
                    proc_label_instructions[proc.Key] = local_labels;

                    proc_label_uses[proc.Key] = local_label_uses;

                    delayed_const_uses[proc.Key] = local_delayed_const_uses;

                    // We can't use assembledProcs because the key compares file too...
                    if (metadata.ContainsKey(proc.Value.name))
                        Error(file.Value.Raw, proc.Value.line, $"Redefining proc '{proc.Value}'!");

                    assembledProcs[proc.Value] = instructions;

                    procmeta.size = instructions.Count;
                    
                    metadata[proc.Value.name] = procmeta;
                    procMap[proc.Value.name] = proc.Value;
                    
                    Log(verbose, "----------------------");
                }

                verbose = temp_verbose;
            }

            offset = 0;

            if (verbose) Console.WriteLine();
            
            // Place all procedures
            foreach (var asem in assembledProcs)
            {
                if (asem.Key.location_const != null)
                {
                    int location = asem.Key.location ?? ROM_OFFSET;
                    Log(verbose, $"Proc {asem.Key.name} at specified offset: {location:X}");

                    // Shift location to be relative to ROM_START
                    location -= ROM_OFFSET;

                    metadata[asem.Key.name].location = location;
                    // FIXME: Procs can overlap!!!
                }
                else
                {
                    Log(verbose, $"Proc {asem.Key.name} at offset: {offset:X}");

                    metadata[asem.Key.name].location = offset;
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
                // Resolve all delayed constants
                foreach (var delayedConst in delayed_const_uses[proc.Key.name])
                {
                    string evalRes = EvalConstant(delayedConst.Value.constant, proc.Key.file.Constants, fileConstants, metadata, out bool delay);

                    if (delay)
                    {
                        Error(delayedConst.Value.constant.file, delayedConst.Value.constant.line, $"Cannot resolve delayed const '{delayedConst.Value.constant.value}'!");
                    }

                    short[] data = ParseLitteral(delayedConst.Value.constant.file, delayedConst.Value.constant.line, evalRes, false, proc.Key.file.Constants);

                    if (data.Length > delayedConst.Value.size)
                    {
                        Error(delayedConst.Value.constant.file, delayedConst.Value.constant.line, $"Size of delayed const does not match the given loader! Size: {data.Length}, Expected: {delayedConst.Value.size}");
                    }

                    bool sign_ext = data[data.Length - 1] == 0xFFF;
                    for (int i = 0; i < delayedConst.Value.size; i++)
                    {
                        proc.Value[delayedConst.Key + i] = delayedConst.Value.size - 1 - i >= data.Length ? (short)(sign_ext ? 0xFFF : 0) : data[delayedConst.Value.size - 1 - i];
                    }

                    Log(verbose_lit, ConsoleColor.DarkYellow, $"Evaluated delayed constant '{delayedConst.Value.constant.value}' to value '{ToInt(data):X}'");
                }
            }
            
            foreach (var proc in assembledProcs)
            {
                // Resolve all labels
                foreach (var use in proc_label_uses[proc.Key.name])
                {
                    if (proc_label_instructions[proc.Key.name].TryGetValue(use.Value, out int lbl_offset))
                    {
                        short[] offset_inst = IntToShortArray(lbl_offset + metadata[proc.Key.name].location + (executable ? ROM_OFFSET : 0));
                        proc.Value[use.Key] = offset_inst[1];
                        proc.Value[use.Key + 1] = offset_inst[0];
                        Log(verbose_addr, ConsoleColor.DarkCyan, $"{use.Value,-12} matched local at instruction: {metadata[proc.Key.name].location + use.Key:X6} Offset: {lbl_offset + metadata[proc.Key.name].location:X6}");
                    }
                    else if (metadata.TryGetValue(use.Value, out var procMetadata))
                    {
                        short[] offset_inst = IntToShortArray(procMetadata.location + (executable ? ROM_OFFSET : 0));
                        proc.Value[use.Key] = offset_inst[1];
                        proc.Value[use.Key + 1] = offset_inst[0];
                        Log(verbose_addr, ConsoleColor.DarkMagenta, $"{use.Value,-12} matched call at instruction: {metadata[proc.Key.name].location + use.Key:X6} Offset: {procMetadata.location:X6}");
                    }
                    else
                    {
                        Error(metadata[proc.Key.name].source, metadata[proc.Key.name].assembledSource.Procs[proc.Key.name].tokens.Find(t => t.Value == use.Value).Line, $"Could not solve label! {use.Value} in proc {proc.Key.name}");
                    }
                }
                
                // Set breakpoints in metadata
                if (breakpoints.ContainsKey(proc.Key))
                {
                    metadata[proc.Key.name].breaks = breakpoints[proc.Key];
                }
            }
            
            if (verbose) Console.WriteLine();

            short[] compiledInstructions = new short[ROM_SIZE];

            int usedInstructions = 0;

            foreach (var proc in assembledProcs)
            {
                usedInstructions += proc.Value.Count;

                proc.Value.CopyTo(compiledInstructions, metadata[proc.Key.name].location);
            }

            success = true;

            return new LibFile(compiledInstructions, metadata.Values.ToArray(), usedInstructions);
        }

        static string EvalConstant(RawFile file, int line, string constant, Dictionary<string, Constant> constants, Dictionary<string, Dictionary<string, Constant>> fileConstants, Dictionary<string, ProcMetadata> metadata, out bool delay)
        {
            return EvalConstant(new Constant("lit:" + constant, file, line, constant), constants, fileConstants, metadata, out delay);
        }

        static string EvalConstant(Constant expr, Dictionary<string, Constant> constants, Dictionary<string, Dictionary<string, Constant>> fileConstants, Dictionary<string, ProcMetadata> metadata, out bool delay)
        {
            string result = null;

            Match constant_expression = arith_expr.Match(expr.value);
            Match external_expression = extern_expr.Match(expr.value);
            Match sizeof_expression = sizeof_expr.Match(expr.value);
            Match endof_expression = endof_expr.Match(expr.value);
            Match proc_expression = label.Match(expr.value);
            Match sizeof_proc_expression = sizeof_proc_expr.Match(expr.value);
            Match endof_proc_expression = endof_proc_expr.Match(expr.value);
            Match auto_expression = auto.Match(expr.value);

            if (IsLitteral(expr.file, expr.line, expr.value, constants))
            {
                delay = false;
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
                            stack.Push(EvalConstant(new Constant(expr.name + $"_arith{num++}", expr.file, expr.line, token), constants, fileConstants, metadata, out delay));

                            if (delay)
                            {
                                // We need to delay evaluation!
                                return expr.value;
                            }
                            break;
                    }
                }

                if (stack.Count > 1)
                {
                    Error(expr.file, expr.line, $"Could not evaluate expression '{constant_expression.Groups[1].Value}', stack: {{{String.Join(", ", stack)}}}");
                }

                delay = false;

                return stack.Pop();
            }
            else if (expr.value.StartsWith("#"))
            {
                string substring = expr.value.Substring(1);

                // NOTE: Should this really be valid?
                // Right now we have it to allow constant expressions in macros
                // macro(#2) could then use that as a macro and we would be sure that it is the constant number
                // that is being referenced and not the local. We probably want somehting smarter that can detect and remove the precceding '#'
                // Maybe some notation in the macro definition to tell the preprossesor to remove the preceding '#'
                if (IsNumber(expr.file, expr.line, substring))
                {
                    delay = false;
                    return substring;
                }
                else
                {
                    return EvalConstant(constants[substring], constants, fileConstants, metadata, out delay);
                }
            }
            else if (external_expression.Success)
            {
                // We should resolve the external variable
                if (external_expression.Groups[1].Success)
                {
                    // Use the name in the braces as key in the globalConstants

                    if (globalConstants.TryGetValue(external_expression.Groups[1].Value, out Constant value))
                    {
                        return EvalConstant(value, fileConstants[Path.GetFileName(value.file.path)], fileConstants, metadata, out delay);
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
                        return EvalConstant(value, fileConstants[Path.GetFileName(value.file.path)], fileConstants, metadata, out delay);
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

                delay = false;
                return $"{c.Length}";
            }
            else if (endof_expression.Success)
            {
                AutoConst c = autoConstants[endof_expression.Groups[1].Value];

                delay = false;
                return $"0x{c.Location + c.Length:X6}";
            }
            else if (proc_expression.Success)
            {
                // FIXME: Should the optional star at the end of the proc affect the outcome?
                if (metadata == null)
                {
                    // Delay evaluation to when we know this!
                    delay = true;
                    return expr.value;
                }
                else
                {
                    delay = false;
                    string proc_name = proc_expression.Groups[1].Value;
                    if (metadata.TryGetValue(proc_name, out var meta))
                    {
                        // NOTE: This will only work for executable!! But that's the only thing we support atm!
                        return $"{meta.location + ROM_OFFSET}";
                    }
                    else
                    {
                        Error(expr.file, expr.line, $"Could not solve proc '{proc_name}' in const '{expr.value}'");
                    }
                }
            }
            else if (sizeof_proc_expression.Success)
            {
                if (metadata == null)
                {
                    // Delay evaluation to when we know this!
                    delay = true;
                    return expr.value;
                }
                else
                {
                    delay = false;
                    string proc_name = sizeof_proc_expression.Groups[1].Value;
                    if (metadata.TryGetValue(proc_name, out var meta))
                    {
                        return $"{meta.size}";
                    }
                    else
                    {
                        Error(expr.file, expr.line, $"Could not solve proc '{proc_name}' in const '{expr.value}'");
                    }
                }
            }
            else if (endof_proc_expression.Success)
            {
                if (metadata == null)
                {
                    // Delay evaluation to when we know this!
                    delay = true;
                    return expr.value;
                }
                else
                {
                    delay = false;
                    string proc_name = endof_proc_expression.Groups[1].Value;
                    if (metadata.TryGetValue(proc_name, out var meta))
                    {
                        return $"{meta.location + meta.size + ROM_OFFSET}";
                    }
                    else
                    {
                        Error(expr.file, expr.line, $"Could not solve proc '{proc_name}' in const '{expr.value}'");
                    }
                }
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
                    
                    Log(verbose, ConsoleColor.Cyan, $"Defined auto var '{expr.name}' of size {size} to addr {value}");

                    int end = autoVars + size;

                    AutoConst c = new AutoConst(expr.name, value, autoVars, size);

                    autoConstants[expr.name] = c;
                    constants[expr.name + ".size"] = new Constant(expr.name + ".size", expr.file, expr.line, $"0x{(size >> 12) & 0xFFF:X3}_{size & 0xFFF:X3}");
                    constants[expr.name + ".end"] = new Constant(expr.name + ".end", expr.file, expr.line, $"0x{(end >> 12) & 0xFFF:X3}_{end & 0xFFF:X3}");

                    return c;
                }

                if (autoConstants.TryGetValue(expr.name, out AutoConst autoConst))
                {
                    Log(verbose, ConsoleColor.Green, $"Using already allocated auto '{autoConst.Name}' with value {autoConst.Value}");
                    delay = false;
                    return autoConst.Value;
                }
                else if (const_expr.IsMatch(auto_expression.Groups[1].Value))
                {
                    Constant next = new Constant(expr.name + "_auto", expr.file, expr.line, auto_expression.Groups[1].Value);

                    // FIXME: Here there is a bug hiding. If the auto size eval needs to be delayed we cant do that...
                    int size = ToInt(ParseLitteral(expr.file, expr.line, EvalConstant(next, fileConstants[Path.GetFileName(next.file.path)], fileConstants, metadata, out delay), false, constants));

                    AutoConst autoc = allocAutoConst(size);

                    return autoc.Value;
                }
                else
                {
                    // Just do a substitutuion
                    int size = ToInt(ParseLitteral(expr.file, expr.line, EvalConstant(expr.file, expr.line, auto_expression.Groups[1].Value, constants, fileConstants, metadata, out delay), false, constants));

                    AutoConst autoc = allocAutoConst(size);

                    return autoc.Value;
                }
            }
            else
            {
                Error(expr.file, expr.line, $"Could not evaluate const_expr {expr.value} for constant {expr.name}");
            }

            delay = false;
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

        static short[] ParseLitteral(RawFile file, int line, string litteral, bool raw, Dictionary<string, Constant> constants)
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
                value = ParseString(file, line, litteral, raw);
            }
            else if (constants.TryGetValue(litteral, out constant))
            {
                value = ParseLitteral(file, line, constant.value, raw, constants);
            }
            else if (globalConstants.TryGetValue(litteral, out constant))
            {
                value = ParseNumber(file, line, constant.value);
            }

            return value;
        }

        static bool IsNumber(RawFile file, int line, string litteral)
        {
            // FIXME: We will say that "____" is a number!!!

            if (litteral.StartsWith("0x"))
            {
                return litteral.IsOnly(2, "_0123456789abcdefABCDEF".ToCharArray());
            }
            else if (litteral.StartsWith("8x"))
            {
                return litteral.IsOnly(2, "_01234567".ToCharArray());
            }
            else if (litteral.StartsWith("0b"))
            {
                return litteral.IsOnly(2, "_01".ToCharArray());
            }
            else
            {
                // Remove the optional minus
                int offset = litteral.StartsWith("-") ? 1 : 0;
                return litteral.IsOnly(offset, "_0123456789".ToCharArray());
            }
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
                data = Array.ConvertAll(charEncoding.GetBytes(litteral.Substring(1, litteral.Length - 2)), b => (short)b);
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
        
        static void Log(bool condition, string message)
        {
            if (condition)
            {
                Console.WriteLine(message);
            }
        }

        static void Log(bool condition, ConsoleColor color, string message)
        {
            if (condition)
            {
                ConsoleColor prevColor = Console.ForegroundColor;

                Console.ForegroundColor = color;
                Console.WriteLine(message);
                Console.ForegroundColor = prevColor;
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
    }
}