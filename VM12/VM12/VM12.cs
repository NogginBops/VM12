using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;
using VM12_Opcode;

namespace VM12
{
    enum InterruptType : int
    {
        stop,
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

        public readonly short[] MEM;

        public Memory(short[] mem)
        {
            MEM = mem;
        }

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

        short[] MEM = new short[Memory.MEM_SIZE];

        Memory memory;

        public ReadOnlyMemory ReadMemory => new ReadOnlyMemory(memory);

        //ConcurrentQueue<Interrupt> interrupts = new ConcurrentQueue<Interrupt>();

        Interrupt? intrr = null;

        public void Interrupt(Interrupt interrupt) { if (interruptsEnabled) { /*interrupts.Enqueue(interrupt);*/ intrr = intrr ?? interrupt; interrupt_event.Set(); } else { MissedInterrupts++; } }

        bool carry = false;
        bool interruptsEnabled = false;
        bool halt = false;

        AutoResetEvent interrupt_event = new AutoResetEvent(false);

        int PC = Memory.ROM_START;
        int SP = 2;

        long programTime = 0;

        private const float TimerInterval = 1000;

        public bool Running { get; set; }
        public bool Stopped => halt && !interruptsEnabled;
        public int ProgramCounter => PC;
        public int StackPointer => SP;
        public bool InterruptsEnabled => interruptsEnabled;
        public long Ticks => programTime;
        public int Calls => returnStack.Count;

        Stack<int> returnStack = new Stack<int>(20);

#if DEBUG
        public int[] instructionFreq = new int[64];
#endif

        public VM12(short[] ROM)
        {
            memory = new Memory(MEM);
            memory.SetROM(ROM);
        }

        public int InterruptCount = 0;
        public int MissedInterrupts = 0;

        public unsafe void Start()
        {
            //Stopwatch sw = new Stopwatch();

            //sw.Start();

            Running = true;

            fixed (short* mem = MEM)
            {
                while (true)
                {
                    if (intrr != null)
                    {
                        if (intrr.Value.Type == InterruptType.stop)
                        {
                            break;
                        }
                        InterruptCount++;
                        Interrupt intr = intrr.Value;
                        returnStack.Push(PC);
                        PC = (int)intr.Type;
                        for (int i = 0; i < intr.Args.Length; i++)
                        {
                            mem[++SP] = intr.Args[i];
                        }
                        intrr = null;
                    }
                    /*if (interruptsEnabled && interrupts.TryDequeue(out Interrupt intr))
                    {
                        returnStack.Push(PC);
                        PC = (int)intr.Type;
                        for (int i = 0; i < intr.Args.Length; i++)
                        {
                            mem[++SP] = intr.Args[i];
                        }
                    }*/

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

                    Opcode op = (Opcode)(mem[PC] & 0x0FFF);

#if DEBUG
                    instructionFreq[(int)op]++;

                    if ((mem[PC] & 0xF000) != 0)
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
                            int load_address = (mem[++PC] << 12) | (ushort)(mem[++PC] & 0xFFF);
                            //int load_address = ToInt(mem[++PC], mem[++PC]);
                            PC++;
                            mem[++SP] = mem[load_address];
                            break;
                        case Opcode.Load_lit:
                            mem[++SP] = mem[++PC];
                            PC++;
                            break;
                        case Opcode.Load_sp:
                            int load_sp_address = ToInt(mem[SP - 1], mem[SP]);
                            mem[++SP] = mem[load_sp_address];
                            PC++;
                            break;
                        case Opcode.Store_pc:
                            int store_pc_address = ToInt(mem[++PC], mem[++PC]);
                            PC++;
                            mem[store_pc_address] = mem[SP];
                            break;
                        case Opcode.Store_sp:
                            int store_sp_address = (mem[SP - 1] << 12) | (ushort)(mem[SP]); //ToInt(mem[SP - 1], mem[SP]);
                            mem[store_sp_address] = mem[SP - 2];
                            PC++;
                            break;
                        case Opcode.Call_sp:
                            returnStack.Push(PC + 1);
                            PC = ToInt(mem[SP - 1], mem[SP]);
                            SP -= 2;
                            break;
                        case Opcode.Call_pc:
                            returnStack.Push(PC + 3);
                            PC = ToInt(mem[++PC], mem[++PC]);
                            break;
                        case Opcode.Ret:
                            PC = returnStack.Pop();
                            break;
                        case Opcode.Dup:
                            mem[SP + 1] = mem[SP];
                            SP++;
                            PC++;
                            break;
                        case Opcode.Over:
                            SP++;
                            mem[SP] = mem[SP - 2];
                            PC++;
                            break;
                        case Opcode.Swap:
                            short swap_temp = mem[SP];
                            mem[SP] = mem[SP - 1];
                            mem[SP - 1] = swap_temp;
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
                            uint add_temp = (uint)(mem[SP] + mem[SP - 1]);
                            carry = add_temp > 0xFFF;
                            SP--;
                            mem[SP] = (short)(add_temp - (carry ? 0x1000 : 0));
                            PC++;
                            break;
                        case Opcode.Not:
                            mem[SP] = (short)~mem[SP];
                            PC++;
                            break;
                        case Opcode.Neg:
                            mem[SP] = (short)-mem[SP];
                            PC++;
                            break;
                        case Opcode.Or:
                            mem[SP - 1] = (short)(mem[SP] | mem[SP - 1]);
                            SP--;
                            PC++;
                            break;
                        case Opcode.Xor:
                            mem[SP - 1] = (short)(mem[SP] ^ mem[SP - 1]);
                            SP--;
                            PC++;
                            break;
                        case Opcode.And:
                            mem[SP - 1] = (short)(mem[SP] & mem[SP - 1]);
                            SP--;
                            PC++;
                            break;
                        case Opcode.Inc:
                            int mem_val = mem[SP] + 1;
                            carry = mem_val > 0xFFF;
                            mem[SP] = (short)(mem_val & 0xFFF);
                            PC++;
                            break;
                        case Opcode.C_ss:
                            carry = (mem[SP] & 0x0800) != 0;
                            PC++;
                            break;
                        case Opcode.Rot_l_c:
                            ushort rot_l_value = (ushort)mem[SP];
                            bool rot_l_c = (rot_l_value & 0x800) != 0;
                            mem[SP] = (short)(((rot_l_value << 1) | (carry ? 1 : 0)) & 0x0FFF);
                            carry = rot_l_c;
                            PC++;
                            break;
                        case Opcode.Rot_r_c:
                            ushort rot_r_value = (ushort)mem[SP];
                            bool rot_r_c = (rot_r_value & 0x001) != 0;
                            mem[SP] = (short)(((rot_r_value >> 1) | (carry ? 0x800 : 0)) & 0x0FFF);
                            carry = rot_r_c;
                            PC++;
                            break;
                        case Opcode.Jmp:
                            PC = (mem[++PC] << 12) | (ushort)(mem[++PC]); //ToInt(mem[++PC], mem[++PC]);
                            break;
                        case Opcode.Jmp_z:
                            if (mem[SP] == 0)
                            {
                                PC = ToInt(mem[++PC], mem[++PC]);
                            }
                            else
                            {
                                PC += 3;
                            }
                            break;
                        case Opcode.Jmp_nz:
                            if (mem[SP] != 0)
                            {
                                PC = ToInt(mem[++PC], mem[++PC]);
                            }
                            else
                            {
                                PC += 3;
                            }
                            break;
                        case Opcode.Jmp_cz:
                            if (carry == false)
                            {
                                PC = ToInt(mem[++PC], mem[++PC]);
                            }
                            else
                            {
                                PC += 3;
                            }
                            break;
                        case Opcode.Jmp_c:
                            if (mem[SP] == 0)
                            {
                                PC = ToInt(mem[++PC], mem[++PC]);
                            }
                            else
                            {
                                PC += 3;
                            }
                            break;
                        case Opcode.Eni:
                            interruptsEnabled = true;
                            PC++;
                            break;
                        case Opcode.Dsi:
                            interruptsEnabled = false;
                            PC++;
                            break;
                        case Opcode.Hlt:
                            halt = true;
                            if (interruptsEnabled)
                            {
                                interrupt_event.WaitOne();
                                halt = false;
                            }
                            else
                            {
                                goto end;
                            }
                            PC++;
                            break;
                        case Opcode.C_se:
                            carry = true;
                            PC++;
                            break;
                        case Opcode.C_cl:
                            carry = false;
                            PC++;
                            break;
                        case Opcode.C_flp:
                            carry = !carry;
                            PC++;
                            break;
                        case Opcode.Jmp_gz:
                            if (mem[SP] > 0)
                            {
                                PC = ToInt(mem[++PC], mem[++PC]);
                            }
                            else
                            {
                                PC += 3;
                            }
                            break;
                        case Opcode.Jmp_lz:
                            if ((mem[SP] & 0x800) > 0)
                            {
                                PC = ToInt(mem[++PC], mem[++PC]);
                            }
                            else
                            {
                                PC += 3;
                            }
                            break;
                        case Opcode.Jmp_z_l:
                            if ((mem[SP] | mem[SP - 1]) == 0)
                            {
                                PC = (mem[++PC] << 12) | (ushort)(mem[++PC]); //ToInt(mem[++PC], mem[++PC]);
                            }
                            else
                            {
                                PC += 3;
                            }
                            break;
                        case Opcode.Jmp_nz_l:
                            if ((mem[SP] | mem[SP - 1]) != 0)
                            {
                                PC = ToInt(mem[++PC], mem[++PC]);
                            }
                            else
                            {
                                PC += 3;
                            }
                            break;
                        case Opcode.Nand:
                            mem[SP - 1] = (short)~(mem[SP] & mem[SP - 1]);
                            PC++;
                            break;
                        case Opcode.Xnor:
                            mem[SP - 1] = (short)~(mem[SP] ^ mem[SP - 1]);
                            PC++;
                            break;
                        case Opcode.Dec:
                            int dec_value = mem[SP] - 1;
                            carry = dec_value < 0;
                            mem[SP] = (short)dec_value;
                            PC++;
                            break;
                        case Opcode.Add_c:
                            break;
                        case Opcode.Inc_l:
                            int linc_value = ((mem[SP - 1] << 12) | (ushort)(mem[SP])) + 1;
                            mem[SP] = (short) (linc_value & 0xFFF);
                            mem[SP - 1] = (short)((linc_value >> 12) & 0xFFF);
                            carry = (linc_value >> 12) > 0xFFF;
                            PC++;
                            break;
                        case Opcode.Dec_l:
                            uint ldec_value = ((uint)(mem[SP - 1] << 12) | (ushort)(mem[SP])) - 1;
                            mem[SP] = (short)(ldec_value & 0xFFF);
                            mem[SP - 1] = (short)((ldec_value >> 12) & 0xFFF);
                            carry = ldec_value == uint.MaxValue;
                            PC++;
                            break;
                        case Opcode.Add_l:
                            int add1 = (mem[SP - 1] << 12) | (ushort)(mem[SP]); //ToInt(mem[SP - 1], mem[SP]);
                            int add2 =  (mem[SP - 3] << 12) | (ushort)(mem[SP - 2]); //ToInt(mem[SP - 3], mem[SP - 2]);
                            SP -= 2;
                            add2 += add1;
                            carry = add2 >> 12 > 0xFFF;
                            mem[SP - 1] = (short)(add2 >> 12);
                            mem[SP] = (short)(add2 & 0xFFF);
                            PC++;
                            break;
                        case Opcode.Not_l:
                            mem[SP] = (short) ~mem[SP];
                            mem[SP - 1] = (short)~mem[SP - 1];
                            PC++;
                            break;
                        case Opcode.Neg_l:
                            int neg_l_val = -((mem[SP - 1] << 12) | (ushort)(mem[SP])); //-ToInt(mem[SP - 1], mem[SP]);
                            mem[SP] = (short)(neg_l_val & 0xFFF);
                            mem[SP - 1] = (short)(neg_l_val >> 12);
                            PC++;
                            break;
                        case Opcode.Load_lit_l:
                            mem[++SP] = mem[++PC];
                            mem[++SP] = mem[++PC];
                            PC++;
                            break;
                        case Opcode.Memc:
                            break;
                        case Opcode.Read:
                            break;
                        case Opcode.Write:
                            break;
                        case Opcode.Ret_cz:
                            if (carry)
                            {
                                PC = (mem[++PC] << 12) | (ushort) mem[++PC];
                            }
                            else
                            {
                                PC++;
                            }
                            break;
                        case Opcode.Dup_l:
                            mem[SP + 1] = mem[SP - 1];
                            mem[SP + 2] = mem[SP];
                            SP += 2;
                            PC++;
                            break;
                        case Opcode.Over_l_l:
                            mem[SP + 1] = mem[SP - 3];
                            mem[SP + 2] = mem[SP - 2];
                            SP += 2;
                            PC++;
                            break;
                        case Opcode.Over_l_s:
                            mem[SP + 1] = mem[SP - 2];
                            mem[SP + 2] = mem[SP - 1];
                            SP += 2;
                            PC++;
                            break;
                        case Opcode.Swap_l:
                            short[] swap_l_temp = new short[2];
                            swap_l_temp[0] = mem[SP - 3];
                            swap_l_temp[1] = mem[SP - 2];
                            mem[SP - 3] = mem[SP - 1];
                            mem[SP - 2] = mem[SP];
                            mem[SP - 1] = swap_l_temp[0];
                            mem[SP] = swap_l_temp[1];
                            PC++;
                            break;
                        case Opcode.Drop_v:
                            SP -= mem[++PC];
                            PC++;
                            break;
                        case Opcode.Reclaim_v:
                            SP += mem[++PC];
                            PC++;
                            break;
                        default:
                            throw new Exception($"{op}");
                    }

                    programTime++;

                    /*
                    if (programTime % TimerInterval == 0)
                    {
                        Interrupt(new Interrupt(InterruptType.h_Timer, new short[0]));
                    }
                    */

                    //SpinWait.SpinUntil(() => sw.ElapsedTicks > TimerInterval);
                    //sw.Restart();

                    //Thread.SpinWait(1000);
                }
            }
            end:  Running = false;
        }

        public void Stop()
        {
            intrr = new Interrupt(InterruptType.stop, null);
            halt = true;
            interruptsEnabled = false;
        }

        static int ToInt(short upper, short lower)
        {
            return ((upper << 12) | (ushort)lower);
        }
    }
}
