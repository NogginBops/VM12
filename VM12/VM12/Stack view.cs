using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Debugging
{
    using System.IO;
    using VM12;
    using static VM12.VM12;

    public partial class Stack_view : UserControl
    {
        private ProgramDebugger debugger;

        private VM12 vm12;

        public Stack_view()
        {
            InitializeComponent();
            dgvStack.ReadOnly = true;
        }

        internal void SetDebugger(ProgramDebugger debugger)
        {
            this.debugger = debugger;
        }

        internal void SetVM(VM12 vm12)
        {
            this.vm12 = vm12;

            GenerateStackTrace();
        }

        public void UpdateStack()
        {
            GenerateStackTrace();
        }

        private void GenerateStackTrace()
        {
#if DEBUG
            if (vm12 != null)
            {
                dgvStack.Rows.Clear();
                StackFrame frame = vm12.CurrentStackFrame;
                
                while(frame != null)
                {
                    int index = dgvStack.Rows.Add(new[] { frame.procName, $"{frame.file}:{frame.line}" });
                    
                    frame = frame.prev;
                }
            }
#endif
        }

        private void dgvStack_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
#if DEBUG
            if (vm12 != null)
            {
                string[] data = ((string) dgvStack.Rows[e.RowIndex].Cells[1].Value).Split(':');

                int line = int.Parse(data[1]);

                debugger.SourceView.Open(Path.Combine(vm12.sourceDir.FullName, data[0]), line);
            }
#endif
        }
    }
}
