#define BREAKS

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;
using VM12_Opcode;
using System.IO;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace VM12
{
    using Debugger = System.Diagnostics.Debugger;

    enum InterruptType : int
    {
        stop,
        h_Timer = 0xFFF_FF0,
        v_Blank = 0xFFF_FE0,
        keyboard = 0xFFF_FD0,
        mouse = 0xFFF_FC0,
    }
    
    class Interrupt
    {
        public readonly InterruptType Type;
        public readonly int[] Args;

        public Interrupt(InterruptType type, int[] args)
        {
            Type = type;
            Args = args;
        }
    }

    unsafe class VM12
    {
        public const int RAM_SIZE = 4194304;
        public const int VRAM_SIZE = 307200;
        public const int ROM_SIZE = 12275712;

        public const int RAM_START = 0;
        public const int VRAM_START = RAM_SIZE;
        public const int ROM_START = RAM_SIZE + VRAM_SIZE;

        public const int MEM_SIZE = RAM_SIZE + VRAM_SIZE + ROM_SIZE;
        
        public const int SCREEN_WIDTH = 640;
        public const int SCREEN_HEIGHT = 480;

        public const int STORAGE_START_ADDR = 0;
        public const int STORAGE_SIZE = 357_913_941;

        public int[] MEM = new int[MEM_SIZE];

        private FileInfo storageFile;
        //public byte[] STORAGE = new byte[STORAGE_SIZE * 3];
        
        HashSet<InterruptType> activeInterrupts = new HashSet<InterruptType>();

        volatile Interrupt intrr = null;

        public void Interrupt(Interrupt interrupt)
        {
            if (interrupt == null)
            {
                interrupt_event.Set();
                return;
            }

            if (interruptsEnabled && intrr == null)
            {
                //intrr = intrr ?? interrupt;
                intrr = interrupt;
                interrupt_event.Set();
#if DEBUG
                InterruptFreq[InterruptTypeToInt(interrupt.Type)]++;
#endif
            }
            else
            {
#if DEBUG
                MissedInterruptFreq[InterruptTypeToInt(interrupt.Type)]++;
#endif
                MissedInterrupts++;
            }
        }

        bool carry = false;
        bool interruptsEnabled = false;
        bool halt = false;

#if DEBUG
        bool interruptsEnabledActual = false;
#endif

        AutoResetEvent interrupt_event = new AutoResetEvent(false);

#if DEBUG
        /// <summary>
        /// When the VM breaks it waits for this event
        /// </summary>
        public AutoResetEvent ContinueEvent { get; private set; } = new AutoResetEvent(false);

        /// <summary>
        /// Is set when the VM breaks
        /// </summary>
        public AutoResetEvent DebugBreakEvent { get; private set; } = new AutoResetEvent(false);

        public volatile bool CallInstruction = false;

        public volatile bool RetInstruction = false;

        public volatile bool HaltInstruction = false;

        public readonly object DebugSync = new object();

        public event EventHandler HitBreakpoint = null;
#endif
        
        int PC = ROM_START;
        int SP = -1;
        int FP = -1;
        int locals = 0;

        long programTime = 0;
        
        public bool Started { get; set; } = false;
        public bool Running { get; set; } = false;
        public bool Stopped => halt && !interruptsEnabled;
        public int ProgramCounter => PC;
        public int StackPointer => SP;
        public int FramePointer => FP;
        public bool InterruptsEnabled => interruptsEnabled;
        public long Ticks => programTime;
#if DEBUG
        public Opcode Opcode { get; private set; }
#endif

        public int SPWatermark = int.MinValue;
        public int FPWatermark = int.MinValue;

#if !DEBUG
        public VM12(short[] ROM)
        {
            Array.Copy(ROM, 0, MEM, ROM_START, ROM.Length);
        }
#elif DEBUG

        public volatile bool UseDebugger = false;
        
        public static int InterruptTypeToInt(InterruptType type)
        {
            switch (type)
            {
                case InterruptType.stop:
                    return 0;
                case InterruptType.h_Timer:
                    return 1;
                case InterruptType.v_Blank:
                    return 2;
                case InterruptType.keyboard:
                    return 3;
                case InterruptType.mouse:
                    return 4;
                default:
                    return -1;
            }
        }

        public int[] InterruptFreq = new int[Enum.GetValues(typeof(InterruptType)).Length];

        public int[] MissedInterruptFreq = new int[Enum.GetValues(typeof(InterruptType)).Length];

        public int[] instructionFreq = new int[Enum.GetValues(typeof(Opcode)).Length];

        public int[] instructionTimes = new int[Enum.GetValues(typeof(Opcode)).Length];

#region Debug

        public class ProcMetadata
        {
            public string name;
            public string file;
            public int location;
            public int sourceLine;
            public List<int> breaks;
            public Dictionary<int, int> lineLinks;
            public int size;

            public override string ToString()
            {
                return name + ": " + base.ToString();
            }
        }

        public class StackFrame
        {
            public string file;
            public string procName;
            public int line;
            public int FP;
            public int return_addr;
            public int prev_addr;
            public StackFrame prev;
            public int locals;
            public int[] localValues;
        }

        public struct AutoConst
        {
            public readonly string Name;
            public readonly int Addr;
            public readonly int Length;

            public AutoConst(string name, int addr, int length)
            {
                Name = name;
                Addr = addr;
                Length = length;
            }
        }

        public Dictionary<string, AutoConst> autoConsts { get; private set; } = new Dictionary<string, AutoConst>();

        int GetConstValue(string name)
        {
            AutoConst constant = autoConsts[name];
            return GetConstValue(constant);
        }

        int GetConstValue(AutoConst constant)
        {
            switch (constant.Length)
            {
                case 1:
                    return MEM[constant.Addr];
                case 2:
                    return MEM[constant.Addr] << 12 | MEM[constant.Addr + 1];
                default:
                    // TODO: Maybe better printout
                    return MEM[constant.Addr] << 12 | MEM[constant.Addr + 1];
            }
        }

        Dictionary<string, int> ConstValues
        {
            get 
            {
                Dictionary<string, int> values = new Dictionary<string, int>();

                foreach (var c in autoConsts)
                {
                    values[c.Key] = GetConstValue(c.Value);
                }

                return values;
            }
        }

        List<ProcMetadata> metadata = new List<ProcMetadata>();

        public ProcMetadata GetMetadataFromOffset(int offset)
        {
            foreach (var data in metadata)
            {
                if (offset >= data.location && offset < data.location + data.size)
                {
                    return data;
                }
            }

            return null;
        }

        public ProcMetadata CurrentMetadata => GetMetadataFromOffset(PC);
        
        bool[] breaks;

        Dictionary<string, string[]> source = new Dictionary<string, string[]>();

        public string GetSourceCodeLine(int offset)
        {
            ProcMetadata data = GetMetadataFromOffset(offset);
            
            if (data == null) return "Source not available!";
            
            if (source.TryGetValue(data.file, out string[] lines))
            {
                return lines[GetSourceCodeLineFromMetadataAndOffset(data, offset) - 1];
            }
            else
            {
                return "Source not available!";
            }
        }

        int GetSourceCodeLineFromOffset(int offset)
        {
            ProcMetadata meta = GetMetadataFromOffset(offset);

            if (meta == null) return -1;

            return GetSourceCodeLineFromMetadataAndOffset(meta, offset);
        }

        public int GetSourceCodeLineFromMetadataAndOffset(ProcMetadata meta, int offset)
        {
            int localOffset = offset - meta.location;

            int line = -1;
            foreach (var dataOffset in meta.lineLinks.Keys)
            {
                if (dataOffset <= localOffset)
                {
                    line = meta.lineLinks[dataOffset];
                }
                else
                {
                    break;
                }
            }

            return line;
        }

        public int CurrentSourceCodeLine => GetSourceCodeLineFromOffset(PC);
        
        string CurrentSource
        {
            get
            {
                string line = GetSourceCodeLine(PC);

                if (line == null) return null;

                if (line[0] == '¤')
                {
                    line = line.Substring(1);
                }

                line = line.Replace('\t', ' ');

                return line.Trim();
            }
        }

        int[] Locals
        {
            get
            {
                int[] locals = new int[this.locals];
                for (int i = 1; i <= locals.Length; i++)
                {
                    locals[locals.Length - i] = MEM[FP - i];
                }

                return locals;
            }
        }

        int[] CurrentStack
        {
            get
            {
                int stackStart = FP == -1 ? 0 : (FP + 5);
                int values = SP + 1 - stackStart;

                int[] stack = new int[values];

                for (int i = 0; i < values; i++)
                {
                    stack[i] = MEM[stackStart + i];
                }

                return stack;
            }
        }

        string[] CurrentCompactFrame => GetCompactStackFrame(CurrentStackFrame);

        string[] GetCompactStackFrame(StackFrame frame)
        {
            if (frame == null)
            {
                return null;
            }

            List<string> callers = new List<string>();

            StackFrame currFrame = frame;

            do
            {
                callers.Add($"{currFrame.procName}[{currFrame.file}:{currFrame.line}]");

                currFrame = currFrame.prev;
            } while (currFrame != null);

            return callers.Reverse<string>().ToArray();
        }

        public StackFrame CurrentStackFrame => ConstructStackFrame(FP, PC);

        public StackFrame ConstructStackFrame(int fp, int pc)
        {
            if (fp < 0 || fp == 0x00000000)
            {
                ProcMetadata main = GetMetadataFromOffset(pc);

                StackFrame main_frame = new StackFrame();

                main_frame.file = main?.file;
                main_frame.procName = main?.name;
                main_frame.line = main != null ? GetSourceCodeLineFromMetadataAndOffset(main, pc) : -1;

                main_frame.FP = -1;

                main_frame.return_addr = 0;
                main_frame.prev_addr = -1;
                main_frame.prev = null;
                main_frame.locals = 0;
                main_frame.localValues = null;

                return main_frame;
            }

            StackFrame frame = new StackFrame();

            ProcMetadata data = GetMetadataFromOffset(pc);

            frame.file = data?.file;
            frame.procName = data?.name;
            frame.line = data != null ? GetSourceCodeLineFromMetadataAndOffset(data, pc) : -1;

            frame.FP = fp;

            frame.return_addr = MEM[fp] << 12 | (ushort)MEM[fp + 1];

            frame.prev_addr = MEM[fp + 2] << 12 | (ushort)MEM[fp + 3];

            if (frame.return_addr >= 0xFFF_FC0)
            {
                // Interrupts dont have a caller
                frame.prev = ConstructStackFrame(frame.prev_addr, frame.return_addr);
            }
            else
            {
                frame.prev = ConstructStackFrame(frame.prev_addr, frame.return_addr - 3);
            }


            frame.locals = MEM[fp + 4] << 12 | MEM[fp + 5];
            frame.localValues = new int[frame.locals];
            for (int i = 0; i < frame.locals; i++)
            {
                frame.localValues[i] = MEM[fp - frame.locals + i];
            }

            return frame;
        }

        public static int CountStackDepth(StackFrame frame)
        {
            int depth = 1;
            while (frame.prev != null)
            {
                depth++;
                frame = frame.prev;
            }

            return depth;
        }

        public DirectoryInfo sourceDir { get; private set; }

#endregion
        
        public VM12(short[] ROM, FileInfo metadata, FileInfo storage)
        {
            Array.Copy(ROM, 0, MEM, ROM_START, ROM.Length);

            sourceDir = metadata.Directory;

            ParseMetadata(metadata);
            
            breaks = new bool[MEM.Length];
            foreach (var data in this.metadata)
            {
                if (data.breaks == null) continue;

                foreach (var b in data.breaks)
                {
                    breaks[data.location + b] = true;
                }
            }

            /*
            storageFile = storage;
            using (FileStream stream = storage.OpenRead())
            {
                stream.Read(STORAGE, 0, (int) storage.Length);
            }
            */
        }

        Regex command = new Regex("^\\[(\\S+?):(.+)\\]$");

        void ParseMetadata(FileInfo metadataFile)
        {
            string[] lines = File.ReadAllLines(metadataFile.FullName);

            ProcMetadata currMetadata = null;
            foreach (var line in lines)
            {
                if (line.Length <= 0)
                {
                    continue;
                }

                if (line[0] == ':')
                {
                    if (currMetadata != null)
                    {
                        metadata.Add(currMetadata);
                    }
                    currMetadata = new ProcMetadata();
                    currMetadata.name = line;
                }
                else
                {
                    Match match = command.Match(line);
                    if (match.Success)
                    {
                        string command = match.Groups[1].Value;
                        string argument = match.Groups[2].Value;
                        switch (command)
                        {
                            case "constant":
                                string[] values = argument.Split(new[] { '{', ',', '}' }, StringSplitOptions.RemoveEmptyEntries);

                                if (values.Length != 3)
                                {
                                    throw new ArgumentException("A constant entry must consist of three values; name, length, addr!");
                                }

                                int length = int.Parse(values[1]);
                                int addr = Convert.ToInt32(values[2].Substring(2).Replace("_", ""), 16);

                                AutoConst constant = new AutoConst(values[0], addr, length);

                                autoConsts[values[0]] = constant;
                                break;
                            case "file":
                                currMetadata.file = argument;

                                if (!source.ContainsKey(currMetadata.file))
                                {
                                    string path = Path.Combine(metadataFile.DirectoryName, currMetadata.file);
                                    if (File.Exists(path))
                                    {
                                        source[currMetadata.file] = File.ReadAllLines(path);
                                    }
                                }
                                break;
                            case "location":
                                if (int.TryParse(argument, out int location))
                                {
                                    currMetadata.location = ROM_START + location;
                                }
                                else
                                {
                                    Debug.WriteLine($"Could not parse {argument} as int in line \"{line}\"");
                                }
                                break;
                            case "proc-line":
                                if (int.TryParse(argument, out int sourceLine))
                                {
                                    currMetadata.sourceLine = sourceLine;
                                }
                                else
                                {
                                    Debug.WriteLine($"Could not parse {argument} as int in line \"{line}\"");
                                }
                                break;
                            case "break":
                                string[] breaks = argument.Split(new[] { '{', ',', '}' }, StringSplitOptions.RemoveEmptyEntries);

                                currMetadata.breaks = breaks.Select(s => int.Parse(s)).ToList();
                                break;
                            case "link-lines":
                                string[] links = argument.Split(new[] { '{', ',', '}' }, StringSplitOptions.RemoveEmptyEntries);

                                currMetadata.lineLinks = links.Select(s => s.Split(':')).ToDictionary(sa => int.Parse(sa[0]) - 1, sa => int.Parse(sa[1]));
                                break;
                            case "size":
                                if (int.TryParse(argument, out int size))
                                {
                                    currMetadata.size = size;
                                }
                                else
                                {
                                    Debug.WriteLine($"Could not parse {argument} as int in line \"{line}\"");
                                }
                                break;
                            default:
                                Debug.WriteLine($"Unknown command: {command}");
                                break;
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"Could not parse line: \"{line}\"");
                    }
                }
            }

            if (currMetadata != null)
            {
                metadata.Add(currMetadata);
            }
        }
#endif

        public int InterruptCount = 0;
        public int MissedInterrupts = 0;

        public unsafe void Start()
        {
            Running = true;
            Started = true;
            
            fixed (int* mem = MEM)
            {
                // Add start frame
                this.FP = 0;
                mem[this.FP] = 0;       // Return address is 0
                mem[this.FP + 1] = 0;
                mem[this.FP + 2] = 0;   // Last FP is at the same location
                mem[this.FP + 3] = 0;
                mem[this.FP + 4] = 0;   // No locals
                mem[this.FP + 5] = 0;
                this.locals = 0;
                this.SP = this.FP + 5;
                
                //int PC = this.PC;
                //int SP = this.SP;
                //int FP = this.FP;
                int FPloc = this.FP;

                while (true)
                {
                    if (intrr != null)
                    {
                        if (intrr.Type == InterruptType.stop)
                        {
                            break;
                        }
                        InterruptCount++;

                        // Push interrupt arguments
                        for (int i = 0; i < intrr.Args.Length; i++)
                        {
                            mem[++SP] = intrr.Args[i];
                        }

                        int last_fp = FP;
                        mem[++SP] = (PC >> 12) & 0xFFF;         // Return addr
                        //FP = SP;
                        mem[++SP] = PC & 0xFFF;
                        mem[++SP] = (FP >> 12) & 0xFFF;         // Prev FP
                        mem[++SP] = FP & 0xFFF;
                        mem[++SP] = (intrr.Args.Length >> 12) & 0xFFF;
                        mem[++SP] = intrr.Args.Length & 0xFFF;  // Locals
                        FP = SP - 5;                            // Set the Frame Pointer
                        locals = mem[FP + 4] << 12 | mem[FP + 5];
                        FPloc = FP - locals;
                        PC = (int)intrr.Type;

                        intrr = null;
                    }
                    
                    Opcode op = (Opcode)(mem[PC]);

#if DEBUG
                    instructionFreq[(int)op]++;
#if BREAKS
                    if (breaks[PC])
                    {
                        interruptsEnabled = false;

                        HitBreakpoint?.Invoke(this, new EventArgs());
                    }

#endif
                    if (UseDebugger == true)
                    {
                        Opcode = op;
                        DebugBreakEvent.Set();
                        interruptsEnabledActual = interruptsEnabled;
                        interruptsEnabled = false;
                        ContinueEvent.WaitOne();

                        /*
                        bool acquiredLock = Monitor.TryEnter(DebugSync);
                        if (acquiredLock)
                        {
                            Monitor.Exit(DebugSync);
                        }
                        else
                        {
                            
                        }
                        */
                    }
#endif
                    switch (op)
                    {
                        case Opcode.Nop:
                            PC++;
                            break;
                        case Opcode.Pop:
                            SP--;
#if DEBUG
                            if (FP > 0 && SP < FP + 5)
                            {
                                Debugger.Break();
                            }
#endif
                            PC++;
                            break;
                        case Opcode.Fp:
                            mem[SP + 1] = FP >> 12 & 0xFF;
                            mem[SP + 2] = FP & 0xFF;
                            SP += 2;
                            break;
                        case Opcode.Pc:
                            mem[SP + 1] = PC >> 12 & 0xFFF;
                            mem[SP + 2] = PC & 0xFFF;
                            SP += 2;
                            PC++;
                            break;
                        case Opcode.Sp:
                            mem[SP + 1] = SP >> 12 & 0xFFF;
                            mem[SP + 2] = SP & 0xFFF;
                            SP += 2;
                            PC++;
                            break;
                        case Opcode.Set_sp:
                            SP = mem[SP - 1] << 12 | mem[SP];
                            PC++;
                            break;
                        case Opcode.Load_lit:
                            mem[++SP] = mem[++PC];
                            PC++;
                            break;
                        case Opcode.Load_lit_l:
                            mem[++SP] = mem[++PC];
                            mem[++SP] = mem[++PC];
                            PC++;
                            break;
                        case Opcode.Load_sp:
                            int load_sp_address = (mem[SP - 1] << 12) | (ushort)(mem[SP]);
                            mem[SP - 1] = mem[load_sp_address];
                            SP--;
                            PC++;
                            break;
                        case Opcode.Load_sp_l:
                            int load_sp_l_address = (mem[SP - 1] << 12) | (ushort)(mem[SP]);
                            mem[SP - 1] = mem[load_sp_l_address];
                            mem[SP] = mem[load_sp_l_address + 1];
                            PC++;
                            break;
                        case Opcode.Load_local:
                            mem[SP + 1] = mem[FPloc + mem[PC + 1]];
                            SP++;
                            PC += 2;
                            break;
                        case Opcode.Load_local_l:
                            int local_addr = FPloc + mem[PC + 1];
                            mem[SP + 1] = mem[local_addr];
                            mem[SP + 2] = mem[local_addr + 1];
                            SP += 2;
                            PC += 2;
                            break;
                        case Opcode.Store_sp:
                            int store_sp_address = (mem[SP - 2] << 12) | (ushort)(mem[SP - 1]);
                            mem[store_sp_address] = mem[SP];
                            SP -= 3;
                            PC++;
                            break;
                        case Opcode.Store_sp_l:
                            int store_sp_l_address = (mem[SP - 3] << 12) | (ushort)(mem[SP - 2]);
                            mem[store_sp_l_address] = mem[SP - 1];
                            mem[store_sp_l_address + 1] = mem[SP];
                            SP -= 4;
                            PC++;
                            break;
                        case Opcode.Store_local:
                            local_addr = FPloc + mem[PC + 1];
                            mem[local_addr] = mem[SP];
                            SP--;
                            PC += 2;
                            break;
                        case Opcode.Store_local_l:
                            local_addr = FPloc + mem[PC + 1];
                            mem[local_addr] = mem[SP - 1];
                            mem[local_addr + 1] = mem[SP];
                            SP -= 2;
                            PC += 2;
                            break;
                        case Opcode.Swap:
                            int swap_temp = mem[SP];
                            mem[SP] = mem[SP - 1];
                            mem[SP - 1] = swap_temp;
                            PC++;
                            break;
                        case Opcode.Swap_l:
                            int* swap_l_temp = stackalloc int[2];
                            swap_l_temp[0] = mem[SP - 3];
                            swap_l_temp[1] = mem[SP - 2];
                            mem[SP - 3] = mem[SP - 1];
                            mem[SP - 2] = mem[SP];
                            mem[SP - 1] = swap_l_temp[0];
                            mem[SP] = swap_l_temp[1];
                            PC++;
                            break;
                        case Opcode.Swap_s_l:
                            int swap_s_l_temp = mem[SP - 2];
                            mem[SP - 2] = mem[SP - 1];
                            mem[SP - 1] = mem[SP];
                            mem[SP] = swap_s_l_temp;
                            PC++;
                            break;
                        case Opcode.Dup:
                            mem[SP + 1] = mem[SP];
                            SP++;
                            PC++;
                            break;
                        case Opcode.Dup_l:
                            mem[SP + 1] = mem[SP - 1];
                            mem[SP + 2] = mem[SP];
                            SP += 2;
                            PC++;
                            break;
                        case Opcode.Over:
                            SP++;
                            mem[SP] = mem[SP - 2];
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
                        case Opcode.Add:
                            // TODO: The sign might not work here!
                            uint add_temp = (uint)(mem[SP] + mem[SP - 1]);
                            carry = add_temp > 0xFFF;
                            SP--;
                            mem[SP] = (short)(add_temp - (carry ? 0x1000 : 0));
                            PC++;
                            break;
                        case Opcode.Add_l:
                            int add1 = (mem[SP - 1] << 12) | (ushort)(mem[SP]);
                            int add2 = (mem[SP - 3] << 12) | (ushort)(mem[SP - 2]);
                            SP -= 2;
                            add2 += add1;
                            carry = add2 >> 12 > 0xFFF;
                            mem[SP - 1] = add2 >> 12;
                            mem[SP] = add2 & 0xFFF;
                            PC++;
                            break;
                        case Opcode.Add_c:
                            break;
                        case Opcode.Sub:
                            // TODO: The sign might not work here!
                            int sub_temp = mem[SP - 1] - mem[SP];
                            carry = ((uint) sub_temp) > 0xFFF;
                            SP--;
                            mem[SP] = sub_temp & 0xFFF;
                            PC++;
                            break;
                        case Opcode.Sub_l:
                            int sub1 = (mem[SP - 1] << 12) | (ushort)(mem[SP]);
                            int sub2 = (mem[SP - 3] << 12) | (ushort)(mem[SP - 2]);
                            SP -= 2;
                            sub2 -= sub1;
                            carry = ((uint)sub2 >> 12) > 0xFFF;
                            mem[SP - 1] = (sub2 >> 12) & 0xFFF;
                            mem[SP] = sub2 & 0xFFF;
                            PC++;
                            break;
                        case Opcode.Neg:
                            mem[SP] = (-mem[SP]) & 0xFFF;
                            PC++;
                            break;
                        case Opcode.Neg_l:
                            int neg_l_val = -((mem[SP - 1] << 12) | (ushort)(mem[SP]));
                            mem[SP] = neg_l_val & 0xFFF;
                            mem[SP - 1] = (neg_l_val >> 12) & 0xFFF;
                            PC++;
                            break;
                        case Opcode.Inc:
                            int mem_val = mem[SP] + 1;
                            carry = mem_val > 0xFFF;
                            mem[SP] = mem_val & 0xFFF;
                            PC++;
                            break;
                        case Opcode.Inc_l:
                            int linc_value = ((mem[SP - 1] << 12) | (ushort)(mem[SP])) + 1;
                            mem[SP] = linc_value & 0xFFF;
                            mem[SP - 1] = (linc_value >> 12) & 0xFFF;
                            carry = (linc_value >> 12) > 0xFFF;
                            PC++;
                            break;
                        case Opcode.Dec:
                            int dec_value = mem[SP] - 1;
                            carry = dec_value < 0;
                            mem[SP] = dec_value;
                            PC++;
                            break;
                        case Opcode.Dec_l:
                            uint ldec_value = ((uint)(mem[SP - 1] << 12) | (ushort)(mem[SP])) - 1;
                            mem[SP] = (int)ldec_value & 0xFFF;
                            mem[SP - 1] = (int)(ldec_value >> 12) & 0xFFF;
                            carry = ldec_value == uint.MaxValue;
                            PC++;
                            break;
                        case Opcode.Or:
                            mem[SP - 1] = mem[SP] | mem[SP - 1];
                            SP--;
                            PC++;
                            break;
                        case Opcode.Xor:
                            mem[SP - 1] = mem[SP] ^ mem[SP - 1];
                            SP--;
                            PC++;
                            break;
                        case Opcode.And:
                            mem[SP - 1] = mem[SP] & mem[SP - 1];
                            SP--;
                            PC++;
                            break;
                        case Opcode.Not:
                            mem[SP] = (~mem[SP]) & 0xFFF;
                            PC++;
                            break;
                        case Opcode.C_ss:
                            carry = (mem[SP] & 0x0800) != 0;
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
                        case Opcode.Rot_l_c:
                            uint rot_l_value = (uint)mem[SP];
                            bool rot_l_c = (rot_l_value & 0x800) != 0;
                            mem[SP] = (((int)rot_l_value << 1) | (carry ? 1 : 0)) & 0x0FFF;
                            carry = rot_l_c;
                            PC++;
                            break;
                        case Opcode.Rot_r_c:
                            uint rot_r_value = (uint)mem[SP];
                            bool rot_r_c = (rot_r_value & 0x001) != 0;
                            mem[SP] = (((int)rot_r_value >> 1) | (carry ? 0x800 : 0)) & 0x0FFF;
                            carry = rot_r_c;
                            PC++;
                            break;
                        case Opcode.Mul:
                            int mul_value = (mem[SP - 1] * mem[SP]);
                            carry = mul_value > 0xFFF;
                            mem[SP - 1] = mul_value & 0xFFF;
                            SP--;
                            PC++;
                            break;
                        case Opcode.Div:
                            int div_value = (mem[SP - 1] / mem[SP]);
                            carry = div_value > 0xFFF;
                            mem[SP - 1] = div_value & 0xFFF;
                            SP--;
                            PC++;
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
#if DEBUG
                            interruptsEnabled = interruptsEnabled || interruptsEnabledActual;
#endif
                            if (interruptsEnabled)
                            {
                                interrupt_event.WaitOne();
                                halt = false;
                            }
                            else
                            {
                                    goto end;
                            }

#if DEBUG
                            HaltInstruction = true;
#endif

                            PC++;
                            break;
                        case Opcode.Jmp:
                            JumpMode mode = (JumpMode) mem[++PC];
                            switch (mode)
                            {
                                case JumpMode.Jmp:
                                    PC = (mem[++PC] << 12) | (ushort)(mem[++PC]);
                                    break;
                                case JumpMode.Z:
                                    if (mem[SP] == 0)
                                    {
                                        PC = (mem[++PC] << 12) | (ushort)(mem[++PC]);
                                    }
                                    else
                                    {
                                        PC += 3;
                                    }
                                    SP--;
                                    break;
                                case JumpMode.Nz:
                                    if (mem[SP] != 0)
                                    {
                                        PC = (mem[++PC] << 12) | (ushort)(mem[++PC]);
                                    }
                                    else
                                    {
                                        PC += 3;
                                    }
                                    SP--;
                                    break;
                                case JumpMode.C:
                                    if (carry == true)
                                    {
                                        PC = (mem[++PC] << 12) | (ushort)(mem[++PC]);
                                    }
                                    else
                                    {
                                        PC += 3;
                                    }
                                    break;
                                case JumpMode.Cz:
                                    if (carry == false)
                                    {
                                        PC = (mem[++PC] << 12) | (ushort)(mem[++PC]);
                                    }
                                    else
                                    {
                                        PC += 3;
                                    }
                                    break;
                                case JumpMode.Gz:
                                    if ((mem[SP] & 0x800) == 0)
                                    {
                                        PC = (mem[++PC] << 12) | (ushort)(mem[++PC]);
                                    }
                                    else
                                    {
                                        PC += 3;
                                    }
                                    SP--;
                                    break;
                                case JumpMode.Lz:
                                    if ((mem[SP] & 0x800) > 0)
                                    {
                                        PC = (mem[++PC] << 12) | (ushort)(mem[++PC]);
                                    }
                                    else
                                    {
                                        PC += 3;
                                    }
                                    SP--;
                                    break;
                                case JumpMode.Ge:
                                    if (mem[SP] >= 0)
                                    {
                                        PC = (mem[++PC] << 12) | (ushort)(mem[++PC]);
                                    }
                                    else
                                    {
                                        PC += 3;
                                    }
                                    SP--;
                                    break;
                                case JumpMode.Le:
                                    int le_temp = mem[SP];
                                    if (le_temp == 0 || (le_temp & 0x800) > 0)
                                    {
                                        PC = (mem[++PC] << 12) | (ushort)(mem[++PC]);
                                    }
                                    else
                                    {
                                        PC += 3;
                                    }
                                    SP--;
                                    break;
                                case JumpMode.Eq:
                                    if (mem[SP - 1] == mem[SP])
                                    {
                                        PC = (mem[++PC] << 12) | (ushort)(mem[++PC]);
                                    }
                                    else
                                    {
                                        PC += 3;
                                    }
                                    SP -= 2;
                                    break;
                                case JumpMode.Neq:
                                    if (mem[SP - 1] != mem[SP])
                                    {
                                        PC = (mem[++PC] << 12) | (ushort)(mem[++PC]);
                                    }
                                    else
                                    {
                                        PC += 3;
                                    }
                                    SP -= 2;
                                    break;
                                case JumpMode.Ro:
                                    int sign_ext(int i) => (int)((i & 0x800) != 0 ? (uint)(i & 0xFFFF_F800) : (uint)i);

                                    PC += sign_ext(mem[SP]) + 1;
                                    SP--;
                                    break;
                                case JumpMode.Z_l:
                                    if ((mem[SP] | mem[SP - 1]) == 0)
                                    {
                                        PC = (mem[++PC] << 12) | (ushort)(mem[++PC]);
                                    }
                                    else
                                    {
                                        PC += 3;
                                    }
                                    SP -= 2;
                                    break;
                                case JumpMode.Nz_l:
                                    if ((mem[SP] | mem[SP - 1]) != 0)
                                    {
                                        PC = (mem[++PC] << 12) | (ushort)(mem[++PC]);
                                    }
                                    else
                                    {
                                        PC += 3;
                                    }
                                    SP -= 2;
                                    break;
                                case JumpMode.Gz_l:
                                    if ((mem[SP - 1] << 12 | mem[SP]) > 0)
                                    {
                                        PC = mem[PC + 1] << 12 | mem[PC + 2];
                                    }
                                    else
                                    {
                                        PC += 3;
                                    }
                                    SP -= 2;
                                    break;
                                case JumpMode.Lz_l:
                                    if ((mem[SP - 1] & 0x800) > 0)
                                    {
                                        PC = mem[PC + 1] << 12 | mem[PC + 2];
                                    }
                                    else
                                    {
                                        PC += 3;
                                    }
                                    SP -= 2;
                                    break;
                                case JumpMode.Ge_l:
                                    break;
                                case JumpMode.Le_l:
                                    break;
                                case JumpMode.Eq_l:
                                    if ((mem[SP - 3] << 12 | mem[SP - 2]) == (mem[SP - 1] << 12 | mem[SP]))
                                    {
                                        PC = mem[PC + 1] << 12 | mem[PC + 2];
                                    }
                                    else
                                    {
                                        PC += 3;
                                    }
                                    SP -= 4;
                                    break;
                                case JumpMode.Neq_l:
                                    if ((mem[SP - 3] << 12 | mem[SP - 2]) != (mem[SP - 1] << 12 | mem[SP]))
                                    {
                                        PC = mem[PC + 1] << 12 | mem[PC + 2];
                                    }
                                    else
                                    {
                                        PC += 3;
                                    }
                                    SP -= 4;
                                    break;
                                case JumpMode.Ro_l:
                                    break;
                                default:
                                    break;
                            }
                            break;
                        case Opcode.Call:
                            {
                                int call_addr = (mem[PC + 1] << 12) | (ushort)(mem[PC + 2]);
                                int return_addr = PC + 3;
                                int last_fp = FP;
                                int parameters = mem[call_addr];
                                int locals = mem[call_addr + 1];
                                PC = call_addr + 2;
                                SP += locals - parameters;              // Reserve space for locals and take locals from the stack
                                mem[++SP] = return_addr >> 12 & 0xFFF;  // Return addr
                                FP = SP;                                // Set the Frame Pointer
                                mem[++SP] = return_addr & 0xFFF;
                                mem[++SP] = last_fp >> 12 & 0xFFF;      // Prev FP
                                mem[++SP] = last_fp & 0xFFF;
                                mem[++SP] = locals >> 12 & 0xFFF;       // Locals
                                mem[++SP] = locals & 0xFFF;
                                this.locals = locals;
                                FPloc = FP - locals;
#if DEBUG
                                CallInstruction = true;
#endif
                                break;
                            }
                        case Opcode.Call_v:
                            {
                                int call_addr = (mem[SP - 1] << 12) | (ushort)(mem[SP]);
                                int return_addr = PC + 1;
                                int last_fp = FP;
                                int parameters = mem[call_addr];
                                int locals = mem[call_addr + 1];
                                PC = call_addr + 2;
                                SP += locals - parameters;
                                mem[++SP] = return_addr >> 12 & 0xFFF;
                                FP = SP;
                                mem[++SP] = return_addr & 0xFFF;
                                mem[++SP] = last_fp >> 12 & 0xFFF;
                                mem[++SP] = 0;
                                mem[++SP] = locals;
                                this.locals = locals;
                                FPloc = FP - locals;
#if DEBUG
                                CallInstruction = true;
#endif
                                break;
                            }
                        case Opcode.Ret:
                            SP = FP - 1 - (mem[FP + 4] << 12 | mem[FP + 5]);
                            PC = mem[FP] << 12 | (ushort) mem[FP + 1];
                            FP = mem[FP + 2] << 12 | (ushort)mem[FP + 3];
                            this.locals = (mem[FP + 4] << 12 | mem[FP + 5]);
                            FPloc = FP - this.locals;
#if DEBUG
                            RetInstruction = true;
#endif
                            break;
                        case Opcode.Ret_1:
                            int ret_val = mem[SP];
                            SP = FP - 1 - (mem[FP + 4] << 12 | mem[FP + 5]) + 1;
                            PC = mem[FP] << 12 | (ushort)mem[FP + 1];
                            FP = mem[FP + 2] << 12 | (ushort)mem[FP + 3];
                            this.locals = (mem[FP + 4] << 12 | mem[FP + 5]);
                            FPloc = FP - this.locals;
                            mem[SP] = ret_val;
#if DEBUG
                            RetInstruction = true;
#endif
                            break;
                        case Opcode.Ret_2:
                            int ret_val_1 = mem[SP - 1];
                            int ret_val_2 = mem[SP];
                            SP = FP - 1 - (mem[FP + 4] << 12 | mem[FP + 5]) + 2;
                            PC = mem[FP] << 12 | mem[FP + 1];
                            FP = mem[FP + 2] << 12 | mem[FP + 3];
                            this.locals = (mem[FP + 4] << 12 | mem[FP + 5]);
                            FPloc = FP - this.locals;
                            mem[SP - 1] = ret_val_1;
                            mem[SP] = ret_val_2;
#if DEBUG
                            RetInstruction = true;
#endif
                            break;
                        case Opcode.Ret_v:
                            int return_values = mem[PC + 1];
                            int* values = stackalloc int[return_values];
                            for (int i = 0; i < return_values; i++)
                            {
                                values[i] = mem[SP - i];
                            }
                            SP = FP - 1 - (mem[FP + 4] << 12 | mem[FP + 5]) + return_values;
                            PC = mem[FP] << 12 | (ushort)mem[FP + 1];
                            FP = mem[FP + 2] << 12 | (ushort)mem[FP + 3];
                            this.locals = (mem[FP + 4] << 12 | mem[FP + 5]);
                            FPloc = FP - this.locals;
                            for (int i = 0; i < return_values; i++)
                            {
                                mem[SP - i] = values[i];
                            }
#if DEBUG
                            RetInstruction = true;
#endif
                            break;
                        case Opcode.Memc:
                            //const int INT_SIZE = 4;
                            int srcOffset = (mem[SP - 5] << 12) | (mem[SP - 4]);
                            int destOffset = (mem[SP - 3] << 12) | (mem[SP - 2]);
                            int length = (mem[SP - 1] << 12) | (mem[SP]); // * INT_SIZE;
                            Array.Copy(MEM, srcOffset, MEM, destOffset, length);
                            //Buffer.BlockCopy(MEM, srcOffset, MEM, destOffset, length);
                            SP -= 6;
                            PC++;
                            break;
                        case Opcode.Inc_local:
                            local_addr = FPloc + mem[PC + 1];
                            mem[local_addr]++;
                            PC += 2;
                            break;
                        case Opcode.Inc_local_l:
                            local_addr = FPloc + mem[PC + 1];
                            int linc_local_value = ((mem[local_addr] << 12) | (mem[local_addr + 1])) + 1;
                            mem[local_addr + 1] = linc_local_value & 0xFFF;
                            mem[local_addr] = (linc_local_value >> 12) & 0xFFF;
                            carry = (linc_local_value >> 12) > 0xFFF;
                            PC += 2;
                            break;
                        case Opcode.Dec_local:
                            local_addr = FPloc + mem[PC + 1];
                            int dec_local_value = mem[local_addr] - 1;
                            carry = dec_local_value < 0;
                            mem[local_addr] = dec_local_value;
                            PC += 2;
                            break;
                        case Opcode.Dec_local_l:
                            local_addr = FPloc + mem[PC + 1];
                            uint ldec_local_value = ((uint)(mem[local_addr] << 12) | (ushort)(mem[local_addr + 1])) - 1;
                            mem[local_addr + 1] = (int)ldec_local_value & 0xFFF;
                            mem[local_addr] = (int)(ldec_local_value >> 12) & 0xFFF;
                            carry = ldec_local_value == uint.MaxValue;
                            PC += 2;
                            break;
                        case Opcode.Mul_2:
                            int mul_2_value = (mem[SP - 1] * mem[SP]);
                            carry = mul_2_value > 0xFFF_FFF ? true : false;
                            mem[SP] = mul_2_value & 0xFFF;
                            mem[SP - 1] = (mul_2_value >> 12) & 0xFFF;
                            PC++;
                            break;
                        case Opcode.Fc:
                            int color = mem[SP];
                            int char_addr = mem[SP - 2] << 12 | mem[SP - 1];
                            int vram_addr = mem[SP - 4] << 12 | mem[SP - 3];
                            if (vram_addr < VRAM_START || vram_addr >= ROM_START)
                            {
                                Debugger.Break();
                            }
                            int start_vram = vram_addr;
                            for (int x = 0; x < 8; x++)
                            {
                                int char_data = mem[char_addr];
                                if (char_data != 0)
                                {
                                    for (int y = 0; y < 12; y++)
                                    {
                                        if ((char_data & 0x800) != 0)
                                        {
                                            mem[vram_addr] = color;
                                        }
                                        vram_addr += SCREEN_WIDTH;
                                        char_data <<= 1;
                                    }
                                }
                                start_vram += 1;
                                vram_addr = start_vram;
                                char_addr += 1;
                            }
                            SP -= 5;
                            PC++;
                            break;
                        case Opcode.Mul_l:
                            int mul_l_value = ((mem[SP - 3] << 12 | mem[SP - 2]) * (mem[SP - 1] << 12 | mem[SP]));
                            carry = mul_l_value > 0xFFF_FFF ? true : false;
                            mem[SP - 3] = (mul_l_value >> 12) & 0xFFF;
                            mem[SP - 2] = mul_l_value & 0xFFF;
                            SP -= 2;
                            PC++;
                            break;
                        case Opcode.Mul_2_l:

                            break;
                        case Opcode.Div_l:
                            int div_l_value = (mem[SP - 3] << 12 | mem[SP - 2]) / (mem[SP - 1] << 12 | mem[SP]);
                            mem[SP - 2] = div_l_value & 0xFFF;
                            mem[SP - 3] = (div_l_value >> 12) & 0xFFF;
                            SP -= 2;
                            PC++;
                            break;
                        case Opcode.Write:
                            int ioAddr = (mem[SP - 6] << 24) | (mem[SP - 5] << 12) | mem[SP - 4];
                            int buf = mem[SP - 3] << 12 | mem[SP - 2];
                            int w_len = mem[SP - 1] << 12 | mem[SP];

                            IOMode ioMode = (IOMode) mem[PC + 1];
                            
                            if ((ioAddr - STORAGE_START_ADDR) < 0/*STORAGE.Length*/)
                            {
                                //const int wordSize = 8;
                                
                                switch (ioMode)
                                {
                                    case IOMode.Compact:
                                        for (int i = 0; i < w_len; i++)
                                        {

                                        }
                                        break;
                                    case IOMode.Fit:
                                        break;
                                    case IOMode.SFit:
                                        break;
                                    case IOMode.Cast:
                                        break;
                                    case IOMode.SCast:
                                        break;
                                    default:
                                        break;
                                }
                                //FIXME: Copy data
                                for (int i = 0; i < w_len * 3; i += 3)
                                {
                                    //STORAGE[i] = (byte)(mem[buf + i] >> 12);
                                    //STORAGE[i + 1] = 0;
                                    //STORAGE[i + 2] = 0;
                                }
                            }

                            SP -= 7;
                            break;
                        default:
                            throw new Exception($"{op}");
                    }
                    
                    programTime++;

                    // this.PC = PC;
                    // this.SP = SP;
                    // this.FP = FP;

#if DEBUG
                    if (SP > SPWatermark)
                    {
                        SPWatermark = SP;
                    }

                    if (FP > FPWatermark)
                    {
                        FPWatermark = FP;
                    }
#endif
                    
                    //SpinWait.SpinUntil(() => sw.ElapsedTicks > TimerInterval);
                    //sw.Restart();

                    //Thread.SpinWait(1000);
                }
            }
            end: Running = false;

            /*
            using (FileStream stream = storageFile.OpenWrite())
            {
                // Only write the changed data!
                stream.Write(STORAGE, 0, STORAGE.Length);
            }
            */
        }

        public void Stop()
        {
            intrr = new Interrupt(InterruptType.stop, null);
            interrupt_event.Set();
            halt = true;
            interruptsEnabled = false;
        }
    }
}
