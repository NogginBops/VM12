using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VM12
{
    public unsafe partial class HeapView : Form
    {
        public struct Heap
        {
            public int* metadata;
            public int metadataSize;
            public int* heap;
            public int blockSize;
            public int heapSize;

            public Heap(int* metadata, int metadataSize, int* heap, int blockSize)
            {
                this.metadata = metadata;
                this.metadataSize = metadataSize;
                this.heap = heap;
                this.blockSize = blockSize;

                heapSize = (metadataSize / 2) * blockSize;
            }
        }

        private Heap heap;

        private Bitmap img;
        
        public HeapView(Heap heap)
        {
            InitializeComponent();

            this.heap = heap;

            heapViewRefreshTimer.Enabled = true;

            img = new Bitmap(1920, 1080, PixelFormat.Format24bppRgb);

            heapViewImg.Image = img;
            heapViewImg.Width = 100;
        }

        // TODO: Calculate heap regions before drawing

        // FIXME: Drawing performance is really bad

        const int min_horizontal_width = 4;

        private void RedrawImage()
        {
            Graphics g = Graphics.FromImage(img);
            
            g.FillRectangle(Brushes.White, 0, 0, heapViewImg.Width, heapViewImg.Height);

            int cells = (heap.metadataSize / 2);
            
            float vCount = 256;
            float hCount = 128;
            
            if (heapViewImg.Width >= heapViewImg.Height)
            {
                vCount = 128;
                hCount = 256;
            }

            // TODO: Scroll bars when view is too small

            float vSide = Math.Max(heapViewImg.Height / vCount, 4);

            float hSide = Math.Max(heapViewImg.Width / hCount, 4);
            
            // TODO: Better colors
            Color[] colors = { Color.Red, Color.Blue, Color.Green, Color.Cyan, Color.Magenta, Color.Yellow, Color.Black };

            Color GetColor(int index)
            {
                while (index >= colors.Length)
                {
                    index -= colors.Length;
                }

                while (index < 0)
                {
                    index += colors.Length;
                }

                return colors[index];
            }

            int occupied = 0;

            int region = 0;

            using (SolidBrush b = new SolidBrush(Color.Black))
            using (Pen p = new Pen(Color.Gray))
            {
                for (int c = 0; c < cells; c++)
                {
                    int offset = c * 2;

                    int data = heap.metadata[offset] << 12 | heap.metadata[offset + 1];
                    
                    if (data != 0)
                    {
                        if (data == 1)
                        {
                            region++;
                        }

                        occupied++;

                        int x = (int)(c % hCount);
                        int y = (int)(c / hCount);

                        b.Color = GetColor(region);
                        g.FillRectangle(b, x * hSide, y * vSide, hSide, vSide);
                    }
                }
                
                for (int y = 0; y < vCount; ++y)
                {
                    g.DrawLine(p, 0, y * vSide, heapViewImg.Width, y * vSide);
                }

                for (int x = 0; x < hCount; x++)
                {
                    g.DrawLine(p, x * hSide, 0, x * hSide, heapViewImg.Height);
                }
            }
        }

        private void heapViewImg_Resize(object sender, EventArgs e)
        {
            heapViewImg.Invalidate();
        }

        private void heapViewRefreshTimer_Tick(object sender, EventArgs e)
        {
            RedrawImage();
            heapViewImg.Invalidate();
        }

        private void refreshToolStripMenuItem_Click(object sender, EventArgs e)
        {
            heapViewRefreshTimer.Enabled = refreshToolStripMenuItem.Checked;
        }
    }
}
