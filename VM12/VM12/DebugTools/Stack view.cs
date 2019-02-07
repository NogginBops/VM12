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
    using System.Runtime.CompilerServices;
    using System.Text.RegularExpressions;
    using VM12;

#if DEBUG
    using static VM12.VM12;
    using StackFrame = VM12.VM12.StackFrame;
#endif

    public partial class Stack_view : UserControl
    {
        private ProgramDebugger debugger;

        private VM12 vm12;

        private DataGridViewCellStyle stackFrameBeginStyle = new DataGridViewCellStyle();
        private DataGridViewCellStyle stackFrameStyle = new DataGridViewCellStyle();
        private DataGridViewCellStyle stackLocalStyle = new DataGridViewCellStyle();
        private DataGridViewCellStyle localStyle = new DataGridViewCellStyle();
        private DataGridViewCellStyle SPStyle = new DataGridViewCellStyle();
        private DataGridViewCellStyle FPStyle = new DataGridViewCellStyle();

        private Dictionary<(string proc, int local), string> generatedDebugInfo = new Dictionary<(string, int), string>();

        private Dictionary<(string proc, int local), string> userDebugInfo = new Dictionary<(string, int), string>();
        
        private Dictionary<(string proc, int local), string> localsDebugInfo = new Dictionary<(string, int), string>();

        private List<DirectoryInfo> debugDefinitionDirectories = new List<DirectoryInfo> { new DirectoryInfo("./Data") };

        private FileInfo editorDefinitions = new FileInfo("./Data/debug.df");

        public Stack_view()
        {
            InitializeComponent();
            dgvCallStack.ReadOnly = true;

            stackFrameBeginStyle.BackColor = Color.FromArgb(247, 195, 140);
            stackFrameStyle.BackColor = Color.FromArgb(247, 215, 160);
            stackLocalStyle.BackColor = Color.FromArgb(247, 215, 160);
            localStyle.BackColor = Color.FromArgb(199, 219, 226);
            SPStyle.BackColor = Color.FromArgb(222, 199, 226);
            FPStyle.BackColor = Color.FromArgb(226, 181, 195);
        }
        
        public void LoadData()
        {
#if DEBUG
            if (vm12 != null)
            {
                // We start working out of the VM folder
                debugDefinitionDirectories.Add(new DirectoryInfo(Path.Combine(vm12.sourceDir.FullName, "Data")));
                editorDefinitions = new FileInfo(Path.Combine(vm12.sourceDir.FullName, "Data", "debug.df"));
            }

            if (editorDefinitions.Exists == false)
            {
                if (editorDefinitions.Directory.Exists == false)
                {
                    editorDefinitions.Directory.Create();
                }

                using (editorDefinitions.Create())
                {
                    // There is no need to load anything because we just created the file
                }
            }
            else
            {
                generatedDebugInfo.Clear();
                userDebugInfo.Clear();
                localsDebugInfo.Clear();

                foreach (var dir in debugDefinitionDirectories)
                {
                    if (dir.Exists)
                    {
                        foreach (var debugDefFile in dir.EnumerateFiles("*.df"))
                        {
                            // NOTE: Should we do any smart merging here?

                            ParseDebugData(File.ReadAllLines(debugDefFile.FullName), generatedDebugInfo);
                        }
                    }
                }

                // Open and parse file
                ParseDebugData(File.ReadAllLines(editorDefinitions.FullName), userDebugInfo);

                foreach (var gDebug in generatedDebugInfo)
                {
                    localsDebugInfo[gDebug.Key] = gDebug.Value;
                }

                // Load the user definitions after the genereted ones as user defs have priority
                foreach (var uDebug in userDebugInfo)
                {
                    localsDebugInfo[uDebug.Key] = uDebug.Value;
                }
            }
#endif
        }

        Regex command = new Regex("^\\[(\\S+?):(.+)\\]$");

        private void ParseDebugData(string[] lines, Dictionary<(string proc, int local), string> localsDict)
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
            LoadData();
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
#if DEBUG
            if (vm12 != null)
            {
                StackFrame frame = vm12.CurrentStackFrame;
                GenerateStackTrace(frame);
                GenerateStack(frame);
            }
#endif
        }

        public void Close()
        {
            // Save definitions file
            StringBuilder sb = new StringBuilder(1000);
            foreach (var userDef in userDebugInfo.GroupBy(kvp => kvp.Key.proc))
            {
                sb.AppendLine();
                sb.AppendLine(userDef.Key);
                foreach (var def in userDef)
                {
                    sb.AppendLine($"[local:{def.Key.local}|{def.Value}]");
                }
            }
            
            File.WriteAllText(editorDefinitions.FullName, sb.ToString());
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
                    // NOTE: This can probably be done a lot more efficient
                    dgvStack.Rows.Add(new[] { $"0x{i:X}", $"0x{vm12.MEM[i]:X}" });
                    dgvStack.Rows[dgvStack.RowCount - 1].Cells[0].ReadOnly = true;
                }
                
                while (frame != null)
                {
                    if (frame.FP + 5 >= dgvStack.RowCount)
                    {
                        break;
                    }

                    if (frame.FP >= 0 && frame.FP < dgvStack.RowCount)
                    {
                        dgvStack.Rows[frame.FP].DefaultCellStyle = stackFrameBeginStyle;
                        dgvStack.Rows[frame.FP + 1].DefaultCellStyle = stackFrameStyle;
                        dgvStack.Rows[frame.FP + 2].DefaultCellStyle = stackFrameStyle;
                        dgvStack.Rows[frame.FP + 3].DefaultCellStyle = stackFrameStyle;
                        dgvStack.Rows[frame.FP + 4].DefaultCellStyle = stackLocalStyle;
                        dgvStack.Rows[frame.FP + 5].DefaultCellStyle = stackLocalStyle;
                        
                        for (int i = 0; i < frame.locals; i++)
                        {
                            dgvStack.Rows[frame.FP - frame.locals + i].Cells[0].ReadOnly = false;

                            dgvStack.Rows[frame.FP - frame.locals + i].DefaultCellStyle = localStyle;
                            
                            if (localsDebugInfo.TryGetValue((frame.procName, i), out string name))
                            {
                                dgvStack.Rows[frame.FP - frame.locals + i].Cells[0].Value = name;
                            }
                        }
                    }

                    frame = frame.prev;
                }

                if (dgvStack.Rows.Count > vm12.FramePointer)
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

                debugger.SourceView.Open(vm12.sourceDir, data[0], line);
            }
#endif
        }
        
        private void dgvStack_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
#if DEBUG
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
                        void SetUserDebugDef((string, int) localKey, string userValue)
                        {
                            userDebugInfo[localKey] = userValue;
                            localsDebugInfo[localKey] = userValue;
                        }

                        string value = e.FormattedValue.ToString();

                        (string, int) key = (frame.procName, e.RowIndex - (frame.FP - frame.locals));

                        if (value.Length == 0)
                        {
                            // The user has removed the value
                            userDebugInfo.Remove(key);
                            localsDebugInfo[key] = generatedDebugInfo[key];
                            dgvStack.EditingControl.Text = localsDebugInfo[key];
                        }
                        else
                        {
                            // This is a user definition
                            SetUserDebugDef((frame.procName, e.RowIndex - (frame.FP - frame.locals)), value);
                        }
                        
                        if (value.EndsWith("_H"))
                        {
                            string newVal = value.ReplaceEnd("_H", "_L");
                            SetUserDebugDef((frame.procName, e.RowIndex - (frame.FP - frame.locals) + 1), newVal);
                            dgvStack.Rows[e.RowIndex + 1].Cells[e.ColumnIndex].Value = newVal;
                        }

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
#endif
        }
    }
}
