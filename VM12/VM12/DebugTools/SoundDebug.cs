using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using VM12;
using NAudio.CoreAudioApi;
using NAudio.Wave.SampleProviders;
using NAudio.Wave;



namespace Debugging
{
    using VM12 = VM12.VM12;

    public partial class SoundDebug : Form
    {
        private VM12 vm12;
        private SoundChip chip;

        public SoundDebug()
        {
            InitializeComponent();
        }

        internal void SetVM12(VM12 vm12)
        {
            if (this.vm12 != null)
            {
                this.vm12.SoundChip.GetVolumeMeter().StreamVolume -= SoundDebug_StreamVolume;
            }

            this.vm12 = vm12;
            this.chip = vm12.SoundChip;

            
            chip.GetVolumeMeter().StreamVolume += SoundDebug_StreamVolume;
        }
        
        private void SoundDebug_StreamVolume(object sender, StreamVolumeEventArgs e)
        {
            volumeMeterR.Amplitude = e.MaxSampleValues[0];
            volumeMeterL.Amplitude = e.MaxSampleValues[0];
        }

        private void SoundDebug_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (this.vm12 != null)
            {
                this.vm12.SoundChip.GetVolumeMeter().StreamVolume -= SoundDebug_StreamVolume;
            }
        }
    }
}
