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

        public Form1()
        {
            InitializeComponent();

            OpenFileDialog dialog = new OpenFileDialog();

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                byte[] bytes = File.ReadAllBytes(dialog.FileName);

                short[] rom = new short[(int)Math.Ceiling(bytes.Length / 2d)];

                Buffer.BlockCopy(bytes, 0, rom, 0, bytes.Length);

                vm12 = new VM12(rom);

                new Thread(vm12.Start).Start();
            }
        }
    }
}
