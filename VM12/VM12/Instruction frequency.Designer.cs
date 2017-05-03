namespace VM12
{
    partial class Instruction_frequency
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
            this.instructionFrequencyListView = new System.Windows.Forms.ListView();
            this.refreshInstructionFreqTimer = new System.Windows.Forms.Timer(this.components);
            this.menuStrip = new System.Windows.Forms.MenuStrip();
            this.optionsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.refreshToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.menuStrip.SuspendLayout();
            this.SuspendLayout();
            // 
            // instructionFrequencyListView
            // 
            this.instructionFrequencyListView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.instructionFrequencyListView.GridLines = true;
            this.instructionFrequencyListView.Location = new System.Drawing.Point(0, 24);
            this.instructionFrequencyListView.Name = "instructionFrequencyListView";
            this.instructionFrequencyListView.Size = new System.Drawing.Size(415, 292);
            this.instructionFrequencyListView.TabIndex = 0;
            this.instructionFrequencyListView.UseCompatibleStateImageBehavior = false;
            this.instructionFrequencyListView.View = System.Windows.Forms.View.Details;
            // 
            // refreshInstructionFreqTimer
            // 
            this.refreshInstructionFreqTimer.Enabled = true;
            this.refreshInstructionFreqTimer.Tick += new System.EventHandler(this.refreshInstructionFreqTimer_Tick);
            // 
            // menuStrip
            // 
            this.menuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.optionsToolStripMenuItem});
            this.menuStrip.Location = new System.Drawing.Point(0, 0);
            this.menuStrip.Name = "menuStrip";
            this.menuStrip.Size = new System.Drawing.Size(415, 24);
            this.menuStrip.TabIndex = 1;
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
            // Instruction_frequency
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(415, 316);
            this.Controls.Add(this.instructionFrequencyListView);
            this.Controls.Add(this.menuStrip);
            this.MainMenuStrip = this.menuStrip;
            this.Name = "Instruction_frequency";
            this.Text = "Instruction_frequency";
            this.Load += new System.EventHandler(this.Instruction_frequency_Load);
            this.menuStrip.ResumeLayout(false);
            this.menuStrip.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ListView instructionFrequencyListView;
        private System.Windows.Forms.Timer refreshInstructionFreqTimer;
        private System.Windows.Forms.MenuStrip menuStrip;
        private System.Windows.Forms.ToolStripMenuItem optionsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem refreshToolStripMenuItem;
    }
}