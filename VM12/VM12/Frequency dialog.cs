using System;
using System.Collections.Generic;
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
    public partial class Frequency_dialog<T> : Form where T : struct, IComparable
    {
        public delegate int EnumToInt(T e);

        Dictionary<T, int> internalFreq;
        int[] freqs;

        EnumToInt etoi;

        internal Frequency_dialog(int[] frequencies, string title, string column_name, EnumToInt etoi)
        {
            if (typeof(T).IsEnum == false)
            {
                throw new ArgumentException("T must be an enumerated type!");
            }
            
            InitializeComponent();
            freqs = frequencies;

            internalFreq = new Dictionary<T, int>(Enum.GetValues(typeof(T)).Length);

            this.etoi = etoi;

            Text = title;

            instructionFrequencyListView.Columns.Add(column_name);
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
            foreach (T eval in Enum.GetValues(typeof(T)))
            {
                internalFreq[eval] = freqs[etoi(eval)];
            }

            instructionFrequencyListView.Items.Clear();
            foreach (var kvp in internalFreq.Where(kvp => kvp.Value > 0).OrderByDescending(kvp => kvp.Value))
            {
                instructionFrequencyListView.Items.Add(kvp.Key.ToString()).SubItems.Add(kvp.Value.ToString());
            }
        }
    }
}
