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
        
        public HeapView(Heap heap)
        {
            InitializeComponent();

            this.heap = heap;

            heapViewRefreshTimer.Enabled = true;
        }

        // TODO: Calculate heap regions before drawing

        // FIXME: Drawing performance is really bad
        
        private void heapViewImg_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;

            int cells = (heap.metadataSize / 2);

            float mArea = (float)(heapViewImg.Width * heapViewImg.Height) / cells;

            float mSide = (float)Math.Sqrt(mArea);

            float vCount = (float)Math.Ceiling((heapViewImg.Height / mSide));

            float vSide = heapViewImg.Height / vCount;

            float hCount = (float)Math.Ceiling((cells / vCount));

            float hSide = heapViewImg.Width / hCount;
            
            Random rand = new Random();
            
            Color color = Color.FromArgb(255, rand.Next(256), rand.Next(256), rand.Next(256));

            int occupied = 0;
            
            using (SolidBrush b = new SolidBrush(Color.Black))
            using (Pen p = new Pen(Color.Gray))
            {
                for (int y = 0; y < vCount; y++)
                {
                    for (int x = 0; x < hCount; x++)
                    {
                        int offset = ((int)(x + (y * hCount)) * 2);

                        if (offset > heap.metadataSize)
                        {
                            continue;
                            throw new ArgumentException();
                        }
                        
                        int data = heap.metadata[offset] << 12 | heap.metadata[offset + 1];

                        if (data == 0)
                        {
                            //b.Color = Color.LightGray;
                        }
                        else
                        {
                            occupied++;

                            if (data == 1)
                            {
                                //color = Color.FromArgb(255, rand.Next(256), rand.Next(256), rand.Next(256));
                                //b.Color = color;
                            }

                            g.FillRectangle(b, x * hSide, y * vSide, hSide, vSide);
                        }
                        
                        //g.DrawRectangle(p, (float)(x * hSide), (float)(y * vSide), (float)hSide, (float)vSide);
                    }
                }

                for (int y = 0; y < vCount; ++y)
                {
                    g.DrawLine(p, 0, y * hSide, hCount * hSide, y * vSide);
                }

                for (int x = 0; x < hCount; x++)
                {
                    g.DrawLine(p, x * hSide, 0, x * hSide, hCount * vSide);
                }
            }

            g.DrawString($"Occupied: {occupied}", Font, Brushes.Black, 10, 10);
        }

        private void heapViewImg_Resize(object sender, EventArgs e)
        {
            heapViewImg.Invalidate();
        }

        private void heapViewRefreshTimer_Tick(object sender, EventArgs e)
        {
            heapViewImg.Invalidate();
        }

        private void refreshToolStripMenuItem_Click(object sender, EventArgs e)
        {
            heapViewRefreshTimer.Enabled = refreshToolStripMenuItem.Checked;
        }
    }
}
