using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;

namespace VM12
{
    enum InterruptType : int
    {
        h_Timer = 0xFFF_FF0,
        v_Blank = 0xFFF_FE0,
        keyboard = 0xFFF_FD0,
        mouse = 0xFFF_FC0,
    }

    struct Interrupt
    {
        public readonly InterruptType Type;
        public readonly short[] Args;

        public Interrupt(InterruptType type, short[] args)
        {
            Type = type;
            Args = args;
        }
    }

    class Memory
    {
        public const int RAM_SIZE = 4194304;
        public const int VRAM_SIZE = 307200;
        public const int ROM_SIZE = 12275712;

        public const int RAM_START = 0;
        public const int VRAM_START = RAM_SIZE;
        public const int ROM_START = RAM_SIZE + VRAM_SIZE;

        public const int MEM_SIZE = RAM_SIZE + VRAM_SIZE + ROM_SIZE;

        public readonly short[] MEM = new short[MEM_SIZE];

        //public readonly short[] RAM = new short[RAM_SIZE];

        //public readonly short[] VRAM = new short[VRAM_SIZE];

        //public readonly short[] ROM = new short[ROM_SIZE];

        public void GetRAM(short[] ram, int index)
        {
            Array.Copy(MEM, RAM_START + index, ram, 0, ram.Length);
        }

        public void GetVRAM(short[] vram, int index)
        {
            Array.Copy(MEM, VRAM_START + index, vram, 0, vram.Length);
        }

        public void GetROM(short[] rom, int index)
        {
            Array.Copy(MEM, ROM_START + index, rom, 0, rom.Length);
        }

        public void SetROM(short[] ROM)
        {
            Array.Copy(ROM, 0, MEM, ROM_START, ROM.Length);
        }

        //TODO: Fix memory access overhead!
        public short this[int i]
        {
            get => MEM[i];
            set
            {
                if (i < ROM_START)
                {
                    MEM[i] = value;
                }
                else if (i < ROM_START + ROM_SIZE)
                {
                    throw new ArgumentException($"Cannot modify ROM! At index {i:X}");
                }
                else
                {
                    throw new IndexOutOfRangeException();
                }
            }
        }
    }

    struct ReadOnlyMemory
    {
        readonly Memory mem;

        public bool HasMemory => mem != null;

        public ReadOnlyMemory(Memory mem)
        {
            this.mem = mem;
        }

        public void GetRAM(short[] ram, int index)
        {
            mem.GetRAM(ram, index);
        }

        public void GetVRAM(short[] vram, int index)
        {
            mem.GetVRAM(vram, index);
        }

        public void GetROM(short[] rom, int index)
        {
            mem.GetROM(rom, index);
        }

        public short this[int i]
        {
            get => mem[i];
        }
    }

    class VM12
    {
        public const int SCREEN_WIDTH = 640;
        public const int SCREEN_HEIGHT = 480;

        Memory memory = new Memory();

        public ReadOnlyMemory ReadMemory => new ReadOnlyMemory(memory);

        ConcurrentQueue<Interrupt> interrupts = new ConcurrentQueue<Interrupt>();

        public void Interrupt(Interrupt interrupt) { if(interruptsEnabled) interrupts.Enqueue(interrupt); }

        bool carry = false;
        bool interruptsEnabled = false;

        int PC = Memory.ROM_START;
        int SP = 2;

        long programTime = 0;

        bool StopRunning = false;

        private float TimerInterval = 1000;

        public bool Stopped => StopRunning;
        public int ProgramCounter => PC;
        public int StackPointer => SP;
        public bool InterruptsEnabled => interruptsEnabled;
        public long Ticks => programTime;

        Stack<int> returnStack = new Stack<int>(20);

        public ConcurrentDictionary<Opcode, int> instructionFreq = new ConcurrentDictionary<Opcode, int>(1, 64);
        
        public VM12(short[] ROM)
        {
            memory.SetROM(ROM);
        }

        public void Start()
        {
            //Stopwatch sw = new Stopwatch();

            //sw.Start();

            while (StopRunning != true)
            {
                /*if (interruptsEnabled && interrupts.Count > 0)
                {
                    if (interrupts.TryDequeue(out Interrupt interrupt))
                    {
                        //TODO: Push arguemnts!
                        returnStack.Push(PC);
                        PC = (int)interrupt.Type;
                    }
                    else
                    {
                        throw new Exception("Could not read interrupt instruction");
                    }
                }*/

                Opcode op = (Opcode)(memory.MEM[PC] & 0x0FFF);

#if DEBUG
                if (instructionFreq.TryGetValue(op, out int v))
                {
                    instructionFreq[op] = v + 1;
                }
                else
                {
                    instructionFreq[op] = 1;
                }
                
                if ((memory[PC] & 0xF000) != 0)
                {
                    //sw.Stop();
                    ;
                    //sw.Start();
                }
#endif       

                switch (op)
                {
                    case Opcode.Nop:
                        PC++;
                        break;
                    case Opcode.Load_addr:
                        int load_address = ToInt(memory.MEM[++PC], memory.MEM[++PC]);
                        PC++;
                        memory.MEM[++SP] = memory.MEM[load_address];
                        break;
                    case Opcode.Load_lit:
                        memory.MEM[++SP] = memory.MEM[++PC];
                        PC++;
                        break;
                    case Opcode.Load_sp:
                        int load_sp_address = ToInt(memory.MEM[SP--], memory.MEM[SP]);
                        memory.MEM[SP] = memory.MEM[load_sp_address];
                        PC++;
                        break;
                    case Opcode.Store_pc:
                        int store_pc_address = ToInt(memory.MEM[++PC], memory.MEM[++PC]);
                        PC++;
                        memory.MEM[store_pc_address] = memory.MEM[SP];
                        break;
                    case Opcode.Store_sp:
                        int store_sp_address = ToInt(memory.MEM[SP - 1], memory.MEM[SP]);
                        memory.MEM[store_sp_address] = memory.MEM[SP - 2];
                        PC++;
                        break;
                    case Opcode.Call_sp:
                        returnStack.Push(PC + 1);
                        PC = ToInt(memory.MEM[SP--], memory.MEM[SP--]);
                        break;
                    case Opcode.Call_pc:
                        returnStack.Push(PC + 3);
                        PC = ToInt(memory.MEM[++PC], memory.MEM[++PC]);
                        break;
                    case Opcode.Ret:
                        PC = returnStack.Pop();
                        break;
                    case Opcode.Dup:
                        memory.MEM[SP + 1] = memory.MEM[SP];
                        SP++;
                        PC++;
                        break;
                    case Opcode.Over:
                        SP++;
                        memory.MEM[SP] = memory.MEM[SP - 2];
                        PC++;
                        break;
                    case Opcode.Swap:
                        short swap_temp = memory.MEM[SP];
                        memory.MEM[SP] = memory.MEM[SP - 1];
                        memory.MEM[SP - 1] = swap_temp;
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
                        int add_temp = memory.MEM[SP] + memory.MEM[SP - 1];
                        carry = add_temp > 0xFFF;
                        SP--;
                        memory.MEM[SP] = (short)(add_temp - (carry ? 0x1000 : 0));
                        PC++;
                        break;
                    case Opcode.Not:
                        memory.MEM[SP] = (short)~memory.MEM[SP];
                        PC++;
                        break;
                    case Opcode.Neg:
                        memory.MEM[SP] = (short)-memory.MEM[SP];
                        PC++;
                        break;
                    case Opcode.Or:
                        memory.MEM[SP - 1] = (short)(memory.MEM[SP] | memory.MEM[SP - 1]);
                        SP--;
                        PC++;
                        break;
                    case Opcode.Xor:
                        memory.MEM[SP - 1] = (short)(memory.MEM[SP] ^ memory.MEM[SP - 1]);
                        SP--;
                        PC++;
                        break;
                    case Opcode.And:
                        memory.MEM[SP - 1] = (short)(memory.MEM[SP] & memory.MEM[SP - 1]);
                        SP--;
                        PC++;
                        break;
                    case Opcode.Inc:
                        int mem_val = memory.MEM[SP] + 1;
                        carry = mem_val > 0xFFF;
                        memory.MEM[SP] = (short)(mem_val & 0xFFF);
                        PC++;
                        break;
                    case Opcode.C_ss:
                        carry = (memory.MEM[SP] & 0x0800) != 0;
                        PC++;
                        break;
                    case Opcode.Rot_l_c:
                        ushort rot_l_value = (ushort)memory.MEM[SP];
                        bool rot_l_c = (rot_l_value & 0x800) != 0;
                        memory.MEM[SP] = (short)(((rot_l_value << 1) | (carry ? 1 : 0)) & 0x0FFF);
                        carry = rot_l_c;
                        PC++;
                        break;
                    case Opcode.Rot_r_c:
                        ushort rot_r_value = (ushort)memory.MEM[SP];
                        bool rot_r_c = (rot_r_value & 0x001) != 0;
                        memory.MEM[SP] = (short)(((rot_r_value >> 1) | (carry ? 0x800 : 0)) & 0x0FFF);
                        carry = rot_r_c;
                        PC++;
                        break;
                    case Opcode.Jmp:
                        int jmp_address = ToInt(memory.MEM[++PC], memory.MEM[++PC]);
                        PC = jmp_address;
                        break;
                    case Opcode.Jmp_z:
                        if (memory.MEM[SP] == 0)
                        {
                            PC = ToInt(memory.MEM[++PC], memory.MEM[++PC]);
                        }
                        else
                        {
                            PC += 3;
                        }
                        break;
                    case Opcode.Jmp_nz:
                        if (memory.MEM[SP] != 0)
                        {
                            PC = ToInt(memory.MEM[++PC], memory.MEM[++PC]);
                        }
                        else
                        {
                            PC += 3;
                        }
                        break;
                    case Opcode.Jmp_cz:
                        if (carry == false)
                        {
                            PC = ToInt(memory.MEM[++PC], memory.MEM[++PC]);
                        }
                        else
                        {
                            PC += 3;
                        }
                        break;
                    case Opcode.Jmp_fz:
                        throw new NotImplementedException();
                    case Opcode.Eni:
                        interruptsEnabled = true;
                        PC++;
                        break;
                    case Opcode.Dsi:
                        interruptsEnabled = false;
                        PC++;
                        break;
                    case Opcode.Hlt:
                        StopRunning = true;
                        break;
                    case Opcode.C_se:
                        carry = true;
                        break;
                    case Opcode.C_cl:
                        carry = false;
                        break;
                    case Opcode.C_flp:
                        carry = !carry;
                        break;
                    case Opcode.Jmp_c:
                        if (memory.MEM[SP] == 0)
                        {
                            PC = ToInt(memory.MEM[++PC], memory.MEM[++PC]);
                        }
                        else
                        {
                            PC += 3;
                        }
                        break;
                    case Opcode.Jmp_gz:
                        if (memory.MEM[SP] > 0)
                        {
                            PC = ToInt(memory.MEM[++PC], memory.MEM[++PC]);
                        }
                        else
                        {
                            PC += 3;
                        }
                        break;
                    case Opcode.Jmp_lz:
                        if ((memory.MEM[SP] & 0x800) > 0)
                        {
                            PC = ToInt(memory.MEM[++PC], memory.MEM[++PC]);
                        }
                        else
                        {
                            PC += 3;
                        }
                        break;
                    case Opcode.Jmp_z_l:
                        if ((memory.MEM[SP] | memory.MEM[SP - 1]) == 0)
                        {
                            PC = ToInt(memory.MEM[++PC], memory.MEM[++PC]);
                        }
                        else
                        {
                            PC += 3;
                        }
                        break;
                    case Opcode.Jmp_nz_l:
                        if ((memory.MEM[SP] | memory.MEM[SP - 1]) != 0)
                        {
                            PC = ToInt(memory.MEM[++PC], memory.MEM[++PC]);
                        }
                        else
                        {
                            PC += 3;
                        }
                        break;
                    case Opcode.Nand:
                        memory.MEM[SP - 1] = (short)~(memory.MEM[SP] & memory.MEM[SP - 1]);
                        PC++;
                        break;
                    case Opcode.Xnor:
                        memory.MEM[SP - 1] = (short)~(memory.MEM[SP] ^ memory.MEM[SP - 1]);
                        PC++;
                        break;
                    case Opcode.Dec:
                        // TODO: Carry
                        memory.MEM[SP] = (short)(memory.MEM[SP] - 1);
                        PC++;
                        break;
                    case Opcode.Add_c:
                        break;
                    case Opcode.Inc_l:
                        short linc_value;
                        memory.MEM[SP] = (short)((linc_value = (short) (memory.MEM[SP] + 1)) & 0xFFF);
                        memory.MEM[SP - 1] = (short)((linc_value = (short)(memory.MEM[SP - 1] + (linc_value > 0xFFF ? 1 : 0))) & 0xFFF);
                        carry = linc_value > 0xFFF;
                        PC++;
                        break;
                    case Opcode.Dec_l:
                        break;
                    case Opcode.Add_l:
                        //FIXME!!
                        int add1 = ToInt(memory.MEM[SP--], memory.MEM[SP--]);
                        int add2 = ToInt(memory.MEM[SP], memory.MEM[SP - 1]);
                        add2 += add1;
                        carry = add2 >> 12 > 0xFFF;
                        memory.MEM[SP] = (short)(add2 >> 12);
                        memory.MEM[SP - 1] = (short)(add2 & 0xFFF);
                        break;
                    case Opcode.Not_l:
                        break;
                    case Opcode.Neg_l:
                        int add_l_val = -ToInt(memory.MEM[SP], memory.MEM[SP - 1]);
                        memory.MEM[SP] = (short)(add_l_val >> 12);
                        memory.MEM[SP - 1] = (short)(add_l_val & 0xFFF);
                        PC++;
                        break;
                    case Opcode.Load_lit_l:
                        break;
                    case Opcode.Memc:
                        break;
                    case Opcode.Read:
                        break;
                    case Opcode.Write:
                        break;
                    case Opcode.Call_pc_nz:
                        break;
                    case Opcode.Call_pc_cz:
                        break;
                    case Opcode.Call_sp_nz:
                        break;
                    case Opcode.Call_sp_cz:
                        break;
                    case Opcode.Ret_z:
                        break;
                    case Opcode.Ret_nz:
                        break;
                    case Opcode.Ret_cz:
                        break;
                    case Opcode.Dup_l:
                        break;
                    case Opcode.Over_l_l:
                        break;
                    case Opcode.Over_l_s:
                        break;
                    case Opcode.Swap_l:
                        break;
                    default:
                        throw new Exception($"{op}");
                }

                programTime++;

                /*if (programTime % TimerInterval == 0)
                {
                    Interrupt(new Interrupt(InterruptType.h_Timer, null));
                }*/

                //SpinWait.SpinUntil(() => sw.ElapsedTicks > TimerInterval);
                //sw.Restart();
            }
        }

        public void Stop()
        {
            StopRunning = true;
        }

        static int ToInt(short upper, short lower)
        {
            return ((upper << 12) | (ushort)lower);
        }
    }
}
