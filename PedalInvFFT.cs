// PedalInvFFT.cs — v1.0.
//
// K5000-inspired additive synth using inverse FFT with overlap-add (OLA)
// resynthesis. 16 partials of a 1/n harmonic series rendered into an
// FFT_SIZE=2048 spectrum, inverse-FFT'd and accumulated with 75% overlap
// (HOP_SIZE=512, Hann window, COLA constant = 2).
//
// Parameters:
//   Volume                              — output level
//   Amp ADSR                            — amplitude envelope
//   Brightness, Tilt, Balance           — static spectrum shaping
//   Bright ADSR + Bright Amount         — envelope on Brightness (K5000 style)
//   Formant Centre/Width/Amount         — resonant peak in log-frequency
//   Glide                               — exponential pitch slide between notes
//
// Companion files:
//   FFT.cs        — radix-2 in-place complex FFT
//   Envelope.cs   — shared ADSR class for amp and brightness envelopes
//
// Architecture and conventions documented in
// ReBuzz_ManagedMachine_Notes_Core.md and the Pedal-series build notes.
//
// The v0.4 envelope architecture: a second Envelope instance modulates
// Brightness over the note. K5000-style "additive synth that breathes."
//
// The Bright env reuses the Envelope class verbatim (no changes to that
// file). It runs in lockstep with the Amp env in the drain loop, but
// its value is only consulted at hop boundaries (in RunHop) where it
// combines with the static Brightness param to produce an effective
// brightness for that frame's spectrum build.
//
// Modulation math (in RunHop):
//   brightEnvAmount = (BrightAmount - 64) / 64   in [-1, +1]
//   brightMod       = brightEnvAmount * _brightEnv.Level * 127
//   effectiveBright = clamp(Brightness + brightMod, 0, 127)
//   brightCutoff    = (effectiveBright / 127) * N_PARTIALS
//
// At Bright Amount = 64 (default) there is no modulation. v0.4.1 sounds
// identical to v0.3.1. Above 64, the env opens brightness up; below 64,
// the env closes brightness down.

using System;
using BuzzGUI.Interfaces;
using Buzz.MachineInterface;

namespace PedalInvFFT
{
    [MachineDecl(Name        = "Pedal invFFT",
                 ShortName   = "invFFT",
                 Author      = "Pedal",
                 MaxTracks   = 1,
                 InputCount  = 0,
                 OutputCount = 2)]
    public class PedalInvFFTMachine : IBuzzMachine
    {
        // ── DSP constants ─────────────────────────────────────────────────
        public const int FFT_SIZE   = 2048;
        public const int HOP_SIZE   = 512;
        public const int N_PARTIALS = 16;

        const float DEPOSIT_GAIN = FFT_SIZE / 4f;
        const float COLA_INV     = 1f / 2f;

        const float BRIGHTNESS_STEEPNESS = 0.7f;
        const float MAX_TILT_AMOUNT      = 0.5f;

        // ── Pre-allocated buffers (no per-Work allocation) ────────────────
        readonly float[]   _specRe    = new float[FFT_SIZE];
        readonly float[]   _specIm    = new float[FFT_SIZE];
        readonly float[]   _olaBuf    = new float[FFT_SIZE];
        readonly float[]   _phases    = new float[N_PARTIALS];
        readonly FFT       _fft       = new FFT(FFT_SIZE);
        readonly Envelope  _ampEnv    = new Envelope();
        readonly Envelope  _brightEnv = new Envelope();   // modulates Brightness

        // ── Voice state ───────────────────────────────────────────────────
        int   _samplesUntilNextHop = 0;
        int   _olaReadPos          = 0;
        float _freqHz              = 440f;

        // Glide state — _currentMidi follows _targetMidi via per-hop one-pole.
        // Continuous semitones (float) keeps the slide linear in pitch space,
        // which is more musical than linear in Hz.
        float _currentMidi = 60f;
        float _targetMidi  = 60f;

        bool  _wasPlaying = false;     // transport edge detection (Core §27)

        // ── Pending events (drained at top of Work, PedalSH101 §6.3) ──────
        bool _hasNoteOn       = false;
        bool _hasNoteOff      = false;
        byte _pendingBuzzNote = 0;

        // ── Parameters ────────────────────────────────────────────────────
        // Order matters for preset compatibility (Build §3.3).

        [ParameterDecl(Name        = "Volume",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 64,
                       Description = "Output level (64 is unity for a single partial)")]
        public int Volume { get; set; } = 64;

        // Amp ADSR (v0.2)
        [ParameterDecl(Name        = "Amp Attack",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 24,
                       Description = "Amp envelope attack time (linear ramp)")]
        public int AmpAttack { get; set; } = 24;

        [ParameterDecl(Name        = "Amp Decay",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 64,
                       Description = "Amp envelope decay time (1 over e to sustain)")]
        public int AmpDecay { get; set; } = 64;

        [ParameterDecl(Name        = "Amp Sustain",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 100,
                       Description = "Amp envelope sustain level (0 to 1)")]
        public int AmpSustain { get; set; } = 100;

        [ParameterDecl(Name        = "Amp Release",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 48,
                       Description = "Amp envelope release time (1 over e to zero)")]
        public int AmpRelease { get; set; } = 48;

        // Spectrum shape (v0.3)
        [ParameterDecl(Name        = "Brightness",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 127,
                       Description = "Spectral lowpass: 0 dark (fundamental only), 127 full saw")]
        public int Brightness { get; set; } = 127;

        [ParameterDecl(Name        = "Tilt",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 64,
                       Description = "Spectral tilt: 64 neutral, lower boosts lows, higher boosts highs")]
        public int Tilt { get; set; } = 64;

        [ParameterDecl(Name        = "Balance",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 64,
                       Description = "Even-odd harmonic balance: 0 odd only, 64 saw, 127 even only")]
        public int Balance { get; set; } = 64;

        // ── New in v0.4 (renamed in v0.4.1): Bright ADSR + amount ─────────
        // Same time-mapping convention as Amp ADSR. Defaults shape a
        // medium-decay filter envelope; user dials Bright Amount above 64
        // to hear it.

        [ParameterDecl(Name        = "Bright Attack",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 32,
                       Description = "Bright envelope attack time (linear ramp)")]
        public int BrightAttack { get; set; } = 32;

        [ParameterDecl(Name        = "Bright Decay",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 80,
                       Description = "Bright envelope decay time (1 over e to sustain)")]
        public int BrightDecay { get; set; } = 80;

        [ParameterDecl(Name        = "Bright Sustain",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 0,
                       Description = "Bright envelope sustain level (0 to 1)")]
        public int BrightSustain { get; set; } = 0;

        [ParameterDecl(Name        = "Bright Release",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 48,
                       Description = "Bright envelope release time (1 over e to zero)")]
        public int BrightRelease { get; set; } = 48;

        [ParameterDecl(Name        = "Bright Amount",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 64,
                       Description = "Bright envelope modulation depth (64 is neutral)")]
        public int BrightAmount { get; set; } = 64;

        // ── New in v0.5 — appended at the end so v0.4.x preset indices stay valid ──

        [ParameterDecl(Name        = "Formant Centre",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 64,
                       Description = "Formant centre frequency (100 Hz to 6 kHz, log)")]
        public int FormantCentre { get; set; } = 64;

        [ParameterDecl(Name        = "Formant Width",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 64,
                       Description = "Formant peak width (0 narrow, 127 broad)")]
        public int FormantWidth { get; set; } = 64;

        [ParameterDecl(Name        = "Formant Amount",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 0,
                       Description = "Formant peak gain (0 off, 127 maximum boost)")]
        public int FormantAmount { get; set; } = 0;

        // ── New in v1.0 — appended at the end so v0.x preset indices stay valid ──

        [ParameterDecl(Name        = "Glide",
                       MinValue    = 0,
                       MaxValue    = 127,
                       DefValue    = 0,
                       Description = "Pitch glide time (0 instant, 127 slow)")]
        public int Glide { get; set; } = 0;

        // ── Track parameter ───────────────────────────────────────────────
        [ParameterDecl(Name        = "Note",
                       IsStateless = true,
                       Description = "Note trigger")]
        public void SetNote(Note value, int track)
        {
            byte v = value.Value;
            if (v == 0)        return;
            if (v == Note.Off) { _hasNoteOff = true; return; }
            _pendingBuzzNote = v;
            _hasNoteOn       = true;
        }

        // ── Construction ──────────────────────────────────────────────────
        IBuzzMachineHost host;

        public PedalInvFFTMachine(IBuzzMachineHost host)
        {
            this.host = host;

            var rng = new Random();
            for (int p = 0; p < N_PARTIALS; p++)
                _phases[p] = (float)(rng.NextDouble() * 2.0 * Math.PI);
        }

        // ── Audio loop ────────────────────────────────────────────────────
        public bool Work(Sample[] output, int n, WorkModes mode)
        {
            if (_hasNoteOn)
            {
                TriggerNote(_pendingBuzzNote);
                _hasNoteOn = false;
            }
            if (_hasNoteOff)
            {
                _ampEnv.NoteOff();
                _brightEnv.NoteOff();
                _hasNoteOff = false;
            }

            int sr = host?.MasterInfo?.SamplesPerSec ?? 48000;

            bool nowPlaying = _wasPlaying;
            try { nowPlaying = host?.Machine?.Graph?.Buzz?.Playing ?? false; }
            catch { /* keep nowPlaying = _wasPlaying */ }
            if (_wasPlaying && !nowPlaying)
            {
                _ampEnv.ForcedRelease(sr);
                _brightEnv.ForcedRelease(sr);
            }
            _wasPlaying = nowPlaying;

            _ampEnv   .UpdateCoefs(sr, AmpAttack,    AmpDecay,    AmpSustain,    AmpRelease);
            _brightEnv.UpdateCoefs(sr, BrightAttack, BrightDecay, BrightSustain, BrightRelease);

            if (!_ampEnv.IsActive)
            {
                for (int i = 0; i < n; i++) output[i] = new Sample(0f, 0f);
                return true;
            }

            float gain = (Volume / 64f) * 32768f * COLA_INV;

            int produced = 0;
            while (produced < n)
            {
                if (_samplesUntilNextHop <= 0)
                {
                    RunHop();
                    _samplesUntilNextHop = HOP_SIZE;
                    _olaReadPos          = 0;
                }

                int batch = Math.Min(n - produced, _samplesUntilNextHop);
                for (int i = 0; i < batch; i++)
                {
                    float aEnv = _ampEnv.Process();
                    // _brightEnv NOT advanced here — see RunHop() below.
                    float s    = _olaBuf[_olaReadPos + i] * gain * aEnv;
                    output[produced + i] = new Sample(s, s);
                }
                produced            += batch;
                _olaReadPos         += batch;
                _samplesUntilNextHop -= batch;
            }
            return true;
        }

        // ── Note triggering ───────────────────────────────────────────────
        void TriggerNote(byte buzzNote)
        {
            int octave = (buzzNote >> 4);
            int semi   = (buzzNote & 0xF) - 1;
            int midi   = octave * 12 + semi;

            // Glide control (PedalSH101 §6.1): first note from rest doesn't
            // glide, so a fresh trigger lands cleanly on the dialed pitch
            // without a sweep up from whatever _currentMidi happened to be.
            bool wasIdle = !_ampEnv.IsActive;

            _targetMidi = midi;
            if (Glide == 0 || wasIdle)
                _currentMidi = midi;
            // else: leave _currentMidi to glide via per-hop one-pole in RunHop

            if (wasIdle)
            {
                Array.Clear(_olaBuf, 0, FFT_SIZE);
                _samplesUntilNextHop = 0;
                _olaReadPos          = 0;
            }

            _ampEnv   .NoteOn();
            _brightEnv.NoteOn();
        }

        // ── Per-hop synthesis ─────────────────────────────────────────────
        void RunHop()
        {
            Array.Copy (_olaBuf, HOP_SIZE, _olaBuf, 0, FFT_SIZE - HOP_SIZE);
            Array.Clear(_olaBuf, FFT_SIZE - HOP_SIZE, HOP_SIZE);
            Array.Clear(_specRe, 0, FFT_SIZE);
            Array.Clear(_specIm, 0, FFT_SIZE);

            int   sr           = host?.MasterInfo?.SamplesPerSec ?? 48000;
            float nyquist      = sr * 0.5f;
            float binsPerHz    = (float)FFT_SIZE / sr;
            float phaseAdvCo   = 2f * MathF.PI * HOP_SIZE / sr;
            int   posBinLimit  = FFT_SIZE / 2;

            // Glide — advance _currentMidi toward _targetMidi by one hop's worth.
            // Time mapping: 5 ms .. 2 s exponential across 1..127 (Glide=0
            // already snapped in TriggerNote, so here we only run when >0).
            if (Glide > 0 && _currentMidi != _targetMidi)
            {
                float glideMs = 5f * MathF.Pow(400f, Glide / 127f);
                float coef    = MathF.Exp(-1000f * HOP_SIZE / (glideMs * sr));
                _currentMidi  = _targetMidi + (_currentMidi - _targetMidi) * coef;
                if (MathF.Abs(_currentMidi - _targetMidi) < 0.001f)
                    _currentMidi = _targetMidi;   // snap when sub-cent residual
            }
            _freqHz = 440f * MathF.Pow(2f, (_currentMidi - 69f) / 12f);

            // Bright env modulation of brightness.
            // At BrightAmount=64 (default), brightEnvAmount=0 and brightMod=0,
            // so effectiveBright = Brightness — identical to v0.3.1.
            float brightEnvAmount = (BrightAmount - 64) / 64f;                 // -1..+1
            float brightMod       = brightEnvAmount * _brightEnv.Level * 127f;
            float effectiveBright = MathF.Max(0f, MathF.Min(127f, Brightness + brightMod));

            // Hop-constant spectrum-shape modifiers.
            float brightCutoff = (effectiveBright / 127f) * N_PARTIALS;
            float tiltAmount   = (Tilt - 64) / 64f * MAX_TILT_AMOUNT;
            float balOffset    = (Balance - 64) / 64f;
            float evenScale    = MathF.Max(0f, MathF.Min(1f, 1f + balOffset));
            float oddScale     = MathF.Max(0f, MathF.Min(1f, 1f - balOffset));

            // Formant filter — Gaussian bell on each partial in log-frequency.
            // FormantAmount=0 short-circuits the per-partial branch entirely.
            bool  useFormant       = FormantAmount > 0;
            float formantCenterHz  = 0f;
            float formantInvWidth  = 0f;   // 1 / widthOctaves, hoisted to skip per-partial divide
            float formantPeakMinus = 0f;   // peakLin - 1, hoisted to skip per-partial subtract
            if (useFormant)
            {
                formantCenterHz       = 100f * MathF.Pow(60f, FormantCentre / 127f);   // 100..6000 Hz
                float widthOct        = 0.1f + (FormantWidth / 127f) * 1.9f;            // 0.1..2 octaves
                formantInvWidth       = 1f / widthOct;
                float peakDb          = (FormantAmount / 127f) * 18f;                   // 0..18 dB
                formantPeakMinus      = MathF.Pow(10f, peakDb / 20f) - 1f;              // ~0..6.94
            }

            for (int p = 0; p < N_PARTIALS; p++)
            {
                float partialFreq = _freqHz * (p + 1);
                if (partialFreq >= nyquist) break;

                float kStar  = partialFreq * binsPerHz;
                int   kCent  = (int)MathF.Round(kStar);

                float amp = 1f / (p + 1);

                if (p > brightCutoff)
                    amp *= MathF.Exp(-(p - brightCutoff) * BRIGHTNESS_STEEPNESS);

                if (tiltAmount != 0f)
                    amp *= MathF.Pow(p + 1, tiltAmount);

                if (balOffset != 0f)
                    amp *= ((p + 1) % 2 == 0) ? evenScale : oddScale;

                if (useFormant)
                {
                    float deltaOct = MathF.Log2(partialFreq / formantCenterHz);
                    float u        = deltaOct * formantInvWidth;
                    float weight   = MathF.Exp(-u * u);                  // Gaussian bell, 0..1
                    amp *= 1f + formantPeakMinus * weight;               // blends 1..peakLin
                }

                float dep    = DEPOSIT_GAIN * amp;
                float phase  = _phases[p];
                float reC    = dep * MathF.Cos(phase);
                float imC    = dep * MathF.Sin(phase);

                for (int dk = -2; dk <= 2; dk++)
                {
                    int bin = kCent + dk;
                    if (bin <= 0 || bin >= posBinLimit) continue;

                    float w = HannLobeWeight(bin - kStar);

                    _specRe[bin] += reC * w;
                    _specIm[bin] += imC * w;

                    int negBin = FFT_SIZE - bin;
                    _specRe[negBin] += reC * w;
                    _specIm[negBin] -= imC * w;
                }

                _phases[p] += phaseAdvCo * partialFreq;
                while (_phases[p] >  MathF.PI) _phases[p] -= 2f * MathF.PI;
                while (_phases[p] < -MathF.PI) _phases[p] += 2f * MathF.PI;
            }

            _fft.Inverse(_specRe, _specIm);

            for (int i = 0; i < FFT_SIZE; i++)
                _olaBuf[i] += _specRe[i];

            // Advance spec env by HOP_SIZE samples — one tick per
            // sample of audio this hop will produce. Equivalent in
            // total to advancing inside the drain loop, but consolidated
            // here to dodge the v0.4 hang. The Level read at the top of
            // the next RunHop reflects the env state at the start of
            // that hop, exactly matching the per-sample timing.
            for (int s = 0; s < HOP_SIZE; s++)
                _brightEnv.Process();
        }

        static float HannLobeWeight(float d)
        {
            return Sinc(d) - 0.5f * Sinc(d - 1f) - 0.5f * Sinc(d + 1f);
        }

        static float Sinc(float x)
        {
            if (MathF.Abs(x) < 1e-6f) return 1f;
            float px = MathF.PI * x;
            return MathF.Sin(px) / px;
        }
    }
}
