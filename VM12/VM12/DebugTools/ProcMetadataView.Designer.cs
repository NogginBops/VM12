namespace VM12.DebugTools
{
    partial class ProcMetadataView
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
            this.procName = new System.Windows.Forms.Label();
            this.dgvProcMetadata = new System.Windows.Forms.DataGridView();
            this.property = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.value = new System.Windows.Forms.DataGridViewTextBoxColumn();
            ((System.ComponentModel.ISupportInitialize)(this.dgvProcMetadata)).BeginInit();
            this.SuspendLayout();
            // 
            // procName
            // 
            this.procName.Dock = System.Windows.Forms.DockStyle.Top;
            this.procName.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.procName.Location = new System.Drawing.Point(0, 0);
            this.procName.MinimumSize = new System.Drawing.Size(100, 0);
            this.procName.Name = "procName";
            this.procName.Size = new System.Drawing.Size(234, 20);
            this.procName.TabIndex = 0;
            this.procName.Text = ":proc_name_example";
            this.procName.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            // 
            // dgvProcMetadata
            // 
            this.dgvProcMetadata.AllowUserToAddRows = false;
            this.dgvProcMetadata.AllowUserToDeleteRows = false;
            this.dgvProcMetadata.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.AllCells;
            this.dgvProcMetadata.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvProcMetadata.ColumnHeadersVisible = false;
            this.dgvProcMetadata.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.property,
            this.value});
            this.dgvProcMetadata.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvProcMetadata.Location = new System.Drawing.Point(0, 20);
            this.dgvProcMetadata.Name = "dgvProcMetadata";
            this.dgvProcMetadata.ReadOnly = true;
            this.dgvProcMetadata.RowHeadersVisible = false;
            this.dgvProcMetadata.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvProcMetadata.Size = new System.Drawing.Size(234, 231);
            this.dgvProcMetadata.TabIndex = 1;
            // 
            // property
            // 
            this.property.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.property.HeaderText = "Property";
            this.property.Name = "property";
            this.property.ReadOnly = true;
            // 
            // value
            // 
            this.value.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.value.HeaderText = "Value";
            this.value.Name = "value";
            this.value.ReadOnly = true;
            // 
            // ProcMetadataView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.Controls.Add(this.dgvProcMetadata);
            this.Controls.Add(this.procName);
            this.MinimumSize = new System.Drawing.Size(200, 100);
            this.Name = "ProcMetadataView";
            this.Size = new System.Drawing.Size(234, 251);
            ((System.ComponentModel.ISupportInitialize)(this.dgvProcMetadata)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label procName;
        private System.Windows.Forms.DataGridView dgvProcMetadata;
        private System.Windows.Forms.DataGridViewTextBoxColumn property;
        private System.Windows.Forms.DataGridViewTextBoxColumn value;
    }
}
