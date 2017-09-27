using System;
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

namespace VM12
{
    public partial class VM12Form : Form
    {
        volatile VM12 vm12;

        public static long StartTime = -1;

        static byte[] data = new byte[VM12.SCREEN_WIDTH * 3 * VM12.SCREEN_HEIGHT];

        private static GCHandle BitsHandle = GCHandle.Alloc(data, GCHandleType.Pinned);

        Bitmap bitmap = new Bitmap(VM12.SCREEN_WIDTH, VM12.SCREEN_HEIGHT, VM12.SCREEN_WIDTH * 3, PixelFormat.Format24bppRgb, BitsHandle.AddrOfPinnedObject());
        
        int[] vram = new int[Memory.VRAM_SIZE];

        System.Threading.Thread hTimer;

#if DEBUG
        System.Threading.Timer perfTimer;

        Dictionary<string, int> sourceHitCount = new Dictionary<string, int>();
        
        private void MeasurePerf(object state)
        {
            if (vm12 != null)
            {
                VM12.ProcMetadata data = vm12.CurrentMetadata;
                if (data != null)
                {
                    string key = $"{data.file}:{vm12.GetSourceCodeLineFromMetadataAndOffset(data, vm12.ProgramCounter)}";
                    if (sourceHitCount.TryGetValue(key, out int val))
                    {
                        sourceHitCount[key] = val + 1;
                    }
                    else
                    {
                        sourceHitCount[key] = 1;
                    }
                }
            }
        }
#endif

        public VM12Form()
        {
            InitializeComponent();
            
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            
            Shown += (s, e1) => LoadProgram();

            pbxMain.Image = bitmap;

            // SetSize(480);

#if !DEBUG
            MainMenuStrip.Items.RemoveAt(1);
#endif
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
        }
        
        private void LoadProgram()
        {
            OpenFileDialog dialog = new OpenFileDialog();

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                FileInfo inf = new FileInfo(dialog.FileName);
                
                Task.Run(() =>
                {

                    if (inf.Extension == ".12asm")
                    {
                        VM12Asm.VM12Asm.Main("-src", inf.FullName, "-dst", Path.GetFileNameWithoutExtension(inf.Name), "-e", "-o");

                        inf = new FileInfo(Path.Combine(inf.DirectoryName, Path.GetFileNameWithoutExtension(inf.FullName) + ".12exe"));

                        VM12Asm.VM12Asm.Reset();
                    }
                    
                    short[] rom = new short[(int)Math.Ceiling(inf.Length / 2d)];

                    using (BinaryReader br = new BinaryReader(File.OpenRead(inf.FullName)))
                    {
                        for (int i = 0; i < rom.Length; i++)
                        {
                            rom[i] = br.ReadInt16();
                        }
                    }

                    if (vm12 != null && vm12.Running)
                    {
                        vm12.Stop();
#if DEBUG
                        perfTimer.Dispose();
#endif
                    }
                    
#if DEBUG
                    FileInfo metadataFile = new FileInfo(Path.Combine(inf.DirectoryName, Path.GetFileNameWithoutExtension(inf.FullName) + ".12meta"));

                    vm12 = new VM12(rom, metadataFile);
#else
                    vm12 = new VM12(rom);
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
                    
#if DEBUG
                    perfTimer = new System.Threading.Timer(MeasurePerf, null, 0, 1);
#endif
                });
            }
        }

        long lastIntsructionCount = -1;
        double maxInstructionsPerSecond = 600000000;
        double utilization = 0;

        int timer = 0;

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            if (vm12 != null && vm12.Running)
            {
                timer += refreshTimer.Interval;

                if (timer > 100)
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

                    utilization = delta / (maxInstructionsPerSecond * (refreshTimer.Interval / 1000d));

                    if (utilization > 1)
                    {
                        maxInstructionsPerSecond *= utilization;
                        utilization = 1;
                    }
                    
                    timer = 0;

                    Text = vm12.Stopped ? "Stopped" : "Running";
                    Text += $" Inst executed: {vm12.Ticks / 1000000}m, Util: {utilization:P}, Interrupts: {vm12.InterruptCount}, Missed: {vm12.MissedInterrupts}, SP: {vm12.SPWatermark}, FP: {vm12.FPWatermark}, ";
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

                const int size = VM12.SCREEN_WIDTH * 3 * VM12.SCREEN_HEIGHT; // bData.Stride * bData.Height;

                // System.Runtime.InteropServices.Marshal.Copy(bData.Scan0, data, 0, size);
                
                unsafe
                {
                    fixed (int* mem = vm12.MEM)
                    {
                        int* vram = mem + Memory.VRAM_START;
                        for (int i = 0; i < size; i += bytesPerPixel)
                        {
                            int index = i / (bytesPerPixel);

                            int val = vram[index];

                            // r
                            data[i] = (byte)((val & 0x00F) << 4);
                            // g
                            data[i + 1] = (byte)(val & 0x0F0);
                            // b
                            data[i + 2] = (byte)((val >> 4) & 0x0F0);
                        }
                    }
                }
                
                // System.Runtime.InteropServices.Marshal.Copy(data, 0, bData.Scan0, data.Length);

                // bitmap.UnlockBits(bData);
            }

            // pbxMain.Image = bitmap;

            pbxMain.Invalidate();

            vm12?.Interrupt(new Interrupt(InterruptType.v_Blank, new int[0]));
        }

        private void VM12Form_FormClosing(object sender, FormClosingEventArgs e)
        {
            vm12?.Stop();

            BitsHandle.Free();
        }

        private void instructionFrequencyToolStripMenuItem_Click(object sender, EventArgs e)
        {
#if DEBUG
            if (vm12 != null)
            {
                Frequency_dialog<VM12_Opcode.Opcode> instructionFreq = new Frequency_dialog<VM12_Opcode.Opcode>(vm12.instructionFreq, "Opcode Frequencies", "Opcode", op => (int) op);
                
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
            Debug.WriteLine($"Keycode: {(int)e.KeyCode:X2}");
            
            vm12?.Interrupt(new Interrupt(InterruptType.keyboard, new[] { 1, (int) e.KeyCode & 0xFFF }));
        }

        private void VM12Form_KeyUp(object sender, KeyEventArgs e)
        {
            vm12?.Interrupt(new Interrupt(InterruptType.keyboard, new[] { 0, (int) e.KeyCode & 0xFFF }));
        }

        private void stopToolStripMenuItem_Click(object sender, EventArgs e)
        {
            vm12?.Stop();

            while (vm12?.Running ?? false);

            vm12 = null;
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            LoadProgram();
        }

        int lastPosX;
        int lastPosY;

        private void PressButtons(MouseButtons buttons)
        {
            currentButtons |= (int)buttons >> 20;
        }

        private void ReleaseButtons(MouseButtons buttons)
        {
            currentButtons &= ~((int)buttons >> 20);
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

            vm12?.Interrupt(new Interrupt(InterruptType.mouse, new int[] { x, y, currentButtons }));

            lastPosX = x;
            lastPosY = y;
        }

        private void pbxMain_MouseDown(object sender, MouseEventArgs e)
        {
            int x = map(e.X, 0, pbxMain.Width, 0, VM12.SCREEN_WIDTH);
            int y = map(e.Y, 0, pbxMain.Height, 0, VM12.SCREEN_HEIGHT);

            PressButtons(e.Button);
            vm12?.Interrupt(new Interrupt(InterruptType.mouse, new int[] { x, y, currentButtons }));
        }

        private void pbxMain_MouseUp(object sender, MouseEventArgs e)
        {
            int x = map(e.X, 0, pbxMain.Width, 0, VM12.SCREEN_WIDTH);
            int y = map(e.Y, 0, pbxMain.Height, 0, VM12.SCREEN_HEIGHT);

            ReleaseButtons(e.Button);
            vm12?.Interrupt(new Interrupt(InterruptType.mouse, new int[] { x, y, currentButtons }));
        }
        
        private void pbxMain_MouseEnter(object sender, EventArgs e)
        {
            if (vm12 != null)
            {
                Cursor.Hide();
            }
        }

        private void pbxMain_MouseLeave(object sender, EventArgs e)
        {
            if (vm12 != null)
            {
                Cursor.Show();
            }
        }

        //private void hTimer_Tick(object sender, EventArgs e)
        //{
        //    vm12?.Interrupt(new Interrupt(InterruptType.h_Timer, new int[] { hTimer.Interval }));
        //}

        private void hTime_Thread(object state)
        {
            Stopwatch hTimer_watch = new Stopwatch();

            while (true)
            {
                long elapsed = hTimer_watch.ElapsedMilliseconds;
                
                vm12?.Interrupt(new Interrupt(InterruptType.h_Timer, new int[] { (int) elapsed & 0xFFF }));

                hTimer_watch.Restart();

                try
                {
                    Thread.Sleep(9);
                }
                catch
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
                const int metadata_size = 65536;
                const int block_size = 64;

                // FIXME: autoConsts should not be public
                Dictionary<string, VM12.AutoConst> autoCosnts = vm12.autoConsts;
                
                int metadata_addr = autoCosnts.TryGetValue("metadata", out VM12.AutoConst m_addr) ? m_addr.Addr : throw new Exception();
                int heap_addr = autoCosnts.TryGetValue("metadata", out VM12.AutoConst h_addr) ? h_addr.Addr : throw new Exception();

                HeapView.Heap heap_struct;

                unsafe
                {
                    fixed (int* mem = vm12.MEM)
                    {
                        int* metadata = mem + metadata_addr;
                        int* heap = mem + heap_addr;

                        heap_struct = new HeapView.Heap(metadata, metadata_size, heap, block_size);
                    }
                }

                HeapView view = new HeapView(heap_struct);

                view.Show();
            }
#endif
        }
    }
}
