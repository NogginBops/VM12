using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;
using System.Windows.Forms;

namespace VM12
{
    public partial class VM12Form : Form
    {
        VM12 vm12;

        ReadOnlyMemory read_mem;

        Bitmap bitmap = new Bitmap(VM12.SCREEN_WIDTH, VM12.SCREEN_HEIGHT, PixelFormat.Format24bppRgb);

        byte[] data = new byte[VM12.SCREEN_WIDTH * 3 * VM12.SCREEN_HEIGHT];

        short[] vram = new short[Memory.VRAM_SIZE];

        public VM12Form()
        {
            InitializeComponent();

            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);

            OpenFileDialog dialog = new OpenFileDialog();

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                FileInfo inf = new FileInfo(dialog.FileName);

                if (inf.Extension == ".12asm")
                {
                    VM12Asm.VM12Asm.Main(inf.FullName, Path.GetFileNameWithoutExtension(inf.Name), "-e", "-o");

                    inf = new FileInfo(Path.Combine(inf.DirectoryName, Path.GetFileNameWithoutExtension(inf.FullName) + ".12exe"));
                }

                short[] rom = new short[(int)Math.Ceiling(inf.Length / 2d)];

                using (BinaryReader br = new BinaryReader(File.OpenRead(dialog.FileName)))
                {
                    for (int i = 0; i < rom.Length; i++)
                    {
                         rom[i] = br.ReadInt16();
                    }
                }
                
                vm12 = new VM12(rom);

                read_mem = vm12.ReadMemory;

                Thread thread = new Thread(vm12.Start);
                thread.IsBackground = true;

                thread.Start();
            }
        }
        
        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            if (read_mem.HasMemory)
            {
                read_mem.GetVRAM(vram, 0);
                
                BitmapData bData = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadWrite, bitmap.PixelFormat);

                byte bitsPerPixel = 24;

                int size = bData.Stride * bData.Height;
                
                System.Runtime.InteropServices.Marshal.Copy(bData.Scan0, data, 0, size);

                for (int i = 0; i < size; i += bitsPerPixel / 8)
                {
                    int index = i / bitsPerPixel;

                    short val = vram[index];

                    byte r = (byte)((val & 0xF) * 16);
                    byte g = (byte)(((val >> 4) & 0xF) * 16);
                    byte b = (byte)(((val >> 8) & 0xF) * 16);

                    data[i] = r;
                    data[i + 1] = g;
                    data[i + 2] = b;
                }

                System.Runtime.InteropServices.Marshal.Copy(data, 0, bData.Scan0, data.Length);

                bitmap.UnlockBits(bData);
            }

            pictureBox1.Image = bitmap;

            vm12?.Interrupt(new Interrupt(InterruptType.v_Blank, null));
        }
    }
}
