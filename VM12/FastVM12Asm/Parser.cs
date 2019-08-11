﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VM12_Opcode;

namespace FastVM12Asm
{
    public struct Trace
    {
        public FileData File;
        public int StartLine, EndLine;
        public StringRef TraceString;

        public Trace(FileData file, int line, StringRef traceString)
        {
            File = file;
            StartLine = line;
            EndLine = line;
            TraceString = traceString;
        }
        
        public static Trace FromToken(Token tok)
        {
            Trace trace;
            trace.File = tok.File;
            trace.StartLine = tok.Line;
            trace.EndLine = tok.Line;
            trace.TraceString = tok.ToStringRef();
            return trace;
        }
        public static Trace FromToken(Token start, Token end)
        {
            Trace trace;
            trace.File = start.File;
            trace.StartLine = start.Line;
            trace.EndLine = end.Line;
            trace.TraceString = new StringRef(start.File.Data, start.Index, end.Length + (end.Index - start.Index));
            return trace;
        }

        internal static Trace FromTrace(Trace start, Trace end)
        {
            Trace trace;
            trace.File = start.File;
            trace.StartLine = start.StartLine;
            trace.EndLine = end.EndLine;
            trace.TraceString = new StringRef(start.File.Data, start.TraceString.Index, end.TraceString.Length + (end.TraceString.Index - start.TraceString.Index));
            return trace;
        }
    }

    public struct StringRef : IEquatable<StringRef>
    {
        public string Data;
        public int Index;
        public int Length;

        public StringRef(string data, int index, int length)
        {
            Data = data;
            Index = index;
            Length = length;
        }

        public override bool Equals(object obj)
        {
            return obj is StringRef @ref && Equals(@ref);
        }

        public bool Equals(StringRef other)
        {
            if (Length != other.Length) return false;
            for (int i = 0; i < Length; i++)
            {
                if (Data[Index + i] != other.Data[other.Index + i]) return false;
            }
            return true;
        }

        public override int GetHashCode()
        {
            var hashCode = 596452045;
            for (int i = 0; i < Length; i++)
            {
                hashCode = hashCode * -1521134295 + Data[Index + i].GetHashCode();
            }
            hashCode = hashCode * -1521134295 + Length.GetHashCode();
            return hashCode;
        }

        public override string ToString() => Data?.Substring(Index, Length);

        public static bool operator ==(StringRef left, StringRef right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(StringRef left, StringRef right)
        {
            return !(left == right);
        }

        public static explicit operator StringRef(string str) => new StringRef(str, 0, str.Length);

        public bool StartsWith(string str)
        {
            if (str.Length > Length) return false;

            for (int i = 0; i < str.Length; i++)
            {
                if (Data[Index + i] != str[i]) return false;
            }

            return true;
        }

        public char this[int i] => Data[Index + i];
    }

    [Flags]
    public enum ConstFlags
    {
        None = 0,
        Public = 1 << 0,
        Extern = 1 << 1,
        Litteral = 1 << 2,
        Auto = 1 << 3,
    }

    public class ConstantExpression
    {
        // FIXME actually set this!!
        public Trace Trace; 

        public ConstFlags Flags;
        // FIXME: represent this better!!!
        public List<Token> Expression;

        public Token Litteral;

        public ConstantExpression AutoExpr;

        public ConstantExpression() { }

        public ConstantExpression(ConstFlags flags, List<Token> expr)
        {
            Trace = default;
            Flags = flags;
            Expression = expr;
        }
    }

    [Flags]
    public enum InstructionFlags
    {
        None = 0,
        RawOpcode =  1 << 0,
        WordArg =    1 << 1,
        DwordArg =   1 << 2,
        LabelArg =   1 << 3,
        AutoString = 1 << 4,
        RawNumber =  1 << 5,
        IdentArg =   1 << 6,
        // FIXME: Should this flag exist?
        Label =      1 << 7,

        ConstExprArg = 1 << 8,
        NumberStrArg = 1 << 9,
    }

    // FIXME: Add traces!!!
    public struct Instruction
    {
        public InstructionFlags Flags;
        public Opcode Opcode;
        public int Arg;
        // Should maybe be something else? Token?
        public StringRef StrArg;
        public Trace Trace;
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
        public List<Token> IncludeFiles = new List<Token>();
        public Dictionary<StringRef, ConstantExpression> ConstExprs = new Dictionary<StringRef, ConstantExpression>();
        public Dictionary<Token, List<Instruction>> Procs;

        public ParsedFile(FileData file, List<Token> includeFiles, Dictionary<StringRef, ConstantExpression> constExprs, Dictionary<Token, List<Instruction>> procs)
        {
            File = file;
            IncludeFiles = includeFiles;
            ConstExprs = constExprs;
            Procs = procs;
        }
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

        static Dictionary<string, JumpMode> JmpArguments = new Dictionary<string, JumpMode>()
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
        
        static Dictionary<string, SetMode> SetArguments = new Dictionary<string, SetMode>()
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

        private FileData File;
        private TokenQueue Tokens;
        
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
            //Debugger.Break();
            Console.ReadLine();
            Environment.Exit(1);
        }

        public ParsedFile Parse()
        {
            Stopwatch flagWatch = new Stopwatch();
            Stopwatch constWatch = new Stopwatch();
            Stopwatch importWatch = new Stopwatch();
            Stopwatch procWatch = new Stopwatch();

            List<Token> IncludeFiles = new List<Token>(100);

            Dictionary<StringRef, ConstantExpression> ConstExprs = new Dictionary<StringRef, ConstantExpression>(100);

            Dictionary<Token, List<Instruction>> Procs = new Dictionary<Token, List<Instruction>>();
            
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
                            else if(tok.ContentsMatch("!private"))
                            {
                                isGlobal = false;
                            }
                            else if (tok.ContentsMatch("!noprintouts"))
                            {
                                // FIXME: Implenent this flag!
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
                            constExpr.Flags |= isGlobal ? ConstFlags.Public : ConstFlags.None;

                            // Dequeue close angle
                            var closeAngleTok = Tokens.Dequeue();
                            if (closeAngleTok.Type != TokenType.Close_angle) Error(closeAngleTok, "Expected '>'!");

                            // FIXME: We might want to do flag validation here so that e.g. an extern const is not global

                            constExpr.Trace = Trace.FromToken(tok, closeAngleTok);

                            ConstExprs.Add(identTok.ToStringRef(), constExpr);

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
                                var res = locationTok.ToStringRef(1).ParseNumber();
                                if (res.Size > 2) Error(locationTok, "Specified location is too big!");
                                location = res.Value;
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

                                            inst.Flags |= InstructionFlags.WordArg;
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
                                                    inst.Flags = InstructionFlags.DwordArg;
                                                    inst.Opcode = Opcode.Load_lit_l;
                                                }
                                                else
                                                {
                                                    if (size > 1) Error(litTok, "Number too big!");
                                                    inst.Flags = InstructionFlags.WordArg;
                                                    inst.Opcode = Opcode.Load_lit;
                                                }
                                            }
                                            else if (litTok.Type == TokenType.Identifier)
                                            {
                                                if (instTok.GetLastChar() == 'l')
                                                    inst.Opcode = Opcode.Load_lit_l;
                                                else inst.Opcode = Opcode.Load_lit;
                                                inst.Flags |= InstructionFlags.IdentArg;
                                                inst.StrArg = litTok.ToStringRef();
                                            }
                                            else if (litTok.Type == TokenType.Open_paren)
                                            {
                                                // Go back to the open_paren token so that the constant expression parsing will work correctly
                                                Tokens.Revert(1);
                                                ConstantExpression expr = ParseConstExpr(Tokens);

                                                // FIXME: Better number and name!
                                                StringRef constName = (StringRef)$"auto_created_const_expr{new Random().Next()}";

                                                ConstExprs.Add(constName, expr);

                                                inst.Flags |= InstructionFlags.ConstExprArg;
                                                inst.StrArg = constName;
                                                if (instTok.GetLastChar() == 'l')
                                                    inst.Opcode = Opcode.Load_lit_l;
                                                else inst.Opcode = Opcode.Load_lit;
                                            }
                                            else Error(litTok, $"Unknown load token!");

                                            inst.Trace = Trace.FromToken(instTok, litTok);
                                        }
                                        else if (loadTok.Type == TokenType.Label)
                                        {
                                            inst.Opcode = Opcode.Load_lit_l;
                                            inst.Flags |= InstructionFlags.LabelArg;
                                            inst.StrArg = loadTok.ToStringRef(1);
                                            inst.Trace = Trace.FromToken(instTok, loadTok);
                                        }
                                        else if (loadTok.Type == TokenType.Register)
                                        {
                                            if (loadTok.ContentsMatch("[SP]"))
                                            {
                                                inst.Flags |= InstructionFlags.RawOpcode;
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
                                            inst.Flags |= InstructionFlags.WordArg;
                                            inst.Arg = loadTok.GetFirstChar();
                                            inst.Trace = Trace.FromToken(instTok, loadTok);
                                        }
                                        else if (loadTok.Type == TokenType.String_litteral)
                                        {
                                            inst.Flags |= InstructionFlags.LabelArg;
                                            inst.Flags |= InstructionFlags.AutoString;
                                            inst.StrArg = loadTok.ToStringRef();
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

                                            inst.Flags |= InstructionFlags.WordArg;

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

                                            // FIXME! This will not work for constant expressions!
                                            var litTok = Tokens.Dequeue();
                                            if (litTok.Type == TokenType.Number_litteral)
                                            {
                                                var (num, size) = litTok.ParseNumber();
                                                inst.Arg = num;

                                                if (instTok.GetLastChar() == 'l')
                                                {
                                                    if (size > 2) Error(litTok, "Number too big!");
                                                    inst.Flags = InstructionFlags.DwordArg;
                                                    //inst.Opcode = Opcode.Store_lit_l;
                                                }
                                                else
                                                {
                                                    if (size > 1) Error(litTok, "Number too big!");
                                                    inst.Flags = InstructionFlags.WordArg;
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
                                        }
                                        else if (storeTok.Type == TokenType.Register)
                                        {
                                            if (storeTok.ContentsMatch("[SP]"))
                                            {
                                                inst.Flags |= InstructionFlags.RawOpcode;
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
                                                inst.Flags |= InstructionFlags.RawOpcode;
                                                inst.Opcode = Opcode.Set_sp;
                                            }
                                            else if (regTok.ContentsMatch("[FP]"))
                                            {
                                                inst.Flags |= InstructionFlags.RawOpcode;
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
                                                    inst.Flags |= InstructionFlags.WordArg;
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
                                                inst.Flags |= InstructionFlags.WordArg;
                                                inst.Arg = (int)jmparg.Value;
                                                inst.Opcode = Opcode.Jmp;

                                                // Parse the jmp label
                                                var labelTok = Tokens.Dequeue();
                                                if (labelTok.Type != TokenType.Label) Error(labelTok, "Can only jump to labels!");

                                                inst.Flags |= InstructionFlags.LabelArg;
                                                inst.StrArg = labelTok.ToStringRef();

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

                                            inst.Flags |= InstructionFlags.WordArg;
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
                                            inst.Flags |= InstructionFlags.RawOpcode;
                                            if (instTok.GetChar(1) == 'i')
                                                if (instTok.GetFirstChar() == 'l')
                                                    inst.Opcode = Opcode.Inc_l;
                                                else inst.Opcode = Opcode.Inc;
                                            else
                                                if (instTok.GetFirstChar() == 'l')
                                                inst.Opcode = Opcode.Dec_l;
                                            else inst.Opcode = Opcode.Dec;

                                            inst.Trace = Trace.FromToken(instTok);
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
                                                inst.Flags |= InstructionFlags.RawOpcode;
                                                inst.Opcode = rawop.Value;
                                                inst.Trace = Trace.FromToken(instTok);
                                                break;
                                            }
                                        }

                                        if (found == false) Error(instTok, "Unknown instruction!");
                                    }
                                }
                                else if (instTok.Type == TokenType.Number_litteral)
                                {
                                    // Here we assume we just want to output the number raw
                                    inst.Flags |= InstructionFlags.RawNumber;

                                    var (number, size) = instTok.ParseNumber();
                                    switch (size)
                                    {
                                        case 1:
                                            inst.Flags |= InstructionFlags.WordArg;
                                            inst.Arg = number;
                                            break;
                                        case 2:
                                            inst.Flags |= InstructionFlags.DwordArg;
                                            inst.Arg = number;
                                            break;
                                        default:
                                            inst.Flags |= InstructionFlags.NumberStrArg;
                                            inst.StrArg = instTok.ToStringRef();
                                            break;
                                    }

                                    inst.Trace = Trace.FromToken(instTok);
                                }
                                else if (instTok.Type == TokenType.Register)
                                {
                                    inst.Flags |= InstructionFlags.RawOpcode;
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
                                        inst.Flags |= InstructionFlags.RawOpcode;
                                        inst.Opcode = Opcode.Call_v;
                                    }
                                    else
                                    {
                                        inst.Opcode = Opcode.Call;
                                        inst.Flags |= InstructionFlags.LabelArg;
                                        inst.StrArg = instTok.ToStringRef(1);
                                    }

                                    inst.Trace = Trace.FromToken(instTok);
                                }
                                else if (instTok.Type == TokenType.Label)
                                {
                                    // How should we do this? Emit an instruction like we do atm?
                                    // FIXME: Should this flag exist?
                                    inst.Flags |= InstructionFlags.Label;
                                    inst.StrArg = instTok.ToStringRef();
                                    inst.Trace = Trace.FromToken(instTok);
                                }
                                else Error(instTok, "Unknown instruction type!");

                                if (inst.Trace.File == null) Debugger.Break();

                                ProcContent.Add(inst);
                                if (Tokens.Count > 0)
                                    peek = Tokens.Peek();
                                else break;
                            }
                            
                            // FIXME: Add the location of the proc!!! if there is one!
                            Procs.Add(tok, ProcContent);

                            //Console.ForegroundColor = ConsoleColor.Green;
                            //Console.WriteLine($"Proc: {tok.GetContents(),-40} Content: {ProcContent.Count,3} tokens");
                            break;
                        }
                    default:
                        Error(tok, "Unknown token!");
                        break;
                }
            }

            return new ParsedFile(File, IncludeFiles, ConstExprs, Procs);
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
            else if (peek.Type == TokenType.Number_litteral || peek.Type == TokenType.Char_litteral || peek.Type == TokenType.String_litteral)
            {
                Tokens.Dequeue();
                // FIXME: Consider splitting litteral up into number, string, and char
                constExpr.Flags |= ConstFlags.Litteral;
                constExpr.Litteral = peek;
            }
            else if (peek.Type == TokenType.Open_paren)
            {
                // Here we do proper parsing!!

                Tokens.Dequeue();

                // FIXME: We should make the paren handling here are we are doing a recursing decent anyway!
                List<Token> Expr = new List<Token>();
                int parenCounter = 1;
                var exprPeek = Tokens.Peek();
                do
                {
                    // FIXME: Validate that what we are adding makes some kind of sense!
                    Expr.Add(exprPeek);
                    Tokens.Dequeue();

                    exprPeek = Tokens.Peek();
                    if (exprPeek.Type == TokenType.Open_paren) parenCounter++;
                    if (exprPeek.Type == TokenType.Close_paren) parenCounter--;
                }
                while (parenCounter > 0) ;

                var closeParenTok = Tokens.Dequeue();
                if (closeParenTok.Type != TokenType.Close_paren) Error(closeParenTok, "Expected ')'!");

                if (Expr.Count == 1 && Expr[0].Type == TokenType.Number_litteral || Expr[0].Type == TokenType.String_litteral || Expr[0].Type == TokenType.Char_litteral)
                    constExpr.Flags |= ConstFlags.Litteral;

                constExpr.Expression = Expr;
            }
            else if (peek.ContentsMatch("extern"))
            {
                // Dequeue 'extern'
                Tokens.Dequeue();
                constExpr.Flags |= ConstFlags.Extern;
            }
            else if (peek.ContentsMatch("auto"))
            {
                // Parse auto expression
                Tokens.Dequeue();
                constExpr.Flags |= ConstFlags.Auto;

                var openParenPeek = Tokens.Peek();
                if (openParenPeek.Type != TokenType.Open_paren) Error(openParenPeek, "Expected '('!");

                ConstantExpression autoExpr = ParseConstExpr(Tokens);
                constExpr.AutoExpr = autoExpr;
            }
            else Error(peek, "Unknown constant type!");

            return constExpr;
        }
    }
}
