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
            this.splitContainer = new System.Windows.Forms.SplitContainer();
            this.dgvCallStack = new System.Windows.Forms.DataGridView();
            this.colHProc = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colHLocation = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.dgvStack = new System.Windows.Forms.DataGridView();
            this.colHAddress = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colHValue = new System.Windows.Forms.DataGridViewTextBoxColumn();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).BeginInit();
            this.splitContainer.Panel1.SuspendLayout();
            this.splitContainer.Panel2.SuspendLayout();
            this.splitContainer.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvCallStack)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.dgvStack)).BeginInit();
            this.SuspendLayout();
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
            this.splitContainer.Panel1.Controls.Add(this.dgvCallStack);
            // 
            // splitContainer.Panel2
            // 
            this.splitContainer.Panel2.Controls.Add(this.dgvStack);
            this.splitContainer.Size = new System.Drawing.Size(571, 714);
            this.splitContainer.SplitterDistance = 181;
            this.splitContainer.TabIndex = 1;
            // 
            // dgvCallStack
            // 
            this.dgvCallStack.AllowUserToAddRows = false;
            this.dgvCallStack.AllowUserToDeleteRows = false;
            this.dgvCallStack.AllowUserToResizeRows = false;
            this.dgvCallStack.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dgvCallStack.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvCallStack.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colHProc,
            this.colHLocation});
            this.dgvCallStack.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvCallStack.EditMode = System.Windows.Forms.DataGridViewEditMode.EditOnEnter;
            this.dgvCallStack.Location = new System.Drawing.Point(0, 0);
            this.dgvCallStack.MultiSelect = false;
            this.dgvCallStack.Name = "dgvCallStack";
            this.dgvCallStack.RowHeadersVisible = false;
            this.dgvCallStack.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.CellSelect;
            this.dgvCallStack.ShowCellErrors = false;
            this.dgvCallStack.ShowEditingIcon = false;
            this.dgvCallStack.ShowRowErrors = false;
            this.dgvCallStack.Size = new System.Drawing.Size(571, 181);
            this.dgvCallStack.TabIndex = 2;
            this.dgvCallStack.CellDoubleClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.dgvStack_CellDoubleClick);
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
            // dgvStack
            // 
            this.dgvStack.AllowUserToAddRows = false;
            this.dgvStack.AllowUserToDeleteRows = false;
            this.dgvStack.AllowUserToResizeRows = false;
            this.dgvStack.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dgvStack.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvStack.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colHAddress,
            this.colHValue});
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
            this.dgvStack.Size = new System.Drawing.Size(571, 529);
            this.dgvStack.TabIndex = 3;
            this.dgvStack.CellValidating += new System.Windows.Forms.DataGridViewCellValidatingEventHandler(this.dgvStack_CellValidating);
            // 
            // colHAddress
            // 
            this.colHAddress.HeaderText = "Address";
            this.colHAddress.Name = "colHAddress";
            this.colHAddress.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            // 
            // colHValue
            // 
            this.colHValue.HeaderText = "Value";
            this.colHValue.Name = "colHValue";
            this.colHValue.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
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
            ((System.ComponentModel.ISupportInitialize)(this.dgvCallStack)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.dgvStack)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.SplitContainer splitContainer;
        private System.Windows.Forms.DataGridView dgvCallStack;
        private System.Windows.Forms.DataGridViewTextBoxColumn colHProc;
        private System.Windows.Forms.DataGridViewTextBoxColumn colHLocation;
        private System.Windows.Forms.DataGridView dgvStack;
        private System.Windows.Forms.DataGridViewTextBoxColumn colHAddress;
        private System.Windows.Forms.DataGridViewTextBoxColumn colHValue;
    }
}
