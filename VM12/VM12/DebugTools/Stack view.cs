using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Globalization;

namespace Debugging
{
    using System.Diagnostics;
    using System.IO;
    using System.Text.RegularExpressions;
    using VM12;
    using static VM12.VM12;
    using StackFrame = VM12.VM12.StackFrame;

    public partial class Stack_view : UserControl
    {
        private ProgramDebugger debugger;

        private VM12 vm12;
        
        private DataGridViewCellStyle stackFrameStyle = new DataGridViewCellStyle();
        private DataGridViewCellStyle stackLocalStyle = new DataGridViewCellStyle();
        private DataGridViewCellStyle localStyle = new DataGridViewCellStyle();
        private DataGridViewCellStyle SPStyle = new DataGridViewCellStyle();
        private DataGridViewCellStyle FPStyle = new DataGridViewCellStyle();

        private Dictionary<(string proc, int local), string> localsDict = new Dictionary<(string, int), string>();
        
        FileInfo debugDefinitions = new FileInfo("./Data/debug.df");
        
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
        
        public void LoadData()
        {
            if (vm12 != null)
            {
                debugDefinitions = new FileInfo(Path.Combine(vm12.sourceDir.FullName, "Data", "debug.df"));
            }

            if (debugDefinitions.Exists == false)
            {
                if (debugDefinitions.Directory.Exists == false)
                {
                    debugDefinitions.Directory.Create();
                }
                StreamWriter writer = debugDefinitions.CreateText();
                writer.Close();
            }
            else
            {
                // Open and parse file
                ParseDebugData(File.ReadAllLines(debugDefinitions.FullName));
            }
        }

        Regex command = new Regex("^\\[(\\S+?):(.+)\\]$");

        private void ParseDebugData(string[] lines)
        {
            string currentProc = null;
            foreach (string line in lines)
            {
                if (line.Length <= 0)
                {
                    continue;
                }

                if (line[0] == ':')
                {
                    currentProc = line;
                }
                else
                {
                    Match match = command.Match(line);
                    if (match.Success)
                    {
                        string command = match.Groups[1].Value;
                        string argument = match.Groups[2].Value;
                        switch (command)
                        {
                            case "local":
                                string[] values = argument.Split('|');
                                if (values.Length != 2)
                                {
                                    throw new ArgumentException("A local entry must consist of two values; localNum, name!");
                                }

                                int local = int.Parse(values[0]);

                                localsDict[(currentProc, local)] = values[1];
                                break;
                        }
                    }
                }
            }
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
            dgvCallStack.EndEdit();
            dgvStack.CurrentCell = null;
            dgvCallStack.ClearSelection();
            dgvCallStack.Rows.Clear();

            dgvStack.EndEdit();
            dgvStack.CurrentCell = null;
            dgvStack.ClearSelection();
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

        public void Close()
        {
            // Save definitions file
            StringBuilder sb = new StringBuilder(1000);
            foreach (var localDef in localsDict.GroupBy(kvp => kvp.Key.proc))
            {
                sb.AppendLine();
                sb.AppendLine(localDef.Key);
                foreach (var def in localDef)
                {
                    sb.AppendLine($"[local:{def.Key.local}|{def.Value}]");
                }
            }

            File.WriteAllText(debugDefinitions.FullName, sb.ToString());
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
                    dgvStack.Rows[dgvStack.RowCount - 1].Cells[0].ReadOnly = true;
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
                        
                        for (int i = 0; i < frame.locals; i++)
                        {
                            dgvStack.Rows[frame.FP - frame.locals + i].Cells[0].ReadOnly = false;

                            dgvStack.Rows[frame.FP - frame.locals + i].DefaultCellStyle = localStyle;
                            
                            if (localsDict.TryGetValue((frame.procName, i), out string name))
                            {
                                dgvStack.Rows[frame.FP - frame.locals + i].Cells[0].Value = name;
                            }
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
        
        private void dgvStack_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.ColumnIndex == 0)
            {
                // Check that this entry is a local value!
                bool isLocal = false;
                StackFrame frame = vm12.CurrentStackFrame;
                while (frame != null)
                {
                    if (e.RowIndex >= (frame.FP - frame.locals) && e.RowIndex < frame.FP)
                    {
                        isLocal = true;
                        break;
                    }

                    frame = frame.prev;
                }

                if (isLocal)
                {
                    if (dgvStack.Rows[e.RowIndex].Cells[e.ColumnIndex].FormattedValue.ToString() != e.FormattedValue.ToString())
                    {
                        localsDict[(frame.procName, e.RowIndex - (frame.FP - frame.locals))] = e.FormattedValue.ToString();
                        e.Cancel = false;
                    }
                }
                else if (dgvStack.Rows[e.RowIndex].Cells[e.ColumnIndex].FormattedValue.ToString() != e.FormattedValue.ToString())
                {
                    e.Cancel = true;
                }
            }
            else if (e.ColumnIndex == 1)
            {
                // Set the MEM value to this
                string cellValue = e.FormattedValue.ToString();
                if (cellValue.StartsWith("0x"))
                {
                    cellValue = cellValue.Substring(2);
                }

                if (Utils.TryParseHex(cellValue, out int val))
                {
                    if (vm12.MEM[e.RowIndex] != val)
                    {
                        vm12.MEM[e.RowIndex] = val;
                    }
                    e.Cancel = false;
                }
                else
                {
                    e.Cancel = true;
                }
            }
        }
    }
}
