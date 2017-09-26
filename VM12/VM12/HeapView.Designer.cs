namespace VM12
{
    partial class HeapView
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
            this.optionsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.refreshToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.settingsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.heapViewSplitContainer = new System.Windows.Forms.SplitContainer();
            this.heapViewImg = new System.Windows.Forms.PictureBox();
            this.label1 = new System.Windows.Forms.Label();
            this.heapViewRefreshTimer = new System.Windows.Forms.Timer(this.components);
            this.menuStrip.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.heapViewSplitContainer)).BeginInit();
            this.heapViewSplitContainer.Panel1.SuspendLayout();
            this.heapViewSplitContainer.Panel2.SuspendLayout();
            this.heapViewSplitContainer.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.heapViewImg)).BeginInit();
            this.SuspendLayout();
            // 
            // menuStrip
            // 
            this.menuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.optionsToolStripMenuItem,
            this.settingsToolStripMenuItem});
            this.menuStrip.Location = new System.Drawing.Point(0, 0);
            this.menuStrip.Name = "menuStrip";
            this.menuStrip.Size = new System.Drawing.Size(914, 24);
            this.menuStrip.TabIndex = 2;
            this.menuStrip.Text = "menuStrip1";
            // 
            // optionsToolStripMenuItem
            // 
            this.optionsToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.refreshToolStripMenuItem});
            this.optionsToolStripMenuItem.Name = "optionsToolStripMenuItem";
            this.optionsToolStripMenuItem.Size = new System.Drawing.Size(61, 20);
            this.optionsToolStripMenuItem.Text = "Options";
            // 
            // refreshToolStripMenuItem
            // 
            this.refreshToolStripMenuItem.Checked = true;
            this.refreshToolStripMenuItem.CheckOnClick = true;
            this.refreshToolStripMenuItem.CheckState = System.Windows.Forms.CheckState.Checked;
            this.refreshToolStripMenuItem.Name = "refreshToolStripMenuItem";
            this.refreshToolStripMenuItem.Size = new System.Drawing.Size(152, 22);
            this.refreshToolStripMenuItem.Text = "Refresh";
            this.refreshToolStripMenuItem.Click += new System.EventHandler(this.refreshToolStripMenuItem_Click);
            // 
            // settingsToolStripMenuItem
            // 
            this.settingsToolStripMenuItem.Name = "settingsToolStripMenuItem";
            this.settingsToolStripMenuItem.Size = new System.Drawing.Size(61, 20);
            this.settingsToolStripMenuItem.Text = "Settings";
            // 
            // heapViewSplitContainer
            // 
            this.heapViewSplitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.heapViewSplitContainer.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.heapViewSplitContainer.Location = new System.Drawing.Point(0, 24);
            this.heapViewSplitContainer.Name = "heapViewSplitContainer";
            this.heapViewSplitContainer.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // heapViewSplitContainer.Panel1
            // 
            this.heapViewSplitContainer.Panel1.Controls.Add(this.heapViewImg);
            // 
            // heapViewSplitContainer.Panel2
            // 
            this.heapViewSplitContainer.Panel2.Controls.Add(this.label1);
            this.heapViewSplitContainer.Size = new System.Drawing.Size(914, 520);
            this.heapViewSplitContainer.SplitterDistance = 491;
            this.heapViewSplitContainer.TabIndex = 3;
            // 
            // heapViewImg
            // 
            this.heapViewImg.Dock = System.Windows.Forms.DockStyle.Fill;
            this.heapViewImg.Location = new System.Drawing.Point(0, 0);
            this.heapViewImg.Name = "heapViewImg";
            this.heapViewImg.Size = new System.Drawing.Size(914, 491);
            this.heapViewImg.TabIndex = 0;
            this.heapViewImg.TabStop = false;
            this.heapViewImg.Paint += new System.Windows.Forms.PaintEventHandler(this.heapViewImg_Paint);
            this.heapViewImg.Resize += new System.EventHandler(this.heapViewImg_Resize);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(440, 161);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(38, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "TODO";
            // 
            // heapViewRefreshTimer
            // 
            this.heapViewRefreshTimer.Tick += new System.EventHandler(this.heapViewRefreshTimer_Tick);
            // 
            // HeapView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(914, 544);
            this.Controls.Add(this.heapViewSplitContainer);
            this.Controls.Add(this.menuStrip);
            this.Name = "HeapView";
            this.Text = "Heap View";
            this.menuStrip.ResumeLayout(false);
            this.menuStrip.PerformLayout();
            this.heapViewSplitContainer.Panel1.ResumeLayout(false);
            this.heapViewSplitContainer.Panel2.ResumeLayout(false);
            this.heapViewSplitContainer.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.heapViewSplitContainer)).EndInit();
            this.heapViewSplitContainer.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.heapViewImg)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip menuStrip;
        private System.Windows.Forms.ToolStripMenuItem optionsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem refreshToolStripMenuItem;
        private System.Windows.Forms.SplitContainer heapViewSplitContainer;
        private System.Windows.Forms.ToolStripMenuItem settingsToolStripMenuItem;
        private System.Windows.Forms.PictureBox heapViewImg;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Timer heapViewRefreshTimer;
    }
}