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

namespace VM12
{
    public partial class VM12Form : Form
    {
        volatile VM12 vm12;

        Memory read_mem;

        Bitmap bitmap = new Bitmap(VM12.SCREEN_WIDTH, VM12.SCREEN_HEIGHT, PixelFormat.Format24bppRgb);

        byte[] data = new byte[VM12.SCREEN_WIDTH * 3 * VM12.SCREEN_HEIGHT];

        int[] vram = new int[Memory.VRAM_SIZE];

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

        public VM12Form()
        {
            InitializeComponent();
            
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);

            Shown += (s, e1) => LoadProgram();

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
                        perfTimer.Dispose();
                    }

#if !DEBUG
                    vm12 = new VM12(rom);
#elif DEBUG
                    FileInfo metadataFile = new FileInfo(Path.Combine(inf.DirectoryName, Path.GetFileNameWithoutExtension(inf.FullName) + ".12meta"));

                    vm12 = new VM12(rom, metadataFile);

                    
#endif

                    read_mem = vm12.ReadMemory;

                    Thread thread = new Thread(vm12.Start)
                    {
                        IsBackground = true
                    };

                    thread.Name = "VM12";
                    thread.Start();

                    perfTimer = new System.Threading.Timer(MeasurePerf, null, 0, 1);
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
            
            if (read_mem != null)
            {
                read_mem.GetVRAM(vram, 0);
                
                BitmapData bData = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.WriteOnly, bitmap.PixelFormat);

                const byte bitsPerPixel = 24;
                const byte bytesPerPixel = bitsPerPixel / 8;

                int size = bData.Stride * bData.Height;
                
                System.Runtime.InteropServices.Marshal.Copy(bData.Scan0, data, 0, size);
                
                for (int i = 0; i < size; i += bytesPerPixel)
                {
                    int index = i / (bytesPerPixel);

                    int val = vram[index];

                    byte r = (byte)((val & 0x00F) << 4);
                    byte g = (byte)(((val >> 4) & 0x00F) << 4);
                    byte b = (byte)(((val >> 8) & 0x00F) << 4);

                    data[i] = r;
                    data[i + 1] = g;
                    data[i + 2] = b;
                }

                System.Runtime.InteropServices.Marshal.Copy(data, 0, bData.Scan0, data.Length);

                bitmap.UnlockBits(bData);
            }

            pbxMain.Image = bitmap;

            vm12?.Interrupt(new Interrupt(InterruptType.v_Blank, new short[0]));
        }

        private void VM12Form_FormClosing(object sender, FormClosingEventArgs e)
        {
            vm12?.Stop();
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
            
            vm12?.Interrupt(new Interrupt(InterruptType.keyboard, new[] { (short) 1, (short) e.KeyCode }));
        }

        private void VM12Form_KeyUp(object sender, KeyEventArgs e)
        {
            vm12?.Interrupt(new Interrupt(InterruptType.keyboard, new[] { (short) 0, (short) e.KeyCode }));
        }

        private void stopToolStripMenuItem_Click(object sender, EventArgs e)
        {
            vm12?.Stop();

            while (vm12.Running);

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

            vm12?.Interrupt(new Interrupt(InterruptType.mouse, new short[] { (short)(x), (short)(y), (short)currentButtons }));

            lastPosX = x;
            lastPosY = y;
        }

        private void pbxMain_MouseDown(object sender, MouseEventArgs e)
        {
            int x = map(e.X, 0, pbxMain.Width, 0, VM12.SCREEN_WIDTH);
            int y = map(e.Y, 0, pbxMain.Height, 0, VM12.SCREEN_HEIGHT);

            PressButtons(e.Button);
            vm12?.Interrupt(new Interrupt(InterruptType.mouse, new short[] { (short)(x), (short)(y), (short)currentButtons }));
        }

        private void pbxMain_MouseUp(object sender, MouseEventArgs e)
        {
            int x = map(e.X, 0, pbxMain.Width, 0, VM12.SCREEN_WIDTH);
            int y = map(e.Y, 0, pbxMain.Height, 0, VM12.SCREEN_HEIGHT);

            ReleaseButtons(e.Button);
            vm12?.Interrupt(new Interrupt(InterruptType.mouse, new short[] { (short)(x), (short)(y), (short)currentButtons }));
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

        private void hTimer_Tick(object sender, EventArgs e)
        {
            vm12?.Interrupt(new Interrupt(InterruptType.h_Timer, new short[] { (short) hTimer.Interval }));
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
    }
}
