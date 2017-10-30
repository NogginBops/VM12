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
        
        private DataGridViewCellStyle stackFrameStyle = new DataGridViewCellStyle();
        private DataGridViewCellStyle stackLocalStyle = new DataGridViewCellStyle();
        private DataGridViewCellStyle localStyle = new DataGridViewCellStyle();
        private DataGridViewCellStyle SPStyle = new DataGridViewCellStyle();
        private DataGridViewCellStyle FPStyle = new DataGridViewCellStyle();

        public Stack_view()
        {
            InitializeComponent();
            dgvCallStack.ReadOnly = true;

            stackFrameStyle.BackColor = Color.FromArgb(247, 215, 160);
            stackLocalStyle.BackColor = Color.FromArgb(247, 215, 160);
            localStyle.BackColor = Color.FromArgb(199, 219, 226);
            SPStyle.BackColor = Color.FromArgb(222, 199, 226);
            FPStyle.BackColor = Color.FromArgb(226, 181, 195);
        }

        internal void SetDebugger(ProgramDebugger debugger)
        {
            this.debugger = debugger;
        }

        internal void SetVM(VM12 vm12)
        {
            this.vm12 = vm12;
        }

        public void ClearStack()
        {
            dgvCallStack.Rows.Clear();
            dgvStack.Rows.Clear();
        }

        public void UpdateStack()
        {
            if (vm12 != null)
            {
                StackFrame frame = vm12.CurrentStackFrame;
                GenerateStackTrace(frame);
                GenerateStack(frame);
            }
        }

        private void GenerateStackTrace(StackFrame frame)
        {
#if DEBUG
            if (vm12 != null)
            {
                dgvCallStack.Rows.Clear();
                
                while(frame != null)
                {
                    int index = dgvCallStack.Rows.Add(new[] { frame.procName, $"{frame.file}:{frame.line}" });
                    
                    frame = frame.prev;
                }
            }
#endif
        }

        private void GenerateStack(StackFrame frame)
        {
#if DEBUG
            if (vm12 != null)
            {
                dgvStack.Rows.Clear();

                for (int i = 0; i <= vm12.StackPointer; i++)
                {
                    dgvStack.Rows.Add(new[] { $"0x{i:X}", $"0x{vm12.MEM[i]:X}" });
                }
                
                while (frame != null)
                {
                    if (frame.FP >= 0)
                    {
                        dgvStack.Rows[frame.FP].DefaultCellStyle = stackFrameStyle;
                        dgvStack.Rows[frame.FP + 1].DefaultCellStyle = stackFrameStyle;
                        dgvStack.Rows[frame.FP + 2].DefaultCellStyle = stackFrameStyle;
                        dgvStack.Rows[frame.FP + 3].DefaultCellStyle = stackFrameStyle;
                        dgvStack.Rows[frame.FP + 4].DefaultCellStyle = stackLocalStyle;
                        dgvStack.Rows[frame.FP + 5].DefaultCellStyle = stackLocalStyle;

                        for (int i = 0; i <= frame.locals; i++)
                        {
                            dgvStack.Rows[frame.FP - i].DefaultCellStyle = localStyle;
                        }
                    }

                    frame = frame.prev;
                }

                dgvStack.Rows[vm12.FramePointer].DefaultCellStyle = FPStyle;
                //dgvStack.Rows[vm12.StackPointer].DefaultCellStyle = SPStyle;
                dgvStack.FirstDisplayedScrollingRowIndex = dgvStack.RowCount - 1;
            }
#endif
        }

        private void dgvStack_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
#if DEBUG
            if (vm12 != null)
            {
                string[] data = ((string) dgvCallStack.Rows[e.RowIndex].Cells[1].Value).Split(':');

                int line = int.Parse(data[1]);

                debugger.SourceView.Open(Path.Combine(vm12.sourceDir.FullName, data[0]), line);
            }
#endif
        }

        private void dgvStack_CellParsing(object sender, DataGridViewCellParsingEventArgs e)
        {
            // Set the MEM value to this
            
        }
    }
}
