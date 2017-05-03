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

namespace VM12
{
    public partial class VM12Form : Form
    {
        volatile VM12 vm12;

        ReadOnlyMemory read_mem;

        Bitmap bitmap = new Bitmap(VM12.SCREEN_WIDTH, VM12.SCREEN_HEIGHT, PixelFormat.Format24bppRgb);

        byte[] data = new byte[VM12.SCREEN_WIDTH * 3 * VM12.SCREEN_HEIGHT];

        short[] vram = new short[Memory.VRAM_SIZE];

        public VM12Form()
        {
            InitializeComponent();

            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);

            Shown += (s, e1) => LoadProgram();
        }

        private void LoadProgram()
        {
            OpenFileDialog dialog = new OpenFileDialog();

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                FileInfo inf = new FileInfo(dialog.FileName);

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

                vm12 = new VM12(rom);

                read_mem = vm12.ReadMemory;

                Thread thread = new Thread(vm12.Start)
                {
                    IsBackground = true
                };

                thread.Start();
            }
        }
        
        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            if (vm12 != null)
            {
                Text = vm12.Stopped ? "Stopped" : "Running";
                Text += $" Instructions executed: {vm12.Ticks/1000000}m, PC: {vm12.ProgramCounter}, SP: {vm12.StackPointer}";
            }
            else
            {
                Text = "Uninitialized";
            }
            
            if (read_mem.HasMemory)
            {
                read_mem.GetVRAM(vram, 0);
                
                BitmapData bData = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.WriteOnly, bitmap.PixelFormat);

                byte bitsPerPixel = 24;

                int size = bData.Stride * bData.Height;

                Text += $", Size: {size}, Vram Size: {vram.Length}";

                System.Runtime.InteropServices.Marshal.Copy(bData.Scan0, data, 0, size);
                
                for (int i = 0; i < size; i += bitsPerPixel / 8)
                {
                    int index = i / (bitsPerPixel / 8);

                    short val = vram[index];

                    byte r = (byte)((val & 0x00F) * 16);
                    byte g = (byte)(((val >> 4) & 0x00F) * 16);
                    byte b = (byte)(((val >> 8) & 0x00F) * 16);

                    data[i] = r;
                    data[i + 1] = g;
                    data[i + 2] = b;
                }

                System.Runtime.InteropServices.Marshal.Copy(data, 0, bData.Scan0, data.Length);

                bitmap.UnlockBits(bData);
            }

            pbxMain.Image = bitmap;

            vm12?.Interrupt(new Interrupt(InterruptType.v_Blank, null));
        }

        private void VM12Form_FormClosing(object sender, FormClosingEventArgs e)
        {
            vm12?.Stop();
        }

        private void instructionFrequencyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (vm12 != null)
            {
                Instruction_frequency ifreq = new Instruction_frequency(vm12.instructionFreq);

                ifreq.ShowDialog();
            }
        }
    }
}
