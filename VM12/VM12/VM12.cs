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
using SKON;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace VM12
{
    using Debugger = System.Diagnostics.Debugger;
    
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
        public const int RAM_SIZE = Constants.RAM_SIZE; // before gram: 10_485_760
        public const int GRAM_SIZE = Constants.GRAM_SIZE;
        public const int VRAM_SIZE = Constants.VRAM_SIZE;
        public const int ROM_SIZE = Constants.ROM_SIZE;

        public const int RAM_START = Constants.RAM_START;
        public const int GRAM_START = Constants.GRAM_START;
        public const int VRAM_START = Constants.VRAM_START;
        public const int ROM_START = Constants.ROM_START;

        public const int MEM_SIZE = Constants.MEM_SIZE;

        public const int SCREEN_WIDTH = Constants.SCREEN_WIDTH;
        public const int SCREEN_HEIGHT = Constants.SCREEN_HEIGHT;

        public const int STORAGE_START_ADDR = Constants.STORAGE_START_ADDR;
        public const int STORAGE_SIZE = Constants.STORAGE_SIZE;

        public const int STACK_MAX_ADDRESS = Constants.STACK_MAX_ADDRESS;

        public int[] MEM = new int[MEM_SIZE];
        
        private FileInfo storageFile;
        //public byte[] STORAGE = new byte[STORAGE_SIZE * 2];

        private ThreadStart StartCoProssess => StartGraphicsCoProssessor;
        private Thread CoProssessor;
        private readonly ManualResetEventSlim CoProcessHaltSignal = new ManualResetEventSlim();
        private readonly ManualResetEventSlim CoProcessInterrupt = new ManualResetEventSlim();

        public SoundChip SoundChip { get; private set; }

        private static byte[][,] AllocateStorageMemory(int addresses, int chunkChunks, int chunkSize)
        {
            byte[][,] array = new byte[addresses / chunkChunks][,];

            return array;
        }

        private const int STORAGE_CHUNK_SIZE = 128;
        private const int STORAGE_CHUNK_GROUPING = 4096;

        public static readonly byte[][,] S = AllocateStorageMemory(MEM_SIZE, STORAGE_CHUNK_GROUPING, STORAGE_CHUNK_SIZE);
        public static readonly bool[] S_HIT = new bool[MEM_SIZE / STORAGE_CHUNK_SIZE];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteStorage(byte* data, int address)
        {
            int group_index = (address % STORAGE_CHUNK_GROUPING) * STORAGE_CHUNK_SIZE;
            int s_index = address / STORAGE_CHUNK_GROUPING;

            if (S[s_index] == null) S[s_index] = new byte[STORAGE_CHUNK_GROUPING, STORAGE_CHUNK_SIZE];

            fixed (byte* storage_data = S[s_index])
            {
                for (int i = 0; i < STORAGE_CHUNK_SIZE; i++)
                {
                    storage_data[group_index + i] = data[i];
                }
            }

            S_HIT[address] = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteStorage(int* data, int address)
        {
            int group_index = (address % STORAGE_CHUNK_GROUPING) * STORAGE_CHUNK_SIZE;
            int s_index = address / STORAGE_CHUNK_GROUPING;

            if (S[s_index] == null) S[s_index] = new byte[STORAGE_CHUNK_GROUPING, STORAGE_CHUNK_SIZE];

            //Console.WriteLine($"Writing to address {address}:");
            fixed (byte* storage_data = S[s_index])
            {
                for (int i = 0; i < STORAGE_CHUNK_SIZE / 2; i++)
                {
                    storage_data[group_index + i * 2] = (byte) (data[i] >> 8 & 0xFFF);
                    storage_data[group_index + i * 2 + 1] = (byte) (data[i] & 0xFFF);

                    //Console.Write($"{data[i]:X}, ");
                }
            }
            //Console.WriteLine();

            S_HIT[address] = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ReadStorage(byte* data, int address)
        {
            int group_index = (address % STORAGE_CHUNK_GROUPING) * STORAGE_CHUNK_SIZE;
            int s_index = address / STORAGE_CHUNK_GROUPING;

            if (S[s_index] == null) S[s_index] = new byte[STORAGE_CHUNK_GROUPING, STORAGE_CHUNK_SIZE];

            fixed (byte* storage_data = S[s_index])
            {
                for (int i = 0; i < STORAGE_CHUNK_SIZE; i++)
                {
                    data[i] = storage_data[group_index + i];
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ReadStorage(int* data, int address)
        {
            int group_index = (address % STORAGE_CHUNK_GROUPING) * STORAGE_CHUNK_SIZE;
            int s_index = address / STORAGE_CHUNK_GROUPING;

            if (S[s_index] == null) S[s_index] = new byte[STORAGE_CHUNK_GROUPING, STORAGE_CHUNK_SIZE];

            //Console.WriteLine($"Reading from address {address}:");
            fixed (byte* storage_data = S[s_index])
            {
                for (int i = 0; i < STORAGE_CHUNK_SIZE / 2; i++)
                {
                    data[i] = storage_data[group_index + i * 2] << 8 | storage_data[group_index + i * 2 + 1];
                    //Console.Write($"{data[i]:X}, ");
                }
            }
            //Console.WriteLine();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte[,] GetChunk(int address, out int offset)
        {
            offset = (address % STORAGE_CHUNK_GROUPING) * STORAGE_CHUNK_SIZE;
            return S[address / STORAGE_CHUNK_GROUPING];
        }
        
        private bool interruptSet = false;

        private Interrupt[] intrr = new Interrupt[Enum.GetValues(typeof(InterruptType)).Length];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Interrupt(Interrupt interrupt)
        {
            if (interrupt == null)
            {
                interrupt_event.Set();
                return;
            }

            int index = InterruptTypeToInt(interrupt.Type);

            if (intrr[index] != null)
            {
#if DEBUG
                MissedInterruptFreq[InterruptTypeToInt(interrupt.Type)]++;
#endif
                MissedInterrupts++;
            }

            intrr[index] = interrupt;
            interrupt_event.Set();
            interruptSet = true;

#if DEBUG
            InterruptFreq[index]++;
#endif
        }

        private static readonly int[] interruptTypeLut = new int[16]
        {
            0,
            -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1,
            4, 3, 2, 1
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int InterruptTypeToInt(InterruptType type)
        {
            return interruptTypeLut[(((int) type) & 0xF0) >> 4];
        }

        bool carry = false;
        bool interruptsEnabled = false;
        bool halt = false;

#if DEBUG
        bool interruptsEnabledActual = false;
#endif

        AutoResetEvent interrupt_event = new AutoResetEvent(false);

#if DEBUG
        // NOTE: These events should maybe not be properties to avoid the extra level of indirection!

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

        private int PC = ROM_START;
        private int SP = -1;
        private int FP = -1;
        private int locals = 0;

        // Graphics instruction pointer
        private int GP = -1;
        private long graphicsTime = 0;

        private long programTime = 0;

        public bool Started { get; set; } = false;
        public bool Running { get; set; } = false;
        public bool Stopped => halt && !interruptsEnabled;
        public int ProgramCounter => PC;
        public int StackPointer => SP;
        public int FramePointer => FP;
        public bool InterruptsEnabled => interruptsEnabled;
        public long Ticks => programTime;
        public long GraphicsTicks => graphicsTime;

#if DEBUG
        public Opcode Opcode { get; private set; }
#endif

        public int SPWatermark = int.MinValue;
        public int FPWatermark = int.MinValue;

#if !DEBUG
        public VM12(short[] ROM, FileInfo storage)
        {
            Array.Copy(ROM, 0, MEM, ROM_START, ROM.Length);

            storageFile = storage;
            ReadStorageData(storageFile);

            SoundChip = new SoundChip(this);
        }
#elif DEBUG
        
        public volatile bool UseDebugger = false;
        
        public long[] InterruptFreq = new long[Enum.GetValues(typeof(InterruptType)).Length];

        public long[] MissedInterruptFreq = new long[Enum.GetValues(typeof(InterruptType)).Length];

        public long[] instructionFreq = new long[Enum.GetValues(typeof(Opcode)).Length];

        public long[] instructionTimes = new long[Enum.GetValues(typeof(Opcode)).Length];

        public long[] romInstructionCounter = new long[MEM_SIZE];

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
                return name;
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

        int[] GetConstValue(string name)
        {
            AutoConst constant = autoConsts[name];
            return GetConstValue(constant);
        }

        int[] GetConstValue(AutoConst constant)
        {
            switch (constant.Length)
            {
                case 1:
                    return new int[] { MEM[constant.Addr] };
                case 2:
                    return new int[] { MEM[constant.Addr] << 12 | MEM[constant.Addr + 1] };
                default:
                    // TODO: Maybe better printout
                    int[] values = new int[constant.Length];
                    for (int i = 0; i < values.Length; i++)
                    {
                        values[i] = MEM[constant.Addr + i];
                    }
                    return values;
            }
        }

        Dictionary<string, int[]> ConstValues
        {
            get
            {
                Dictionary<string, int[]> values = new Dictionary<string, int[]>();

                foreach (var c in autoConsts)
                {
                    values[c.Key] = GetConstValue(c.Value);
                }

                return values;
            }
        }

        public List<ProcMetadata> metadata = new List<ProcMetadata>();

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
                int values = SP - stackStart;

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

        // FIXME: There is a thread sync problem where the other thread does not get the up to date values of these feilds
        public StackFrame CurrentStackFrame => ConstructStackFrame(FP, PC);

        private StackFrame ConstructHollowFrame(int fp, int pc)
        {
            StackFrame frame = new StackFrame();

            ProcMetadata data = GetMetadataFromOffset(pc);

            frame.file = data?.file;
            frame.procName = data?.name;
            frame.line = data != null ? GetSourceCodeLineFromMetadataAndOffset(data, pc) : -1;

            frame.FP = fp;

            frame.return_addr = MEM[fp] << 12 | (ushort)MEM[fp + 1];

            frame.prev_addr = MEM[fp + 2] << 12 | (ushort)MEM[fp + 3];

            frame.locals = MEM[fp + 4] << 12 | MEM[fp + 5];

            if (frame.locals > fp) frame.locals &= 0xFFF;

            if (frame.locals > fp) frame.locals = -1;
            else frame.localValues = new int[frame.locals];

            for (int i = 0; i < frame.locals; i++)
            {
                frame.localValues[i] = MEM[fp - frame.locals + i];
            }

            return frame;
        }

        public StackFrame ConstructStackFrame(int fp, int pc)
        {
            StackFrame root = ConstructHollowFrame(fp, pc);

            StackFrame current = root;

            while (current.FP != 0)
            {
                StackFrame next = ConstructHollowFrame(current.prev_addr, current.return_addr);

                current.prev = next;
                current = next;
            }
            
            return root;
        }
        
        public StackFrame ConstructStackFrameRecursive(int fp, int pc)
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

        public int GetStackDepth(int fp)
        {
            int currentFP = fp;
            int count = 0;

            do
            {
                count++;

                currentFP = MEM[currentFP + 2] << 12 | MEM[currentFP + 3];
            } while (currentFP != 0);

            return count;
        }

        public DirectoryInfo sourceDir { get; private set; }

        #endregion

        public VM12(short[] ROM, FileInfo metadata, FileInfo storage)
        {
            Array.Copy(ROM, 0, MEM, ROM_START, ROM.Length);

            sourceDir = metadata.Directory;

            //ReadSKONMetadata(metadata);
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

            storageFile = storage;
            ReadStorageData(storageFile);

            SoundChip = new SoundChip(this);
        }

        Regex command = new Regex("^\\[(\\S+?):(.+)\\]$");

        void ReadSKONMetadata(FileInfo metadataFile)
        {
            SKONObject metadata = SKON.SKON.LoadFile(Path.ChangeExtension(metadataFile.FullName, "skon"));

            foreach (var constant in metadata["constants"].Values)
            {
                AutoConst c = new AutoConst(constant["name"].String, Convert.ToInt32(constant["value"].String.Substring(2).Replace("_", ""), 16), constant["length"].Int ?? -1);

                autoConsts[c.Name] = c;
            }

            foreach (var proc in metadata["procs"].Values)
            {
                ProcMetadata procMeta = new ProcMetadata();

                procMeta.name = proc["name"].String;
                procMeta.file = proc["file"].String;
                procMeta.location = (ROM_START + proc["location"].Int) ?? -1;
                procMeta.sourceLine = proc["proc-line"].Int ?? -1;
                procMeta.breaks = new List<int>(proc["break"].Values.Select(skon => skon.Int ?? -1));
                procMeta.lineLinks = proc["link-lines"].Values
                    .Select(v => v.String.Split(':'))
                    .Select(v => v.Select(i => int.Parse(i)).ToArray())
                    .ToDictionary(kvs => kvs[0], kvs => kvs[1]);
                procMeta.size = proc["size"].Int ?? -1;

                if (!source.ContainsKey(procMeta.file))
                {
                    string file = Directory.EnumerateFiles(metadataFile.DirectoryName, Path.GetExtension(procMeta.file), SearchOption.AllDirectories).FirstOrDefault(p => Path.GetFileName(p) == procMeta.file);
                    if (File.Exists(file))
                    {
                        source[procMeta.file] = File.ReadAllLines(file);
                    }
                }

                this.metadata.Add(procMeta);
            }
        }

        void ParseMetadata(FileInfo metadataFile)
        {
            if (metadataFile.Exists == false) return;

            string[] lines = File.ReadAllLines(metadataFile.FullName);

            var dirFiles = Directory.EnumerateFiles(metadataFile.DirectoryName, "*", SearchOption.AllDirectories).ToList();

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
                                    string file = dirFiles.FirstOrDefault(p => Path.GetFileName(p) == currMetadata.file);
                                    if (file != null)
                                    {
                                        source[currMetadata.file] = File.ReadAllLines(file);
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

        void ReadStorageData(FileInfo storageFile)
        {
            byte[] chunk = new byte[STORAGE_CHUNK_SIZE];

            using (FileStream stream = storageFile.OpenRead())
            using (BinaryReader reader = new BinaryReader(stream))
            {
                while (stream.Position != stream.Length)
                {
                    // Read one chunk of data
                    int addr = reader.ReadInt32();
                    stream.Read(chunk, 0, chunk.Length);

                    fixed (byte* chunk_data = chunk)
                    {
                        WriteStorage(chunk_data, addr);
                    }

#if DEBUG_DISK_STORAGE
                    Console.WriteLine($"Read chunk {addr}");
                    Console.WriteLine(String.Join(",", chunk));
#endif
                }
            }
        }

        public int InterruptCount = 0;
        public int MissedInterrupts = 0;
        
        public unsafe void Start()
        {
            Running = true;
            Started = true;
            SoundChip.StartSoundChip();

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
                this.SP = this.FP + 6;

                //int PC = this.PC;
                //int SP = this.SP;
                //int FP = this.FP;
                int FPloc = this.FP;

                int* char_data = stackalloc int[8];

                while (true)
                {
                    if (interruptSet == true && interruptsEnabled)
                    {
                        // Stop interrupt
                        if (intrr[0] != null)
                        {
                            break;
                        }

                        for (int i = 1; i < intrr.Length; i++)
                        {
                            if (intrr[i] != null)
                            {
                                InterruptCount++;

                                // Push interrupt arguments
                                for (int arg = 0; arg < intrr[i].Args.Length; arg++)
                                {
                                    mem[++SP] = intrr[i].Args[arg];
                                }
                                
                                int last_fp = FP;
                                mem[++SP] = (PC >> 12) & 0xFFF;         // Return addr
                                                                        //FP = SP;
                                mem[++SP] = PC & 0xFFF;
                                mem[++SP] = (FP >> 12) & 0xFFF;         // Prev FP
                                mem[++SP] = FP & 0xFFF;
                                mem[++SP] = (intrr[i].Args.Length >> 12) & 0xFFF;
                                mem[++SP] = intrr[i].Args.Length & 0xFFF;  // Locals
                                FP = SP - 5;                            // Set the Frame Pointer
                                locals = mem[FP + 4] << 12 | mem[FP + 5];
                                FPloc = FP - locals;
                                PC = (int)intrr[i].Type;

                                intrr[i] = null;
                            }
                        }
                    }

                    Opcode op = (Opcode)(mem[PC]);
                    
#if DEBUG
                    romInstructionCounter[PC]++;
                    instructionFreq[(int)op]++;
#if BREAKS
                    if (breaks[PC] || op == Opcode.Brk)
                    {
                        interruptsEnabledActual = interruptsEnabled || interruptsEnabledActual;
                        interruptsEnabled = false;

                        HitBreakpoint?.Invoke(this, new EventArgs());
                    }

#endif
                    if (UseDebugger != false)
                    {
                        Opcode = op;
                        DebugBreakEvent.Set();
                        interruptsEnabledActual = interruptsEnabled || interruptsEnabledActual;
                        interruptsEnabled = false;
                        ContinueEvent.WaitOne();
                        if (UseDebugger == false)
                        {
                            interruptsEnabled = interruptsEnabledActual;
                        }
                    }

                    if ((uint) SP > STACK_MAX_ADDRESS)
                    {
                        interruptsEnabledActual = interruptsEnabled || interruptsEnabledActual;
                        interruptsEnabled = false;

                        HitBreakpoint?.Invoke(this, new EventArgs());
                    }
#endif
                    switch (op)
                    {
                        case Opcode.Brk:
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
                            PC++;
                            break;
                        case Opcode.Pc:
                            mem[SP + 1] = PC >> 12 & 0xFFF;
                            mem[SP + 2] = PC & 0xFFF;
                            SP += 2;
                            PC++;
                            break;
                        case Opcode.Pt:
                            // FIXME: Use a bigger register
                            mem[SP + 1] = (int) (programTime >> 12 & 0xFFF);
                            mem[SP + 2] = (int) (programTime & 0xFFF);
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
                        case Opcode.Set_fp:
                            FP = mem[SP - 1] << 12 | mem[SP];
                            PC++;
                            break;
                        case Opcode.Add_sp_lit_l:
                            SP += mem[++PC] << 12 | mem[++PC];
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
                            if (store_sp_address >= ROM_START) Debugger.Break();
                            mem[store_sp_address] = mem[SP];
                            SP -= 3;
                            PC++;
                            break;
                        case Opcode.Store_sp_l:
                            int store_sp_l_address = (mem[SP - 3] << 12) | (ushort)(mem[SP - 2]);
                            if (store_sp_l_address >= ROM_START) Debugger.Break();
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
                            int swap_l_temp = mem[SP];
                            mem[SP] = mem[SP - 2];
                            mem[SP - 2] = swap_l_temp;
                            swap_l_temp = mem[SP - 1];
                            mem[SP - 1] = mem[SP - 3];
                            mem[SP - 3] = swap_l_temp;
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
                            mem[SP + 1] = mem[SP - 1];
                            SP++;
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
                        case Opcode.Over_s_l:
                            mem[SP + 1] = mem[SP - 2];
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
                        case Opcode.Add_l:
                            int add1 = (mem[SP - 1] << 12) | (ushort)(mem[SP]);
                            int add2 = (mem[SP - 3] << 12) | (ushort)(mem[SP - 2]);
                            SP -= 2;
                            add2 += add1;
                            carry = add2 >> 12 > 0xFFF;
                            mem[SP - 1] = add2 >> 12 & 0xFFF;
                            mem[SP] = add2 & 0xFFF;
                            PC++;
                            break;
                        case Opcode.Add_c:
                            throw new NotImplementedException();
                        case Opcode.Sub:
                            // TODO: The sign might not work here!
                            int sub_temp = mem[SP - 1] - mem[SP];
                            carry = ((uint)sub_temp) > 0xFFF;
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
#if DEBUG
                            interruptsEnabledActual = false;
#endif
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
                            JumpMode mode = (JumpMode)mem[++PC];
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
                                    int gz_value = mem[SP];
                                    if ((gz_value & 0x800) == 0 && gz_value != 0)
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
                                    if ((mem[SP] & 0x800) != 0)
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
                                    int ge_temp = mem[SP];
                                    if (ge_temp >= 0 && (ge_temp & 0x800) == 0)
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
                                    int ro_value = mem[SP];
                                    // Sign extend and add one (jmp argument) and then to PC
                                    PC += ((int)((ro_value & 0x800) != 0 ? (uint)(ro_value | 0xFFFF_F800) : (uint)ro_value)) + 1;
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
                                    int gz_l_value = (mem[SP - 1] << 12 | mem[SP]);
                                    if ((gz_l_value & 0x800) == 0 && gz_l_value > 0)
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
                                    int ge_l_temp = mem[SP - 1];
                                    if (ge_l_temp >= 0 && (ge_l_temp & 0x800) == 0)
                                    {
                                        PC = mem[PC + 1] << 12 | mem[PC + 2];
                                    }
                                    else
                                    {
                                        PC += 3;
                                    }
                                    SP -= 2;
                                    break;
                                case JumpMode.Le_l:
                                    if ((mem[SP - 1] & 0x800) > 0 || (mem[SP -1] | mem[SP]) == 0)
                                    {
                                        PC = mem[PC + 1] << 12 | mem[PC + 2];
                                    }
                                    else
                                    {
                                        PC += 3;
                                    }
                                    SP -= 2;
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
                                    int ro_l_value = mem[SP - 1] << 12 | mem[SP];
                                    // Sign extend and add one (jmp argument) and then to PC
                                    int ro_l_jump_length = ((int)((ro_l_value & 0x80_0000) != 0 ? (uint)(ro_l_value | 0xFF80_0000) : (uint)ro_l_value)) + 1;
                                    PC += ro_l_jump_length;
                                    SP -= 2;
                                    break;
                                default:
                                    Debugger.Break();
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
                                SP -= 2;
                                int return_addr = PC + 1;
                                int last_fp = FP;
                                int parameters = mem[call_addr];
                                int locals = mem[call_addr + 1];
                                PC = call_addr + 2;
                                SP += locals - parameters;
                                mem[++SP] = return_addr >> 12 & 0xFFF;
                                FP = SP;
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
                        case Opcode.Ret:
                            SP = FP - 1 - (mem[FP + 4] << 12 | mem[FP + 5]);
                            if (SP < 0) Debugger.Break();
                            PC = mem[FP] << 12 | (ushort)mem[FP + 1];
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
                            int returnValuesStart = SP;
                            SP = FP - 1 - (mem[FP + 4] << 12 | mem[FP + 5]) + return_values;
                            PC = mem[FP] << 12 | (ushort)mem[FP + 1];
                            FP = mem[FP + 2] << 12 | (ushort)mem[FP + 3];
                            this.locals = (mem[FP + 4] << 12 | mem[FP + 5]);
                            FPloc = FP - this.locals;
                            for (int i = 0; i < return_values; i++)
                            {
                                mem[SP - i] = mem[returnValuesStart - i];
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
                            if (srcOffset > MEM.Length)
                            {
                                Array.Clear(MEM, destOffset, length);
                            }
                            else
                            {
                                Array.Copy(MEM, srcOffset, MEM, destOffset, length);
                            }
                            //Buffer.BlockCopy(MEM, srcOffset, MEM, destOffset, length);
                            SP -= 6;
                            PC++;
                            break;
                        case Opcode.Mul_Add:
                            int mul_add_value = mem[SP - 2] + (mem[SP - 1] * mem[SP]);
                            carry = mul_add_value > 0xFFF;
                            mem[SP - 2] = mul_add_value & 0xFFF;
                            SP -= 2;
                            PC++;
                            break;
                        case Opcode.Mul_Add_l:
                            int mul_add_l_value = (mem[SP - 5] << 12 | mem[SP - 4]) + ((mem[SP - 3] << 12 | mem[SP - 2]) * (mem[SP - 1] << 12 | mem[SP]));
                            carry = mul_add_l_value > 0xFFF_FFF ? true : false;
                            mem[SP - 5] = (mul_add_l_value >> 12) & 0xFFF;
                            mem[SP - 4] = mul_add_l_value & 0xFFF;
                            SP -= 4;
                            PC++;
                            break;
                        case Opcode.Inc_local:
                            int inc_local_addr = FPloc + mem[PC + 1];
                            int inc_local_value = mem[inc_local_addr] + 1;
                            carry = inc_local_value > 0xFFF;
                            mem[inc_local_addr] = inc_local_value & 0xFFF;
                            PC += 2;
                            break;
                        case Opcode.Inc_local_l:
                            int linc_local_addr = FPloc + mem[PC + 1];
                            int linc_local_value = ((mem[linc_local_addr] << 12) | (mem[linc_local_addr + 1])) + 1;
                            mem[linc_local_addr + 1] = linc_local_value & 0xFFF;
                            mem[linc_local_addr] = (linc_local_value >> 12) & 0xFFF;
                            carry = (linc_local_value >> 12) > 0xFFF;
                            PC += 2;
                            break;
                        case Opcode.Dec_local:
                            local_addr = FPloc + mem[PC + 1];
                            int dec_local_value = mem[local_addr] - 1;
                            carry = dec_local_value < 0;
                            mem[local_addr] = dec_local_value & 0xFFF;
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
                            
                            bool not_zero = false;
                            for (int i = 0; i < 8; i++)
                            {
                                char_data[i] = mem[char_addr + i];
                                not_zero |= char_data[i] != 0;
                            }

                            if (not_zero)
                            {
                                int mask = 0x800;
                                for (int i = 0; i < 12; i++)
                                {
                                    if ((char_data[0] & mask) != 0) mem[vram_addr + 0] = color;
                                    if ((char_data[1] & mask) != 0) mem[vram_addr + 1] = color;
                                    if ((char_data[2] & mask) != 0) mem[vram_addr + 2] = color;
                                    if ((char_data[3] & mask) != 0) mem[vram_addr + 3] = color;
                                    if ((char_data[4] & mask) != 0) mem[vram_addr + 4] = color;
                                    if ((char_data[5] & mask) != 0) mem[vram_addr + 5] = color;
                                    if ((char_data[6] & mask) != 0) mem[vram_addr + 6] = color;
                                    if ((char_data[7] & mask) != 0) mem[vram_addr + 7] = color;

                                    vram_addr += SCREEN_WIDTH;
                                    mask >>= 1;
                                }
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
                            // TODO!
                            throw new InvalidOperationException();
                        case Opcode.Div_l:
                            int div_l_value = (mem[SP - 3] << 12 | mem[SP - 2]) / (mem[SP - 1] << 12 | mem[SP]);
                            mem[SP - 2] = div_l_value & 0xFFF;
                            mem[SP - 3] = (div_l_value >> 12) & 0xFFF;
                            SP -= 2;
                            PC++;
                            break;
                        case Opcode.Mod:
                            mem[SP - 1] = mem[SP - 1] % mem[SP];
                            SP--;
                            PC++;
                            break;
                        case Opcode.Mod_l:
                            int mod_l_value = (mem[SP - 3] << 12 | mem[SP - 2]) % (mem[SP - 1] << 12 | mem[SP]);
                            mem[SP - 2] = mod_l_value & 0xFFF;
                            mem[SP - 3] = mod_l_value >> 12;
                            SP -= 2;
                            PC++;
                            break;
                        case Opcode.Write:
                            int w_ioAddr = mem[SP - 5] << 12 | mem[SP - 4];
                            int w_buf = mem[SP - 3] << 12 | mem[SP - 2];
                            int w_len = mem[SP - 1] << 12 | mem[SP];

                            for (int i = 0; i < w_len; i++)
                            {
                                WriteStorage(&mem[w_buf + (i * STORAGE_CHUNK_SIZE)], w_ioAddr + i);
                            }

                            SP -= 6;
                            PC++;
                            break;
                        case Opcode.Read:
                            int r_ioAddr = (mem[SP - 5] << 12) | mem[SP - 4];
                            int r_buf = mem[SP - 3] << 12 | mem[SP - 2];
                            int r_len = mem[SP - 1] << 12 | mem[SP];
                            
                            for (int i = 0; i < r_len; i++)
                            {
                                ReadStorage(&mem[r_buf + (i * STORAGE_CHUNK_SIZE)], r_ioAddr + i);
                            }

                            SP -= 6;
                            PC++;
                            break;
                        case Opcode.Clz:
                            int clz_data = mem[SP];
                            int clz_result = 12;
                            if (clz_data != 0)
                            {
                                clz_result = 0;
                                if ((clz_data & 0xFFFF0000) == 0) { clz_result += 16; clz_data <<= 16; }
                                if ((clz_data & 0xFF000000) == 0) { clz_result += 8; clz_data <<= 8; }
                                if ((clz_data & 0xF0000000) == 0) { clz_result += 4; clz_data <<= 4; }
                                if ((clz_data & 0xC0000000) == 0) { clz_result += 2; clz_data <<= 2; }
                                if ((clz_data & 0x80000000) == 0) { clz_result += 1; }

                                clz_result -= 20;
                            }
                            mem[SP] = clz_result;
                            PC++;
                            break;
                        case Opcode.Ctz:
                            int ctz_data = mem[SP];
                            int ctz_result = 12;
                            if (ctz_data != 0)
                            {
                                ctz_result = 0;
                                if ((ctz_data & 0x0000FFFF) == 0) { ctz_result += 16; ctz_data >>= 16; }
                                if ((ctz_data & 0x000000FF) == 0) { ctz_result += 8; ctz_data >>= 8; }
                                if ((ctz_data & 0x0000000F) == 0) { ctz_result += 4; ctz_data >>= 4; }
                                if ((ctz_data & 0x00000003) == 0) { ctz_result += 2; ctz_data >>= 2; }
                                if ((ctz_data & 0x00000001) == 0) { ctz_result += 1; }

                                ctz_result -= 20;
                            }
                            mem[SP] = ctz_result;
                            PC++;
                            break;
                        case Opcode.Selz:
                            mem[SP - 2] = mem[SP] == 0 ? mem[SP - 1] : mem[SP - 2];
                            SP -= 2;
                            PC++;
                            break;
                        case Opcode.Selgz:
                            mem[SP - 2] = ((mem[SP] & 0x800) == 0 && mem[SP] > 0) ? mem[SP - 1] : mem[SP - 2];
                            SP -= 2;
                            PC++;
                            break;
                        case Opcode.Selge:
                            mem[SP - 2] = ((mem[SP] & 0x800) == 0 && mem[SP] >= 0) ? mem[SP - 1] : mem[SP - 2];
                            SP -= 2;
                            PC++;
                            break;
                        case Opcode.Selc:
                            mem[SP - 1] = carry ? mem[SP] : mem[SP - 1];
                            SP -= 1;
                            PC++;
                            break;
                        case Opcode.Set:
                            SetMode set_mode = (SetMode) mem[PC + 1];
                            switch (set_mode)
                            {
                                case SetMode.Z:
                                    mem[SP] = mem[SP] == 0 ? 1 : 0;
                                    break;
                                case SetMode.Nz:
                                    mem[SP] = mem[SP] != 0 ? 1 : 0;
                                    break;
                                case SetMode.C:
                                    mem[SP] = carry ? 1 : 0;
                                    break;
                                case SetMode.Cz:
                                    mem[SP] = carry == false ? 1 : 0;
                                    break;
                                case SetMode.Gz:
                                    mem[SP] = (mem[SP] & 0x800) == 0 && mem[SP] != 0 ? 1 : 0;
                                    break;
                                case SetMode.Lz:
                                    mem[SP] = (mem[SP] & 0x800) != 0 ? 1 : 0;
                                    break;
                                case SetMode.Ge:
                                    mem[SP] = (mem[SP] & 0x800) == 0 && mem[SP] >= 0 ? 1 : 0;
                                    break;
                                case SetMode.Le:
                                    mem[SP] = (mem[SP] & 0x800) != 0 || mem[SP] == 0 ? 1 : 0;
                                    break;
                                case SetMode.Z_l:
                                    mem[SP] = (mem[SP - 1] << 12 | mem[SP]) == 0 ? 1 : 0;
                                    mem[SP - 1] = 0;
                                    break;
                                case SetMode.Nz_l:
                                    mem[SP] = (mem[SP - 1] << 12 | mem[SP]) != 0 ? 1 : 0;
                                    mem[SP - 1] = 0;
                                    break;
                                case SetMode.C_l:
                                    mem[SP] = carry ? 1 : 0;
                                    mem[SP - 1] = 0;
                                    break;
                                case SetMode.Cz_l:
                                    mem[SP] = carry == false ? 1 : 0;
                                    mem[SP - 1] = 0;
                                    break;
                                case SetMode.Gz_l:
                                    int set_gz_l_value = mem[SP - 1] << 12 | mem[SP];
                                    mem[SP] = (set_gz_l_value & 0x800_000) == 0 && set_gz_l_value != 0 ? 1 : 0;
                                    mem[SP - 1] = 0;
                                    break;
                                case SetMode.Lz_l:
                                    int set_lz_l_value = mem[SP - 1] << 12 | mem[SP];
                                    mem[SP] = (set_lz_l_value & 0x800_000) != 0 ? 1 : 0;
                                    mem[SP - 1] = 0;
                                    break;
                                case SetMode.Ge_l:
                                    int set_ge_l_value = mem[SP - 1] << 12 | mem[SP];
                                    mem[SP] = (set_ge_l_value & 0x800_000) == 0 && set_ge_l_value >= 0 ? 1 : 0;
                                    break;
                                case SetMode.Le_l:
                                    int set_le_l_value = mem[SP - 1] << 12 | mem[SP];
                                    mem[SP] = (set_le_l_value & 0x800_000) != 0 || set_le_l_value == 0 ? 1 : 0;
                                    mem[SP - 1] = 0;
                                    break;
                                default:
                                    throw new NotImplementedException($"No implementation for '{set_mode}'");
                            }
                            PC += 2;
                            break;
                        case Opcode.Graf_clear:
                            int clear_color = mem[SP];
                            SP -= 1;
                            Utils.MemSet(mem + VRAM_START, clear_color, VRAM_SIZE);
                            PC++;
                            break;
                        case Opcode.Start_coproc:
                            if (CoProssessor == null)
                            {
                                CoProssessor = new Thread(StartCoProssess)
                                {
                                    IsBackground = true,
                                    Name = "VM12CoProc"
                                };
                                CoProssessor.Start();
                            }
                            PC++;
                            break;
                        case Opcode.Hlt_coproc:
                            CoProcessHaltSignal.Set();
                            while (CoProssessor?.IsAlive ?? false) CoProcessInterrupt.Set();
                            CoProssessor = null;
                            PC++;
                            break;
                        case Opcode.Int_coproc:
                            CoProcessInterrupt.Set();
                            PC++;
                            break;
                        case Opcode.Int_snd_chip:
                            SoundChip.UpdateOscillators(mem);
                            PC++;
                            break;
                        default:
                            throw new Exception($"{op}");
                    }
                    
                    programTime++;

                    //this.PC = PC;
                    //this.SP = SP;
                    //this.FP = FP;

#if DEBUG
                    if (SP > 1000)
                    {
                        ;
                    }

                    if (SP > SPWatermark)
                    {
                        SPWatermark = SP;
                    }

                    if (FP > FPWatermark)
                    {
                        FPWatermark = FP;
                    }

                    if (SP < 0)
                    {
                       Debugger.Break();
                    }
#endif
                    
                    //SpinWait.SpinUntil(() => sw.ElapsedTicks > TimerInterval);
                    //sw.Restart();

                    //Thread.SpinWait(1000);
                }
            }
            end:

            this.SP = 0;
            this.FP = 0;
            this.PC = ROM_START;

            SoundChip.StopSoundChip();

            CoProcessHaltSignal.Set();
            CoProcessInterrupt.Set();
            while (CoProssessor != null && CoProssessor.IsAlive);
            CoProssessor = null;

#if DEBUG
            DebugBreakEvent.Set();
#endif

            Console.WriteLine();
            Console.WriteLine();

            using (FileStream stream = storageFile.OpenWrite())
            {
                // Only write the changed data!
                BinaryWriter writer = new BinaryWriter(stream);

                byte[] data = new byte[STORAGE_CHUNK_SIZE];

                for (int chunk = 0; chunk < S_HIT.Length; chunk++)
                {
                    if (S_HIT[chunk])
                    {
                        int addr = chunk;

                        writer.Write(addr);

                        byte[,] chunkGroup = GetChunk(addr, out int chunkOffset);
                        
                        Buffer.BlockCopy(chunkGroup, chunkOffset, data, 0, data.Length);

                        writer.Write(data);

#if DEBUG_DISK_STORAGE
                        Console.WriteLine($"Write chunk {addr}");
                        Console.WriteLine(string.Join(",", data));
#endif
                    }
                }
            }

            Running = false;
        }

        public void Stop()
        {
            intrr[0] = new Interrupt(InterruptType.stop, null);
            interrupt_event.Set();
            halt = true;
            interruptsEnabled = true;
        }

        public const int CHAR_WIDTH = 8;
        public const int CHAR_HEIGHT = 12;
        
        public void StartGraphicsCoProssessor()
        {
            // We start executing instructions
            GP = GRAM_START;

            fixed (int* mem = MEM)
            {
                int* char_data = stackalloc int[8];

                while (true)
                {
                    if (CoProcessHaltSignal.IsSet)
                    {
                        CoProcessHaltSignal.Reset();
                        break;
                    }

                    GrapicOps gOp = (GrapicOps) mem[GP];
                    
                    switch (gOp)
                    {
                        case GrapicOps.Nop:
                            GP++;
                            break;
                        case GrapicOps.Hlt:
                            CoProcessInterrupt.Wait();
                            CoProcessInterrupt.Reset();
                            GP++;
                            break;
                        case GrapicOps.Hlt_reset:
                            CoProcessInterrupt.Wait();
                            CoProcessInterrupt.Reset();
                            GP = GRAM_START;
                            break;
                        case GrapicOps.Jmp:
                            int offset = mem[GP + 1];
                            GP = VRAM_START + offset;
                            break;
                        case GrapicOps.Line:
                            throw new NotImplementedException();
                        case GrapicOps.Rectangle:
                            {
                                int color = mem[GP + 1];
                                int x = mem[GP + 2];
                                int y = mem[GP + 3];
                                int width = mem[GP + 4];
                                int height = mem[GP + 5];
                                
                                int vram_address = VRAM_START + x + (y * SCREEN_WIDTH);

                                int line_pad = SCREEN_WIDTH - width;

                                for (int j = 0; j < height; j++)
                                {
                                    for (int i = 0; i < width; i++)
                                    {
                                        if (vram_address > (VRAM_START + VRAM_SIZE - 1))
                                        {
                                            Debugger.Break();
                                        }
                                        mem[vram_address++] = color;
                                    }

                                    vram_address += line_pad;
                                }

                                GP += 6;
                                break;
                            }
                        case GrapicOps.Ellipse:
                            throw new NotImplementedException();
                        case GrapicOps.Fontchar:
                            {
                                int color = mem[GP + 1];
                                int char_addr = mem[GP + 2] << 12 | mem[GP + 3];
                                int vram_addr = mem[GP + 4] << 12 | mem[GP + 5];

                                if (vram_addr < VRAM_START || vram_addr >= ROM_START)
                                {
                                    Debugger.Break();
                                }

                                bool not_zero = false;
                                for (int i = 0; i < 8; i++)
                                {
                                    char_data[i] = mem[char_addr + i];
                                    not_zero |= char_data[i] != 0;
                                }

                                if (not_zero)
                                {
                                    int mask = 0x800;
                                    for (int i = 0; i < 12; i++)
                                    {
                                        if ((char_data[0] & mask) != 0) mem[vram_addr + 0] = color;
                                        if ((char_data[1] & mask) != 0) mem[vram_addr + 1] = color;
                                        if ((char_data[2] & mask) != 0) mem[vram_addr + 2] = color;
                                        if ((char_data[3] & mask) != 0) mem[vram_addr + 3] = color;
                                        if ((char_data[4] & mask) != 0) mem[vram_addr + 4] = color;
                                        if ((char_data[5] & mask) != 0) mem[vram_addr + 5] = color;
                                        if ((char_data[6] & mask) != 0) mem[vram_addr + 6] = color;
                                        if ((char_data[7] & mask) != 0) mem[vram_addr + 7] = color;

                                        vram_addr += SCREEN_WIDTH;
                                        mask >>= 1;
                                    }
                                }

                                GP += 6;
                                break;
                            }
                        case GrapicOps.TrueColorSprite:
                            throw new NotImplementedException();
                        case GrapicOps.PalettedSprite:
                            {
                                int vram_addr = mem[GP + 1] << 12 | mem[GP + 2];
                                int sprite = mem[GP + 3] << 12 | mem[GP + 4];
                                int palette = mem[GP + 5] << 12 | mem[GP + 6];
                                int stride = mem[GP + 7];
                                int width = mem[GP + 8];
                                int height = mem[GP + 9];
                                
                                // stride must be a multiple of 3
                                if (stride % 3 != 0)
                                {
                                    throw new InvalidOperationException();
                                }
                                
                                for (int y = 0; y < height; y++)
                                {
                                    int sprite_data;
                                    for (int x = 0; x < width / 3; x++)
                                    {
                                        sprite_data = mem[sprite++];

                                        mem[vram_addr++] = mem[palette + ((sprite_data >> 8) & 0xF)];
                                        mem[vram_addr++] = mem[palette + ((sprite_data >> 4) & 0xF)];
                                        mem[vram_addr++] = mem[palette + (sprite_data & 0xF)];
                                    }

                                    // Draw the horizontal pixels not on a 3 multiple boundrary
                                    for (int i = 0; i < width % 3; i++)
                                    {
                                        sprite_data = mem[sprite++];
                                        mem[vram_addr++] = mem[palette + ((sprite_data >> ((1 - i) * 4)) & 0xFF)];
                                    }
                                    
                                    // The the rest of the stride
                                    sprite += (stride / 3) - ((width + 2) / 3);
                                    vram_addr += SCREEN_WIDTH - width;
                                }

                                GP += 10;
                                break;
                            }
                        case GrapicOps.Fontchar_Mask:
                            throw new NotImplementedException();
                        case GrapicOps.TrueColorSprite_Mask:
                            throw new NotImplementedException();
                        case GrapicOps.PalettedSprite_Mask:
                            {
                                int vram_addr = mem[GP + 1] << 12 | mem[GP + 2];
                                int sprite = mem[GP + 3] << 12 | mem[GP + 4];
                                int palette = mem[GP + 5] << 12 | mem[GP + 6];
                                int mask = mem[GP + 7] << 12 | mem[GP + 8];
                                int stride = mem[GP + 9];
                                int width = mem[GP + 10];
                                int height = mem[GP + 11];

                                // stride must be a multiple of 3
                                if (stride % 3 != 0)
                                {
                                    throw new InvalidOperationException();
                                }

                                int mask_counter = 0;
                                for (int y = 0; y < height; y++)
                                {
                                    int sprite_data;
                                    for (int x = 0; x < width / 3; x++)
                                    {
                                        sprite_data = mem[sprite++];

                                        mem[vram_addr] = 
                                            ((MEM[mask + (mask_counter / 12)] >> (11 - mask_counter % 12)) & 0x01) == 0x00 ?
                                            mem[vram_addr] : mem[palette + ((sprite_data >> 8) & 0xF)];
                                        mask_counter++;
                                        vram_addr++;
                                        mem[vram_addr] = ((MEM[mask + (mask_counter / 12)] >> (11 - mask_counter % 12)) & 0x01) == 0x00 ?
                                            mem[vram_addr] : mem[palette + ((sprite_data >> 4) & 0xF)];
                                        mask_counter++;
                                        vram_addr++;
                                        mem[vram_addr] = ((MEM[mask + (mask_counter / 12)] >> (11 - mask_counter % 12)) & 0x01) == 0x00 ?
                                            mem[vram_addr] : mem[palette + (sprite_data & 0xF)];
                                        mask_counter++;
                                        vram_addr++;
                                    }

                                    // Draw the horizontal pixels not on a 3 multiple boundrary
                                    for (int i = 0; i < width % 3; i++)
                                    {
                                        sprite_data = mem[sprite++];
                                        mem[vram_addr] = ((MEM[mask + (mask_counter / 12)] >> (11 - mask_counter % 12)) & 0x01) == 0x00 ?
                                            mem[vram_addr] : mem[palette + ((sprite_data >> ((1 - i) * 4)) & 0xFF)];
                                        mask_counter++;
                                        vram_addr++;
                                    }

                                    // The the rest of the stride
                                    sprite += (stride / 3) - ((width + 2) / 3);
                                    vram_addr += SCREEN_WIDTH - width;
                                }

                                GP += 12;
                                break;
                            }
                        case GrapicOps.FontcharBuffer:
                            {
                                // FIMXME: Switch to using a color buffer!
                                int color = mem[GP + 1];
                                int char_buffer_addr = mem[GP + 2] << 12 | mem[GP + 3];
                                int buffer_length = mem[GP + 4] << 12 | mem[GP + 5];
                                int vram_addr = mem[GP + 6] << 12 | mem[GP + 7];
                                int font_addr = mem[GP + 8] << 12 | mem[GP + 9];
                                
                                for (int i = 0; i < buffer_length; i++)
                                {
                                    int c = mem[char_buffer_addr++];
                                    int char_addr = font_addr + (c * CHAR_WIDTH);
                                    
                                    bool not_zero = false;
                                    for (int j = 0; j < 8; j++)
                                    {
                                        char_data[j] = mem[char_addr + j];
                                        not_zero |= char_data[j] != 0;
                                    }

                                    if (not_zero)
                                    {
                                        int draw_addr = vram_addr;
                                        int mask = 0x800;
                                        for (int j = 0; j < 12; j++)
                                        {
                                            if ((char_data[0] & mask) != 0) mem[draw_addr + 0] = color;
                                            if ((char_data[1] & mask) != 0) mem[draw_addr + 1] = color;
                                            if ((char_data[2] & mask) != 0) mem[draw_addr + 2] = color;
                                            if ((char_data[3] & mask) != 0) mem[draw_addr + 3] = color;
                                            if ((char_data[4] & mask) != 0) mem[draw_addr + 4] = color;
                                            if ((char_data[5] & mask) != 0) mem[draw_addr + 5] = color;
                                            if ((char_data[6] & mask) != 0) mem[draw_addr + 6] = color;
                                            if ((char_data[7] & mask) != 0) mem[draw_addr + 7] = color;

                                            draw_addr += SCREEN_WIDTH;
                                            mask >>= 1;
                                        }
                                    }
                                    
                                    vram_addr += CHAR_WIDTH;

                                    if (vram_addr < VRAM_START || vram_addr >= (VRAM_START + VRAM_SIZE))
                                    {
                                        Debugger.Break();
                                    }
                                }

                                GP += 10;
                                break;
                            }
                        case GrapicOps.FontcharBufferColor:
                            {
                                int char_buffer_addr = mem[GP + 1] << 12 | mem[GP + 2];
                                int color_buffer_addr = mem[GP + 3] << 12 | mem[GP + 4];
                                int buffer_length = mem[GP + 5] << 12 | mem[GP + 6];
                                int vram_addr = mem[GP + 7] << 12 | mem[GP + 8];
                                int font_addr = mem[GP + 9] << 12 | mem[GP + 10];

                                for (int i = 0; i < buffer_length; i++)
                                {
                                    int color = mem[color_buffer_addr++];
                                    int c = mem[char_buffer_addr++];
                                    int char_addr = font_addr + (c * CHAR_WIDTH);

                                    bool not_zero = false;
                                    for (int j = 0; j < 8; j++)
                                    {
                                        char_data[j] = mem[char_addr + j];
                                        not_zero |= char_data[j] != 0;
                                    }

                                    if (not_zero)
                                    {
                                        int draw_addr = vram_addr;
                                        int mask = 0x800;
                                        for (int j = 0; j < 12; j++)
                                        {
                                            if ((char_data[0] & mask) != 0) mem[draw_addr + 0] = color;
                                            if ((char_data[1] & mask) != 0) mem[draw_addr + 1] = color;
                                            if ((char_data[2] & mask) != 0) mem[draw_addr + 2] = color;
                                            if ((char_data[3] & mask) != 0) mem[draw_addr + 3] = color;
                                            if ((char_data[4] & mask) != 0) mem[draw_addr + 4] = color;
                                            if ((char_data[5] & mask) != 0) mem[draw_addr + 5] = color;
                                            if ((char_data[6] & mask) != 0) mem[draw_addr + 6] = color;
                                            if ((char_data[7] & mask) != 0) mem[draw_addr + 7] = color;

                                            draw_addr += SCREEN_WIDTH;
                                            mask >>= 1;
                                        }
                                    }

                                    vram_addr += CHAR_WIDTH;

                                    if (vram_addr < VRAM_START || vram_addr >= (VRAM_START + VRAM_SIZE))
                                    {
                                        Debugger.Break();
                                    }
                                }

                                GP += 11;
                                break;
                            }
                        default:
                            throw new InvalidOperationException();
                    }

                    graphicsTime++;

                    // If we are at the end loop back
                    if (GP > Constants.GRAM_END)
                    {
                        GP = GRAM_START;
                    }
                }
            }
        }
    }
}
