using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using VM12Opcode;
using VM12Util;

namespace FastVM12Asm
{
    public struct Trace
    {
        public string FileData;
        public string FilePath;
        public int StartLine, EndLine;
        public StringRef TraceString;

        public Trace(FileData file, int line, StringRef traceString)
        {
            FileData = file.Data;
            FilePath = file.Path;
            StartLine = line;
            EndLine = line;
            TraceString = traceString;
        }
        
        public static Trace FromToken(Token tok)
        {
            Trace trace;
            trace.FileData = tok.Data.Data;
            trace.FilePath = tok.Path;
            trace.StartLine = tok.Line;
            trace.EndLine = tok.Line;
            trace.TraceString = tok.Data;
            return trace;
        }
        public static Trace FromToken(Token start, Token end)
        {
            Trace trace;
            trace.FileData = start.Data.Data;
            trace.FilePath = start.Path;
            trace.StartLine = start.Line;
            trace.EndLine = end.Line;
            trace.TraceString = new StringRef(start.Data.Data, start.Data.Index, end.Data.Length + (end.Data.Index - start.Data.Index));
            return trace;
        }

        internal static Trace FromTrace(Trace start, Trace end)
        {
            Trace trace;
            trace.FileData = start.FileData;
            trace.FilePath = start.FilePath;
            trace.StartLine = start.StartLine;
            trace.EndLine = end.EndLine;
            trace.TraceString = new StringRef(start.FileData, start.TraceString.Index, end.TraceString.Length + (end.TraceString.Index - start.TraceString.Index));
            return trace;
        }
    }

    public struct SizedNumber
    {
        public int Number;
        public int Size;

        public SizedNumber(int number, int size)
        {
            Number = number;
            Size = size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CalcSize(int number, int prevSize) => Math.Max(prevSize, (Util.Log2(number) / 12) + 1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SizedNumber operator +(SizedNumber a, SizedNumber b)
        {
            SizedNumber res;
            res.Number = a.Number + b.Number;
            res.Size = CalcSize(res.Number, Math.Max(a.Size, b.Size));
            return res;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SizedNumber operator -(SizedNumber a, SizedNumber b)
        {
            SizedNumber res;
            res.Number = a.Number - b.Number;
            res.Size = CalcSize(res.Number, Math.Max(a.Size, b.Size));
            return res;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SizedNumber operator *(SizedNumber a, SizedNumber b)
        {
            SizedNumber res;
            res.Number = a.Number * b.Number;
            res.Size = CalcSize(res.Number, Math.Max(a.Size, b.Size));
            return res;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SizedNumber operator /(SizedNumber a, SizedNumber b)
        {
            SizedNumber res;
            res.Number = a.Number / b.Number;
            res.Size = CalcSize(res.Number, Math.Max(a.Size, b.Size));
            return res;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SizedNumber operator %(SizedNumber a, SizedNumber b)
        {
            SizedNumber res;
            res.Number = a.Number % b.Number;
            res.Size = CalcSize(res.Number, Math.Max(a.Size, b.Size));
            return res;
        }

        public static explicit operator SizedNumber((int Number, int Size) v) => new SizedNumber() { Number = v.Number, Size = v.Size };

        public void Deconstruct(out int number, out int size)
        {
            number = Number;
            size = Size;
        }

        public static SizedNumber From(int number) => new SizedNumber(number, CalcSize(number, 0));

        public override string ToString()
        {
            return $"({Number}, Size: {Size})";
        }
    }

    public enum ConstOperation
    {
        Unknown,
        Addition,
        Subtraction,
        Multiplication,
        Division,
        Modulo,
    }

    public enum CompoundType
    {
        NumberLitteral,
        CharLitteral,
        Operation,
        LabelRef,
        Ident,
        Sizeof,
    }

    public struct CompoundElement
    {
        public Trace Trace;
        public CompoundType Type;
        public ConstOperation Operation;
        public SizedNumber NumberLit;
        public char CharLit;
        public StringRef StringRef;
    }

    public enum ConstantExprType
    {
        Unknown,
        NumberLit,
        CharLit,
        StringLit,
        Auto,
        Extern,
        Compound,
    }

    public class ConstantExpression
    {
        public Trace Trace;

        public bool IsGlobal;
        public ConstantExprType Type;

        public bool Delayed;

        public SizedNumber NumberLit;
        public char CharLit;
        public StringRef StringLit;
        public List<CompoundElement> CompoundExpr;

        // This is not really needed with the CompoundExpr member....
        public ConstantExpression AutoExpr;

        public ConstantExpression() { }
    }

    public enum InstructionType
    {
        Unknown,
        RawOpcode,
        WordArgOpcode,
        DwordArgOpcode,
        LabelArgOpcode,
        IdentArgOpcode,
        ConstExprArgOpcode,

        AutoStringLoad,

        String,
        RawString,
        Label,
        LabelRef,
        Number,
        Jump,
        Call,
        Identifier,
        RawWord,
        ConstExpr,
    }

    public struct Instruction
    {
        public Trace Trace;
        public InstructionType Type;
        public Opcode Opcode;
        public int Arg;
        public StringRef StrArg;
        public ConstantExpression ConstantArg;
        public int VarSizeArgSize;
    }

    public class TokenQueue
    {
        public Token[] Tokens;
        public int Index;

        public int Count => Tokens.Length - Index;

        public TokenQueue(List<Token> tokens)
        {
            // NOTE: We could get away from this and just use the list or ToArray directly
            // Using an array would allow us to do ref return some stuff
            Tokens = tokens.Where(tok => tok.Type != TokenType.Comment).ToArray();
            Index = 0;
        }

        public Token Dequeue()
        {
            if (Index >= Tokens.Length) throw new InvalidOperationException("Token queue is empty");
            return Tokens[Index++];
        }

        public Token Peek()
        {
            if (Index >= Tokens.Length) throw new InvalidOperationException("Token queue is empty");
            return Tokens[Index];
        }

        internal void Revert(int tokens)
        {
            if (Index - tokens < 0) throw new InvalidOperationException("Cannot revert past the start!");
            Index -= tokens;
        }
    }

    public class ParsedFile
    {
        public FileData File;
        public HashSet<StringRef> Flags = new HashSet<StringRef>();
        public List<Token> IncludeFiles = new List<Token>();
        public Dictionary<StringRef, ConstantExpression> ConstExprs = new Dictionary<StringRef, ConstantExpression>();
        public Dictionary<Token, List<Instruction>> Procs;
        public Dictionary<StringRef, int> ProcLocations;
        // All of the strings that where referenced
        public List<StringRef> AutoStrings;

        public ParsedFile(FileData file, HashSet<StringRef> flags, List<Token> includeFiles, Dictionary<StringRef, ConstantExpression> constExprs, Dictionary<Token, List<Instruction>> procs, Dictionary<StringRef, int> procLocations, List<StringRef> autoStrings)
        {
            File = file;
            Flags = flags;
            IncludeFiles = includeFiles;
            ConstExprs = constExprs;
            Procs = procs;
            ProcLocations = procLocations;
            AutoStrings = autoStrings;
        }

        public override string ToString() => $"ParsedFile{{{Path.GetFileName(File.Path)}}}";
    }

    public class Parser
    {
        public static Dictionary<string, Opcode> RawInstructions = new Dictionary<string, Opcode>()
        {
            { "nop", Opcode.Nop },
            { "pop", Opcode.Pop },
            // These are duplicaty parsed as registers!
            { "fp", Opcode.Fp },
            { "pc", Opcode.Pc },
            { "pt", Opcode.Pt },

            { "swap", Opcode.Swap },
            { "lswap", Opcode.Swap_l },
            { "slswap", Opcode.Swap_s_l },
            { "dup", Opcode.Dup },
            { "ldup", Opcode.Dup_l },
            { "over", Opcode.Over },
            { "lover", Opcode.Over_l_l },
            { "lovers", Opcode.Over_l_s },
            { "soverl", Opcode.Over_s_l },
            { "add", Opcode.Add },
            { "ladd", Opcode.Add_l },
            { "sub", Opcode.Sub },
            { "lsub", Opcode.Sub_l },
            { "neg", Opcode.Neg },
            { "lneg", Opcode.Neg_l },
            { "or", Opcode.Or },
            { "xor", Opcode.Xor },
            { "and", Opcode.And },
            { "not", Opcode.Not },
            { "css", Opcode.C_ss },
            { "cse", Opcode.C_se },
            { "ccl", Opcode.C_cl },
            { "cflp", Opcode.C_flp },
            { "rtcl", Opcode.Rot_l_c },
            { "rtcr", Opcode.Rot_r_c },
            { "mul", Opcode.Mul },
            { "div", Opcode.Div },
            { "eni", Opcode.Eni },
            { "dsi", Opcode.Dsi },
            { "hlt", Opcode.Hlt },
            { "call.v", Opcode.Call_v },
            { "ret", Opcode.Ret },
            { "ret1", Opcode.Ret_1 },
            { "ret2", Opcode.Ret_2 },
            { "retv", Opcode.Ret_v },
            { "memc", Opcode.Memc },
            { "muladd", Opcode.Mul_Add },
            { "lmuladd", Opcode.Mul_Add_l },

            { "mul2", Opcode.Mul_2 },
            { "lmul", Opcode.Mul_l },
            { "lmul2", Opcode.Mul_2_l },
            { "ldiv", Opcode.Div_l },
            { "mod", Opcode.Mod },
            { "lmod", Opcode.Mod_l },
            { "write", Opcode.Write },
            { "read", Opcode.Read },

            { "clz", Opcode.Clz },
            { "ctz", Opcode.Ctz },
            { "selz", Opcode.Selz },
            { "selgz", Opcode.Selgz },
            { "selge", Opcode.Selge },
            { "selc", Opcode.Selc },

            { "brk", Opcode.Brk },

            { "strt_coproc", Opcode.Start_coproc },
            { "hlt_coproc", Opcode.Hlt_coproc },
            { "int_coproc", Opcode.Int_coproc },

            { "int_snd_chip", Opcode.Int_snd_chip },

            { "fc", Opcode.Fc },
            { "fcb", Opcode.Fc_b },
        };

        public static Dictionary<string, JumpMode> JmpArguments = new Dictionary<string, JumpMode>()
        {
            { "jmp", JumpMode.Jmp },
            { "jz",   JumpMode.Z },
            { "jnz",  JumpMode.Nz },
            { "jc",   JumpMode.C },
            { "jcz",  JumpMode.Cz },
            { "jgz",  JumpMode.Gz },
            { "jlz",  JumpMode.Lz },
            { "jge",  JumpMode.Ge },
            { "jle",  JumpMode.Le },
            { "jeq",  JumpMode.Eq },
            { "jneq", JumpMode.Neq },
            { "jro",  JumpMode.Ro },

            { "jzl",   JumpMode.Z_l },
            { "jnzl",  JumpMode.Nz_l },
            { "jgzl",  JumpMode.Gz_l },
            { "jlzl",  JumpMode.Lz_l },
            { "jgel",  JumpMode.Ge_l },
            { "jlel",  JumpMode.Le_l },
            { "jeql",  JumpMode.Eq_l },
            { "jneql", JumpMode.Neq_l },
            { "jrol",  JumpMode.Ro_l },
        };

        public static Dictionary<string, SetMode> SetArguments = new Dictionary<string, SetMode>()
        {
            { "setz",    SetMode.Z },
            { "setnz",   SetMode.Nz },
            { "setc",    SetMode.C },
            { "setcz",   SetMode.Cz },
            { "setgz",   SetMode.Gz },
            { "setge",   SetMode.Ge },
            { "setlz",   SetMode.Lz },
            { "setle",   SetMode.Le },
            { "lsetz",   SetMode.Z_l },
            { "lsetnz",  SetMode.Nz_l },
            { "lsetc",   SetMode.C_l },
            { "lsetcz",  SetMode.Cz_l },
            { "lsetgz",  SetMode.Gz_l },
            { "lsetge",  SetMode.Ge_l },
            { "lsetlz",  SetMode.Lz_l },
            { "lsetle",  SetMode.Le_l },
        };

        private readonly FileData File;
        private readonly TokenQueue Tokens;
        
        public Parser(FileData file, List<Token> tokens)
        {
            File = file;
            // FIXME: Custom Queue that doesn't copy the memory!
            Tokens = new TokenQueue(tokens);
        }

        public void Error(Token tok, string error)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"In file {Path.GetFileName(File.Path)} on line {tok.Line} character {tok.LineCharIndex}: '{tok.GetContents()}'");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"    {error}");
            Console.ReadLine();
            Environment.Exit(1);
        }

        public ParsedFile Parse()
        {
            HashSet<StringRef> Flags = new HashSet<StringRef>();

            List<Token> IncludeFiles = new List<Token>(100);

            Dictionary<StringRef, ConstantExpression> ConstExprs = new Dictionary<StringRef, ConstantExpression>(100);

            Dictionary<Token, List<Instruction>> Procs = new Dictionary<Token, List<Instruction>>();

            Dictionary<StringRef, int> ProcLocations = new Dictionary<StringRef, int>();

            List<StringRef> AutoStrings = new List<StringRef>();
            
            bool isGlobal = false;

            // While there are tokens left
            while (Tokens.Count > 0)
            {
                var tok = Tokens.Dequeue();
                switch (tok.Type)
                {
                    case TokenType.Flag:
                        {
                            if (tok.ContentsMatch("!global"))
                            {
                                isGlobal = true;
                            }
                            else if (tok.ContentsMatch("!private"))
                            {
                                isGlobal = false;
                            }
                            else if (tok.ContentsMatch("!noprintout") || tok.ContentsMatch("!noprintouts"))
                            {
                                Flags.Add(tok.Data);
                            }
                            else if (tok.ContentsMatch("!no_map"))
                            {
                                Flags.Add(tok.Data);
                            }
                            else Error(tok, "Unknown flag!");
                            break;
                        }
                    case TokenType.Open_angle:
                        {
                            var identTok = Tokens.Dequeue();
                            if (identTok.Type != TokenType.Identifier) Error(identTok, "Expected constant name!");

                            var equalsTok = Tokens.Dequeue();
                            if (equalsTok.Type != TokenType.Equals) Error(equalsTok, "Expected equals!");

                            var constExpr = ParseConstExpr(Tokens);
                            constExpr.IsGlobal = isGlobal;

                            // Dequeue close angle
                            var closeAngleTok = Tokens.Dequeue();
                            if (closeAngleTok.Type != TokenType.Close_angle) Error(closeAngleTok, "Expected '>'!");

                            // FIXME: We might want to do flag validation here so that e.g. an extern const is not global

                            constExpr.Trace = Trace.FromToken(tok, closeAngleTok);

                            StringRef constName = identTok.Data;
                            if (ConstExprs.TryGetValue(constName, out _) == true)
                                Error(identTok, "A constant with the same name already exists!");

                            ConstExprs.Add(constName, constExpr);

                            //Console.ForegroundColor = Program.GetColor(TokenType.Open_angle);
                            //Console.WriteLine($"Constant: '{identTok.GetContents()}', public: '{isGlobal}', extern: '{flags.HasFlag(ConstFlags.Extern)}'");

                            break;
                        }
                    case TokenType.And:
                        {
                            var nameTok = Tokens.Dequeue();
                            if (nameTok.Type != TokenType.Identifier) Error(nameTok, "Expected identifier!");

                            var fileNameTok = Tokens.Dequeue();
                            if (fileNameTok.Type != TokenType.Identifier) Error(fileNameTok, "Expected file name!");
                            if (fileNameTok.EndsWith(".12asm") == false)  Error(fileNameTok, "Expected file name!");

                            IncludeFiles.Add(fileNameTok);

                            //Console.ForegroundColor = Program.GetColor(TokenType.And);
                            //Console.WriteLine($"File import: '{fileName}'");
                            break;
                        }
                    case TokenType.Label:
                        {
                            List<Instruction> ProcContent = new List<Instruction>(100);

                            int? location = null;
                            if (Tokens.Peek().Type == TokenType.ProcLocation)
                            {
                                var locationTok = Tokens.Dequeue();
                                var res = locationTok.Data.Substring(1).ParseNumber();
                                if (res.Size > 2) Error(locationTok, "Specified location is too big!");
                                location = res.Number;
                            }

                            var peek = Tokens.Peek();
                            int currentLine = peek.Line;
                            while (peek.Line == currentLine || peek.Type == TokenType.StartOfLineTab)
                            {
                                if (peek.Type == TokenType.StartOfLineTab)
                                {
                                    currentLine = peek.Line;
                                    Tokens.Dequeue();
                                    if (Tokens.Count > 0)
                                        peek = Tokens.Peek();
                                    else break;
                                    continue;
                                }

                                Instruction inst = default;

                                // Here we get a stream of tokens that we should parse!
                                var instTok = Tokens.Dequeue();
                                if (instTok.Type == TokenType.Identifier)
                                {
                                    // Here we want to figure out what kind of instruction this is
                                    if (instTok.ContentsMatch("load") || instTok.ContentsMatch("loadl"))
                                    {
                                        // Here we look at the next character and figure out what that should be!
                                        var loadTok = Tokens.Dequeue();
                                        if (loadTok.Type == TokenType.Number_litteral)
                                        {
                                            // Here we are loading a local!
                                            if (instTok.GetLastChar() == 'l')
                                                inst.Opcode = Opcode.Load_local_l;
                                            else inst.Opcode = Opcode.Load_local;

                                            var (num, size) = loadTok.ParseNumber();
                                            if (size > 1) Error(loadTok, "Local index too big!");

                                            inst.Type = InstructionType.WordArgOpcode;
                                            inst.Arg = num;
                                            inst.Trace = Trace.FromToken(instTok, loadTok);
                                        }
                                        else if (loadTok.Type == TokenType.Numbersign)
                                        {
                                            // FIXME! This will not work for constant expressions!
                                            var litTok = Tokens.Dequeue();
                                            if (litTok.Type == TokenType.Number_litteral)
                                            {
                                                var (num, size) = litTok.ParseNumber();
                                                inst.Arg = num;

                                                if (instTok.GetLastChar() == 'l')
                                                {
                                                    if (size > 2) Error(litTok, "Number too big!");
                                                    inst.Type = InstructionType.DwordArgOpcode;
                                                    inst.Opcode = Opcode.Load_lit_l;
                                                }
                                                else
                                                {
                                                    if (size > 1) Error(litTok, "Number too big!");
                                                    inst.Type = InstructionType.WordArgOpcode;
                                                    inst.Opcode = Opcode.Load_lit;
                                                }
                                            }
                                            else if (litTok.Type == TokenType.Identifier)
                                            {
                                                if (instTok.GetLastChar() == 'l')
                                                    inst.Opcode = Opcode.Load_lit_l;
                                                else inst.Opcode = Opcode.Load_lit;
                                                inst.VarSizeArgSize = inst.Opcode == Opcode.Load_lit_l ? 2 : 1;
                                                // FIXME: Specifiy the size we expect!!
                                                inst.Type = InstructionType.IdentArgOpcode;
                                                inst.StrArg = litTok.Data;
                                            }
                                            else if (litTok.Type == TokenType.Open_paren)
                                            {
                                                // Go back to the open_paren token so that the constant expression parsing will work correctly
                                                Tokens.Revert(1);
                                                
                                                inst.Type = InstructionType.ConstExprArgOpcode;
                                                inst.ConstantArg = ParseConstExpr(Tokens);
                                                if (instTok.GetLastChar() == 'l')
                                                    inst.Opcode = Opcode.Load_lit_l;
                                                else inst.Opcode = Opcode.Load_lit;
                                                inst.VarSizeArgSize = inst.Opcode == Opcode.Load_lit_l ? 2 : 1;
                                            }
                                            else Error(litTok, $"Unknown load token!");

                                            inst.Trace = Trace.FromToken(instTok, litTok);
                                        }
                                        else if (loadTok.Type == TokenType.Label)
                                        {
                                            inst.Opcode = Opcode.Load_lit_l;
                                            inst.Type = InstructionType.LabelArgOpcode;
                                            inst.StrArg = loadTok.Data;
                                            inst.Trace = Trace.FromToken(instTok, loadTok);
                                        }
                                        else if (loadTok.Type == TokenType.Register)
                                        {
                                            if (loadTok.ContentsMatch("[SP]"))
                                            {
                                                inst.Type = InstructionType.RawOpcode;
                                                if (instTok.GetLastChar() == 'l')
                                                    inst.Opcode = Opcode.Load_sp_l;
                                                else inst.Opcode = Opcode.Load_sp;
                                                inst.Trace = Trace.FromToken(instTok, loadTok);
                                            }
                                            else Error(loadTok, "Can only load using the address at [SP]!");
                                        }
                                        else if (loadTok.Type == TokenType.Char_litteral)
                                        {
                                            if (instTok.GetLastChar() == 'l') Error(loadTok, "Cannot loadl a char litteral!");
                                            inst.Opcode = Opcode.Load_lit;
                                            inst.Type = InstructionType.WordArgOpcode;
                                            inst.Arg = loadTok.GetChar(1);
                                            inst.Trace = Trace.FromToken(instTok, loadTok);
                                        }
                                        else if (loadTok.Type == TokenType.String_litteral)
                                        {
                                            AutoStrings.Add(loadTok.Data);

                                            inst.Type = InstructionType.AutoStringLoad;
                                            // We put the string ref here so that we can later get the label from a de-duplicated map
                                            inst.StrArg = loadTok.Data;
                                            inst.Opcode = Opcode.Load_lit_l;
                                            inst.Trace = Trace.FromToken(instTok, loadTok);
                                        }
                                        else Error(loadTok, "Cannot load this!");
                                    }
                                    else if (instTok.ContentsMatch("store") || instTok.ContentsMatch("storel"))
                                    {
                                        var storeTok = Tokens.Dequeue();
                                        if (storeTok.Type == TokenType.Number_litteral)
                                        {
                                            // Here we are storing a local!
                                            if (instTok.GetLastChar() == 'l')
                                                inst.Opcode = Opcode.Store_local_l;
                                            else inst.Opcode = Opcode.Store_local;

                                            inst.Type = InstructionType.WordArgOpcode;

                                            var (num, size) = storeTok.ParseNumber();
                                            if (size > 1) Error(storeTok, "Local index too big!");

                                            inst.Arg = num;
                                            inst.Trace = Trace.FromToken(instTok, storeTok);
                                        }
                                        else if (storeTok.Type == TokenType.Numbersign)
                                        {
                                            // FIXME: This requires this loop to be able to output more than one instructions at the time!
                                            // Or that we implement a Store_lit and Store_lit_l instruction
                                            Error(instTok, "Not implemented yet!");

                                            /**
                                            // FIXME! This will not work for constant expressions!
                                            var litTok = Tokens.Dequeue();
                                            if (litTok.Type == TokenType.Number_litteral)
                                            {
                                                var (num, size) = litTok.ParseNumber();
                                                inst.Arg = num;

                                                if (instTok.GetLastChar() == 'l')
                                                {
                                                    if (size > 2) Error(litTok, "Number too big!");
                                                    inst.Type = InstructionType.DwordArgOpcode;
                                                    //inst.Opcode = Opcode.Store_lit_l;
                                                }
                                                else
                                                {
                                                    if (size > 1) Error(litTok, "Number too big!");
                                                    inst.Type = InstructionType.WordArgOpcode;
                                                    //inst.Opcode = Opcode.Store_lit;
                                                }
                                            }
                                            else if (litTok.Type == TokenType.Identifier)
                                            {
                                                // FIXME
                                                Error(instTok, "Not implemented yet!");
                                            }
                                            else Error(litTok, "Unknown store token!");

                                            inst.Trace = Trace.FromToken(instTok, litTok);
                                            */
                                        }
                                        else if (storeTok.Type == TokenType.Register)
                                        {
                                            if (storeTok.ContentsMatch("[SP]"))
                                            {
                                                inst.Type = InstructionType.RawOpcode;
                                                if (instTok.GetLastChar() == 'l')
                                                    inst.Opcode = Opcode.Store_sp_l;
                                                else inst.Opcode = Opcode.Store_sp;

                                                inst.Trace = Trace.FromToken(instTok, storeTok);
                                            }
                                            else Error(storeTok, "Can only store using the address at [SP]!");
                                        }
                                        else Error(storeTok, "Cannot store this!");
                                    }
                                    else if (instTok.StartsWith("set") || instTok.StartsWith("lset"))
                                    {
                                        if (instTok.ContentsMatch("set"))
                                        {
                                            // Here we handle the 'set [SP]' and 'set [FP]' instructions
                                            var regTok = Tokens.Dequeue();
                                            if (regTok.Type != TokenType.Register) Error(regTok, "Must be either [SP] or [FP]!");

                                            if (regTok.ContentsMatch("[SP]"))
                                            {
                                                inst.Type = InstructionType.RawOpcode;
                                                inst.Opcode = Opcode.Set_sp;
                                            }
                                            else if (regTok.ContentsMatch("[FP]"))
                                            {
                                                inst.Type = InstructionType.RawOpcode;
                                                inst.Opcode = Opcode.Set_fp;
                                            }
                                            else Error(regTok, "Must be either [SP] or [FP]!");

                                            inst.Trace = Trace.FromToken(instTok, regTok);
                                        }
                                        else
                                        {
                                            // This is for the set.. (setz, setnz) family of instructions
                                            bool found = false;
                                            foreach (var setarg in SetArguments)
                                            {
                                                if (instTok.ContentsMatch(setarg.Key))
                                                {
                                                    found = true;
                                                    inst.Type = InstructionType.WordArgOpcode;
                                                    inst.Arg = (int)setarg.Value;
                                                    inst.Opcode = Opcode.Set;
                                                    inst.Trace = Trace.FromToken(instTok);
                                                    break;
                                                }
                                            }

                                            if (found == false) Error(instTok, "Unknown set mode!");
                                        }
                                    }
                                    else if (instTok.StartsWith("j"))
                                    {
                                        // This is for jump instructions
                                        bool found = false;
                                        foreach (var jmparg in JmpArguments)
                                        {
                                            if (instTok.ContentsMatch(jmparg.Key))
                                            {
                                                found = true;
                                                inst.Type = InstructionType.Jump;
                                                inst.Arg = (int)jmparg.Value;
                                                inst.Opcode = Opcode.Jmp;

                                                // Parse the jmp label
                                                var labelTok = Tokens.Dequeue();
                                                if (labelTok.Type != TokenType.Label) Error(labelTok, "Can only jump to labels!");

                                                inst.StrArg = labelTok.Data;

                                                inst.Trace = Trace.FromToken(instTok, labelTok);
                                                break;
                                            }
                                        }

                                        if (found == false) Error(instTok, "Unknown jump mode!");
                                    }
                                    else if (instTok.ContentsMatch("inc") || instTok.ContentsMatch("linc") ||
                                        instTok.ContentsMatch("dec") || instTok.ContentsMatch("ldec"))
                                    {
                                        var incPeek = Tokens.Peek();
                                        if (incPeek.Type == TokenType.Number_litteral)
                                        {
                                            // This is a inc_local inst
                                            Tokens.Dequeue();

                                            var (num, size) = incPeek.ParseNumber();
                                            if (size > 1) Error(incPeek, "Local index too big!");

                                            inst.Type = InstructionType.WordArgOpcode;
                                            inst.Arg = num;

                                            if (instTok.GetFirstChar() == 'l')
                                                if (instTok.GetChar(1) == 'i')
                                                    inst.Opcode = Opcode.Inc_local_l;
                                                else inst.Opcode = Opcode.Dec_local_l;
                                            else
                                                if (instTok.GetFirstChar() == 'i')
                                                    inst.Opcode = Opcode.Inc_local;
                                                else inst.Opcode = Opcode.Dec_local;
                                            
                                            inst.Trace = Trace.FromToken(instTok, incPeek);
                                        }
                                        else
                                        {
                                            // This is a raw inc inst
                                            inst.Type = InstructionType.RawOpcode;
                                            if (instTok.GetFirstChar() == 'l')
                                                if (instTok.GetChar(1) == 'i')
                                                    inst.Opcode = Opcode.Inc_l;
                                                else inst.Opcode = Opcode.Dec_l;
                                            else
                                                if (instTok.GetFirstChar() == 'i')
                                                    inst.Opcode = Opcode.Inc;
                                                else inst.Opcode = Opcode.Dec;

                                            inst.Trace = Trace.FromToken(instTok);
                                        }
                                    }
                                    else if (instTok.ContentsMatch("ladd") && Tokens.Count > 0 && Tokens.Peek().Type == TokenType.Register)
                                    {
                                        // FIXME: Change the inst name so we don't have to do this!
                                        var regTok = Tokens.Dequeue();
                                        var constTok = Tokens.Dequeue();
                                        if (regTok.ContentsMatch("[SP]") == false || constTok.Type != TokenType.Numbersign)
                                        {
                                            // This is not the right thing...
                                            Tokens.Revert(2);
                                            inst.Type = InstructionType.RawOpcode;
                                            inst.Opcode = Opcode.Add_l;
                                            inst.Trace = Trace.FromToken(instTok);
                                        }
                                        else
                                        {
                                            // Here we parse an add_sp_lit_l instruction!
                                            var litTok = Tokens.Dequeue();
                                            if (litTok.Type == TokenType.Number_litteral)
                                            {
                                                var (num, size) = litTok.ParseNumber();
                                                inst.Arg = num;

                                                if (size > 2) Error(litTok, "Number too big!");
                                                inst.Type = InstructionType.DwordArgOpcode;
                                                inst.Opcode = Opcode.Add_sp_lit_l;

                                                inst.Trace = Trace.FromToken(instTok, litTok);
                                            }
                                            else if (litTok.Type == TokenType.Identifier) Error(litTok, "We don't do static analasys for this yet");
                                            else if (litTok.Type == TokenType.Open_paren) Error(litTok, "We don't do static analasys for this yet");
                                            else Error(litTok, "Must add a constant to the stack pointer!");
                                        }
                                    }
                                    else
                                    {
                                        bool found = false;
                                        foreach (var rawop in RawInstructions)
                                        {
                                            if (instTok.ContentsMatch(rawop.Key))
                                            {
                                                found = true;
                                                inst.Type = InstructionType.RawOpcode;
                                                inst.Opcode = rawop.Value;
                                                inst.Trace = Trace.FromToken(instTok);
                                                break;
                                            }
                                        }

                                        if (found == false)
                                        {
                                            if (instTok.Type == TokenType.Identifier)
                                            {
                                                // Here we guess that this is a constant that we want to resolve!
                                                inst.Type = InstructionType.Identifier;
                                                inst.StrArg = instTok.Data;
                                                inst.Trace = Trace.FromToken(instTok);
                                            }
                                            else Error(instTok, "Unknown instruction!");
                                        }
                                    }
                                }
                                else if (instTok.Type == TokenType.Number_litteral)
                                {
                                    // Here we assume we just want to output the number raw
                                    inst.Type = InstructionType.Number;
                                    inst.StrArg = instTok.Data;
                                    inst.Trace = Trace.FromToken(instTok);
                                }
                                else if (instTok.Type == TokenType.Register)
                                {
                                    inst.Type = InstructionType.RawOpcode;
                                    if (instTok.ContentsMatch("[FP]"))
                                        inst.Opcode = Opcode.Fp;
                                    else if (instTok.ContentsMatch("[PC]"))
                                        inst.Opcode = Opcode.Pc;
                                    else if (instTok.ContentsMatch("[PT]"))
                                        inst.Opcode = Opcode.Pt;
                                    else if (instTok.ContentsMatch("[SP]"))
                                        inst.Opcode = Opcode.Sp;
                                    else Error(instTok, "Unknown register!");
                                    inst.Trace = Trace.FromToken(instTok);
                                }
                                else if (instTok.Type == TokenType.Call)
                                {
                                    if (instTok.ContentsMatch("::[SP]"))
                                    {
                                        inst.Type = InstructionType.RawOpcode;
                                        inst.Opcode = Opcode.Call_v;
                                    }
                                    else
                                    {
                                        inst.Opcode = Opcode.Call;
                                        inst.Type = InstructionType.Call;
                                        inst.StrArg = instTok.Data.Substring(1);
                                    }

                                    inst.Trace = Trace.FromToken(instTok);
                                }
                                else if (instTok.Type == TokenType.Label)
                                {
                                    if (instTok.GetLastChar() == '*')
                                    {
                                        inst.Type = InstructionType.LabelRef;
                                        // Remove the '*' at the end
                                        inst.StrArg = instTok.Data;
                                        inst.Trace = Trace.FromToken(instTok);
                                    }
                                    else
                                    {
                                        inst.Type = InstructionType.Label;
                                        inst.StrArg = instTok.Data;
                                        inst.Trace = Trace.FromToken(instTok);
                                    }
                                }
                                else if (instTok.Type == TokenType.String_litteral)
                                {
                                    if (ProcContent.Count > 0) Error(instTok, "A string label can only contain a string!");

                                    if (instTok.GetFirstChar() == '@')
                                        inst.Type = InstructionType.RawString;
                                    else inst.Type = InstructionType.String;
                                    inst.StrArg = instTok.Data;
                                    inst.Trace = Trace.FromToken(instTok);

                                    // FIXME: Generate an error if there are more things to add to this label!!!
                                }
                                else if (instTok.Type == TokenType.Char_litteral)
                                {
                                    inst.Type = InstructionType.RawWord;
                                    // FIXME: Char escapes!!
                                    inst.Arg = instTok.GetChar(1);
                                    inst.Trace = Trace.FromToken(instTok);
                                }
                                else if (instTok.Type == TokenType.Numbersign)
                                {
                                    // This is a constant that we should eval!
                                    var litTok = Tokens.Dequeue();
                                    if (litTok.Type == TokenType.Identifier)
                                    {
                                        // This is just a const that we want to eval!
                                        Error(litTok, "We don't support raw constants yet!");
                                    }
                                    else if (litTok.Type == TokenType.Open_paren)
                                    {
                                        // Go back to the open_paren token so that the constant expression parsing will work correctly
                                        Tokens.Revert(1);
                                        ConstantExpression expr = ParseConstExpr(Tokens);

                                        inst.Type = InstructionType.ConstExpr;
                                        inst.ConstantArg = expr;

                                        inst.Trace = Trace.FromToken(instTok, litTok);
                                    }
                                    else Error(litTok, $"Unknown constant type {litTok.Type}!! ");
                                }
                                else Error(instTok, "Unknown instruction type!");

                                if (inst.Trace.FilePath == null || inst.Trace.FileData == null) Debugger.Break();

                                ProcContent.Add(inst);
                                if (Tokens.Count > 0)
                                    peek = Tokens.Peek();
                                else break;
                            }

                            // FIXME: Add the location of the proc!!! if there is one!
                            Procs.Add(tok, ProcContent);
                            if (location.HasValue)
                                ProcLocations.Add(tok.Data, location ?? 0);

                            //Console.ForegroundColor = ConsoleColor.Green;
                            //Console.WriteLine($"Proc: {tok.GetContents(),-40} Content: {ProcContent.Count,3} tokens");
                            break;
                        }
                    default:
                        Error(tok, "Unknown token!");
                        break;
                }
            }

            return new ParsedFile(File, Flags, IncludeFiles, ConstExprs, Procs, ProcLocations, AutoStrings);
        }

        private ConstantExpression ParseConstExpr(TokenQueue Tokens)
        {
            // This will only parse the expression part of the constant
            ConstantExpression constExpr = new ConstantExpression();

            var peek = Tokens.Peek();
            if (peek.Type == TokenType.Numbersign)
            {
                // Remove the '#' and keep parsing
                Tokens.Dequeue();
                constExpr = ParseConstExpr(Tokens);
            }
            else if (peek.Type == TokenType.Number_litteral)
            {
                Tokens.Dequeue();
                constExpr.Type = ConstantExprType.NumberLit;
                constExpr.NumberLit = peek.ParseNumber();
            }
            else if (peek.Type == TokenType.Char_litteral)
            {
                Tokens.Dequeue();
                constExpr.Type = ConstantExprType.CharLit;
                constExpr.CharLit = peek.GetChar(1);
            }
            else if (peek.Type == TokenType.String_litteral)
            {
                Tokens.Dequeue();
                constExpr.Type = ConstantExprType.StringLit;
                constExpr.StringLit = peek.Data;
            }
            else if (peek.Type == TokenType.Open_paren)
            {
                // Here we do proper parsing!
                constExpr = ParseCompoundConst(Tokens);
            }
            else if (peek.ContentsMatch("extern"))
            {
                // Dequeue 'extern'
                Tokens.Dequeue();
                constExpr.Type = ConstantExprType.Extern;
            }
            else if (peek.ContentsMatch("auto"))
            {
                // Parse auto expression
                Tokens.Dequeue();
                constExpr.Type = ConstantExprType.Auto;

                var openParenPeek = Tokens.Peek();
                if (openParenPeek.Type != TokenType.Open_paren) Error(openParenPeek, "Expected '('!");

                ConstantExpression autoExpr = ParseConstExpr(Tokens);
                constExpr.AutoExpr = autoExpr;
            }
            else Error(peek, "Unknown constant type!");

            return constExpr;
        }

        private ConstantExpression ParseCompoundConst(TokenQueue Tokens)
        {
            var openTok = Tokens.Dequeue();
            if (openTok.Type != TokenType.Open_paren) Error(openTok, "Exprected '('!");

            ConstantExpression expr = new ConstantExpression();
            expr.Type = ConstantExprType.Compound;
            expr.CompoundExpr = new List<CompoundElement>(10);

            bool delayed = false;
            while (Tokens.Peek().Type != TokenType.Close_paren)
            {
                var element = ParseCompoundElement(Tokens);
                expr.CompoundExpr.Add(element);
                delayed |= element.Type == CompoundType.Sizeof || element.Type == CompoundType.LabelRef;
            }
            
            var closeTok = Tokens.Dequeue();

            expr.Delayed = delayed;
            expr.Trace = Trace.FromToken(openTok, closeTok);

            return expr;
        }

        private CompoundElement ParseCompoundElement(TokenQueue Tokens)
        {
            CompoundElement element = default;
            var tok = Tokens.Dequeue();
            switch (tok.Type)
            {
                case TokenType.Numbersign:
                    // We just ignore the numbersing and try parsing again
                    element = ParseCompoundElement(Tokens);
                    // FIXME: This is ugly
                    element.Trace = Trace.FromTrace(Trace.FromToken(tok), element.Trace);
                    break;
                case TokenType.Asterisk:
                    element.Type = CompoundType.Operation;
                    element.Operation = ConstOperation.Multiplication;
                    element.Trace = Trace.FromToken(tok);
                    break;
                case TokenType.Plus:
                    element.Type = CompoundType.Operation;
                    element.Operation = ConstOperation.Addition;
                    element.Trace = Trace.FromToken(tok);
                    break;
                case TokenType.Minus:
                    element.Type = CompoundType.Operation;
                    element.Operation = ConstOperation.Subtraction;
                    element.Trace = Trace.FromToken(tok);
                    break;
                case TokenType.Slash:
                    element.Type = CompoundType.Operation;
                    element.Operation = ConstOperation.Division;
                    element.Trace = Trace.FromToken(tok);
                    break;
                case TokenType.Percent:
                    element.Type = CompoundType.Operation;
                    element.Operation = ConstOperation.Modulo;
                    element.Trace = Trace.FromToken(tok);
                    break;
                case TokenType.Identifier:
                    // Identifier here can be special things like sizeof
                    if (tok.ContentsMatch("sizeof"))
                    {
                        element.Type = CompoundType.Sizeof;

                        var openTok = Tokens.Dequeue();
                        if (openTok.Type != TokenType.Open_paren) Error(openTok, "Expected '(' after sizeof");

                        var labelTok = Tokens.Dequeue();
                        if (labelTok.Type != TokenType.Label) Error(labelTok, "Expected label in sizeof!");
                        element.StringRef = labelTok.Data;

                        var closeTok = Tokens.Dequeue();
                        if (closeTok.Type != TokenType.Close_paren) Error(closeTok, "Expected ')'");

                        element.Trace = Trace.FromToken(tok, closeTok);
                    }
                    else
                    {
                        element.Type = CompoundType.Ident;
                        element.StringRef = tok.Data;
                        element.Trace = Trace.FromToken(tok);
                    }
                    break;
                case TokenType.Label:
                    element.Type = CompoundType.LabelRef;
                    element.StringRef = tok.Data;
                    element.Trace = Trace.FromToken(tok);
                    break;
                case TokenType.Number_litteral:
                    element.Type = CompoundType.NumberLitteral;
                    element.NumberLit = tok.ParseNumber();
                    element.Trace = Trace.FromToken(tok);
                    break;
                case TokenType.Char_litteral:
                    element.Type = CompoundType.CharLitteral;
                    element.CharLit = tok.GetChar(1);
                    element.Trace = Trace.FromToken(tok);
                    break;
                default:
                    Error(tok, $"Unknown compound constant token: '{tok}'");
                    break;
            }

            return element;
        }
    }
}
