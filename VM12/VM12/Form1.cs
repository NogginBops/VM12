using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;
using System.Windows.Forms;

namespace VM12
{
    public partial class Form1 : Form
    {
        VM12 vm12;

        ReadOnlyMemory read_mem;

        Bitmap bitmap = new Bitmap(VM12.SCREEN_WIDTH, VM12.SCREEN_HEIGHT);

        short[] vram = new short[Memory.VRAM_SIZE];

        public Form1()
        {
            InitializeComponent();

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

        private void panel1_Paint(object sender, PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;

            read_mem.GetVRAM(vram, 0);

            for (int x = 0; x < VM12.SCREEN_WIDTH; x++)
            {
                for (int y = 0; y < VM12.SCREEN_HEIGHT; y++)
                {
                    short val = read_mem[x + (y * VM12.SCREEN_WIDTH)];

                    int r = val & 0xF;
                    int g = (val >> 4) & 0xF;
                    int b = (val >> 8) & 0xF;

                    Color c = Color.FromArgb(r, g, b);

                    bitmap.SetPixel(x, y, c);
                }
            }

            graphics.DrawImage(bitmap, e.ClipRectangle);
        }

        private void refreshTimer_Tick(object sender, EventArgs e)
        {
            panel1.Invalidate();
        }
    }
}
