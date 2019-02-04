namespace VM12
{
    partial class VM12Form
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
            this.components = new System.ComponentModel.Container();
            this.menuStrip = new System.Windows.Forms.MenuStrip();
            this.fileToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.openToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.stopToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.developerToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.instructionFrequencyToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.interruptFrequencyToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.interruptFrequencyToolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
            this.instructionTimesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.heapViewToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.debuggerToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.profilerToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.memoryToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.refreshTimer = new System.Windows.Forms.Timer(this.components);
            this.pbxMain = new Util.ExtendedPictureBox();
            this.soundDebugToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.menuStrip.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pbxMain)).BeginInit();
            this.SuspendLayout();
            // 
            // menuStrip
            // 
            this.menuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fileToolStripMenuItem,
            this.developerToolStripMenuItem});
            this.menuStrip.Location = new System.Drawing.Point(0, 0);
            this.menuStrip.Name = "menuStrip";
            this.menuStrip.Size = new System.Drawing.Size(640, 24);
            this.menuStrip.TabIndex = 0;
            this.menuStrip.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            this.fileToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.openToolStripMenuItem,
            this.stopToolStripMenuItem});
            this.fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            this.fileToolStripMenuItem.Size = new System.Drawing.Size(37, 20);
            this.fileToolStripMenuItem.Text = "File";
            // 
            // openToolStripMenuItem
            // 
            this.openToolStripMenuItem.Name = "openToolStripMenuItem";
            this.openToolStripMenuItem.Size = new System.Drawing.Size(103, 22);
            this.openToolStripMenuItem.Text = "Open";
            this.openToolStripMenuItem.Click += new System.EventHandler(this.openToolStripMenuItem_Click);
            // 
            // stopToolStripMenuItem
            // 
            this.stopToolStripMenuItem.Name = "stopToolStripMenuItem";
            this.stopToolStripMenuItem.Size = new System.Drawing.Size(103, 22);
            this.stopToolStripMenuItem.Text = "Stop";
            this.stopToolStripMenuItem.Click += new System.EventHandler(this.stopToolStripMenuItem_Click);
            // 
            // developerToolStripMenuItem
            // 
            this.developerToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.instructionFrequencyToolStripMenuItem,
            this.interruptFrequencyToolStripMenuItem,
            this.interruptFrequencyToolStripMenuItem1,
            this.instructionTimesToolStripMenuItem,
            this.heapViewToolStripMenuItem,
            this.debuggerToolStripMenuItem,
            this.profilerToolStripMenuItem,
            this.memoryToolStripMenuItem,
            this.soundDebugToolStripMenuItem});
            this.developerToolStripMenuItem.Name = "developerToolStripMenuItem";
            this.developerToolStripMenuItem.Size = new System.Drawing.Size(72, 20);
            this.developerToolStripMenuItem.Text = "Developer";
            // 
            // instructionFrequencyToolStripMenuItem
            // 
            this.instructionFrequencyToolStripMenuItem.Name = "instructionFrequencyToolStripMenuItem";
            this.instructionFrequencyToolStripMenuItem.Size = new System.Drawing.Size(218, 22);
            this.instructionFrequencyToolStripMenuItem.Text = "Instruction Frequency";
            this.instructionFrequencyToolStripMenuItem.Click += new System.EventHandler(this.instructionFrequencyToolStripMenuItem_Click);
            // 
            // interruptFrequencyToolStripMenuItem
            // 
            this.interruptFrequencyToolStripMenuItem.Name = "interruptFrequencyToolStripMenuItem";
            this.interruptFrequencyToolStripMenuItem.Size = new System.Drawing.Size(218, 22);
            this.interruptFrequencyToolStripMenuItem.Text = "Missed Interrupt Frequency";
            this.interruptFrequencyToolStripMenuItem.Click += new System.EventHandler(this.interruptFrequencyToolStripMenuItem_Click);
            // 
            // interruptFrequencyToolStripMenuItem1
            // 
            this.interruptFrequencyToolStripMenuItem1.Name = "interruptFrequencyToolStripMenuItem1";
            this.interruptFrequencyToolStripMenuItem1.Size = new System.Drawing.Size(218, 22);
            this.interruptFrequencyToolStripMenuItem1.Text = "Interrupt Frequency";
            this.interruptFrequencyToolStripMenuItem1.Click += new System.EventHandler(this.interruptFrequencyToolStripMenuItem1_Click);
            // 
            // instructionTimesToolStripMenuItem
            // 
            this.instructionTimesToolStripMenuItem.Name = "instructionTimesToolStripMenuItem";
            this.instructionTimesToolStripMenuItem.Size = new System.Drawing.Size(218, 22);
            this.instructionTimesToolStripMenuItem.Text = "Instruction Times";
            this.instructionTimesToolStripMenuItem.Click += new System.EventHandler(this.instructionTimesToolStripMenuItem_Click);
            // 
            // heapViewToolStripMenuItem
            // 
            this.heapViewToolStripMenuItem.Name = "heapViewToolStripMenuItem";
            this.heapViewToolStripMenuItem.Size = new System.Drawing.Size(218, 22);
            this.heapViewToolStripMenuItem.Text = "Heap View";
            this.heapViewToolStripMenuItem.Click += new System.EventHandler(this.heapViewToolStripMenuItem_Click);
            // 
            // debuggerToolStripMenuItem
            // 
            this.debuggerToolStripMenuItem.Name = "debuggerToolStripMenuItem";
            this.debuggerToolStripMenuItem.Size = new System.Drawing.Size(218, 22);
            this.debuggerToolStripMenuItem.Text = "Debugger";
            this.debuggerToolStripMenuItem.Click += new System.EventHandler(this.debuggerToolStripMenuItem_Click);
            // 
            // profilerToolStripMenuItem
            // 
            this.profilerToolStripMenuItem.Name = "profilerToolStripMenuItem";
            this.profilerToolStripMenuItem.Size = new System.Drawing.Size(218, 22);
            this.profilerToolStripMenuItem.Text = "Profiler";
            this.profilerToolStripMenuItem.Click += new System.EventHandler(this.profilerToolStripMenuItem_Click);
            // 
            // memoryToolStripMenuItem
            // 
            this.memoryToolStripMenuItem.Name = "memoryToolStripMenuItem";
            this.memoryToolStripMenuItem.Size = new System.Drawing.Size(218, 22);
            this.memoryToolStripMenuItem.Text = "Memory";
            this.memoryToolStripMenuItem.Click += new System.EventHandler(this.memoryToolStripMenuItem_Click);
            // 
            // refreshTimer
            // 
            this.refreshTimer.Enabled = true;
            this.refreshTimer.Interval = 8;
            this.refreshTimer.Tick += new System.EventHandler(this.RefreshTimer_Tick);
            // 
            // pbxMain
            // 
            this.pbxMain.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.pbxMain.Cursor = System.Windows.Forms.Cursors.Default;
            this.pbxMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pbxMain.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            this.pbxMain.Location = new System.Drawing.Point(0, 24);
            this.pbxMain.Name = "pbxMain";
            this.pbxMain.Size = new System.Drawing.Size(640, 480);
            this.pbxMain.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.pbxMain.TabIndex = 1;
            this.pbxMain.TabStop = false;
            this.pbxMain.MouseDown += new System.Windows.Forms.MouseEventHandler(this.pbxMain_MouseDown);
            this.pbxMain.MouseEnter += new System.EventHandler(this.pbxMain_MouseEnter);
            this.pbxMain.MouseLeave += new System.EventHandler(this.pbxMain_MouseLeave);
            this.pbxMain.MouseMove += new System.Windows.Forms.MouseEventHandler(this.pbxMain_MouseMove);
            this.pbxMain.MouseUp += new System.Windows.Forms.MouseEventHandler(this.pbxMain_MouseUp);
            // 
            // soundDebugToolStripMenuItem
            // 
            this.soundDebugToolStripMenuItem.Name = "soundDebugToolStripMenuItem";
            this.soundDebugToolStripMenuItem.Size = new System.Drawing.Size(218, 22);
            this.soundDebugToolStripMenuItem.Text = "Sound Debug";
            this.soundDebugToolStripMenuItem.Click += new System.EventHandler(this.soundDebugToolStripMenuItem_Click);
            // 
            // VM12Form
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(640, 504);
            this.Controls.Add(this.pbxMain);
            this.Controls.Add(this.menuStrip);
            this.DoubleBuffered = true;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.KeyPreview = true;
            this.MainMenuStrip = this.menuStrip;
            this.MaximizeBox = false;
            this.Name = "VM12Form";
            this.Text = "VM12";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.VM12Form_FormClosing);
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.VM12Form_KeyDown);
            this.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.VM12Form_KeyPress);
            this.KeyUp += new System.Windows.Forms.KeyEventHandler(this.VM12Form_KeyUp);
            this.menuStrip.ResumeLayout(false);
            this.menuStrip.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pbxMain)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip menuStrip;
        private System.Windows.Forms.ToolStripMenuItem fileToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem openToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem developerToolStripMenuItem;
        private System.Windows.Forms.Timer refreshTimer;
        private Util.ExtendedPictureBox pbxMain;
        private System.Windows.Forms.ToolStripMenuItem instructionFrequencyToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem stopToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem interruptFrequencyToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem interruptFrequencyToolStripMenuItem1;
        private System.Windows.Forms.ToolStripMenuItem instructionTimesToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem heapViewToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem debuggerToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem profilerToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem memoryToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem soundDebugToolStripMenuItem;
    }
}

