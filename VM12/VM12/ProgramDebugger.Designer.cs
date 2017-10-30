namespace Debugging
{
    partial class ProgramDebugger
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ProgramDebugger));
            this.menuStrip = new System.Windows.Forms.MenuStrip();
            this.fileToolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
            this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.toolLabelLine = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolLabelColumn = new System.Windows.Forms.ToolStripStatusLabel();
            this.toolLabelIndex = new System.Windows.Forms.ToolStripStatusLabel();
            this.splitDebugPanel = new System.Windows.Forms.SplitContainer();
            this.toolStrip = new System.Windows.Forms.ToolStrip();
            this.tsbContinue = new System.Windows.Forms.ToolStripButton();
            this.tsbPause = new System.Windows.Forms.ToolStripButton();
            this.tsbStop = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.tsbStepIn = new System.Windows.Forms.ToolStripButton();
            this.tsbStepOver = new System.Windows.Forms.ToolStripButton();
            this.tsbStepOut = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator3 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripLabelOpcode = new System.Windows.Forms.ToolStripLabel();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripButton1 = new System.Windows.Forms.ToolStripButton();
            this.toolStripButton2 = new System.Windows.Forms.ToolStripButton();
            this.toolStripButton3 = new System.Windows.Forms.ToolStripButton();
            this.toolStripButton4 = new System.Windows.Forms.ToolStripButton();
            this.toolStripButton5 = new System.Windows.Forms.ToolStripButton();
            this.toolStripButton6 = new System.Windows.Forms.ToolStripButton();
            this.stack_view = new Debugging.Stack_view();
            this.sourceView = new Debugger.SourceView();
            this.menuStrip.SuspendLayout();
            this.statusStrip.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitDebugPanel)).BeginInit();
            this.splitDebugPanel.Panel1.SuspendLayout();
            this.splitDebugPanel.Panel2.SuspendLayout();
            this.splitDebugPanel.SuspendLayout();
            this.toolStrip.SuspendLayout();
            this.SuspendLayout();
            // 
            // menuStrip
            // 
            this.menuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fileToolStripMenuItem1});
            this.menuStrip.Location = new System.Drawing.Point(0, 0);
            this.menuStrip.Name = "menuStrip";
            this.menuStrip.Size = new System.Drawing.Size(1082, 24);
            this.menuStrip.TabIndex = 0;
            this.menuStrip.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem1
            // 
            this.fileToolStripMenuItem1.Name = "fileToolStripMenuItem1";
            this.fileToolStripMenuItem1.Size = new System.Drawing.Size(37, 20);
            this.fileToolStripMenuItem1.Text = "File";
            // 
            // fileToolStripMenuItem
            // 
            this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            this.fileToolStripMenuItem.Size = new System.Drawing.Size(37, 20);
            this.fileToolStripMenuItem.Text = "File";
            // 
            // statusStrip
            // 
            this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolLabelLine,
            this.toolLabelColumn,
            this.toolLabelIndex});
            this.statusStrip.Location = new System.Drawing.Point(0, 520);
            this.statusStrip.Name = "statusStrip";
            this.statusStrip.Size = new System.Drawing.Size(1082, 22);
            this.statusStrip.TabIndex = 1;
            this.statusStrip.Text = "statusStrip";
            // 
            // toolLabelLine
            // 
            this.toolLabelLine.Name = "toolLabelLine";
            this.toolLabelLine.Size = new System.Drawing.Size(32, 17);
            this.toolLabelLine.Text = "Ln: 0";
            // 
            // toolLabelColumn
            // 
            this.toolLabelColumn.Name = "toolLabelColumn";
            this.toolLabelColumn.Size = new System.Drawing.Size(37, 17);
            this.toolLabelColumn.Text = "Col: 5";
            // 
            // toolLabelIndex
            // 
            this.toolLabelIndex.Name = "toolLabelIndex";
            this.toolLabelIndex.Size = new System.Drawing.Size(36, 17);
            this.toolLabelIndex.Text = "Ind: 0";
            // 
            // splitDebugPanel
            // 
            this.splitDebugPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitDebugPanel.Location = new System.Drawing.Point(0, 49);
            this.splitDebugPanel.Name = "splitDebugPanel";
            // 
            // splitDebugPanel.Panel1
            // 
            this.splitDebugPanel.Panel1.Controls.Add(this.stack_view);
            // 
            // splitDebugPanel.Panel2
            // 
            this.splitDebugPanel.Panel2.Controls.Add(this.sourceView);
            this.splitDebugPanel.Size = new System.Drawing.Size(1082, 471);
            this.splitDebugPanel.SplitterDistance = 360;
            this.splitDebugPanel.TabIndex = 3;
            // 
            // toolStrip
            // 
            this.toolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.toolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.tsbContinue,
            this.tsbPause,
            this.tsbStop,
            this.toolStripSeparator2,
            this.tsbStepIn,
            this.tsbStepOver,
            this.tsbStepOut,
            this.toolStripSeparator3,
            this.toolStripLabelOpcode});
            this.toolStrip.Location = new System.Drawing.Point(0, 24);
            this.toolStrip.Name = "toolStrip";
            this.toolStrip.Size = new System.Drawing.Size(1082, 25);
            this.toolStrip.TabIndex = 2;
            this.toolStrip.Text = "toolStrip";
            // 
            // tsbContinue
            // 
            this.tsbContinue.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsbContinue.Image = ((System.Drawing.Image)(resources.GetObject("tsbContinue.Image")));
            this.tsbContinue.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tsbContinue.Name = "tsbContinue";
            this.tsbContinue.Size = new System.Drawing.Size(23, 22);
            this.tsbContinue.Text = "Continue";
            this.tsbContinue.ToolTipText = "Contine";
            this.tsbContinue.Click += new System.EventHandler(this.tsbContinue_Click);
            // 
            // tsbPause
            // 
            this.tsbPause.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsbPause.Image = ((System.Drawing.Image)(resources.GetObject("tsbPause.Image")));
            this.tsbPause.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tsbPause.Name = "tsbPause";
            this.tsbPause.Size = new System.Drawing.Size(23, 22);
            this.tsbPause.Text = "Pause";
            this.tsbPause.ToolTipText = "Pause";
            this.tsbPause.Click += new System.EventHandler(this.tsbPause_Click);
            // 
            // tsbStop
            // 
            this.tsbStop.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsbStop.Image = ((System.Drawing.Image)(resources.GetObject("tsbStop.Image")));
            this.tsbStop.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tsbStop.Name = "tsbStop";
            this.tsbStop.Size = new System.Drawing.Size(23, 22);
            this.tsbStop.Text = "Stop";
            this.tsbStop.ToolTipText = "Stop";
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(6, 25);
            // 
            // tsbStepIn
            // 
            this.tsbStepIn.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsbStepIn.Image = ((System.Drawing.Image)(resources.GetObject("tsbStepIn.Image")));
            this.tsbStepIn.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tsbStepIn.Name = "tsbStepIn";
            this.tsbStepIn.Size = new System.Drawing.Size(23, 22);
            this.tsbStepIn.Text = "Step In";
            this.tsbStepIn.ToolTipText = "Step In";
            this.tsbStepIn.Click += new System.EventHandler(this.tsbStepIn_Click);
            // 
            // tsbStepOver
            // 
            this.tsbStepOver.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsbStepOver.Image = ((System.Drawing.Image)(resources.GetObject("tsbStepOver.Image")));
            this.tsbStepOver.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tsbStepOver.Name = "tsbStepOver";
            this.tsbStepOver.Size = new System.Drawing.Size(23, 22);
            this.tsbStepOver.Text = "Step Over";
            this.tsbStepOver.ToolTipText = "Step Over";
            this.tsbStepOver.Click += new System.EventHandler(this.tsbStepOver_Click);
            // 
            // tsbStepOut
            // 
            this.tsbStepOut.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.tsbStepOut.Image = ((System.Drawing.Image)(resources.GetObject("tsbStepOut.Image")));
            this.tsbStepOut.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.tsbStepOut.Name = "tsbStepOut";
            this.tsbStepOut.Size = new System.Drawing.Size(23, 22);
            this.tsbStepOut.Text = "Step Out";
            this.tsbStepOut.ToolTipText = "Step Out";
            this.tsbStepOut.Click += new System.EventHandler(this.tsbStepOut_Click);
            // 
            // toolStripSeparator3
            // 
            this.toolStripSeparator3.Name = "toolStripSeparator3";
            this.toolStripSeparator3.Size = new System.Drawing.Size(6, 25);
            // 
            // toolStripLabelOpcode
            // 
            this.toolStripLabelOpcode.Name = "toolStripLabelOpcode";
            this.toolStripLabelOpcode.Size = new System.Drawing.Size(30, 22);
            this.toolStripLabelOpcode.Text = "Nop";
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(6, 25);
            // 
            // toolStripButton1
            // 
            this.toolStripButton1.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButton1.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripButton1.Name = "toolStripButton1";
            this.toolStripButton1.Size = new System.Drawing.Size(23, 22);
            this.toolStripButton1.Text = "tsbContinue";
            // 
            // toolStripButton2
            // 
            this.toolStripButton2.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButton2.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripButton2.Name = "toolStripButton2";
            this.toolStripButton2.Size = new System.Drawing.Size(23, 22);
            this.toolStripButton2.Text = "tsbPause";
            // 
            // toolStripButton3
            // 
            this.toolStripButton3.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButton3.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripButton3.Name = "toolStripButton3";
            this.toolStripButton3.Size = new System.Drawing.Size(23, 22);
            this.toolStripButton3.Text = "toolStripButton3";
            // 
            // toolStripButton4
            // 
            this.toolStripButton4.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButton4.Image = ((System.Drawing.Image)(resources.GetObject("toolStripButton4.Image")));
            this.toolStripButton4.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripButton4.Name = "toolStripButton4";
            this.toolStripButton4.Size = new System.Drawing.Size(23, 22);
            this.toolStripButton4.Text = "toolStripButton4";
            // 
            // toolStripButton5
            // 
            this.toolStripButton5.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButton5.Image = ((System.Drawing.Image)(resources.GetObject("toolStripButton5.Image")));
            this.toolStripButton5.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripButton5.Name = "toolStripButton5";
            this.toolStripButton5.Size = new System.Drawing.Size(23, 22);
            this.toolStripButton5.Text = "toolStripButton5";
            // 
            // toolStripButton6
            // 
            this.toolStripButton6.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButton6.Image = ((System.Drawing.Image)(resources.GetObject("toolStripButton6.Image")));
            this.toolStripButton6.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripButton6.Name = "toolStripButton6";
            this.toolStripButton6.Size = new System.Drawing.Size(23, 22);
            this.toolStripButton6.Text = "toolStripButton6";
            // 
            // stack_view
            // 
            this.stack_view.AutoScroll = true;
            this.stack_view.Dock = System.Windows.Forms.DockStyle.Fill;
            this.stack_view.Location = new System.Drawing.Point(0, 0);
            this.stack_view.Name = "stack_view";
            this.stack_view.Size = new System.Drawing.Size(360, 471);
            this.stack_view.TabIndex = 0;
            // 
            // sourceView
            // 
            this.sourceView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.sourceView.Location = new System.Drawing.Point(0, 0);
            this.sourceView.Name = "sourceView";
            this.sourceView.Size = new System.Drawing.Size(718, 471);
            this.sourceView.TabIndex = 0;
            this.sourceView.TextSelectionChanged += new System.EventHandler(this.sourceView_TextSelectionChanged);
            // 
            // ProgramDebugger
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1082, 542);
            this.Controls.Add(this.splitDebugPanel);
            this.Controls.Add(this.statusStrip);
            this.Controls.Add(this.toolStrip);
            this.Controls.Add(this.menuStrip);
            this.MainMenuStrip = this.menuStrip;
            this.Name = "ProgramDebugger";
            this.Text = "Debugger";
            this.menuStrip.ResumeLayout(false);
            this.menuStrip.PerformLayout();
            this.statusStrip.ResumeLayout(false);
            this.statusStrip.PerformLayout();
            this.splitDebugPanel.Panel1.ResumeLayout(false);
            this.splitDebugPanel.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitDebugPanel)).EndInit();
            this.splitDebugPanel.ResumeLayout(false);
            this.toolStrip.ResumeLayout(false);
            this.toolStrip.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip menuStrip;
        private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.SplitContainer splitDebugPanel;
        private Stack_view stack_view;
        private System.Windows.Forms.ToolStrip toolStrip;
        private System.Windows.Forms.ToolStripButton toolStripButton1;
        private System.Windows.Forms.ToolStripButton toolStripButton2;
        private System.Windows.Forms.ToolStripButton toolStripButton3;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripButton toolStripButton4;
        private System.Windows.Forms.ToolStripButton toolStripButton5;
        private System.Windows.Forms.ToolStripButton toolStripButton6;
        private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem1;
        private System.Windows.Forms.ToolStripButton tsbContinue;
        private System.Windows.Forms.ToolStripButton tsbPause;
        private System.Windows.Forms.ToolStripButton tsbStop;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ToolStripButton tsbStepIn;
        private System.Windows.Forms.ToolStripButton tsbStepOver;
        private System.Windows.Forms.ToolStripButton tsbStepOut;
        private Debugger.SourceView sourceView;
        private System.Windows.Forms.ToolStripStatusLabel toolLabelLine;
        private System.Windows.Forms.ToolStripStatusLabel toolLabelColumn;
        private System.Windows.Forms.ToolStripStatusLabel toolLabelIndex;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator3;
        private System.Windows.Forms.ToolStripLabel toolStripLabelOpcode;
    }
}