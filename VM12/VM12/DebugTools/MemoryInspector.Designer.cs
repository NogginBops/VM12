namespace Debugging
{
    partial class MemoryInspector
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
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.optionsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.memEndAddress = new Debugging.Field();
            this.memLength = new Debugging.Field();
            this.memStartAddress = new Debugging.Field();
            this.memoryView = new Debugging.MemoryView();
            this.memViewRefreshTimer = new System.Windows.Forms.Timer(this.components);
            this.refreshToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.menuStrip1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.SuspendLayout();
            // 
            // menuStrip1
            // 
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.optionsToolStripMenuItem});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new System.Drawing.Size(1069, 24);
            this.menuStrip1.TabIndex = 1;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // optionsToolStripMenuItem
            // 
            this.optionsToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.refreshToolStripMenuItem});
            this.optionsToolStripMenuItem.Name = "optionsToolStripMenuItem";
            this.optionsToolStripMenuItem.Size = new System.Drawing.Size(61, 20);
            this.optionsToolStripMenuItem.Text = "Options";
            // 
            // splitContainer1
            // 
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.Location = new System.Drawing.Point(0, 24);
            this.splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.memEndAddress);
            this.splitContainer1.Panel1.Controls.Add(this.memLength);
            this.splitContainer1.Panel1.Controls.Add(this.memStartAddress);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.memoryView);
            this.splitContainer1.Size = new System.Drawing.Size(1069, 500);
            this.splitContainer1.SplitterDistance = 220;
            this.splitContainer1.TabIndex = 2;
            // 
            // memEndAddress
            // 
            this.memEndAddress.AutoSize = true;
            this.memEndAddress.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.memEndAddress.LableText = "End address:";
            this.memEndAddress.Location = new System.Drawing.Point(12, 67);
            this.memEndAddress.Name = "memEndAddress";
            this.memEndAddress.Size = new System.Drawing.Size(182, 26);
            this.memEndAddress.TabIndex = 2;
            this.memEndAddress.ValueText = "";
            // 
            // memLength
            // 
            this.memLength.AutoSize = true;
            this.memLength.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.memLength.LableText = "Length:";
            this.memLength.Location = new System.Drawing.Point(12, 35);
            this.memLength.Name = "memLength";
            this.memLength.Size = new System.Drawing.Size(182, 26);
            this.memLength.TabIndex = 1;
            this.memLength.ValueText = "";
            // 
            // memStartAddress
            // 
            this.memStartAddress.AutoSize = true;
            this.memStartAddress.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.memStartAddress.LableText = "Start address:";
            this.memStartAddress.Location = new System.Drawing.Point(12, 3);
            this.memStartAddress.Name = "memStartAddress";
            this.memStartAddress.Size = new System.Drawing.Size(182, 26);
            this.memStartAddress.TabIndex = 0;
            this.memStartAddress.ValueText = "";
            // 
            // memoryView1
            // 
            this.memoryView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.memoryView.Location = new System.Drawing.Point(0, 0);
            this.memoryView.Name = "memoryView1";
            this.memoryView.Size = new System.Drawing.Size(845, 500);
            this.memoryView.TabIndex = 0;
            // 
            // memViewRefreshTimer
            // 
            this.memViewRefreshTimer.Tick += new System.EventHandler(this.memViewRefreshTimer_Tick);
            // 
            // refreshToolStripMenuItem
            // 
            this.refreshToolStripMenuItem.Checked = true;
            this.refreshToolStripMenuItem.CheckOnClick = true;
            this.refreshToolStripMenuItem.CheckState = System.Windows.Forms.CheckState.Checked;
            this.refreshToolStripMenuItem.Name = "refreshToolStripMenuItem";
            this.refreshToolStripMenuItem.Size = new System.Drawing.Size(180, 22);
            this.refreshToolStripMenuItem.Text = "Refresh";
            this.refreshToolStripMenuItem.Click += new System.EventHandler(this.refreshToolStripMenuItem_Click);
            // 
            // MemoryInspector
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1069, 524);
            this.Controls.Add(this.splitContainer1);
            this.Controls.Add(this.menuStrip1);
            this.MainMenuStrip = this.menuStrip1;
            this.Name = "MemoryInspector";
            this.Text = "MemoryInspector";
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel1.PerformLayout();
            this.splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private MemoryView memoryView;
        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.ToolStripMenuItem optionsToolStripMenuItem;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private Field memStartAddress;
        private Field memEndAddress;
        private Field memLength;
        private System.Windows.Forms.Timer memViewRefreshTimer;
        private System.Windows.Forms.ToolStripMenuItem refreshToolStripMenuItem;
    }
}