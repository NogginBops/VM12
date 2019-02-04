namespace Debugging
{
    partial class SoundDebug
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
            this.waveViewer = new NAudio.Gui.WaveViewer();
            this.volumeMeterR = new NAudio.Gui.VolumeMeter();
            this.volumeMeterL = new NAudio.Gui.VolumeMeter();
            this.SuspendLayout();
            // 
            // waveViewer
            // 
            this.waveViewer.Location = new System.Drawing.Point(12, 12);
            this.waveViewer.Name = "waveViewer";
            this.waveViewer.SamplesPerPixel = 128;
            this.waveViewer.Size = new System.Drawing.Size(554, 150);
            this.waveViewer.StartPosition = ((long)(0));
            this.waveViewer.TabIndex = 0;
            this.waveViewer.WaveStream = null;
            // 
            // volumeMeterR
            // 
            this.volumeMeterR.Amplitude = 0F;
            this.volumeMeterR.Location = new System.Drawing.Point(12, 168);
            this.volumeMeterR.MaxDb = 18F;
            this.volumeMeterR.MinDb = -60F;
            this.volumeMeterR.Name = "volumeMeterR";
            this.volumeMeterR.Size = new System.Drawing.Size(32, 143);
            this.volumeMeterR.TabIndex = 1;
            this.volumeMeterR.Text = "volumeMeterR";
            // 
            // volumeMeterL
            // 
            this.volumeMeterL.Amplitude = 0F;
            this.volumeMeterL.Location = new System.Drawing.Point(50, 168);
            this.volumeMeterL.MaxDb = 18F;
            this.volumeMeterL.MinDb = -60F;
            this.volumeMeterL.Name = "volumeMeterL";
            this.volumeMeterL.Size = new System.Drawing.Size(32, 143);
            this.volumeMeterL.TabIndex = 2;
            this.volumeMeterL.Text = "volumeMeterL";
            // 
            // SoundDebug
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(578, 323);
            this.Controls.Add(this.volumeMeterL);
            this.Controls.Add(this.volumeMeterR);
            this.Controls.Add(this.waveViewer);
            this.Name = "SoundDebug";
            this.Text = "SoundDebug";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.SoundDebug_FormClosing);
            this.ResumeLayout(false);

        }

        #endregion

        private NAudio.Gui.WaveViewer waveViewer;
        private NAudio.Gui.VolumeMeter volumeMeterR;
        private NAudio.Gui.VolumeMeter volumeMeterL;
    }
}