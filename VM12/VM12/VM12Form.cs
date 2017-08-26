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

namespace VM12
{
    public partial class VM12Form : Form
    {
        volatile VM12 vm12;

        Memory read_mem;

        Bitmap bitmap = new Bitmap(VM12.SCREEN_WIDTH, VM12.SCREEN_HEIGHT, PixelFormat.Format24bppRgb);

        byte[] data = new byte[VM12.SCREEN_WIDTH * 3 * VM12.SCREEN_HEIGHT];

        int[] vram = new int[Memory.VRAM_SIZE];
        
        public VM12Form()
        {
            InitializeComponent();

            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);

            Shown += (s, e1) => LoadProgram();

#if !DEBUG
            MainMenuStrip.Items.RemoveAt(1);
#endif
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
                });
            }
        }

        long lastIntsructionCount = -1;
        double maxInstructionsPerSecond = 60000000000;
        double utilization = 0;

        int timer = 0;

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            if (vm12 != null && vm12.Running)
            {
                timer += refreshTimer.Interval;

                if (timer > 50)
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
                }

                Text = vm12.Stopped ? "Stopped" : "Running";
                Text += $" Instructions executed: {vm12.Ticks/1000000}m, Utilization: {utilization:P}, Interrupts: {vm12.InterruptCount}, Missed: {vm12.MissedInterrupts}, FP: {vm12.FPWatermark}, SP: {vm12.SPWatermark}";
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

        //TODO: Fix more resonable keybindings and more info about releases and presses of buttons
        static Dictionary<Keys, short> keycode_transformations = new Dictionary<Keys, short>()
        {
            { Keys.A, 0 },
            { Keys.B, 1 },
            { Keys.C, 2 },
            { Keys.D, 3 },
            { Keys.E, 4 },
            { Keys.F, 5 },
            { Keys.G, 6 },
            { Keys.H, 7 },
            { Keys.I, 8 },
            { Keys.J, 9 },
            { Keys.K, 10 },
            { Keys.L, 11 },
            { Keys.M, 12 },
            { Keys.N, 13 },
            { Keys.O, 14 },
            { Keys.P, 15 },
            { Keys.Q, 16 },
            { Keys.R, 17 },
            { Keys.S, 18 },
            { Keys.T, 19 },
            { Keys.U, 20 },
            { Keys.V, 21 },
            { Keys.W, 22 },
            { Keys.X, 23 },
            { Keys.Y, 24 },
            { Keys.Z, 25 },

            { Keys.D0, 26 },
            { Keys.D1, 27 },
            { Keys.D2, 28 },
            { Keys.D3, 29 },
            { Keys.D4, 30 },
            { Keys.D5, 31 },
            { Keys.D6, 32 },
            { Keys.D7, 33 },
            { Keys.D8, 34 },
            { Keys.D9, 35 },
            
            { Keys.D1 | Keys.Shift, 36 }, // !
            { Keys.Oemplus | Keys.Shift, 37 }, // ?
            { Keys.OemPeriod | Keys.Shift, 38 }, // :
            { Keys.Oemcomma | Keys.Shift, 39 }, // ;
            { Keys.OemPeriod, 40 }, // .
            { Keys.Oemcomma, 41 }, // ,
            { Keys.D8 | Keys.Shift, 42 }, // (
            { Keys.D9 | Keys.Shift, 43 }, // )
            { Keys.Oemplus, 44 }, // +
            { Keys.OemMinus, 45 }, // -
            { Keys.D7 | Keys.Shift, 46 }, // /
            { Keys.D2 | Keys.Shift, 47 }, // "
            { Keys.OemMinus | Keys.Shift, 48 }, // _
            { Keys.Space, 49 }
        };

        private void VM12Form_KeyDown(object sender, KeyEventArgs e)
        {
            if (keycode_transformations.TryGetValue(e.KeyCode | ModifierKeys, out short code))
            {
                vm12?.Interrupt(new Interrupt(InterruptType.keyboard, new[] { code }));
            }
        }

        private void VM12Form_KeyUp(object sender, KeyEventArgs e)
        {
            if (keycode_transformations.TryGetValue(e.KeyCode | ModifierKeys, out short code))
            {
                vm12?.Interrupt(new Interrupt(InterruptType.keyboard, new[] { code }));
            }
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

        private void pbxMain_MouseMove(object sender, MouseEventArgs e)
        {
            vm12?.Interrupt(new Interrupt(InterruptType.mouse, new short[] { (short)(e.X), (short)(e.Y) }));

            lastPosX = e.X;
            lastPosY = e.Y;
        }

        private void pbxMain_MouseEnter(object sender, EventArgs e)
        {
            Cursor.Hide();
        }

        private void pbxMain_MouseLeave(object sender, EventArgs e)
        {
            Cursor.Show();
        }

        private void hTimer_Tick(object sender, EventArgs e)
        {
            vm12?.Interrupt(new Interrupt(InterruptType.h_Timer, new short[] { (short) hTimer.Interval }));
        }
    }
}
