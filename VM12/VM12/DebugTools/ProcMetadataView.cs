using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VM12.DebugTools
{
    public partial class ProcMetadataView : UserControl
    {
#if DEBUG
        private VM12.ProcMetadata metadata;

        public ProcMetadataView()
        {
            InitializeComponent();
        }

        internal void SetProcMetadata(VM12.ProcMetadata metadata)
        {
            this.metadata = metadata;

            dgvProcMetadata.Rows.Clear();

            if (metadata != null)
            {
                procName.Text = metadata.name;

                dgvProcMetadata.Rows.Add("Source", $"{metadata.file}:{metadata.sourceLine}");
                dgvProcMetadata.Rows.Add("Address", $"{metadata.location:X}");
                dgvProcMetadata.Rows.Add("Size", $"{metadata.size}");
            }
            else
            {
                procName.Text = "N/A";
            }
        }
#endif
    }
}
