namespace Debugger
{
    partial class SourceView
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
            this.rtbSource = new System.Windows.Forms.RichTextBox();
            this.SuspendLayout();
            // 
            // rtbSource
            // 
            this.rtbSource.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(16)))), ((int)(((byte)(16)))), ((int)(((byte)(16)))));
            this.rtbSource.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rtbSource.Font = new System.Drawing.Font("Microsoft Sans Serif", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.rtbSource.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(224)))), ((int)(((byte)(224)))), ((int)(((byte)(224)))));
            this.rtbSource.Location = new System.Drawing.Point(0, 0);
            this.rtbSource.Name = "rtbSource";
            this.rtbSource.ReadOnly = true;
            this.rtbSource.Size = new System.Drawing.Size(1001, 600);
            this.rtbSource.TabIndex = 0;
            this.rtbSource.Text = "";
            this.rtbSource.WordWrap = false;
            // 
            // SourceView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.rtbSource);
            this.Name = "SourceView";
            this.Size = new System.Drawing.Size(1001, 600);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.RichTextBox rtbSource;
    }
}
