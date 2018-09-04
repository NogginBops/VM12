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
            this.heapViewImg = new System.Windows.Forms.PictureBox();
            this.heapViewRefreshTimer = new System.Windows.Forms.Timer(this.components);
            this.scrollPanel = new System.Windows.Forms.Panel();
            this.menuStrip.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.heapViewImg)).BeginInit();
            this.scrollPanel.SuspendLayout();
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
            this.refreshToolStripMenuItem.Size = new System.Drawing.Size(113, 22);
            this.refreshToolStripMenuItem.Text = "Refresh";
            this.refreshToolStripMenuItem.Click += new System.EventHandler(this.refreshToolStripMenuItem_Click);
            // 
            // settingsToolStripMenuItem
            // 
            this.settingsToolStripMenuItem.Name = "settingsToolStripMenuItem";
            this.settingsToolStripMenuItem.Size = new System.Drawing.Size(61, 20);
            this.settingsToolStripMenuItem.Text = "Settings";
            // 
            // heapViewImg
            // 
            this.heapViewImg.BackColor = System.Drawing.Color.Red;
            this.heapViewImg.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.heapViewImg.Dock = System.Windows.Forms.DockStyle.Fill;
            this.heapViewImg.Location = new System.Drawing.Point(0, 0);
            this.heapViewImg.Name = "heapViewImg";
            this.heapViewImg.Size = new System.Drawing.Size(914, 520);
            this.heapViewImg.TabIndex = 0;
            this.heapViewImg.TabStop = false;
            this.heapViewImg.DoubleClick += new System.EventHandler(this.heapViewImg_DoubleClick);
            this.heapViewImg.Resize += new System.EventHandler(this.heapViewImg_Resize);
            // 
            // heapViewRefreshTimer
            // 
            this.heapViewRefreshTimer.Tick += new System.EventHandler(this.heapViewRefreshTimer_Tick);
            // 
            // scrollPanel
            // 
            this.scrollPanel.AutoScroll = true;
            this.scrollPanel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.scrollPanel.Controls.Add(this.heapViewImg);
            this.scrollPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.scrollPanel.Location = new System.Drawing.Point(0, 24);
            this.scrollPanel.Name = "scrollPanel";
            this.scrollPanel.Size = new System.Drawing.Size(914, 520);
            this.scrollPanel.TabIndex = 3;
            // 
            // HeapView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(914, 544);
            this.Controls.Add(this.scrollPanel);
            this.Controls.Add(this.menuStrip);
            this.DoubleBuffered = true;
            this.Name = "HeapView";
            this.Text = "Heap View";
            this.menuStrip.ResumeLayout(false);
            this.menuStrip.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.heapViewImg)).EndInit();
            this.scrollPanel.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.MenuStrip menuStrip;
        private System.Windows.Forms.ToolStripMenuItem optionsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem refreshToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem settingsToolStripMenuItem;
        private System.Windows.Forms.PictureBox heapViewImg;
        private System.Windows.Forms.Timer heapViewRefreshTimer;
        private System.Windows.Forms.Panel scrollPanel;
    }
}