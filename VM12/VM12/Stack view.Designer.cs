namespace Debugging
{
    partial class Stack_view
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.Windows.Forms.ListViewItem listViewItem2 = new System.Windows.Forms.ListViewItem(new string[] {
            "0x000_004",
            "0x000"}, -1, System.Drawing.Color.Empty, System.Drawing.SystemColors.Window, null);
            this.listStack = new System.Windows.Forms.ListView();
            this.colHAddress = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.colHValue = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.splitContainer = new System.Windows.Forms.SplitContainer();
            this.dgvStack = new System.Windows.Forms.DataGridView();
            this.colHProc = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colHLocation = new System.Windows.Forms.DataGridViewTextBoxColumn();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).BeginInit();
            this.splitContainer.Panel1.SuspendLayout();
            this.splitContainer.Panel2.SuspendLayout();
            this.splitContainer.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvStack)).BeginInit();
            this.SuspendLayout();
            // 
            // listStack
            // 
            this.listStack.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.colHAddress,
            this.colHValue});
            this.listStack.Dock = System.Windows.Forms.DockStyle.Fill;
            listViewItem2.StateImageIndex = 0;
            this.listStack.Items.AddRange(new System.Windows.Forms.ListViewItem[] {
            listViewItem2});
            this.listStack.Location = new System.Drawing.Point(0, 0);
            this.listStack.Name = "listStack";
            this.listStack.ShowGroups = false;
            this.listStack.Size = new System.Drawing.Size(571, 529);
            this.listStack.TabIndex = 0;
            this.listStack.UseCompatibleStateImageBehavior = false;
            this.listStack.View = System.Windows.Forms.View.Details;
            // 
            // colHAddress
            // 
            this.colHAddress.Text = "Address";
            this.colHAddress.Width = 113;
            // 
            // colHValue
            // 
            this.colHValue.Text = "Value";
            this.colHValue.Width = 82;
            // 
            // splitContainer
            // 
            this.splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer.Location = new System.Drawing.Point(0, 0);
            this.splitContainer.Name = "splitContainer";
            this.splitContainer.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // splitContainer.Panel1
            // 
            this.splitContainer.Panel1.Controls.Add(this.dgvStack);
            // 
            // splitContainer.Panel2
            // 
            this.splitContainer.Panel2.Controls.Add(this.listStack);
            this.splitContainer.Size = new System.Drawing.Size(571, 714);
            this.splitContainer.SplitterDistance = 181;
            this.splitContainer.TabIndex = 1;
            // 
            // dgvStack
            // 
            this.dgvStack.AllowUserToAddRows = false;
            this.dgvStack.AllowUserToDeleteRows = false;
            this.dgvStack.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dgvStack.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvStack.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colHProc,
            this.colHLocation});
            this.dgvStack.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvStack.EditMode = System.Windows.Forms.DataGridViewEditMode.EditOnEnter;
            this.dgvStack.Location = new System.Drawing.Point(0, 0);
            this.dgvStack.MultiSelect = false;
            this.dgvStack.Name = "dgvStack";
            this.dgvStack.RowHeadersVisible = false;
            this.dgvStack.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.CellSelect;
            this.dgvStack.ShowCellErrors = false;
            this.dgvStack.ShowEditingIcon = false;
            this.dgvStack.ShowRowErrors = false;
            this.dgvStack.Size = new System.Drawing.Size(571, 181);
            this.dgvStack.TabIndex = 2;
            this.dgvStack.CellDoubleClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.dgvStack_CellDoubleClick);
            // 
            // colHProc
            // 
            this.colHProc.HeaderText = "Proc";
            this.colHProc.Name = "colHProc";
            this.colHProc.ReadOnly = true;
            this.colHProc.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // colHLocation
            // 
            this.colHLocation.HeaderText = "Location";
            this.colHLocation.Name = "colHLocation";
            this.colHLocation.ReadOnly = true;
            this.colHLocation.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // Stack_view
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoScroll = true;
            this.Controls.Add(this.splitContainer);
            this.Name = "Stack_view";
            this.Size = new System.Drawing.Size(571, 714);
            this.splitContainer.Panel1.ResumeLayout(false);
            this.splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).EndInit();
            this.splitContainer.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dgvStack)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ListView listStack;
        private System.Windows.Forms.ColumnHeader colHAddress;
        private System.Windows.Forms.ColumnHeader colHValue;
        private System.Windows.Forms.SplitContainer splitContainer;
        private System.Windows.Forms.DataGridView dgvStack;
        private System.Windows.Forms.DataGridViewTextBoxColumn colHProc;
        private System.Windows.Forms.DataGridViewTextBoxColumn colHLocation;
    }
}
