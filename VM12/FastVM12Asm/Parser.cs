using System;
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
        public string FilePath;
        public int Line;
        public StringRef TraceString;
    }

    public struct StringRef
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

        public override string ToString() => Data?.Substring(Index, Length);
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

    public struct ConstantExpression
    {
        // FIXME actually set this!!
        public Trace Trace; 

        public ConstFlags Flags;
        // FIXME: represent this better!!!
        public List<Token> Expression;

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
    }

    // FIXME: Add traces!!!
    public struct Instruction
    {
        public InstructionFlags Flags;
        public Opcode Opcode;
        public int Arg;
        // Should maybe be something else? Token?
        public StringRef StrArg;
    }

    public struct TokenQueue
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
    }

    public class ParsedFile
    {
        public FileData File;
        public List<Token> IncludeFiles = new List<Token>();
        public Dictionary<Token, ConstantExpression> ConstExprs = new Dictionary<Token, ConstantExpression>();
        public Dictionary<Token, List<Instruction>> Procs;

        public ParsedFile(FileData file, List<Token> includeFiles, Dictionary<Token, ConstantExpression> constExprs, Dictionary<Token, List<Instruction>> procs)
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
        };

        // FIXME: Names!
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
            { "setzl",   SetMode.Z_l },
            { "setnzl",  SetMode.Nz_l },
            { "setcl",   SetMode.C_l },
            { "setczl",  SetMode.Cz_l },
            { "setgzl",  SetMode.Gz_l },
            { "setgel",  SetMode.Ge_l },
            { "setlzl",  SetMode.Lz_l },
            { "setlel",  SetMode.Le_l },
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

            Dictionary<Token, ConstantExpression> ConstExprs = new Dictionary<Token, ConstantExpression>(100);

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
                            else Error(tok, "Unknown flag!");
                            break;
                        }
                    case TokenType.Open_angle:
                        {
                            var identTok = Tokens.Dequeue();
                            if (identTok.Type != TokenType.Identifier) Error(identTok, "Expected constant name!");

                            var equalsTok = Tokens.Dequeue();
                            if (equalsTok.Type != TokenType.Equals) Error(equalsTok, "Expected equals!");

                            // Here we should parse the actual expression!

                            ConstFlags flags = isGlobal ? ConstFlags.Public : ConstFlags.None;

                            List<Token> Expr = null;
                            var peek = Tokens.Peek();
                            if (peek.ContentsMatch("extern"))
                            {
                                // Dequeue 'extern'
                                Tokens.Dequeue();
                                flags |= ConstFlags.Extern;
                            }
                            else if (peek.ContentsMatch("auto"))
                            {
                                // Parse auto expression
                                Tokens.Dequeue();
                                flags |= ConstFlags.Auto;

                                var openParenTok = Tokens.Dequeue();
                                if (openParenTok.Type != TokenType.Open_paren) Error(openParenTok, "Expected '('!");

                                Expr = new List<Token>();
                                while (Tokens.Peek().Type != TokenType.Close_paren)
                                    Expr.Add(Tokens.Dequeue());

                                Tokens.Dequeue();
                            }
                            else
                            {
                                Expr = new List<Token>();
                                while (Tokens.Peek().Type != TokenType.Close_angle)
                                    Expr.Add(Tokens.Dequeue());
                            }
                            
                            // Dequeue close angle
                            var closeAngleTok = Tokens.Dequeue();
                            if (closeAngleTok.Type != TokenType.Close_angle) Error(closeAngleTok, "Expected '>'!");

                            // Flag if this is a litteral!
                            if (Expr?.Count == 1 &&
                                (Expr[0].Type == TokenType.Number_litteral ||
                                Expr[0].Type == TokenType.Char_litteral ||
                                Expr[0].Type == TokenType.String_litteral))
                                flags |= ConstFlags.Litteral;
                            
                            // FIXME: We might want to do flag validation here so that e.g. an extern const is not global

                            ConstExprs.Add(identTok, new ConstantExpression(flags, Expr));

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
                                            else Error(litTok, $"Unknown load token!");
                                        }
                                        else if (loadTok.Type == TokenType.Label)
                                        {
                                            inst.Opcode = Opcode.Load_lit_l;
                                            inst.Flags |= InstructionFlags.LabelArg;
                                            inst.StrArg = loadTok.ToStringRef(1);
                                        }
                                        else if (loadTok.Type == TokenType.Register)
                                        {
                                            if (loadTok.ContentsMatch("[SP]"))
                                            {
                                                inst.Flags |= InstructionFlags.RawOpcode;
                                                if (instTok.GetLastChar() == 'l')
                                                    inst.Opcode = Opcode.Load_sp_l;
                                                else inst.Opcode = Opcode.Load_sp;
                                            }
                                            else Error(loadTok, "Can only load using the address at [SP]!");
                                        }
                                        else if (loadTok.Type == TokenType.Char_litteral)
                                        {
                                            if (instTok.GetLastChar() == 'l') Error(loadTok, "Cannot loadl a char litteral!");
                                            inst.Flags |= InstructionFlags.WordArg;
                                            inst.Arg = loadTok.GetFirstChar();
                                        }
                                        else if (loadTok.Type == TokenType.String_litteral)
                                        {
                                            inst.Flags |= InstructionFlags.LabelArg;
                                            inst.Flags |= InstructionFlags.AutoString;
                                            inst.StrArg = loadTok.ToStringRef();
                                            inst.Opcode = Opcode.Load_lit_l;
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
                                        }
                                        else if (storeTok.Type == TokenType.Register)
                                        {
                                            if (storeTok.ContentsMatch("[SP]"))
                                            {
                                                inst.Flags |= InstructionFlags.RawOpcode;
                                                if (instTok.GetLastChar() == 'l')
                                                    inst.Opcode = Opcode.Store_sp_l;
                                                else inst.Opcode = Opcode.Store_sp;
                                            }
                                            else Error(storeTok, "Can only store using the address at [SP]!");
                                        }
                                        else Error(storeTok, "Cannot store this!");
                                    }
                                    else if (instTok.StartsWith("set"))
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

                                            if (instTok.GetChar(1) == 'i')
                                                if (instTok.GetFirstChar() == 'l')
                                                    inst.Opcode = Opcode.Inc_local_l;
                                                else inst.Opcode = Opcode.Inc_local;
                                            else
                                                if (instTok.GetFirstChar() == 'l')
                                                    inst.Opcode = Opcode.Dec_local_l;
                                                else inst.Opcode = Opcode.Dec_local;
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
                                    inst.Arg = number;
                                    switch (size)
                                    {
                                        case 1:
                                            inst.Flags |= InstructionFlags.WordArg;
                                            break;
                                        case 2:
                                            inst.Flags |= InstructionFlags.DwordArg;
                                            break;
                                        default:
                                            Error(instTok, "We don't support number litterals bigger than a dword atm!");
                                            break;
                                    }
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
                                }
                                else if (instTok.Type == TokenType.Label)
                                {
                                    // How should we do this? Emit an instruction like we do atm?

                                    // FIXME: Should this flag exist?
                                    inst.Flags |= InstructionFlags.Label;
                                    inst.StrArg = instTok.ToStringRef();
                                }
                                else
                                {
                                    // idk...
                                    Error(instTok, "Not implemented!");
                                }

                                ProcContent.Add(inst);
                                if (Tokens.Count > 0)
                                    peek = Tokens.Peek();
                                else break;
                            }

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
    }
}
