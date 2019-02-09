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
using NAudio.Dsp;
using System.Runtime.CompilerServices;
using NAudio.Utils;

namespace VM12
{
    public class SoundChip
    {
        private VM12 vm12;
        public WasapiOut DriverOut;
        
        private const int OscillatorCount = 30;

        // TODO: Measure sound from all oscillators!
        private SimpleMixerProvider MixProvider;
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

                //if (Envelopes[i].Triggered != trigger) Console.WriteLine($"Setting osc {i + 1} to type {generatorType} freq {freq} gain {gain} trigger {trigger} attack {attack} decay {decay} sustain {sustain} release {release} pan {pan}");

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
            }

            //DebugTriggers();
        }

        internal SoundChip(VM12 vm12)
        {
            this.vm12 = vm12;
            
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
                Envelopes[i] = new AdsrSampleProvider(i, SignalGenerators[i]);

                Panners[i] = new PanningSampleProvider(Envelopes[i]);
                // NOTE! We might want to change the panning strategy!
            }

            MixProvider = new SimpleMixerProvider(Panners);

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
    }

    public class SimpleMixerProvider : ISampleProvider
    {
        public ISampleProvider[] Providers;
        public WaveFormat format;
        public float[] providerBuffer;

        public SimpleMixerProvider(ISampleProvider[] providers)
        {
            Providers = providers;
            foreach (var prov in Providers)
            {
                if (format == null) format = prov.WaveFormat;

                if (format.SampleRate != prov.WaveFormat.SampleRate ||
                    format.Channels != prov.WaveFormat.Channels) throw new ArgumentException($"All providers must use the same WaveFormat!! {prov}");
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            providerBuffer = BufferHelpers.Ensure(providerBuffer, count);

            int maxSamples = 0;

            for (int i = 0; i < Providers.Length; i++)
            {
                int samplesRead = Providers[i].Read(providerBuffer, 0, count);

                int outIndex = offset;
                for (int n = 0; n < samplesRead; n++)
                {
                    if (n >= maxSamples)
                    {
                        buffer[outIndex++] = providerBuffer[n];
                    }
                    else
                    {
                        buffer[outIndex++] += providerBuffer[n];
                    }
                }

                // Get the biggest number of samples read
                maxSamples = Math.Max(samplesRead, maxSamples);
            }
            
            for (int i = maxSamples; i < count; i++)
            {
                buffer[i] = 0;
            }

            return count;
        }

        public WaveFormat WaveFormat => format;
    }

    /// <summary>
    /// ADSR sample provider allowing you to specify attack, decay, sustain and release values
    /// </summary>
    public class AdsrSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        public ADSREnvelope Envelope { get; }
        private volatile bool triggered;
        private int Ident;
        
        /// <summary>
        /// Creates a new AdsrSampleProvider with default values
        /// </summary>
        public AdsrSampleProvider(int ident, ISampleProvider source)
        {
            Ident = ident;
            if (source.WaveFormat.Channels > 1) throw new ArgumentException("Currently only supports mono inputs");
            this.source = source;
            Envelope = new ADSREnvelope(ident, source.WaveFormat.SampleRate, 0, 0, 0, 0);
            AttackSeconds = 0f;
            SustainLevel = 0f;
            DecaySeconds = 0f;
            ReleaseSeconds = 0f;
        }

        /// <summary>
        /// Attack time in seconds
        /// </summary>
        public double AttackSeconds
        {
            get => Envelope.Attack;
            set => Envelope.Attack = value;
        }

        /// <summary>
        /// Decay time in seconds
        /// </summary>
        public double DecaySeconds
        {
            get => Envelope.Decay;
            set => Envelope.Decay = value;
        }

        /// <summary>
        /// Sustain level (1 = 100%)
        /// </summary>
        public double SustainLevel
        {
            get => Envelope.Sustain;
            set => Envelope.Sustain = value;
        }

        /// <summary>
        /// Release time in seconds
        /// </summary>
        public double ReleaseSeconds
        {
            get => Envelope.Release;
            set => Envelope.Release = value;
        }

        /// <summary>
        /// Start or end the envelope
        /// </summary>
        public bool Triggered
        {
            get => triggered;
            set {
                if (triggered != value)
                    Envelope.Gate(triggered = value);
            }
        }
        
        /// <summary>
        /// Reads audio from this sample provider
        /// </summary>
        public int Read(float[] buffer, int offset, int count)
        {
            if (Envelope.State == ADSREnvelope.ADSRState.Idle) return 0;

            var samples = source.Read(buffer, offset, count);
            for (int n = 0; n < samples; n++)
            {
                buffer[offset++] *= (float) Envelope.NextValue();
            }

            return samples;
        }
        
        /// <summary>
        /// The output WaveFormat
        /// </summary>
        public WaveFormat WaveFormat { get { return source.WaveFormat; } }
    }

    public class ADSREnvelope
    {
        public enum ADSRState
        {
            Idle,
            Attack,
            Decay,
            Sustain,
            Release,
        }

        public enum SmoothingType
        {
            Linear,
            Square,
            Cubic,
            Custom,
        }

        private volatile ADSRState state;
        public ADSRState State { get => state; private set => state = value; }
        public double Attack;
        public double Decay;
        public double Sustain;
        public double Release;
        public double Gain;
        
        public int SampleRate;
        public int SamplesInState;

        // The highest volume before we got into the release state
        private double CurrentLevel;
        private double ReleaseLevel;

        private SmoothingType Smoothing = SmoothingType.Square;
        private int CustomSmoothingLevel = 10;

        private int Ident;

        public ADSREnvelope(int ident, int sampleRate, double attack, double decay, double sustain, double release)
        {
            Ident = ident;
            Gain = 1;
            Attack = attack;
            Decay = decay;
            Sustain = sustain;
            Release = release;
            SampleRate = sampleRate;
        }

        public double NextValue()
        {
            if (state == ADSRState.Idle) return 0;
            if (state == ADSRState.Sustain) return Sustain;
            
            // FIXME: When we change state we want to change time!
            double Time = SamplesInState / (double) SampleRate;

            if (state == ADSRState.Attack && Time > Attack)
            {
                ChangeState(ADSRState.Decay);
                CurrentLevel = Gain;
            }

            if (state == ADSRState.Decay && Time > Decay)
            {
                ChangeState(ADSRState.Sustain);
                CurrentLevel = Sustain;
            }

            if (state == ADSRState.Release && Time > Release)
            {
                ChangeState(ADSRState.Idle);
                CurrentLevel = 0;
                ReleaseLevel = 0;
            }

            SamplesInState++;
            
            return GetValue(Time);
        }

        public double GetValue(double Time)
        {
            double val;
            double x;
            switch (State)
            {
                case ADSRState.Idle:
                    val = 0;
                    CurrentLevel = 0;
                    break;
                case ADSRState.Attack:
                    x = Math.Abs(Time - Attack) / Attack;
                    switch (Smoothing)
                    {
                        case SmoothingType.Linear:
                            val = (Gain - ReleaseLevel) * -x + Gain;
                            break;
                        case SmoothingType.Square:
                            val = (Gain - ReleaseLevel) * -(x * x) + Gain;
                            break;
                        case SmoothingType.Cubic:
                            val = (Gain - ReleaseLevel) * -(x * x * x) + Gain;
                            break;
                        case SmoothingType.Custom:
                            val = (Gain - ReleaseLevel) * -Math.Pow(x, CustomSmoothingLevel) + Gain;
                            break;
                        default: throw new NotImplementedException();
                    }
                    CurrentLevel = val;
                    break;
                case ADSRState.Decay:
                    x = Math.Abs(Time - Decay);
                    switch (Smoothing)
                    {
                        case SmoothingType.Linear:
                            val = (Gain - Sustain) * (1 / Decay) * x + Sustain;
                            break;
                        case SmoothingType.Square:
                            val = (Gain - Sustain) * (1 / (Decay * Decay)) * (x * x) + Sustain;
                            break;
                        case SmoothingType.Cubic:
                            val = (Gain - Sustain) * (1 / (Decay * Decay * Decay)) * (x * x * x) + Sustain;
                            break;
                        case SmoothingType.Custom:
                            val = (Gain - Sustain) * (1 / Math.Pow(Decay, CustomSmoothingLevel)) * Math.Pow(x, CustomSmoothingLevel) + Sustain;
                            break;
                        default: throw new NotImplementedException();
                    }
                    CurrentLevel = val;
                    break;
                case ADSRState.Sustain:
                    val = Sustain;
                    CurrentLevel = val;
                    break;
                case ADSRState.Release:
                    x = Math.Abs(Time - Release);
                    switch (Smoothing)
                    {
                        case SmoothingType.Linear:
                            val = CurrentLevel * (1 / Release) * x;
                            break;
                        case SmoothingType.Square:
                            val = CurrentLevel * (1 / Release * Release) * x * x;
                            break;
                        case SmoothingType.Cubic:
                            val = CurrentLevel * (1 / (Release * Release * Release)) * x * x * x;
                            break;
                        case SmoothingType.Custom:
                            val = CurrentLevel * (1 / Math.Pow(Release, CustomSmoothingLevel)) * Math.Pow(x, CustomSmoothingLevel);
                            break;
                        default: throw new NotImplementedException();
                    }
                    ReleaseLevel = val;
                    break;
                default: throw new NotImplementedException();
            }

            return val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ChangeState(ADSRState state)
        {
            State = state;
            SamplesInState = 0;
        }

        public void Gate(bool trigger)
        {
            ChangeState(trigger ? ADSRState.Attack : ADSRState.Release);
            if (State == ADSRState.Attack && Attack <= 0) CurrentLevel = Gain;
        }
    }
}
