using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using VM12;

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

            memViewRefreshTimer.Enabled = true;

            memStartAddress.ValueTextChanged += MemStartAddress_ValueTextChanged;
            memLength.ValueTextChanged += MemLength_ValueTextChanged;
            memEndAddress.ValueTextChanged += MemEndAddress_ValueTextChanged;
        }

        internal void SetVM12(VM12 vm12)
        {
            this.vm12 = vm12;
            memoryView.SetVM12(vm12);

            memStartAddress.ValueText = "0";
            memLength.ValueText = "100";
        }
        
        bool changingLength = false;
        bool changingEnd = false;

        private void MemStartAddress_ValueTextChanged(object sender, EventArgs e)
        {
            if (Utils.TryParseNumber(memStartAddress.ValueText, out int val)) 
            {
                startAddress = val > VM12.MEM_SIZE ? VM12.MEM_SIZE : val;

                if (startAddress + dataLength > VM12.MEM_SIZE)
                {
                    dataLength = VM12.MEM_SIZE - startAddress;

                    memLength.ValueText = $"{dataLength}";
                }

                memEndAddress.ValueText = $"0x{val + dataLength:X}";

                memoryView.SetStartAndLength(startAddress, dataLength);
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
                
                memoryView.SetLength(dataLength);
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
                
                memoryView.SetLength(dataLength);
            }

            changingEnd = false;
        }

        private void memViewRefreshTimer_Tick(object sender, EventArgs e)
        {
            memoryView.UpdateView();
        }

        private void refreshToolStripMenuItem_Click(object sender, EventArgs e)
        {
            memViewRefreshTimer.Enabled = refreshToolStripMenuItem.Checked;
        }
    }
}
