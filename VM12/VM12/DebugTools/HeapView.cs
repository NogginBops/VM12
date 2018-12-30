using Debugging;
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
        internal struct Heap
        {
            public VM12 vm12;
            public int metadata_address;
            public int* metadata;
            public int metadataSize;
            public int heap_address;
            public int* heap;
            public int blockSize;
            public int heapSize;

            internal Heap(VM12 vm12, int metadata_address, int* metadata, int metadataSize, int heap_address, int* heap, int blockSize)
            {
                this.vm12 = vm12;
                this.metadata_address = metadata_address;
                this.metadata = metadata;
                this.metadataSize = metadataSize;
                this.heap_address = heap_address;
                this.heap = heap;
                this.blockSize = blockSize;

                this.heapSize = (metadataSize / 2) * blockSize;
            }
        }

        private Heap heap;

        private volatile Bitmap img;
        
        internal HeapView(Heap heap)
        {
            InitializeComponent();

            this.heap = heap;

            heapViewRefreshTimer.Enabled = true;

            img = new Bitmap(heapViewImg.Width * 2, heapViewImg.Height * 2, PixelFormat.Format24bppRgb);

            heapViewImg.Image = img;
            //heapViewImg.Width = 100;

            //heapViewImg.Dock = DockStyle.Fill;
            heapViewImg.SizeMode = PictureBoxSizeMode.StretchImage;

            RedrawImage();
        }

        // TODO: Calculate heap regions before drawing
        
        const int min_horizontal_width = 4;

        int[] prevHeap = new int[0];

        private bool HeapChanged()
        {
            List<int> currHeap = new List<int>(prevHeap.Length);

            int cells = heap.metadataSize / 2;

            for (int offset = 0; offset < heap.metadataSize; offset++)
            {
                int data = heap.metadata[offset] << 12 | heap.metadata[offset + 1];

                if (data != 0)
                {
                    if (data == 1)
                    {
                        currHeap.Add(1);
                    }
                    else
                    {
                        currHeap[currHeap.Count - 1]++;
                    }
                }
            }

            int[] newHeap = currHeap.ToArray();

            if (prevHeap.SequenceEqual(newHeap))
            {
                prevHeap = newHeap;
                return false;
            }
            else
            {
                prevHeap = newHeap;
                return true;
            }
        }

        private (float vCount, float hCount) GetCellLayout()
        {
            float vCount = 256;
            float hCount = 128;

            if (img.Width >= img.Height)
            {
                vCount = 128;
                hCount = 256;
            }

            return (vCount, hCount);
        }

        private (float width, float height) CalcCellSize(int width, int height)
        {
            // TODO: Calc this with the number of cells to display!
            int cells = (heap.metadataSize / 2);

            var (vCount, hCount) = GetCellLayout();
            
            // TODO: Scroll bars when view is too small
            float hSide = width / hCount;
            float vSide = height / vCount;

            return (hSide, vSide);
        }

        private void RedrawImage()
        {
            Graphics g = Graphics.FromImage(img);
            
            g.FillRectangle(Brushes.White, 0, 0, img.Width, img.Height);

            int cells = (heap.metadataSize / 2);
            var (vCount, hCount) = GetCellLayout();
            var (hSide, vSide) = CalcCellSize(img.Width, img.Height);

            Console.WriteLine($"vSide: {vSide}, hSide: {hSide}, width: {img.Width}, height: {img.Height}");

            // TODO: Better colors
            Color[] colors = { Color.Red, Color.Blue, Color.Green, Color.Cyan, Color.Magenta, Color.DarkGoldenrod, Color.Black };

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
                    g.DrawLine(p, 0, y * vSide, img.Width, y * vSide);
                }

                for (int x = 0; x < hCount; x++)
                {
                    //if (x * hSide > img.Width) System.Diagnostics.Debugger.Break();
                    g.DrawLine(p, x * hSide, 0, x * hSide, img.Height);
                }
            }
        }

        private void heapViewImg_Resize(object sender, EventArgs e)
        {
            heapViewImg.Invalidate();
        }

        private void heapViewRefreshTimer_Tick(object sender, EventArgs e)
        {
            if (HeapChanged())
            {
                RedrawImage();
                heapViewImg.Invalidate();
            }
        }

        private void refreshToolStripMenuItem_Click(object sender, EventArgs e)
        {
            heapViewRefreshTimer.Enabled = refreshToolStripMenuItem.Checked;
        }

        MemoryInspector inspector = new MemoryInspector();
        private void heapViewImg_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            var (vCount, hCount) = GetCellLayout();
            var (hSide, vSide) = CalcCellSize(heapViewImg.Width, heapViewImg.Height);

            int cell_x = (int)(e.X / hSide);
            int cell_y = (int)(e.Y / vSide);

            int index = cell_x + (int)(cell_y * hCount);
            
            int address = heap.heap_address + (index * heap.blockSize);

            Console.WriteLine($"X: {cell_x}, Y: {cell_y}, Index: {index}, Address: {address} vSide: {vSide}, hSide: {hSide}, Location: {e.Location}");

            // TODO: Get the start address of the allocation!
            // TODO: Figure out the length of the allocation!

            if (inspector.IsDisposed)
                inspector = new MemoryInspector();

            inspector.SetVM12(heap.vm12, address, heap.blockSize);
            inspector.Show();
        }
    }
}
