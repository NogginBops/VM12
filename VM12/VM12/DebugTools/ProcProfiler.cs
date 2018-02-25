using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Profiler
{
    using VM12 = VM12.VM12;

    public partial class ProcProfiler : Form
    {
        private VM12 vm12;

        private Dictionary<VM12.ProcMetadata, int> instExecuted = new Dictionary<VM12.ProcMetadata, int>();

        private VM12.ProcMetadata currMetadata = null;

        private List<int> instructionConuts = new List<int>();

        public ProcProfiler()
        {
            InitializeComponent();
        }

        internal void SetVM(VM12 vm12)
        {
            this.vm12 = vm12;
            hotSpotView.SetVM(vm12);

            updateProcs();
        }

        private void profilerRefresh_Tick(object sender, EventArgs e)
        {
            hotSpotView.UpdateData();
            procMetadataView.SetProcMetadata(currMetadata);
        }

        private void updateProcs()
        {
            procSelector.Items.Clear();
            procSelector.Items.AddRange(vm12.metadata.ToArray());
            procSelector.SelectedIndex = 0;
        }

        private void refreshToolStripMenuItem_Click(object sender, EventArgs e)
        {
            profilerRefresh.Enabled = refreshToolStripMenuItem.Checked;
        }

        private void procSelector_SelectedIndexChanged(object sender, EventArgs e)
        {
            currMetadata = (VM12.ProcMetadata) procSelector.SelectedItem;
            hotSpotView.SetProc(currMetadata);
            procMetadataView.SetProcMetadata(currMetadata);
        }
    }
}
