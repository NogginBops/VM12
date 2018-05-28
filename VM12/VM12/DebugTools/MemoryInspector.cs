using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Debugging
{
    using VM12 = VM12.VM12;

    public partial class MemoryInspector : Form
    {
        private VM12 vm12;

        private int startAddress;
        private int dataLength;
        
        public MemoryInspector()
        {
            InitializeComponent();
            
            memStartAddress.ValueTextChanged += MemStartAddress_ValueTextChanged;
            memLength.ValueTextChanged += MemLength_ValueTextChanged;
            memEndAddress.ValueTextChanged += MemEndAddress_ValueTextChanged;
        }

        internal void SetVM12(VM12 vm12)
        {
            this.vm12 = vm12;
        }

        public void UpdateData()
        {
            int[] data = new int[dataLength];

            Array.Copy(vm12.MEM, startAddress, data, 0, dataLength);

            memoryView1.setData(data.SelectMany(i => new byte[] { (byte)(i >> 8), (byte)(i & 0xFF) }).ToArray());
        }

        bool changingLength = false;
        bool changingEnd = false;

        private void MemStartAddress_ValueTextChanged(object sender, EventArgs e)
        {
            if (int.TryParse(memStartAddress.ValueText, out int val))
            {
                startAddress = val > VM12.MEM_SIZE ? VM12.MEM_SIZE : val;

                if (startAddress + dataLength > VM12.MEM_SIZE)
                {
                    dataLength = VM12.MEM_SIZE - startAddress;

                    memLength.ValueText = $"{dataLength}";
                }

                memEndAddress.ValueText = $"0x{val + dataLength:X}";

                UpdateData();
            }
        }

        private void MemLength_ValueTextChanged(object sender, EventArgs e)
        {
            if (changingEnd) return;

            changingLength = true;

            if (int.TryParse(memLength.ValueText, out int val))
            {
                if (startAddress + val > VM12.MEM_SIZE)
                {
                    val = VM12.MEM_SIZE - startAddress;

                    memLength.ValueText = $"{val}";
                }

                dataLength = val;

                memEndAddress.ValueText = $"0x{val + startAddress}";

                UpdateData();
            }

            changingLength = false;
        }

        private void MemEndAddress_ValueTextChanged(object sender, EventArgs e)
        {
            if (changingLength) return;

            changingEnd = true;

            if (int.TryParse(memEndAddress.ValueText, out int val))
            {
                if (val > VM12.MEM_SIZE)
                {
                    val = VM12.MEM_SIZE;

                    memEndAddress.ValueText = $"{val}";
                }

                if (val - startAddress >= 0)
                {
                    dataLength = val - startAddress;
                }
                else
                {
                    // TODO: Error
                }

                memLength.ValueText = $"{dataLength}";

                UpdateData();
            }

            changingEnd = false;
        }
    }
}
