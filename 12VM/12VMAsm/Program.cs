using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace _12VMAsm
{
    class Program
    {
        enum Opcode
        {
            Nop,
            Load_addr,
            Load_lit,
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
            Label,
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

        static void Main(string[] args)
        {
        }
    }
}
