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
                FileInfo inf = new FileInfo(dialog.FileName);

                if (inf.Extension == "12asm")
                {
                    //TODO: Assemble file!

                    VM12Asm.VM12Asm.Main($"\"{inf.FullName}\" ", inf.Name, "-e", "-o");

                    inf = new FileInfo(Path.GetFileNameWithoutExtension(inf.FullName) + ".12exe");
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

                new Thread(vm12.Start).Start();
            }
        }
    }
}
