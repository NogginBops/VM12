﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Windows.Forms;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Debugging;
using Profiler;
using VM12Opcode;
using VM12Util;

namespace VM12
{
    public partial class VM12Form : Form
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public unsafe struct ThreeByteColor
        {
            public byte r;
            public byte g;
            public byte b;
        }
        
        public static readonly ThreeByteColor[] colorLUT = new ThreeByteColor[4096];

        public static VM12Form form { get; private set; }

        private volatile VM12 vm12;

        public static long StartTime = -1;
        
        private readonly static byte[] data = new byte[VM12.SCREEN_WIDTH * 3 * VM12.SCREEN_HEIGHT];

        private readonly static GCHandle BitsHandle = GCHandle.Alloc(data, GCHandleType.Pinned);

        private readonly static Bitmap bitmap = new Bitmap(VM12.SCREEN_WIDTH, VM12.SCREEN_HEIGHT, VM12.SCREEN_WIDTH * 3, PixelFormat.Format24bppRgb, BitsHandle.AddrOfPinnedObject());
        
        private readonly static int[] vram = new int[VM12.VRAM_SIZE];

        private Thread hTimer;

        private readonly static ProgramDebugger debugger = new ProgramDebugger();

        private readonly static ProcProfiler profiler = new ProcProfiler();
        
        public VM12Form()
        {
            if (Constants.VALID_MEM_PARTITIONS == false)
            {
                throw new Exception("Memory partitions don't match the memory size!!");
            }

            if (form != null)
            {
                throw new InvalidOperationException("Cannot create more than one VM12Form");
            }

            form = this;

            InitializeComponent();
            
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            
            Shown += (s, e1) => LoadProgram();
            
            pbxMain.Image = bitmap;

            pbxMain.MouseWheel += PbxMain_MouseWheel;

            GenerateLUT();

            SetSize(VM12.SCREEN_HEIGHT, InterpolationMode.NearestNeighbor);

#if !DEBUG
            MainMenuStrip.Items.RemoveAt(1);
#endif
        }
        
        private void GenerateLUT()
        {
            for (int i = 0; i < colorLUT.Length; i++)
            {
                ThreeByteColor col;

                // r
                col.r = (byte)((i & 0x00F) << 4);
                // g
                col.g = (byte)(i & 0x0F0);
                // b
                col.b = (byte)((i >> 4) & 0x0F0);

                colorLUT[i] = col;
            }
        }

        private void SetSize(int height, InterpolationMode scaleMode = InterpolationMode.Default)
        {
            SetSize((int)((height / (decimal)3) * 4), height, scaleMode);
        }

        private void SetSize(int width, int height, InterpolationMode scaleMode = InterpolationMode.Default)
        {
            this.Width = width + 16;
            this.Height = height + 63;
            pbxMain.SizeMode = PictureBoxSizeMode.StretchImage;
            pbxMain.InterpolationMode = scaleMode;
        }
        
        private void CompileAndRunProgram(FileInfo programFile)
        {
            if (programFile.Extension != ".12exe")
            {
                Stopwatch totTimer = new Stopwatch();
                totTimer.Start();
                if (programFile.Extension == ".t12")
                {
                    Stopwatch t12Timer = new Stopwatch();
                    t12Timer.Start();
                    // FIXME: Send in a proper error handler!
                    T12.Compiler.StartCompiling(programFile.Directory, (e) => Console.WriteLine(e.Message));
                    T12.Compiler.Compile(programFile);
                    T12.Compiler.StopCompiling();
                    t12Timer.Stop();

                    Console.WriteLine($"T12 took: {totTimer.GetMS()}ms for {T12.Compiler.CompiledLines} lines ({T12.Compiler.CompiledFiles} files).");
                    Console.WriteLine($"This is {T12.Compiler.CompiledLines / totTimer.GetSec():0.00} lines/sec");

                    programFile = new FileInfo(Path.ChangeExtension(programFile.FullName, ".12asm"));
                }

                // First argument is the src and the second argument is the name of the exe that should be generated
                FastVM12Asm.Fast12Asm.Main(programFile.FullName, Path.GetFileNameWithoutExtension(programFile.Name));

                int totLines = T12.Compiler.CompiledLines + T12.Compiler.ResultLines;
                totTimer.Stop();
                Console.WriteLine($"Compilation took: {totTimer.GetMS():0.00}ms for {totLines} lines");
                Console.WriteLine($"This is {totLines/totTimer.GetSec():0.00} lines/sec");

                //VM12Asm.VM12Asm.Reset();
                //VM12Asm.VM12Asm.Main("-src", programFile.FullName, "-dst", Path.GetFileNameWithoutExtension(programFile.Name), "-e", "-o");

                programFile = new FileInfo(Path.ChangeExtension(programFile.FullName, "12exe"));
            }

            short[] rom = new short[VM12.ROM_SIZE];

            using (BinaryReader br = new BinaryReader(File.OpenRead(programFile.FullName)))
            {
                while (br.BaseStream.Position < br.BaseStream.Length)
                {
                    int pos = br.ReadInt32();
                    int length = br.ReadInt32();


                    short[] data = new short[length];
                    for (int i = 0; i < length; i++)
                    {
                        data[i] = br.ReadInt16();
                    }

#if DEBUG_BINFORMAT
                            Console.WriteLine($"Reading a block from pos {pos} with length {length} with fist value {data[0]} and last value {data[data.Length - 1]}");
#endif

                    Array.Copy(data, 0, rom, pos, length);
                }

                /*
                for (int i = 0; i < rom.Length; i++)
                {
                    rom[i] = br.ReadInt16();
                }
                */
            }

            if (vm12 != null && vm12.Running)
            {
                vm12.Stop();
            }

            FileInfo storageFile = new FileInfo(Path.Combine(programFile.DirectoryName, "Store.dsk"));
            if (storageFile.Exists == false)
            {
                storageFile.Create();
            }

#if DEBUG
            FileInfo metadataFile = new FileInfo(Path.Combine(programFile.DirectoryName, Path.GetFileNameWithoutExtension(programFile.FullName) + ".12meta"));

            vm12 = new VM12(rom, metadataFile, storageFile);

            vm12.HitBreakpoint += Vm12_HitBreakpoint;
            debugger.SetVM(vm12);
#else
                    vm12 = new VM12(rom, storageFile);
#endif

            // Just use a flag to tell the interrupts to not fire, we want to keep the debug data!
            Thread thread = new Thread(() => { vm12.Start(); vm12 = null; })
            {
                Name = "VM12",
                IsBackground = true,
            };

            thread.Start();

            StartTime = ((DateTimeOffset)DateTime.Now).ToUnixTimeSeconds();

            hTimer?.Interrupt();
            hTimer = new Thread(hTime_Thread)
            {
                Name = "hTimer",
                IsBackground = true,
            };

            hTimer.Start();
        }
        
        private void LoadProgram()
        {
            // NOTE: This could be moved to the constructor,
            // because we really only want to read the cli arguments once
            string[] args = Environment.GetCommandLineArgs();

            FileInfo program = null;

            // If there is a file specified in the commandline
            if (args.Length > 1 && File.Exists(args[1]))
            {
                program = new FileInfo(args[1]);
            }
            else
            {
                OpenFileDialog dialog = new OpenFileDialog();

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    program = new FileInfo(dialog.FileName);
                }
            }

            if (program != null)
            {
                Task.Run(() => CompileAndRunProgram(program));
            }
        }

        private void OpenFileDialogAndLoadProgram()
        {
            OpenFileDialog dialog = new OpenFileDialog();

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                FileInfo program = new FileInfo(dialog.FileName);

                CompileAndRunProgram(program);
            }
        }
        
        private void Vm12_HitBreakpoint(object sender, EventArgs e)
        {
#if DEBUG
            if (InvokeRequired)
            {
                vm12.UseDebugger = true;
                //Interlocked.Exchange(ref vm12.UseDebugger, 1);
                vm12.ContinueEvent.Reset();
                BeginInvoke(new Action(OpenDebuggerBreakpoint));
            }
#endif
        }

        private void OpenDebuggerBreakpoint()
        {
            debugger.CreateControl();
            debugger.Show();
            debugger.HitBreakpoint();
        }

        long lastIntsructionCount = -1;
        double utilization = 0;

        long longTimer = 0;

        long timer = 0;
        
        Stopwatch watch = new Stopwatch();
        
        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            if (vm12 != null && vm12.Running)
            {
                long elapsed = watch.ElapsedMilliseconds;

                timer += elapsed;
                longTimer += elapsed;
                
                watch.Restart();

                if (longTimer > 1000)
                {
                    long delta;
                    if (lastIntsructionCount < 0)
                    {
                        lastIntsructionCount = vm12.Ticks;
                        delta = vm12.Ticks;
                    }
                    else
                    {
                        delta = vm12.Ticks - lastIntsructionCount;
                        lastIntsructionCount += delta;
                    }

                    utilization = delta;

                    longTimer = 0;
                }

                if (timer > 100)
                {
                    timer = 0;

                    Text = vm12.Stopped ? "Stopped" : "Running";
                    // TODO: Format with fitting prefix e.g. k/m/g
                    Text += $" Inst executed: {vm12.Ticks / 1_000}k/{vm12.GraphicsTicks}, Inst/sec: {utilization}/s, Interrupts: {vm12.InterruptCount}, Missed: {vm12.MissedInterrupts}, SP: {vm12.SPWatermark}, FP: {vm12.FPWatermark}, ";
                }
            }
            else
            {
                Text = "Uninitialized";
            }
            
            if (vm12 != null)
            {
                // BitmapData bData = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.WriteOnly, bitmap.PixelFormat);

                const byte bitsPerPixel = 24;
                const byte bytesPerPixel = bitsPerPixel / 8;

                const int size = Constants.SCREEN_WIDTH * 3 * Constants.SCREEN_HEIGHT; // bData.Stride * bData.Height;

                // System.Runtime.InteropServices.Marshal.Copy(bData.Scan0, data, 0, size);
                
                unsafe
                {
                    fixed (int* mem = vm12.MEM)
                    fixed (byte* b_data = data)
                    fixed (ThreeByteColor* lut = colorLUT)
                    {
                        int index = 0;

                        int* vram = mem + VM12.VRAM_START;
                        for (int i = 0; i < size; i += bytesPerPixel)
                        {
                            ThreeByteColor col = lut[vram[index]];
                            
                            b_data[i] = col.r;
                            b_data[i + 1] = col.g;
                            b_data[i + 2] = col.b;
                            
                            index++;
                        }
                    }
                }
                
                // System.Runtime.InteropServices.Marshal.Copy(data, 0, bData.Scan0, data.Length);

                // bitmap.UnlockBits(bData);
            }

            // pbxMain.Image = bitmap;

            pbxMain.Invalidate();

            vm12?.Interrupt(new Interrupt(InterruptType.v_blank, new int[0]));
        }

        private void VM12Form_FormClosing(object sender, FormClosingEventArgs e)
        {
            debugger.Close();

            vm12?.Stop();

#if DEBUG
            vm12?.ContinueEvent.Set();
#endif

            debugger.CloseDebugger();
            
            bitmap.Dispose();
            BitsHandle.Free();
            
            while (vm12?.Running ?? false);
        }

        private void instructionFrequencyToolStripMenuItem_Click(object sender, EventArgs e)
        {
#if DEBUG
            if (vm12 != null)
            {
                Frequency_dialog<VM12Opcode.Opcode> instructionFreq = new Frequency_dialog<VM12Opcode.Opcode>(vm12.instructionFreq, "Opcode Frequencies", "Opcode", op => (int) op);
                
                instructionFreq.Show();
            }
#endif
        }

        private void gpuInstructionFrequencyToolStripMenuItem_Click(object sender, EventArgs e)
        {
#if DEBUG
            if (vm12 != null)
            {
                Frequency_dialog<GrapicOps> instructionFreq = new Frequency_dialog<GrapicOps>(vm12.gpuInstructionFreq, "Gpu Opcode Frequencies", "Opcode", op => (int)op);

                instructionFreq.Show();
            }
#endif
        }

        private void interruptFrequencyToolStripMenuItem_Click(object sender, EventArgs e)
        {
#if DEBUG
            if (vm12 != null)
            {
                Frequency_dialog<InterruptType> missedInterruptFreq = new Frequency_dialog<InterruptType>(vm12.MissedInterruptFreq, "Missed interrupt frequencies", "Interrupt", VM12.InterruptTypeToInt);

                missedInterruptFreq.Show();
            }
#endif
        }

        private void interruptFrequencyToolStripMenuItem1_Click(object sender, EventArgs e)
        {
#if DEBUG
            if (vm12 != null)
            {
                Frequency_dialog<InterruptType> interruptFreq = new Frequency_dialog<InterruptType>(vm12.InterruptFreq, "Interrupt frequencies", "Interrupt", VM12.InterruptTypeToInt);

                interruptFreq.Show();
            }
#endif
        }
        
        private void VM12Form_KeyDown(object sender, KeyEventArgs e)
        {
            //Debug.WriteLine($"Keycode: {(int)e.KeyCode:X2}");
            
            vm12?.Interrupt(new Interrupt(InterruptType.keyboard, new[] { 1, (int) e.KeyCode & 0xFFF }));

            e.Handled = true;
        }

        private void VM12Form_KeyUp(object sender, KeyEventArgs e)
        {
            //Debug.WriteLine($"Released keycode: {(int)e.KeyCode:X2}");

            vm12?.Interrupt(new Interrupt(InterruptType.keyboard, new[] { 0, (int) e.KeyCode & 0xFFF }));

            e.Handled = true;
        }

        private void stopToolStripMenuItem_Click(object sender, EventArgs e)
        {
            vm12?.Stop();

            while (vm12?.Running ?? false);

            vm12 = null;
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialogAndLoadProgram();
        }

        int lastPosX;
        int lastPosY;

        private void PressButtons(MouseButtons buttons)
        {
            currentButtons |= (int)buttons >> 13;
        }

        private void ReleaseButtons(MouseButtons buttons)
        {
            currentButtons &= ~((int)buttons >> 13);
        }

        private void SetScroll(int delta)
        {
            int ticks = delta / SystemInformation.MouseWheelScrollDelta;
            // Clear the scroll part
            currentButtons &= 0xFC0;
            // Cast the ticks
            // Ticks less than -64 will be interpreted as positive...
            if (ticks < -64) ticks = -64;
            ticks &= 0x03F;
            
            currentButtons |= ticks;
        }

        int currentButtons;
        
        private int map(int value, int min, int max, int newMin, int newMax)
        {
            if (min == max)
            {
                return newMin;
            }
            else
            {
                return (int)(newMin + (((double)(value - min) / (max - min)) * newMax));
            }
        }

        private void pbxMain_MouseMove(object sender, MouseEventArgs e)
        {
            int x = map(e.X, 0, pbxMain.Width, 0, VM12.SCREEN_WIDTH);
            int y = map(e.Y, 0, pbxMain.Height, 0, VM12.SCREEN_HEIGHT);
            
            SetScroll(e.Delta);

            vm12?.Interrupt(new Interrupt(InterruptType.mouse, new int[] { x, y, currentButtons }));

            lastPosX = x;
            lastPosY = y;
        }

        private void pbxMain_MouseDown(object sender, MouseEventArgs e)
        {
            int x = map(e.X, 0, pbxMain.Width, 0, VM12.SCREEN_WIDTH);
            int y = map(e.Y, 0, pbxMain.Height, 0, VM12.SCREEN_HEIGHT);

            SetScroll(e.Delta);

            PressButtons(e.Button);
            vm12?.Interrupt(new Interrupt(InterruptType.mouse, new int[] { x, y, currentButtons }));
        }

        private void pbxMain_MouseUp(object sender, MouseEventArgs e)
        {
            int x = map(e.X, 0, pbxMain.Width, 0, VM12.SCREEN_WIDTH);
            int y = map(e.Y, 0, pbxMain.Height, 0, VM12.SCREEN_HEIGHT);

            SetScroll(e.Delta);

            ReleaseButtons(e.Button);
            vm12?.Interrupt(new Interrupt(InterruptType.mouse, new int[] { x, y, currentButtons }));
        }

        private void PbxMain_MouseWheel(object sender, MouseEventArgs e)
        {
            int x = map(e.X, 0, pbxMain.Width, 0, VM12.SCREEN_WIDTH);
            int y = map(e.Y, 0, pbxMain.Height, 0, VM12.SCREEN_HEIGHT);

            SetScroll(e.Delta);
            
            vm12?.Interrupt(new Interrupt(InterruptType.mouse, new int[] { x, y, currentButtons }));
        }
        
        private bool showingCursor = true;

        private void pbxMain_MouseEnter(object sender, EventArgs e)
        {
            if (vm12 == null && showingCursor == false)
            {
                Cursor.Show();
                showingCursor = true;
            }

            if (vm12 != null && vm12.Running)
            {
                if (showingCursor == true)
                {
                    Cursor.Hide();
                    //Debug.WriteLine("Hide");
                    showingCursor = false;
                }
            }
        }

        private void pbxMain_MouseLeave(object sender, EventArgs e)
        {
            if (vm12 == null && showingCursor == false)
            {
                Cursor.Show();
                showingCursor = true;
            }

            if (vm12 != null && vm12.Running)
            {
                if (showingCursor == false)
                {
                    Cursor.Show();
                    //Debug.WriteLine("Show");
                    showingCursor = true;
                }
            }
        }
        
        private void hTime_Thread(object state)
        {
            Stopwatch hTimer_watch = new Stopwatch();

            while (vm12 != null)
            {
                long elapsed = hTimer_watch.ElapsedMilliseconds;
                
                vm12?.Interrupt(new Interrupt(InterruptType.h_timer, new int[] { (int) elapsed & 0xFFF }));

                hTimer_watch.Restart();

                try
                {
                    Thread.Sleep(9);
                }
                catch(ThreadInterruptedException)
                {
                    return;
                }
            }
        }

        private void instructionTimesToolStripMenuItem_Click(object sender, EventArgs e)
        {
#if DEBUG
            if (vm12 != null)
            {
                //Frequency_dialog<VM12_Opcode.Opcode> instructionFreq = new Frequency_dialog<VM12_Opcode.Opcode>(sourceHitCount, "Opcode Times", "Opcode", op => (int)op);

                //instructionFreq.Show();
            }
#endif
        }

        private void heapViewToolStripMenuItem_Click(object sender, EventArgs e)
        {
#if DEBUG
            if (vm12 != null)
            {
                // These should not really be magic numbers!
                const int metadata_size = 65536;
                const int block_size = 64;

                // FIXME: autoConsts should not be public
                Dictionary<string, VM12.AutoConst> autoCosnts = vm12.autoConsts;
                
                // FIXME: Handle the case where there is no heap compiled or we can't find the autoconsts!
                int metadata_addr = autoCosnts.TryGetValue("heap_metadata", out VM12.AutoConst m_addr) ? m_addr.Addr : throw new Exception();
                int heap_addr = autoCosnts.TryGetValue("heap", out VM12.AutoConst h_addr) ? h_addr.Addr : throw new Exception();

                HeapView.Heap heap_struct;

                unsafe
                {
                    fixed (int* mem = vm12.MEM)
                    {
                        int* metadata = mem + metadata_addr;
                        int* heap = mem + heap_addr;

                        heap_struct = new HeapView.Heap(vm12, metadata_addr, metadata, metadata_size, heap_addr, heap, block_size);
                    }
                }

                HeapView view = new HeapView(heap_struct);

                view.Show();
            }
#endif
        }

        private void debuggerToolStripMenuItem_Click(object sender, EventArgs e)
        {
#if DEBUG
            if (vm12 != null)
            {
                debugger.SetVM(vm12);
            }
            
            debugger.Show();
            debugger.BringToFront();
#endif
        }

#if DEBUG
        public static ProgramDebugger GetProgramDebugger()
        {
            return debugger;
        }
#endif

        private void profilerToolStripMenuItem_Click(object sender, EventArgs e)
        {
#if DEBUG
            if (vm12 != null)
            {
                profiler.SetVM(vm12);
            }

            profiler.Show();
            profiler.BringToFront();
#endif
        }

        private void VM12Form_KeyPress(object sender, KeyPressEventArgs e)
        {
            e.Handled = true;
        }

        private void memoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (vm12 != null)
            {
                MemoryInspector inspector = new MemoryInspector();
                inspector.SetVM12(vm12);
                inspector.Show();
            }
        }

        private void soundDebugToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (vm12 != null)
            {
                SoundDebug sndDbg = new SoundDebug();
                sndDbg.SetVM12(vm12);
                sndDbg.Show();
            }
        }
    }
}
