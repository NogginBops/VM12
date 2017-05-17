using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using VM12_Opcode;

namespace VM12
{
    public partial class Instruction_frequency : Form
    {
        ConcurrentDictionary<Opcode, int> freqs;

        internal Instruction_frequency(ConcurrentDictionary<Opcode, int> frequencies)
        {
            freqs = frequencies;
            InitializeComponent();

            instructionFrequencyListView.Columns.Add("Opcode");
            instructionFrequencyListView.Columns.Add("x Times");
        }

        private void Instruction_frequency_Load(object sender, EventArgs e)
        {
            UpdateList();
        }

        private void refreshToolStripMenuItem_Click(object sender, EventArgs e)
        {
            refreshInstructionFreqTimer.Enabled = refreshToolStripMenuItem.Checked;
        }

        private void refreshInstructionFreqTimer_Tick(object sender, EventArgs e)
        {
            UpdateList();
        }

        private void UpdateList()
        {
            instructionFrequencyListView.Items.Clear();
            foreach (var kvp in freqs.OrderByDescending(kvp => kvp.Value))
            {
                instructionFrequencyListView.Items.Add(kvp.Key.ToString()).SubItems.Add(kvp.Value.ToString());
            }
        }
    }
}
