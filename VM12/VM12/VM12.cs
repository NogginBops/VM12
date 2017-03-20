using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace VM12
{
    class Memory
    {
        public const int RAM_SIZE = 4194304;
        public const int VRAM_SIZE = 307200;
        public const int ROM_SIZE = 12275712;

        public const int RAM_START = 0;
        public const int VRAM_START = RAM_SIZE;
        public const int ROM_START = RAM_SIZE + VRAM_SIZE;

        public readonly short[] RAM = new short[RAM_SIZE];

        public readonly short[] VRAM = new short[VRAM_SIZE];

        public readonly short[] ROM = new short[ROM_SIZE];

        public void SetROM(short[] ROM)
        {
            Array.Copy(ROM, this.ROM, ROM.Length);
        }

        public short this[int i]
        {
            get
            {
                if (i < RAM_START + RAM_SIZE)
                {
                    return RAM[i - RAM_START];
                }
                else if (i < VRAM_START + VRAM_SIZE)
                {
                    return VRAM[i - VRAM_START];
                }
                else if (i < ROM_START + ROM_SIZE)
                {
                    return ROM[i - ROM_START];
                }
                else
                {
                    throw new IndexOutOfRangeException();
                }
            }
            set
            {
                if (i < RAM_START + RAM_SIZE)
                {
                    RAM[i - RAM_START] = value;
                }
                else if (i < VRAM_START + VRAM_SIZE)
                {
                    VRAM[i - VRAM_START] = value;
                }
                else if (i < ROM_START + ROM_SIZE)
                {
                    throw new ArgumentException("Cannot modify ROM!");
                }
                else
                {
                    throw new IndexOutOfRangeException();
                }
            }
        }
    }

    class VM12
    {
        Memory memory = new Memory();

        bool carry = false;

        int PC = Memory.ROM_START;
        int SP = 1;

        bool StopRunning = false;

        Stack<int> returnStack = new Stack<int>();
        
        public VM12(short[] ROM)
        {
            memory.SetROM(ROM);
        }

        public void Start()
        {
            while (StopRunning != true)
            {
                if ((memory[PC] & 0xF000) != 0)
                {
                    ;
                }

                Opcode op = (Opcode)(memory[PC] & 0x0FFF);

                switch (op)
                {
                    case Opcode.Nop:
                        PC++;
                        break;
                    case Opcode.Load_addr:
                        int load_address = ToInt(memory[++PC], memory[++PC]);
                        PC++;
                        memory[++SP] = memory[load_address];
                        break;
                    case Opcode.Load_lit:
                        memory[++SP] = memory[++PC];
                        PC++;
                        break;
                    case Opcode.Load_sp:
                        int load_sp_address = ToInt(memory[SP--], memory[SP--]);
                        memory[++SP] = memory[load_sp_address];
                        PC++;
                        break;
                    case Opcode.Store_pc:
                        int store_pc_address = ToInt(memory[++PC], memory[++PC]);
                        PC++;
                        memory[store_pc_address] = memory[SP];
                        break;
                    case Opcode.Store_sp:
                        int store_sp_address = ToInt(memory[SP], memory[SP - 1]);
                        memory[store_sp_address] = memory[SP - 2];
                        PC++;
                        break;
                    case Opcode.Call_sp:
                        returnStack.Push(PC + 1);
                        PC = ToInt(memory[SP--], memory[SP--]);
                        break;
                    case Opcode.Call_pc:
                        returnStack.Push(PC + 3);
                        PC = ToInt(memory[++PC], memory[++PC]);
                        break;
                    case Opcode.Ret:
                        PC = returnStack.Pop();
                        break;
                    case Opcode.Dup:
                        memory[SP + 1] = memory[SP];
                        SP++;
                        PC++;
                        break;
                    case Opcode.Over:
                        memory[SP + 1] = memory[SP - 1];
                        SP++;
                        PC++;
                        break;
                    case Opcode.Swap:
                        short swap_temp = memory[SP];
                        memory[SP] = memory[SP - 1];
                        memory[SP - 1] = swap_temp;
                        PC++;
                        break;
                    case Opcode.Drop:
                        SP--;
                        PC++;
                        break;
                    case Opcode.Reclaim:
                        SP++;
                        PC++;
                        break;
                    case Opcode.Add:
                        // TODO: The sign might not work here!
                        int add_temp = memory[SP] + memory[SP - 1];
                        carry = add_temp > 0xFFF;
                        memory[SP - 1] = (short)(add_temp & 0xFFF);
                        SP--;
                        PC++;
                        break;
                    case Opcode.Sh_l:
                        int shl_temp = memory[SP] << 1;
                        carry = shl_temp > 0xFFF;
                        memory[SP] = (short)(shl_temp & 0xFFF);
                        PC++;
                        break;
                    case Opcode.Sh_r:
                        int shr_temp = memory[SP];
                        carry = (shr_temp & 0x1) > 1;
                        memory[SP] = (short)(shr_temp >> 1);
                        PC++;
                        break;
                    case Opcode.Not:
                        memory[SP] = (short) ~memory[SP];
                        PC++;
                        break;
                    case Opcode.Neg:
                        memory[SP] = (short) -memory[SP];
                        PC++;
                        break;
                    case Opcode.Xor:
                        memory[SP - 1] = (short) (memory[SP] ^ memory[SP - 1]);
                        SP--;
                        PC++;
                        break;
                    case Opcode.And:
                        memory[SP - 1] = (short)(memory[SP] & memory[SP - 1]);
                        PC++;
                        break;
                    case Opcode.Inc:
                        memory[SP] = ++memory[SP];
                        PC++;
                        break;
                    case Opcode.Add_f:
                        throw new NotImplementedException();
                    case Opcode.Neg_f:
                        memory[SP] = (short) (memory[SP] ^ 0x800);
                        PC++;
                        break;
                    case Opcode.Jmp:
                        int jmp_address = ToInt(memory[++PC], memory[++PC]);
                        PC = jmp_address;
                        break;
                    case Opcode.Jpm_z:
                        if (memory[SP] == 0)
                        {
                            PC = ToInt(memory[++PC], memory[++PC]);
                        }
                        else
                        {
                            PC += 3;
                        }
                        break;
                    case Opcode.Jmp_nz:
                        if (memory[SP] != 0)
                        {
                            PC = ToInt(memory[++PC], memory[++PC]);
                        }
                        else
                        {
                            PC += 3;
                        }
                        break;
                    case Opcode.Jmp_cz:
                        if (carry == false)
                        {
                            PC = ToInt(memory[++PC], memory[++PC]);
                        }
                        else
                        {
                            PC += 3;
                        }
                        break;
                    case Opcode.Jmp_fz:
                        throw new NotImplementedException();
                    case Opcode.Hlt:
                        StopRunning = true;
                        break;
                    default:
                        throw new Exception($"{op}");
                }
            }
        }

        static int ToInt(short upper, short lower)
        {
            return (((ushort)upper << 12) | (ushort)lower);
        }
    }
}
