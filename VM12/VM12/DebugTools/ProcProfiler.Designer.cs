namespace Profiler
{
    partial class ProcProfiler
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
            this.procSelector = new System.Windows.Forms.ComboBox();
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.optionsToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.refreshToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.profilerRefresh = new System.Windows.Forms.Timer(this.components);
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.procMetadataView = new VM12.DebugTools.ProcMetadataView();
            this.hotSpotView = new Profiler.HotSpotView();
            this.menuStrip1.SuspendLayout();
            this.tableLayoutPanel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // procSelector
            // 
            this.procSelector.AutoCompleteMode = System.Windows.Forms.AutoCompleteMode.Suggest;
            this.procSelector.AutoCompleteSource = System.Windows.Forms.AutoCompleteSource.ListItems;
            this.procSelector.Dock = System.Windows.Forms.DockStyle.Fill;
            this.procSelector.FormattingEnabled = true;
            this.procSelector.Location = new System.Drawing.Point(3, 3);
            this.procSelector.MinimumSize = new System.Drawing.Size(210, 0);
            this.procSelector.Name = "procSelector";
            this.procSelector.Size = new System.Drawing.Size(210, 21);
            this.procSelector.TabIndex = 1;
            this.procSelector.SelectedIndexChanged += new System.EventHandler(this.procSelector_SelectedIndexChanged);
            // 
            // menuStrip1
            // 
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.optionsToolStripMenuItem});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new System.Drawing.Size(738, 24);
            this.menuStrip1.TabIndex = 2;
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
            // profilerRefresh
            // 
            this.profilerRefresh.Enabled = true;
            this.profilerRefresh.Tick += new System.EventHandler(this.profilerRefresh_Tick);
            // 
            // tableLayoutPanel1
            // 
            this.tableLayoutPanel1.AutoSize = true;
            this.tableLayoutPanel1.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.tableLayoutPanel1.ColumnCount = 2;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tableLayoutPanel1.Controls.Add(this.procMetadataView, 0, 1);
            this.tableLayoutPanel1.Controls.Add(this.hotSpotView, 1, 0);
            this.tableLayoutPanel1.Controls.Add(this.procSelector, 0, 0);
            this.tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayoutPanel1.GrowStyle = System.Windows.Forms.TableLayoutPanelGrowStyle.FixedSize;
            this.tableLayoutPanel1.Location = new System.Drawing.Point(0, 24);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.RowCount = 2;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanel1.Size = new System.Drawing.Size(738, 351);
            this.tableLayoutPanel1.TabIndex = 4;
            // 
            // procMetadataView
            // 
            this.procMetadataView.AutoSize = true;
            this.procMetadataView.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.procMetadataView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.procMetadataView.Location = new System.Drawing.Point(3, 30);
            this.procMetadataView.MinimumSize = new System.Drawing.Size(200, 100);
            this.procMetadataView.Name = "procMetadataView";
            this.procMetadataView.Size = new System.Drawing.Size(210, 318);
            this.procMetadataView.TabIndex = 3;
            // 
            // hotSpotView
            // 
            this.hotSpotView.AutoSize = true;
            this.hotSpotView.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.hotSpotView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.hotSpotView.Location = new System.Drawing.Point(219, 3);
            this.hotSpotView.MinimumSize = new System.Drawing.Size(100, 100);
            this.hotSpotView.Name = "hotSpotView";
            this.tableLayoutPanel1.SetRowSpan(this.hotSpotView, 2);
            this.hotSpotView.Size = new System.Drawing.Size(516, 345);
            this.hotSpotView.TabIndex = 0;
            // 
            // ProcProfiler
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.ClientSize = new System.Drawing.Size(738, 375);
            this.Controls.Add(this.tableLayoutPanel1);
            this.Controls.Add(this.menuStrip1);
            this.MainMenuStrip = this.menuStrip1;
            this.MinimumSize = new System.Drawing.Size(500, 200);
            this.Name = "ProcProfiler";
            this.Text = "ProcProfiler";
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.tableLayoutPanel1.ResumeLayout(false);
            this.tableLayoutPanel1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private HotSpotView hotSpotView;
        private System.Windows.Forms.ComboBox procSelector;
        private System.Windows.Forms.MenuStrip menuStrip1;
        private System.Windows.Forms.ToolStripMenuItem optionsToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem refreshToolStripMenuItem;
        private System.Windows.Forms.Timer profilerRefresh;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private VM12.DebugTools.ProcMetadataView procMetadataView;
    }
}