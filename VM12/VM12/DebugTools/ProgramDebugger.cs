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
    using Debugger;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using VM12;

    public partial class ProgramDebugger : Form
    {
        private VM12 vm12;

        internal SourceView SourceView { get => sourceView; }

        int stackDepth = 0;

        bool catchedVM = false;

        Stopwatch watch = new Stopwatch();

        public ProgramDebugger()
        {
            InitializeComponent();

            stack_view.SetDebugger(this);

            FormClosing += ProgramDebugger_FormClosing;
            Load += ProgramDebugger_Load;
        }

        private void ProgramDebugger_Load(object sender, EventArgs e)
        {
            stack_view.LoadData();
        }
        
        private void ProgramDebugger_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                ReleaseVM();
                Hide();
            }
        }

        public void CloseDebugger()
        {
#if DEBUG
            Debug.WriteLine("CLOSING!!!!");

            if (vm12 != null && !vm12.Stopped)
            {
                ReleaseVM();
                stack_view.Close();
            }
#endif
        }
        
        internal void SetVM(VM12 vm12)
        {
            this.vm12 = vm12;
            stack_view.SetVM(vm12);
            
            // Enable buttons
        }

        public void HitBreakpoint()
        {
            CatchVM();
            UpdateDebug();
        }
        
        private void UpdateDebug()
        {
#if DEBUG
            stack_view.UpdateStack();
            var frame = vm12.CurrentStackFrame;
            sourceView.Open(Path.Combine(vm12.sourceDir.FullName, frame.file), frame.line);
            toolStripLabelOpcode.Text = vm12.Opcode.ToString();
            stackDepth = VM12.CountStackDepth(frame);
#endif
        }

        private void CatchVM()
        {
#if DEBUG
            if (vm12 != null && !vm12.Stopped)
            {
                if (catchedVM == false)
                {
                    //Interlocked.Exchange(ref vm12.UseDebugger, 1);
                    vm12.UseDebugger = true;
                    if (!Monitor.IsEntered(vm12.DebugSync))
                    {
                        Monitor.Enter(vm12.DebugSync);
                    }

                    vm12.DebugBreakEvent.WaitOne();

                    catchedVM = true;
                }
            }
#endif
        }

        private void ReleaseVM()
        {
#if DEBUG
            if (vm12 != null && !vm12.Stopped)
            {
                if (Monitor.IsEntered(vm12.DebugSync))
                {
                    Monitor.Exit(vm12.DebugSync);
                }
                vm12.UseDebugger = false;
                //Interlocked.Exchange(ref vm12.UseDebugger, 0);
                vm12.ContinueEvent.Set();
                catchedVM = false;

                stack_view.ClearStack();
            }
#endif
        }

        private void StepVM()
        {
#if DEBUG
            if (vm12 != null && !vm12.Stopped)
            {
                CatchVM();

                vm12.ContinueEvent.Set();
                vm12.DebugBreakEvent.WaitOne();
            }
#endif
        }
        
        private void sourceView_TextSelectionChanged(object sender, EventArgs e)
        {
            int line = sourceView.TextBox.GetLineFromCharIndex(sourceView.TextBox.SelectionStart);
            int col = sourceView.TextBox.SelectionStart - sourceView.TextBox.GetFirstCharIndexOfCurrentLine();
            toolLabelLine.Text = $"Ln: {line}";
            toolLabelColumn.Text = $"Col: {col}";
            toolLabelIndex.Text = $"Ind: {sourceView.TextBox.SelectionStart} LnStart: {sourceView.TextBox.GetFirstCharIndexOfCurrentLine()}";
        }

        private void tsbPause_Click(object sender, EventArgs e)
        {
            if (vm12 != null && !vm12.Stopped)
            {
                CatchVM();

                UpdateDebug();
            }
        }

        private void tsbContinue_Click(object sender, EventArgs e)
        {
            if (vm12 != null && !vm12.Stopped)
            {
                ReleaseVM();
            }
        }

        private volatile bool steppingOver = false;

        private void tsbStepOver_Click(object sender, EventArgs e)
        {
            if (steppingOver)
            {
                steppingOver = false;
                return;
            }

            if (vm12 != null && !vm12.Stopped)
            {
                steppingOver = true;
                
                // FIXME!! For this we really should just break on the instruction after the call...

                // Step the vm. If the stack depth increased we continue to step until we get back to the original stack depth
                int origDepth = VM12.CountStackDepth(vm12.CurrentStackFrame);

                StepVM();

                int newDepth = VM12.CountStackDepth(vm12.CurrentStackFrame);

                watch.Reset();
                watch.Start();

                while (steppingOver && newDepth > origDepth)
                {
                    StepVM();

                    if (vm12.RetInstruction)
                    {
                        var frame = vm12.CurrentStackFrame;

                        newDepth = VM12.CountStackDepth(frame);

                        vm12.RetInstruction = false;
                    }

                    if (watch.ElapsedMilliseconds > 50)
                    {
                        Application.DoEvents();
                        watch.Restart();
                    }
                }

                watch.Stop();

                steppingOver = false;

                UpdateDebug();
            }
        }
        
        private void tsbStepIn_Click(object sender, EventArgs e)
        {
            if (vm12 != null && !vm12.Stopped)
            {
                StepVM();

                UpdateDebug();
            }
        }

        private volatile bool steppingOut = false;

        private void tsbStepOut_Click(object sender, EventArgs e)
        {
#if DEBUG
            if (steppingOut)
            {
                steppingOut = false;
                return;
            }

            if (vm12 != null && !vm12.Stopped)
            {
                int newDepth = stackDepth;

                if (stackDepth > 1)
                {
                    steppingOut = true;

                    watch.Reset();
                    watch.Start();

                    while (steppingOut && newDepth >= stackDepth)
                    {
                        StepVM();
                        
                        // We need to return control to winforms so that we don't get stuck in hlt-loops

                        if (vm12.RetInstruction)
                        {
                            var frame = vm12.CurrentStackFrame;
                    
                            newDepth = VM12.CountStackDepth(frame);

                            vm12.RetInstruction = false;
                        }

                        if (watch.ElapsedMilliseconds > 50)
                        {
                            Application.DoEvents();
                            watch.Restart();
                        }
                    }

                    watch.Stop();

                    steppingOut = false;

                    UpdateDebug();
                }
                else
                {
                    ReleaseVM();
                }
            }
#endif
        }

        private void tsbStop_Click(object sender, EventArgs e)
        {
            VM12Form.form?.Close();
        }
    }
}
