using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.Dsp;
using NAudio.Dmo;
using NAudio.Utils;
using NAudio.MediaFoundation;
using NAudio.CoreAudioApi;
using NAudio.Gui;

namespace VM12
{
    public class SoundChip
    {
        private VM12 vm12;
        public WasapiOut DriverOut;

        private volatile bool ShouldRun = false;

        private const int OscillatorCount = 30;

        // TODO: Measure sound from all oscillators!
        private MixingSampleProvider MixProvider;
        private SignalGenerator[] SignalGenerators;
        private AdsrSampleProvider[] Envelopes;
        private PanningSampleProvider[] Panners;
        private MeteringSampleProvider VolumeMeter;

        const int OscStructSize = 24;

        const int Type_Offset = 0;
        const int Trigger_Offset = 1;
        const int Gain_Offset = 2;
        const int Freq_Offset = 3;
        const int Attack_Offset = 5;
        const int Decay_Offset = 6;
        const int Sustain_Offset = 7;
        const int Release_Offset = 8;
        const int Pan_Offset = 9;

        const int UPDATE_ADDR = VM12.GRAM_START - 1;
        const int OSC_START_ADDR = UPDATE_ADDR - (OscillatorCount * OscStructSize);

        internal unsafe void UpdateOscillators(int* mem)
        {
            for (int i = 0; i < OscillatorCount; i++)
            {
                int offset = OSC_START_ADDR + (i * OscStructSize);

                SignalGeneratorType generatorType = (SignalGeneratorType)mem[offset + Type_Offset].Clamp(0, (int)SignalGeneratorType.SawTooth);
                bool trigger = mem[offset + Trigger_Offset] != 0;
                double gain = mem[offset + Gain_Offset] / (double)0xFFF;
                double freq = ((mem[offset + Freq_Offset] << 12 | mem[offset + Freq_Offset + 1]) & 0xFFF_FFF) / 100f;
                float attack = mem[offset + Attack_Offset] / 100f;
                float decay = mem[offset + Decay_Offset] / 100f;
                float sustain = mem[offset + Sustain_Offset] / (float)0xFFF;
                float release = mem[offset + Release_Offset] / 100f;
                float pan = (mem[offset + Pan_Offset] - (0xFFF / 2)) / (float)0xFFF;

                if (Envelopes[i].Triggered != trigger) Console.WriteLine($"Setting osc {i + 1} to type {generatorType} freq {freq} gain {gain} trigger {trigger} attack {attack} decay {decay} sustain {sustain} release {release} pan {pan}");

                SignalGenerators[i].Type = generatorType;
                SignalGenerators[i].Frequency = freq;
                SignalGenerators[i].Gain = gain;
                Envelopes[i].AttackSeconds = attack;
                Envelopes[i].DecaySeconds = decay;
                Envelopes[i].SustainLevel = sustain;
                Envelopes[i].ReleaseSeconds = release;
                Panners[i].Pan = pan;
                
                // We might want to trigger them outside of this loop
                Envelopes[i].Triggered = trigger;

                Envelopes[i].Envelope.Process();

                if (Envelopes[i].Envelope.State == EnvelopeGenerator.EnvelopeState.Release)
                {
                    Console.WriteLine("RELEASE!!!!");
                }
            }

            DebugTriggers();
        }

        internal SoundChip(VM12 vm12)
        {
            this.vm12 = vm12;

            ShouldRun = true;

            DriverOut = new WasapiOut(AudioClientShareMode.Shared, true, 40);
            int SampelRate = DriverOut.OutputWaveFormat.SampleRate;

            SignalGenerators = new SignalGenerator[OscillatorCount];
            Envelopes = new AdsrSampleProvider[OscillatorCount];
            Panners = new PanningSampleProvider[OscillatorCount];

            for (int i = 0; i < SignalGenerators.Length; i++)
            {
                SignalGenerators[i] = new SignalGenerator(SampelRate, 1)
                {
                    Type = SignalGeneratorType.Sin,
                    Frequency = 0,
                    Gain = 0,
                };

                int SampleRate = SignalGenerators[i].WaveFormat.SampleRate;
                Envelopes[i] = new AdsrSampleProvider(SignalGenerators[i]);

                Panners[i] = new PanningSampleProvider(Envelopes[i]);
                // NOTE! We might want to change the panning strategy!
            }

            MixProvider = new MixingSampleProvider(Panners);
            MixProvider.ReadFully = true;

            VolumeMeter = new MeteringSampleProvider(MixProvider);

            DriverOut.Init(VolumeMeter);
        }

        internal void StartSoundChip()
        {
            DriverOut.Play();
            DriverOut.PlaybackStopped += DriverOut_PlaybackStopped;
        }

        private void DriverOut_PlaybackStopped(object sender, StoppedEventArgs e)
        {
            Console.WriteLine("PLAYBACK STOPPED!!!");
        }

        internal void StopSoundChip()
        {
            DriverOut.PlaybackStopped -= DriverOut_PlaybackStopped;
            DriverOut.Stop();
            DriverOut.Dispose();
        }

        internal MeteringSampleProvider GetVolumeMeter()
        {
            return VolumeMeter;
        }

        internal void DebugTriggers()
        {
            int i = 1;
            foreach (var env in Envelopes)
            {
                if (env.Triggered)
                {
                    Console.WriteLine($"Osc {i} is triggered!");
                }

                i++;
            }
        }
    }

    /// <summary>
    /// ADSR sample provider allowing you to specify attack, decay, sustain and release values
    /// </summary>
    public class AdsrSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly EnvelopeGenerator adsr;
        private float attackSeconds;
        private float decaySeconds;
        private float releaseSeconds;
        private bool triggered;
        
        /// <summary>
        /// Creates a new AdsrSampleProvider with default values
        /// </summary>
        public AdsrSampleProvider(ISampleProvider source)
        {
            if (source.WaveFormat.Channels > 1) throw new ArgumentException("Currently only supports mono inputs");
            this.source = source;
            adsr = new EnvelopeGenerator();
            AttackSeconds = 0f;
            SustainLevel = 0f;
            DecaySeconds = 0f;
            ReleaseSeconds = 0f;
        }

        /// <summary>
        /// Attack time in seconds
        /// </summary>
        public float AttackSeconds
        {
            get
            {
                return attackSeconds;
            }
            set
            {
                attackSeconds = value;
                adsr.AttackRate = attackSeconds * WaveFormat.SampleRate;
            }
        }

        /// <summary>
        /// Decay time in seconds
        /// </summary>
        public float DecaySeconds
        {
            get
            {
                return decaySeconds;
            }
            set
            {
                decaySeconds = value;
                adsr.DecayRate = decaySeconds * WaveFormat.SampleRate;
            }
        }

        /// <summary>
        /// Sustain level (1 = 100%)
        /// </summary>
        public float SustainLevel
        {
            get => adsr.SustainLevel;
            set => adsr.SustainLevel = value;
        }

        /// <summary>
        /// Release time in seconds
        /// </summary>
        public float ReleaseSeconds
        {
            get
            {
                return releaseSeconds;
            }
            set
            {
                releaseSeconds = value;
                adsr.ReleaseRate = releaseSeconds * WaveFormat.SampleRate;
            }
        }

        /// <summary>
        /// Start or end the envelope
        /// </summary>
        public bool Triggered
        {
            get => triggered;
            set {
                if (triggered != value)
                    adsr.Gate(triggered = value);
            }
        }

        /// <summary>
        /// Reads audio from this sample provider
        /// </summary>
        public int Read(float[] buffer, int offset, int count)
        {
            if (adsr.State == EnvelopeGenerator.EnvelopeState.Idle) return 0; // we've finished
            var samples = source.Read(buffer, offset, count);
            for (int n = 0; n < samples; n++)
            {
                buffer[offset++] *= adsr.Process();
            }
            return samples;
        }
        
        /// <summary>
        /// The output WaveFormat
        /// </summary>
        public WaveFormat WaveFormat { get { return source.WaveFormat; } }

        public EnvelopeGenerator Envelope => adsr;
    }
}
